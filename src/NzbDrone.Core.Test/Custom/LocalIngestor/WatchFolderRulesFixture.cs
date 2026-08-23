using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Custom.LocalIngestor;
using NzbDrone.Core.MediaFiles.EpisodeImport;
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
        [TestCase(50L * 1024 * 1024 + 1, true)]
        [TestCase(50L * 1024 * 1024 - 1, false)]
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
    }
}
