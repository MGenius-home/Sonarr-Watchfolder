using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Custom.LocalIngestor.Models;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Custom.LocalIngestor
{
    public class LocalFolderWatcherService : IExecute<LocalWatchScanCommand>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly ILocalWatchBufferRepository _repository;
        private readonly IParsingService _parsingService;
        private readonly IMakeImportDecision _makeImportDecision;
        private readonly IImportApprovedEpisodes _importApprovedEpisodes;
        private readonly Logger _logger;

        private sealed class PendingFile
        {
            public string Path { get; set; }
            public LocalWatchBuffer Entry { get; set; }
            public Series Series { get; set; }
        }

        public LocalFolderWatcherService(IDiskProvider diskProvider,
                                         ILocalWatchBufferRepository repository,
                                         IParsingService parsingService,
                                         IMakeImportDecision makeImportDecision,
                                         IImportApprovedEpisodes importApprovedEpisodes,
                                         Logger logger)
        {
            _diskProvider = diskProvider;
            _repository = repository;
            _parsingService = parsingService;
            _makeImportDecision = makeImportDecision;
            _importApprovedEpisodes = importApprovedEpisodes;
            _logger = logger;
        }

        private static string Env(string name) => Environment.GetEnvironmentVariable(name);

        private static TimeSpan SettleAge =>
            TimeSpan.FromSeconds(int.TryParse(Env("SONARR_WATCH_SETTLE_SECONDS"), out var seconds) && seconds >= 0
                ? seconds
                : WatchFolderRules.DefaultSettleSeconds);

        private static long MinSizeBytes =>
            long.TryParse(Env("SONARR_WATCH_MIN_SIZE_MB"), out var mb) && mb >= 0
                ? mb * 1024 * 1024
                : (long)WatchFolderRules.DefaultMinSizeMb * 1024 * 1024;

        private static int FailureThreshold =>
            int.TryParse(Env("SONARR_WATCH_FAILURE_THRESHOLD"), out var threshold) && threshold > 0
                ? threshold
                : WatchFolderRules.DefaultFailureThreshold;

        public void Execute(LocalWatchScanCommand message)
        {
            var watchFolder = Env("SONARR_WATCH_FOLDER") ?? "/watch";
            _logger.Info("Starting local watch folder scan: {0}", watchFolder);

            if (!_diskProvider.FolderExists(watchFolder))
            {
                _logger.Warn("Watch folder {0} does not exist", watchFolder);
                return;
            }

            var files = _diskProvider.GetFiles(watchFolder, true)
                        .Where(WatchFolderRules.IsVideoFile)
                        .ToList();

            var fileSet = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            PruneMissingPaths(fileSet);

            _logger.Info("Found {0} video files to process", files.Count);

            var now = DateTime.UtcNow;
            var pending = new List<PendingFile>();

            foreach (var file in files)
            {
                try
                {
                    var item = Prepare(file, now);
                    if (item != null)
                    {
                        pending.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error preparing local watch file: {0}", file);
                }
            }

            foreach (var group in pending.GroupBy(p => p.Series.Id))
            {
                ImportGroup(group.First().Series, group.ToList());
            }

            _logger.Info("Local watch folder scan completed. Processed: {0}/{1}", pending.Count, files.Count);
        }

        /// <summary>Applies the cheap gates for one file. Returns null when nothing more should happen this scan.</summary>
        private PendingFile Prepare(string path, DateTime now)
        {
            var entry = _repository.GetByPath(path);

            if (entry != null && entry.Status != LocalWatchStatus.Pending && entry.Status != LocalWatchStatus.Unmapped)
            {
                _logger.Debug("File {0} skipped: already handled with status {1}", path, entry.Status);
                return null;
            }

            if (_diskProvider.IsFileLocked(path))
            {
                _logger.Debug("File {0} is locked by another process", path);
                return null;
            }

            var size = _diskProvider.GetFileSize(path);

            if (entry == null)
            {
                // Don't create buffer rows for files too small to be episodes
                if (!WatchFolderRules.MeetsMinSize(size, MinSizeBytes))
                {
                    _logger.Debug("File {0} ignored: below minimum size ({1} bytes)", path, size);
                    return null;
                }

                entry = new LocalWatchBuffer
                {
                    Path = path,
                    Status = LocalWatchStatus.Pending,
                    LastSeen = now,
                    Size = size
                };
                _repository.Insert(entry);
            }
            else
            {
                if (WatchFolderRules.HasGrown(entry.Size, size))
                {
                    // Size changed since last scan: still copying, wait for it to stabilise
                    entry.Size = size;
                    entry.LastSeen = now;
                    _repository.Update(entry);
                    _logger.Debug("File {0} is still growing ({1} bytes)", path, size);
                    return null;
                }

                entry.Size = size;
                entry.LastSeen = now;
            }

            var lastWrite = _diskProvider.FileGetLastWrite(path);
            if (!WatchFolderRules.IsSettled(now, lastWrite, SettleAge))
            {
                _logger.Debug("File {0} is still settling", path);
                _repository.Update(entry);
                return null;
            }

            if (entry.Status == LocalWatchStatus.Pending || entry.Status == LocalWatchStatus.Unmapped)
            {
                _repository.Update(entry);
            }

            var parsedEpisodeInfo = Parser.Parser.ParsePath(path);

            if (parsedEpisodeInfo == null || string.IsNullOrWhiteSpace(parsedEpisodeInfo.SeriesTitle))
            {
                SetStatus(entry, LocalWatchStatus.Unmapped, path, "Could not parse series title from path");
                return null;
            }

            var series = _parsingService.GetSeries(parsedEpisodeInfo.SeriesTitle);

            if (series == null)
            {
                SetStatus(entry, LocalWatchStatus.Unmapped, path, string.Format("Series '{0}' not found in library", parsedEpisodeInfo.SeriesTitle));
                return null;
            }

            return new PendingFile { Path = path, Entry = entry, Series = series };
        }

        private void ImportGroup(Series series, List<PendingFile> items)
        {
            var paths = items.Select(i => i.Path).ToList();
            List<ImportDecision> decisions;

            try
            {
                decisions = _makeImportDecision.GetImportDecisions(paths, series);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error building import decisions for series '{0}'", series.Title);
                foreach (var item in items)
                {
                    MarkFailure(item.Entry, item.Path, ex);
                }

                return;
            }

            var byPath = new Dictionary<string, ImportDecision>(StringComparer.OrdinalIgnoreCase);
            foreach (var decision in decisions)
            {
                if (decision.LocalEpisode?.Path != null)
                {
                    byPath[decision.LocalEpisode.Path] = decision;
                }
            }

            var importMode = WatchFolderRules.ParseImportMode(Env("SONARR_IMPORT_MODE"));
            var toImport = new List<PendingFile>();

            foreach (var item in items)
            {
                if (!byPath.TryGetValue(item.Path, out var decision) || decision == null)
                {
                    SetStatus(item.Entry, LocalWatchStatus.Unmapped, item.Path, "No decision could be made");
                    continue;
                }

                if (!decision.Approved)
                {
                    var rejections = string.Join(", ", decision.Rejections.Select(r => r.Reason));
                    SetStatus(item.Entry, LocalWatchStatus.Unmapped, item.Path, rejections);
                    continue;
                }

                if (decision.LocalEpisode.Episodes.Any(ep => ep.EpisodeFileId > 0))
                {
                    SetStatus(item.Entry, LocalWatchStatus.Ignored, item.Path, "Episode already has a file in the library");
                    continue;
                }

                toImport.Add(item);
            }

            if (toImport.Count == 0)
            {
                return;
            }

            try
            {
                foreach (var item in toImport)
                {
                    _logger.Info("Importing approved file: {0}", item.Path);
                }

                _importApprovedEpisodes.Import(toImport.Select(i => byPath[i.Path]).ToList(), true, null, importMode);

                foreach (var item in toImport)
                {
                    item.Entry.Failures = 0;
                    SetStatus(item.Entry, LocalWatchStatus.Imported, item.Path, "Successfully imported");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error importing files for series '{0}'", series.Title);
                foreach (var item in toImport)
                {
                    MarkFailure(item.Entry, item.Path, ex);
                }
            }
        }

        /// <summary>Records a processing failure with escalating log noise and a hard stop after FailureThreshold attempts.</summary>
        private void MarkFailure(LocalWatchBuffer entry, string path, Exception ex)
        {
            entry.Failures++;

            if (entry.Failures >= FailureThreshold)
            {
                entry.Status = LocalWatchStatus.Failed;
                _repository.Update(entry);
                _logger.Error(ex, "File {0}: failed {1} times, giving up (status Failed)", path, entry.Failures);
                return;
            }

            if (entry.Failures == 1)
            {
                _logger.Warn(ex, "File {0}: first failure, will retry next scan", path);
            }
            else
            {
                _logger.Debug("File {0}: failure {1} of {2}: {3}", path, entry.Failures, FailureThreshold, ex.Message);
            }

            _repository.Update(entry);
        }

        private void SetStatus(LocalWatchBuffer entry, LocalWatchStatus newStatus, string path, string reason)
        {
            var changed = entry.Status != newStatus;
            entry.Status = newStatus;
            _repository.Update(entry);

            if (changed)
            {
                _logger.Info("File {0}: {1} -> {2}", path, reason, newStatus);
            }
            else
            {
                // Same decision as previous scans: log quietly to avoid flooding the log
                _logger.Debug("File {0} remains {1}: {2}", path, newStatus, reason);
            }
        }

        /// <summary>Removes buffer rows whose source file no longer exists on disk.</summary>
        private void PruneMissingPaths(HashSet<string> currentFiles)
        {
            var staleIds = _repository.All()
                .Where(row => !currentFiles.Contains(row.Path) && !_diskProvider.FileExists(row.Path))
                .Select(row => row.Id)
                .ToList();

            if (staleIds.Count > 0)
            {
                _repository.DeleteMany(staleIds);
                _logger.Info("Pruned {0} stale watch folder entries", staleIds.Count);
            }
        }
    }
}
