using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Custom.LocalIngestor;
using NzbDrone.Core.Custom.LocalIngestor.Models;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.Custom.LocalIngestor
{
    [TestFixture]
    public class LocalFolderWatcherServiceFixture : CoreTest<LocalFolderWatcherService>
    {
        private string _watchFolder;
        private Series _series;

        [SetUp]
        public void Setup()
        {
            _watchFolder = Path.Combine(Path.GetTempPath(), "sonarr-watch-test");
            Environment.SetEnvironmentVariable("SONARR_WATCH_FOLDER", _watchFolder);
            Environment.SetEnvironmentVariable("SONARR_IMPORT_MODE", "Copy");
            Environment.SetEnvironmentVariable("SONARR_WATCH_MIN_SIZE_MB", "1");
            Environment.SetEnvironmentVariable("SONARR_WATCH_SETTLE_SECONDS", "60");
            Environment.SetEnvironmentVariable("SONARR_WATCH_FAILURE_THRESHOLD", "3");

            _series = Builder<Series>.CreateNew()
                .With(s => s.Id = 10)
                .With(s => s.Title = "Test Show")
                .Build();

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FolderExists(_watchFolder))
                .Returns(true);

            Mocker.GetMock<ILocalWatchBufferRepository>()
                .Setup(r => r.All())
                .Returns(new List<LocalWatchBuffer>());
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable("SONARR_WATCH_FOLDER", null);
            Environment.SetEnvironmentVariable("SONARR_IMPORT_MODE", null);
            Environment.SetEnvironmentVariable("SONARR_WATCH_MIN_SIZE_MB", null);
            Environment.SetEnvironmentVariable("SONARR_WATCH_SETTLE_SECONDS", null);
            Environment.SetEnvironmentVariable("SONARR_WATCH_FAILURE_THRESHOLD", null);
        }

        private string WatchFile(string relativePath) => Path.Combine(_watchFolder, relativePath);

        private void SetupSettledFile(string path, long sizeBytes = 100L * 1024 * 1024)
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetFiles(_watchFolder, true))
                .Returns(new[] { path });
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.IsFileLocked(path))
                .Returns(false);
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetFileSize(path))
                .Returns(sizeBytes);
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FileGetLastWrite(path))
                .Returns(DateTime.UtcNow.AddMinutes(-5));
        }

        private ImportDecision BuildDecision(string path, bool approved, bool hasExistingFile = false, string rejectionReason = "Rejected")
        {
            var episode = Builder<Episode>.CreateNew()
                .With(e => e.Id = 100)
                .With(e => e.EpisodeFileId = hasExistingFile ? 55 : 0)
                .Build();

            var localEpisode = new LocalEpisode
            {
                Path = path,
                Series = _series,
                Episodes = new List<Episode> { episode }
            };

            return approved
                ? new ImportDecision(localEpisode)
                : new ImportDecision(localEpisode, new ImportRejection(ImportRejectionReason.Unknown, rejectionReason));
        }

        [Test]
        public void should_mark_imported_when_import_result_is_imported()
        {
            var path = WatchFile("Test.Show.S01E01.mkv");
            SetupSettledFile(path);

            Mocker.GetMock<IParsingService>()
                .Setup(p => p.Map(It.IsAny<ParsedEpisodeInfo>(), 0, 0, null))
                .Returns(new RemoteEpisode { Series = _series });

            var decision = BuildDecision(path, approved: true);
            Mocker.GetMock<IMakeImportDecision>()
                .Setup(m => m.GetImportDecisions(It.Is<List<string>>(l => l.Contains(path)), _series))
                .Returns(new List<ImportDecision> { decision });

            Mocker.GetMock<IImportApprovedEpisodes>()
                .Setup(i => i.Import(It.IsAny<List<ImportDecision>>(), true, null, ImportMode.Copy))
                .Returns(new List<ImportResult> { new ImportResult(decision, new EpisodeFile { Id = 99 }) });

            Subject.Execute(new LocalWatchScanCommand());

            Mocker.GetMock<ILocalWatchBufferRepository>().Verify(
                r => r.Update(It.Is<LocalWatchBuffer>(b =>
                    b.Path == path &&
                    b.Status == LocalWatchStatus.Imported &&
                    b.Failures == 0 &&
                    b.Reason == "Successfully imported")),
                Times.Once());
            Mocker.GetMock<ILocalWatchBufferRepository>().Verify(r => r.GetByPath(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_mark_ignored_when_import_result_reports_already_imported()
        {
            var path = WatchFile("Test.Show.S01E01.mkv");
            SetupSettledFile(path);

            Mocker.GetMock<IParsingService>()
                .Setup(p => p.Map(It.IsAny<ParsedEpisodeInfo>(), 0, 0, null))
                .Returns(new RemoteEpisode { Series = _series });

            var decision = BuildDecision(path, approved: true);
            Mocker.GetMock<IMakeImportDecision>()
                .Setup(m => m.GetImportDecisions(It.IsAny<List<string>>(), _series))
                .Returns(new List<ImportDecision> { decision });

            Mocker.GetMock<IImportApprovedEpisodes>()
                .Setup(i => i.Import(It.IsAny<List<ImportDecision>>(), true, null, ImportMode.Copy))
                .Returns(new List<ImportResult> { new ImportResult(decision, "Episode has already been imported") });

            Subject.Execute(new LocalWatchScanCommand());

            Mocker.GetMock<ILocalWatchBufferRepository>().Verify(
                r => r.Update(It.Is<LocalWatchBuffer>(b =>
                    b.Path == path &&
                    b.Status == LocalWatchStatus.Ignored &&
                    b.Reason.Contains("already been imported"))),
                Times.Once());
        }

        [Test]
        public void should_increment_failures_and_reach_failed_when_import_result_has_errors()
        {
            var path = WatchFile("Test.Show.S01E01.mkv");
            SetupSettledFile(path);

            var existing = new LocalWatchBuffer
            {
                Id = 1,
                Path = path,
                Status = LocalWatchStatus.Pending,
                Size = 100L * 1024 * 1024,
                Failures = 2,
                LastSeen = DateTime.UtcNow.AddMinutes(-10)
            };

            Mocker.GetMock<ILocalWatchBufferRepository>()
                .Setup(r => r.All())
                .Returns(new List<LocalWatchBuffer> { existing });

            Mocker.GetMock<IParsingService>()
                .Setup(p => p.Map(It.IsAny<ParsedEpisodeInfo>(), 0, 0, null))
                .Returns(new RemoteEpisode { Series = _series });

            var decision = BuildDecision(path, approved: true);
            Mocker.GetMock<IMakeImportDecision>()
                .Setup(m => m.GetImportDecisions(It.IsAny<List<string>>(), _series))
                .Returns(new List<ImportDecision> { decision });

            Mocker.GetMock<IImportApprovedEpisodes>()
                .Setup(i => i.Import(It.IsAny<List<ImportDecision>>(), true, null, ImportMode.Copy))
                .Returns(new List<ImportResult> { new ImportResult(decision, "Disk full") });

            Subject.Execute(new LocalWatchScanCommand());

            Mocker.GetMock<ILocalWatchBufferRepository>().Verify(
                r => r.Update(It.Is<LocalWatchBuffer>(b =>
                    b.Path == path &&
                    b.Status == LocalWatchStatus.Failed &&
                    b.Failures == 3 &&
                    b.Reason == "Disk full")),
                Times.Once());
        }

        [Test]
        public void should_mark_ignored_before_unmapped_when_episode_already_has_file_and_decision_rejected()
        {
            var path = WatchFile("Test.Show.S01E01.mkv");
            SetupSettledFile(path);

            Mocker.GetMock<IParsingService>()
                .Setup(p => p.Map(It.IsAny<ParsedEpisodeInfo>(), 0, 0, null))
                .Returns(new RemoteEpisode { Series = _series });

            var decision = BuildDecision(path, approved: false, hasExistingFile: true, rejectionReason: "Not an upgrade");
            Mocker.GetMock<IMakeImportDecision>()
                .Setup(m => m.GetImportDecisions(It.IsAny<List<string>>(), _series))
                .Returns(new List<ImportDecision> { decision });

            Subject.Execute(new LocalWatchScanCommand());

            Mocker.GetMock<ILocalWatchBufferRepository>().Verify(
                r => r.Update(It.Is<LocalWatchBuffer>(b =>
                    b.Path == path &&
                    b.Status == LocalWatchStatus.Ignored &&
                    b.Reason == "Episode already has a file in the library")),
                Times.Once());
            Mocker.GetMock<IImportApprovedEpisodes>().Verify(
                i => i.Import(It.IsAny<List<ImportDecision>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()),
                Times.Never());
        }

        [Test]
        public void should_fallback_to_get_series_by_filename_when_map_returns_null()
        {
            var path = WatchFile("Test.Show.S01E01.mkv");
            SetupSettledFile(path);

            Mocker.GetMock<IParsingService>()
                .Setup(p => p.Map(It.IsAny<ParsedEpisodeInfo>(), 0, 0, null))
                .Returns(new RemoteEpisode { Series = null });
            Mocker.GetMock<IParsingService>()
                .Setup(p => p.GetSeries("Test.Show.S01E01.mkv"))
                .Returns(_series);

            var decision = BuildDecision(path, approved: true);
            Mocker.GetMock<IMakeImportDecision>()
                .Setup(m => m.GetImportDecisions(It.IsAny<List<string>>(), _series))
                .Returns(new List<ImportDecision> { decision });
            Mocker.GetMock<IImportApprovedEpisodes>()
                .Setup(i => i.Import(It.IsAny<List<ImportDecision>>(), true, null, ImportMode.Copy))
                .Returns(new List<ImportResult> { new ImportResult(decision, new EpisodeFile { Id = 1 }) });

            Subject.Execute(new LocalWatchScanCommand());

            Mocker.GetMock<IParsingService>().Verify(p => p.GetSeries("Test.Show.S01E01.mkv"), Times.Once());
            Mocker.GetMock<ILocalWatchBufferRepository>().Verify(
                r => r.Update(It.Is<LocalWatchBuffer>(b => b.Status == LocalWatchStatus.Imported)),
                Times.Once());
        }

        [Test]
        public void should_mark_unmapped_when_multiple_series_found_exception_is_thrown()
        {
            var path = WatchFile("Shogun.S01E01.mkv");
            SetupSettledFile(path);

            Mocker.GetMock<IParsingService>()
                .Setup(p => p.Map(It.IsAny<ParsedEpisodeInfo>(), 0, 0, null))
                .Throws(new MultipleSeriesFoundException(new List<Series>(), "Multiple series found"));

            Subject.Execute(new LocalWatchScanCommand());

            Mocker.GetMock<ILocalWatchBufferRepository>().Verify(
                r => r.Update(It.Is<LocalWatchBuffer>(b =>
                    b.Path == path &&
                    b.Status == LocalWatchStatus.Unmapped &&
                    b.Reason.Contains("Multiple library series match"))),
                Times.Once());
        }

        [Test]
        public void should_pass_folder_info_when_file_is_in_subdirectory()
        {
            var subfolder = "Test.Show.S01.1080p.WEB-DL";
            var path = WatchFile(Path.Combine(subfolder, "Test.Show.S01E01.mkv"));
            SetupSettledFile(path);

            Mocker.GetMock<IParsingService>()
                .Setup(p => p.Map(It.IsAny<ParsedEpisodeInfo>(), 0, 0, null))
                .Returns(new RemoteEpisode { Series = _series });

            var decision = BuildDecision(path, approved: true);
            Mocker.GetMock<IMakeImportDecision>()
                .Setup(m => m.GetImportDecisions(
                    It.Is<List<string>>(l => l.Contains(path)),
                    _series,
                    null,
                    null,
                    It.Is<ParsedEpisodeInfo>(info => info != null && info.SeasonNumber == 1),
                    false,
                    false))
                .Returns(new List<ImportDecision> { decision });

            Mocker.GetMock<IImportApprovedEpisodes>()
                .Setup(i => i.Import(It.IsAny<List<ImportDecision>>(), true, null, ImportMode.Copy))
                .Returns(new List<ImportResult> { new ImportResult(decision, new EpisodeFile { Id = 1 }) });

            Subject.Execute(new LocalWatchScanCommand());

            Mocker.GetMock<IMakeImportDecision>().Verify(
                m => m.GetImportDecisions(
                    It.IsAny<List<string>>(),
                    _series,
                    null,
                    null,
                    It.Is<ParsedEpisodeInfo>(info => info != null && info.SeasonNumber == 1),
                    false,
                    false),
                Times.Once());
        }

        [Test]
        public void should_skip_growing_and_unsettled_files_and_ignore_below_min_size()
        {
            var smallPath = WatchFile("Sample.S01E01.mkv");
            var growingPath = WatchFile("Growing.S01E01.mkv");
            var settlingPath = WatchFile("Settling.S01E01.mkv");

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetFiles(_watchFolder, true))
                .Returns(new[] { smallPath, growingPath, settlingPath });
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.IsFileLocked(It.IsAny<string>()))
                .Returns(false);

            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFileSize(smallPath)).Returns(100);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFileSize(growingPath)).Returns(20L * 1024 * 1024);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFileSize(settlingPath)).Returns(50L * 1024 * 1024);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileGetLastWrite(settlingPath)).Returns(DateTime.UtcNow.AddSeconds(-10));

            var growingEntry = new LocalWatchBuffer
            {
                Id = 2,
                Path = growingPath,
                Status = LocalWatchStatus.Pending,
                Size = 10L * 1024 * 1024
            };

            Mocker.GetMock<ILocalWatchBufferRepository>()
                .Setup(r => r.All())
                .Returns(new List<LocalWatchBuffer> { growingEntry });

            Subject.Execute(new LocalWatchScanCommand());

            Mocker.GetMock<ILocalWatchBufferRepository>().Verify(
                r => r.Insert(It.Is<LocalWatchBuffer>(b => b.Path == smallPath)),
                Times.Never());
            Mocker.GetMock<ILocalWatchBufferRepository>().Verify(
                r => r.Update(It.Is<LocalWatchBuffer>(b => b.Path == growingPath && b.Size == 20L * 1024 * 1024)),
                Times.Once());
            Mocker.GetMock<ILocalWatchBufferRepository>().Verify(
                r => r.Insert(It.Is<LocalWatchBuffer>(b => b.Path == settlingPath)),
                Times.Once());
            Mocker.GetMock<IParsingService>().Verify(
                p => p.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()),
                Times.Never());
        }

        [Test]
        public void should_prune_missing_paths_that_no_longer_exist()
        {
            var missingPath = WatchFile("Deleted.S01E01.mkv");
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetFiles(_watchFolder, true))
                .Returns(Array.Empty<string>());
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FileExists(missingPath))
                .Returns(false);

            Mocker.GetMock<ILocalWatchBufferRepository>()
                .Setup(r => r.All())
                .Returns(new List<LocalWatchBuffer>
                {
                    new LocalWatchBuffer { Id = 42, Path = missingPath, Status = LocalWatchStatus.Unmapped }
                });

            Subject.Execute(new LocalWatchScanCommand());

            Mocker.GetMock<ILocalWatchBufferRepository>().Verify(
                r => r.DeleteMany(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 42 }))),
                Times.Once());
        }

        [Test]
        public void should_mark_existing_entry_imported_on_episode_imported_event()
        {
            var path = WatchFile("Manual.S01E01.mkv");
            var entry = new LocalWatchBuffer
            {
                Id = 7,
                Path = path,
                Status = LocalWatchStatus.Unmapped,
                Failures = 2,
                Reason = "Series not found"
            };

            Mocker.GetMock<ILocalWatchBufferRepository>()
                .Setup(r => r.GetByPath(path))
                .Returns(entry);

            Subject.Handle(new EpisodeImportedEvent(new LocalEpisode { Path = path }, new EpisodeFile { Id = 1 }, new List<DeletedEpisodeFile>(), false, null));

            Mocker.GetMock<ILocalWatchBufferRepository>().Verify(
                r => r.Update(It.Is<LocalWatchBuffer>(b =>
                    b.Id == 7 &&
                    b.Status == LocalWatchStatus.Imported &&
                    b.Failures == 0 &&
                    b.Reason == "Imported")),
                Times.Once());
        }

        [Test]
        public void should_mark_failed_when_get_import_decisions_throws_at_threshold()
        {
            var path = WatchFile("Test.Show.S01E01.mkv");
            SetupSettledFile(path);

            var entry = new LocalWatchBuffer
            {
                Id = 5,
                Path = path,
                Status = LocalWatchStatus.Pending,
                Size = 100L * 1024 * 1024,
                Failures = 2
            };

            Mocker.GetMock<ILocalWatchBufferRepository>()
                .Setup(r => r.All())
                .Returns(new List<LocalWatchBuffer> { entry });

            Mocker.GetMock<IParsingService>()
                .Setup(p => p.Map(It.IsAny<ParsedEpisodeInfo>(), 0, 0, null))
                .Returns(new RemoteEpisode { Series = _series });

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(m => m.GetImportDecisions(It.IsAny<List<string>>(), _series))
                .Throws(new InvalidOperationException("MediaInfo crash"));

            Subject.Execute(new LocalWatchScanCommand());

            Mocker.GetMock<ILocalWatchBufferRepository>().Verify(
                r => r.Update(It.Is<LocalWatchBuffer>(b =>
                    b.Status == LocalWatchStatus.Failed &&
                    b.Failures == 3 &&
                    b.Reason == "MediaInfo crash")),
                Times.Once());
        }
    }
}
