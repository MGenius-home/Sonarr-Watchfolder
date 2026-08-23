using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Custom.LocalIngestor.Models;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Custom.LocalIngestor
{
    public class LocalFolderWatcherService : IExecute<LocalWatchScanCommand>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly ILocalWatchBufferRepository _repository;
        private readonly IParsingService _parsingService;
        private readonly ISeriesService _seriesService;
        private readonly IEpisodeService _episodeService;
        private readonly IMakeImportDecision _makeImportDecision;
        private readonly IImportApprovedEpisodes _importApprovedEpisodes;
        private readonly Logger _logger;

        public LocalFolderWatcherService(IDiskProvider diskProvider,
                                         ILocalWatchBufferRepository repository,
                                         IParsingService parsingService,
                                         ISeriesService seriesService,
                                         IEpisodeService episodeService,
                                         IMakeImportDecision makeImportDecision,
                                         IImportApprovedEpisodes importApprovedEpisodes,
                                         Logger logger)
        {
            _diskProvider = diskProvider;
            _repository = repository;
            _parsingService = parsingService;
            _seriesService = seriesService;
            _episodeService = episodeService;
            _makeImportDecision = makeImportDecision;
            _importApprovedEpisodes = importApprovedEpisodes;
            _logger = logger;
        }

        public void Execute(LocalWatchScanCommand message)
        {
            var watchFolder = "/watch";
            _logger.Info("Starting local watch folder scan: {0}", watchFolder);

            if (!_diskProvider.FolderExists(watchFolder))
            {
                _logger.Warn("Watch folder {0} does not exist", watchFolder);
                return;
            }

            var videoExtensions = new[] { ".mkv", ".mp4", ".avi", ".ts" };
            var files = _diskProvider.GetFiles(watchFolder, true)
                        .Where(f => videoExtensions.Contains(Path.GetExtension(f).ToLower()))
                        .ToList();

            _logger.Info("Found {0} video files to process", files.Count);

            var processedCount = 0;
            foreach (var file in files)
            {
                ProcessFile(file);
                processedCount++;
            }

            _logger.Info("Local watch folder scan completed. Processed: {0}/{1}", processedCount, files.Count);
        }

        private void ProcessFile(string path)
        {
            try
            {
                var fileInfo = new FileInfo(path);

                if (DateTime.UtcNow - fileInfo.LastWriteTimeUtc < TimeSpan.FromSeconds(60))
                {
                    _logger.Debug("File {0} is still settling", path);
                    return;
                }

                var bufferEntry = _repository.GetByPath(path);

                if (bufferEntry != null && (bufferEntry.Status == LocalWatchStatus.Imported || bufferEntry.Status == LocalWatchStatus.Ignored))
                {
                    _logger.Debug("File {0} skipped: already processed with status {1}", path, bufferEntry.Status);
                    return;
                }
                if (bufferEntry == null)
                {
                    bufferEntry = new LocalWatchBuffer
                    {
                        Path = path,
                        Status = LocalWatchStatus.Pending,
                        LastSeen = DateTime.UtcNow
                    };
                    _repository.Insert(bufferEntry);
                }

                var parsedEpisodeInfo = Parser.Parser.ParsePath(path);

                if (parsedEpisodeInfo == null || string.IsNullOrWhiteSpace(parsedEpisodeInfo.SeriesTitle))
                {
                    SetStatus(bufferEntry, LocalWatchStatus.Unmapped, path, "Could not parse series title from path");
                    return;
                }

                var series = _parsingService.GetSeries(parsedEpisodeInfo.SeriesTitle);

                if (series == null)
                {
                    SetStatus(bufferEntry, LocalWatchStatus.Unmapped, path, string.Format("Series '{0}' not found in library", parsedEpisodeInfo.SeriesTitle));
                    return;
                }

                var decisions = _makeImportDecision.GetImportDecisions(new List<string> { path }, series);
                var decision = decisions.FirstOrDefault();

                if (decision != null && decision.Approved)
                {
                    var hasExisting = false;

                    foreach (var ep in decision.LocalEpisode.Episodes)
                    {
                        if (ep.EpisodeFileId > 0)
                        {
                            hasExisting = true;
                            break;
                        }
                    }

                    if (hasExisting)
                    {
                        SetStatus(bufferEntry, LocalWatchStatus.Ignored, path, "Episode already has a file in the library");
                        return;
                    }

                    _logger.Info("Importing approved file: {0}", path);
                    _importApprovedEpisodes.Import(new List<ImportDecision> { decision }, true, null, ImportMode.Copy);
                    SetStatus(bufferEntry, LocalWatchStatus.Imported, path, "Successfully imported");
                }
                else
                {
                    var rejections = decision == null ? "No decision could be made" : string.Join(", ", decision.Rejections.Select(r => r.Reason));
                    SetStatus(bufferEntry, LocalWatchStatus.Unmapped, path, rejections);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing local watch file: {0}", path);
            }
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
    }
}
