using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Custom.LocalIngestor;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Custom.LocalIngestor
{
    [TestFixture]
    public class WatchFolderRulesFixture : TestBase
    {
        [TestCase("/watch/Show S01E01.mkv", true)]
        [TestCase("/watch/Show S01E01.MKV", true)]
        [TestCase("/watch/Show S01E01.mp4", true)]
        [TestCase("/watch/Show S01E01.avi", true)]
        [TestCase("/watch/Show S01E01.ts", true)]
        [TestCase("/watch/Show S01E01.m4v", true)]
        [TestCase("/watch/Show S01E01.M4V", true)]
        [TestCase("/watch/Show S01E01.mov", true)]
        [TestCase("/watch/Show S01E01.m2ts", true)]
        [TestCase("/watch/Show S01E01.mpg", true)]
        [TestCase("/watch/Show S01E01.mpeg", true)]
        [TestCase("/watch/Show S01E01.wmv", true)]
        [TestCase("/watch/Show S01E01.webm", true)]
        [TestCase("/watch/Show S01E01.txt", false)]
        [TestCase("/watch/Show S01E01.nfo", false)]
        [TestCase("/watch/", false)]
        [TestCase(null, false)]
        public void is_video_file_should_match_only_video_extensions(string path, bool expected)
        {
            WatchFolderRules.IsVideoFile(path).Should().Be(expected);
        }

        [Test]
        public void is_settled_should_be_true_when_file_older_than_min_age()
        {
            var now = DateTime.UtcNow;
            var lastWrite = now.AddSeconds(-61);

            WatchFolderRules.IsSettled(now, lastWrite, TimeSpan.FromSeconds(60)).Should().BeTrue();
        }

        [Test]
        public void is_settled_should_be_false_when_file_recently_written()
        {
            var now = DateTime.UtcNow;
            var lastWrite = now.AddSeconds(-30);

            WatchFolderRules.IsSettled(now, lastWrite, TimeSpan.FromSeconds(60)).Should().BeFalse();
        }

        [Test]
        public void is_settled_boundary_exactly_min_age_is_settled()
        {
            var now = DateTime.UtcNow;
            var lastWrite = now.AddSeconds(-60);

            WatchFolderRules.IsSettled(now, lastWrite, TimeSpan.FromSeconds(60)).Should().BeTrue();
        }

        [Test]
        public void has_grown_should_be_false_when_never_seen_before()
        {
            WatchFolderRules.HasGrown(null, 12345).Should().BeFalse();
        }

        [Test]
        public void has_grown_should_be_false_when_size_unchanged()
        {
            WatchFolderRules.HasGrown(1000, 1000).Should().BeFalse();
        }

        [Test]
        public void has_grown_should_detect_size_changes_in_both_directions()
        {
            WatchFolderRules.HasGrown(1000, 2000).Should().BeTrue();
            WatchFolderRules.HasGrown(2000, 1000).Should().BeTrue();
        }

        [TestCase(50L * 1024 * 1024, true)]
        [TestCase((50L * 1024 * 1024) + 1, true)]
        [TestCase((50L * 1024 * 1024) - 1, false)]
        [TestCase(0, false)]
        public void meets_min_size_should_enforce_threshold(long sizeBytes, bool expected)
        {
            WatchFolderRules.MeetsMinSize(sizeBytes, 50L * 1024 * 1024).Should().Be(expected);
        }

        [TestCase("Move", ImportMode.Move)]
        [TestCase("move", ImportMode.Move)]
        [TestCase(" MOVE ", ImportMode.Move)]
        [TestCase("Copy", ImportMode.Copy)]
        [TestCase("", ImportMode.Copy)]
        [TestCase(null, ImportMode.Copy)]
        [TestCase("bogus", ImportMode.Copy)]
        [TestCase("Hardlink", ImportMode.Copy)]
        public void parse_import_mode_should_be_lenient_and_default_to_copy(string value, ImportMode expected)
        {
            WatchFolderRules.ParseImportMode(value).Should().Be(expected);
        }

        [TestCase("true", true)]
        [TestCase("TRUE", true)]
        [TestCase("1", true)]
        [TestCase("yes", true)]
        [TestCase("false", false)]
        [TestCase("0", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void parse_allow_upgrades_should_default_to_false(string value, bool expected)
        {
            WatchFolderRules.ParseAllowUpgrades(value).Should().Be(expected);
        }

        [TestCase(3840, 2160, 2160)]
        [TestCase(1920, 1080, 1080)]
        [TestCase(1920, 960, 1080)]
        [TestCase(1280, 720, 720)]
        [TestCase(720, 480, 480)]
        [TestCase(0, 0, 0)]
        public void get_resolution_tier_should_classify_standard_and_letterboxed_dimensions(int width, int height, int expectedTier)
        {
            var mi = new MediaInfoModel { Width = width, Height = height };
            WatchFolderRules.GetResolutionTier(mi).Should().Be(expectedTier);
        }

        [Test]
        public void is_media_info_upgrade_should_approve_1080p_over_720p_with_same_or_better_audio()
        {
            var existing = MakeMediaInfo(1280, 720, 2);
            var candidate = MakeMediaInfo(1920, 1080, 2);

            WatchFolderRules.IsMediaInfoUpgrade(candidate, existing, out var reason).Should().BeTrue();
            reason.Should().Contain("720p/2ch -> 1080p/2ch");
        }

        [Test]
        public void is_media_info_upgrade_should_approve_5_1_over_2_0_at_same_resolution()
        {
            var existing = MakeMediaInfo(1920, 1080, 2);
            var candidate = MakeMediaInfo(1920, 960, 6);

            WatchFolderRules.IsMediaInfoUpgrade(candidate, existing, out var reason).Should().BeTrue();
            reason.Should().Contain("1080p/2ch -> 1080p/6ch");
        }

        [Test]
        public void is_media_info_upgrade_should_reject_1080p_2_0_over_720p_5_1_no_regression()
        {
            var existing = MakeMediaInfo(1280, 720, 6);
            var candidate = MakeMediaInfo(1920, 1080, 2);

            WatchFolderRules.IsMediaInfoUpgrade(candidate, existing, out _).Should().BeFalse();
        }

        [Test]
        public void is_media_info_upgrade_should_reject_720p_5_1_over_1080p_2_0_no_regression()
        {
            var existing = MakeMediaInfo(1920, 1080, 2);
            var candidate = MakeMediaInfo(1280, 720, 6);

            WatchFolderRules.IsMediaInfoUpgrade(candidate, existing, out _).Should().BeFalse();
        }

        [Test]
        public void is_media_info_upgrade_should_reject_equal_resolution_and_channels()
        {
            var existing = MakeMediaInfo(1920, 1080, 6);
            var candidate = MakeMediaInfo(1920, 1080, 6);

            WatchFolderRules.IsMediaInfoUpgrade(candidate, existing, out _).Should().BeFalse();
        }

        [Test]
        public void has_file_changed_for_reeval_should_detect_size_or_mtime_change()
        {
            var seen = new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc);

            WatchFolderRules.HasFileChangedForReeval(1000, 1000, seen, seen.AddMinutes(-5)).Should().BeFalse();
            WatchFolderRules.HasFileChangedForReeval(1000, 2000, seen, seen.AddMinutes(-5)).Should().BeTrue();
            WatchFolderRules.HasFileChangedForReeval(1000, 1000, seen, seen.AddMinutes(5)).Should().BeTrue();
            WatchFolderRules.HasFileChangedForReeval(null, 1000, seen, seen.AddMinutes(-5)).Should().BeFalse();
            WatchFolderRules.HasFileChangedForReeval(null, 1000, seen, seen.AddMinutes(5)).Should().BeTrue();
        }

        [Test]
        public void is_only_upgrade_rejection_should_match_only_upgrade_reasons()
        {
            var ep = new LocalEpisode { Path = "/watch/a.mkv" };
            var onlyUpgrade = new ImportDecision(ep, new ImportRejection(ImportRejectionReason.NotQualityUpgrade, "Not upgrade"));
            var mixed = new ImportDecision(
                ep,
                new ImportRejection(ImportRejectionReason.NotQualityUpgrade, "Not upgrade"),
                new ImportRejection(ImportRejectionReason.Sample, "Sample"));

            WatchFolderRules.IsOnlyUpgradeRejection(onlyUpgrade).Should().BeTrue();
            WatchFolderRules.IsOnlyUpgradeRejection(mixed).Should().BeFalse();
            WatchFolderRules.IsOnlyUpgradeRejection(new ImportDecision(ep)).Should().BeFalse();
        }

        private static MediaInfoModel MakeMediaInfo(int width, int height, int channels)
        {
            return new MediaInfoModel
            {
                Width = width,
                Height = height,
                AudioStreams = new List<MediaInfoAudioStreamModel>
                {
                    new MediaInfoAudioStreamModel { Channels = channels }
                }
            };
        }
    }
}
