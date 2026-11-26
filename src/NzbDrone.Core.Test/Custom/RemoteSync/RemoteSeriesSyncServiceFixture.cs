using System;
using System.Collections.Generic;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Custom.RemoteSync;
using NzbDrone.Core.Custom.RemoteSync.Models;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.Custom.RemoteSync
{
    [TestFixture]
    public class RemoteSeriesSyncServiceFixture : CoreTest<RemoteSeriesSyncService>
    {
        [SetUp]
        public void Setup()
        {
            Environment.SetEnvironmentVariable("SONARR_WATCH_ROOT_FOLDER", "/tv");
            Environment.SetEnvironmentVariable("SONARR_SYNC_LOOKBACK_DAYS", null);
            Environment.SetEnvironmentVariable("SONARR_WATCH_QUALITY_PROFILE_ID", null);

            Mocker.GetMock<IQualityProfileService>()
                .Setup(q => q.All())
                .Returns(new List<QualityProfile> { new QualityProfile { Id = 1, Name = "Any" } });
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable("SONARR_WATCH_ROOT_FOLDER", null);
            Environment.SetEnvironmentVariable("SONARR_SYNC_LOOKBACK_DAYS", null);
            Environment.SetEnvironmentVariable("SONARR_WATCH_QUALITY_PROFILE_ID", null);
        }

        [Test]
        public void should_prune_history_with_negative_30_day_cutoff()
        {
            Mocker.GetMock<IRemoteSonarrClient>()
                .Setup(c => c.GetSeries())
                .Returns(new List<RemoteSeries>());

            var before = DateTime.UtcNow.AddDays(-30);
            Subject.Execute(new RemoteSeriesSyncCommand());
            var after = DateTime.UtcNow.AddDays(-30);

            Mocker.GetMock<IRemoteSyncHistoryRepository>().Verify(
                r => r.DeleteOlderThan(It.Is<DateTime>(dt => dt >= before.AddSeconds(-5) && dt <= after.AddSeconds(5))),
                Times.Once());
        }

        [Test]
        public void should_propagate_series_type_and_season_folder_and_skip_existing()
        {
            var newRemote = new RemoteSeries
            {
                TvdbId = 1001,
                Title = "Anime Show",
                TitleSlug = "anime-show",
                QualityProfileId = 4,
                Monitored = true,
                SeasonFolder = false,
                SeriesType = SeriesTypes.Anime,
                Added = DateTime.UtcNow.AddDays(-1)
            };

            var existingRemote = new RemoteSeries
            {
                TvdbId = 1002,
                Title = "Existing Show",
                Added = DateTime.UtcNow.AddDays(-1)
            };

            Mocker.GetMock<IRemoteSonarrClient>()
                .Setup(c => c.GetSeries())
                .Returns(new List<RemoteSeries> { newRemote, existingRemote });

            Mocker.GetMock<ISeriesService>()
                .Setup(s => s.FindByTvdbId(1002))
                .Returns(new Series { Id = 2, TvdbId = 1002 });

            Mocker.GetMock<IQualityProfileService>()
                .Setup(q => q.Exists(4))
                .Returns(true);

            Series addedSeries = null;
            Mocker.GetMock<IAddSeriesService>()
                .Setup(a => a.AddSeries(It.IsAny<Series>()))
                .Callback<Series>(s => addedSeries = s)
                .Returns<Series>(s => s);

            Subject.Execute(new RemoteSeriesSyncCommand());

            addedSeries.Should().NotBeNull();
            addedSeries.TvdbId.Should().Be(1001);
            addedSeries.SeriesType.Should().Be(SeriesTypes.Anime);
            addedSeries.SeasonFolder.Should().BeFalse();
            addedSeries.QualityProfileId.Should().Be(4);
            addedSeries.RootFolderPath.Should().Be("/tv");

            Mocker.GetMock<IRemoteSyncHistoryRepository>().Verify(
                r => r.Insert(It.Is<RemoteSyncHistory>(h => h.TvdbId == 1001 && h.Action == "Added")),
                Times.Once());
            Mocker.GetMock<IRemoteSyncHistoryRepository>().Verify(
                r => r.Insert(It.Is<RemoteSyncHistory>(h => h.TvdbId == 1002)),
                Times.Never());
        }

        [Test]
        public void should_use_env_override_quality_profile_when_valid()
        {
            Environment.SetEnvironmentVariable("SONARR_WATCH_QUALITY_PROFILE_ID", "7");

            var remote = new RemoteSeries
            {
                TvdbId = 2001,
                Title = "Show",
                QualityProfileId = 4,
                Added = DateTime.UtcNow.AddDays(-1)
            };

            Mocker.GetMock<IRemoteSonarrClient>().Setup(c => c.GetSeries()).Returns(new List<RemoteSeries> { remote });
            Mocker.GetMock<IQualityProfileService>().Setup(q => q.Exists(7)).Returns(true);
            Mocker.GetMock<IQualityProfileService>().Setup(q => q.Exists(4)).Returns(true);

            Series addedSeries = null;
            Mocker.GetMock<IAddSeriesService>()
                .Setup(a => a.AddSeries(It.IsAny<Series>()))
                .Callback<Series>(s => addedSeries = s)
                .Returns<Series>(s => s);

            Subject.Execute(new RemoteSeriesSyncCommand());

            addedSeries.QualityProfileId.Should().Be(7);
        }

        [Test]
        public void should_fallback_to_first_local_profile_when_remote_profile_missing()
        {
            var remote = new RemoteSeries
            {
                TvdbId = 2002,
                Title = "Show",
                QualityProfileId = 99,
                Added = DateTime.UtcNow.AddDays(-1)
            };

            Mocker.GetMock<IRemoteSonarrClient>().Setup(c => c.GetSeries()).Returns(new List<RemoteSeries> { remote });
            Mocker.GetMock<IQualityProfileService>().Setup(q => q.Exists(99)).Returns(false);
            Mocker.GetMock<IQualityProfileService>().Setup(q => q.All()).Returns(new List<QualityProfile> { new QualityProfile { Id = 3 } });

            Series addedSeries = null;
            Mocker.GetMock<IAddSeriesService>()
                .Setup(a => a.AddSeries(It.IsAny<Series>()))
                .Callback<Series>(s => addedSeries = s)
                .Returns<Series>(s => s);

            Subject.Execute(new RemoteSeriesSyncCommand());

            addedSeries.QualityProfileId.Should().Be(3);
        }

        [Test]
        public void should_sync_all_series_when_lookback_days_is_zero()
        {
            Environment.SetEnvironmentVariable("SONARR_SYNC_LOOKBACK_DAYS", "0");

            var oldRemote = new RemoteSeries
            {
                TvdbId = 3001,
                Title = "Old Show",
                QualityProfileId = 1,
                Added = DateTime.UtcNow.AddYears(-3)
            };

            Mocker.GetMock<IRemoteSonarrClient>().Setup(c => c.GetSeries()).Returns(new List<RemoteSeries> { oldRemote });
            Mocker.GetMock<IQualityProfileService>().Setup(q => q.Exists(1)).Returns(true);

            Subject.Execute(new RemoteSeriesSyncCommand());

            Mocker.GetMock<IAddSeriesService>().Verify(a => a.AddSeries(It.Is<Series>(s => s.TvdbId == 3001)), Times.Once());
        }

        [Test]
        public void should_skip_old_series_with_default_lookback_and_record_validation_failures()
        {
            var oldRemote = new RemoteSeries
            {
                TvdbId = 4001,
                Title = "Too Old",
                Added = DateTime.UtcNow.AddDays(-14)
            };

            var badRemote = new RemoteSeries
            {
                TvdbId = 4002,
                Title = "Invalid Show",
                QualityProfileId = 1,
                Added = DateTime.UtcNow.AddDays(-1)
            };

            Mocker.GetMock<IRemoteSonarrClient>().Setup(c => c.GetSeries()).Returns(new List<RemoteSeries> { oldRemote, badRemote });
            Mocker.GetMock<IQualityProfileService>().Setup(q => q.Exists(1)).Returns(true);
            Mocker.GetMock<IAddSeriesService>()
                .Setup(a => a.AddSeries(It.Is<Series>(s => s.TvdbId == 4002)))
                .Throws(new ValidationException(new[] { new ValidationFailure("Title", "Title already exists") }));

            Subject.Execute(new RemoteSeriesSyncCommand());

            Mocker.GetMock<IAddSeriesService>().Verify(a => a.AddSeries(It.Is<Series>(s => s.TvdbId == 4001)), Times.Never());
            Mocker.GetMock<IRemoteSyncHistoryRepository>().Verify(
                r => r.Insert(It.Is<RemoteSyncHistory>(h => h.TvdbId == 4002 && h.Action == "Failed" && h.Detail == "Title already exists")),
                Times.Once());
        }
    }
}
