using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using NLog;
using NzbDrone.Core.Custom.RemoteSync.Models;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Custom.RemoteSync
{
    public class RemoteSeriesSyncService : IExecute<RemoteSeriesSyncCommand>
    {
        private const int DefaultLookbackDays = 7;
        private const int HistoryRetentionDays = 30;

        private readonly IRemoteSonarrClient _remoteClient;
        private readonly ISeriesService _seriesService;
        private readonly IAddSeriesService _addSeriesService;
        private readonly IQualityProfileService _qualityProfileService;
        private readonly IRemoteSyncHistoryRepository _historyRepository;
        private readonly Logger _logger;

        public RemoteSeriesSyncService(IRemoteSonarrClient remoteClient,
                                      ISeriesService seriesService,
                                      IAddSeriesService addSeriesService,
                                      IQualityProfileService qualityProfileService,
                                      IRemoteSyncHistoryRepository historyRepository,
                                      Logger logger)
        {
            _remoteClient = remoteClient;
            _seriesService = seriesService;
            _addSeriesService = addSeriesService;
            _qualityProfileService = qualityProfileService;
            _historyRepository = historyRepository;
            _logger = logger;
        }

        private static int LookbackDays =>
            int.TryParse(Environment.GetEnvironmentVariable("SONARR_SYNC_LOOKBACK_DAYS"), out var days) && days >= 0
                ? days
                : DefaultLookbackDays;

        public void Execute(RemoteSeriesSyncCommand message)
        {
            var lookbackDays = LookbackDays;
            _logger.Info("Starting remote series sync (lookback {0} days)", lookbackDays);

            List<RemoteSeries> remoteSeries;
            try
            {
                remoteSeries = _remoteClient.GetSeries();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Remote series sync: failed to fetch series from upstream Sonarr");
                return;
            }

            List<RemoteSeries> toSync;
            if (lookbackDays == 0)
            {
                toSync = remoteSeries;
                _logger.Info("Remote series sync: upstream returned {0} series (full sync, lookback disabled)", remoteSeries.Count);
            }
            else
            {
                var cutoff = DateTime.UtcNow.AddDays(-lookbackDays);
                toSync = remoteSeries.Where(r => r.Added >= cutoff).ToList();
                _logger.Info(
                    "Remote series sync: upstream returned {0} series, {1} added within last {2} days",
                    remoteSeries.Count,
                    toSync.Count,
                    lookbackDays);
            }

            var rootFolder = Environment.GetEnvironmentVariable("SONARR_WATCH_ROOT_FOLDER") ?? "/tv";

            var added = 0;
            var skipped = 0;
            var failed = 0;

            foreach (var remote in toSync)
            {
                try
                {
                    var existing = _seriesService.FindByTvdbId(remote.TvdbId);
                    if (existing != null)
                    {
                        // Already present: count only. Recording a row per run flooded
                        // both the log and the RemoteSyncHistory table.
                        skipped++;
                        _logger.Debug("Remote series sync: skipping '{0}' (tvdbId={1}), already present", remote.Title, remote.TvdbId);
                        continue;
                    }

                    var qualityProfileId = ResolveQualityProfileId(remote);

                    var newSeries = new Series
                    {
                        TvdbId = remote.TvdbId,
                        TitleSlug = remote.TitleSlug,
                        Title = remote.Title,
                        QualityProfileId = qualityProfileId,
                        Monitored = remote.Monitored,
                        SeasonFolder = remote.SeasonFolder,
                        SeriesType = remote.SeriesType,
                        RootFolderPath = rootFolder,
                        AddOptions = new AddSeriesOptions
                        {
                            SearchForMissingEpisodes = false,
                            Monitor = remote.Monitored ? MonitorTypes.All : MonitorTypes.None
                        }
                    };

                    _addSeriesService.AddSeries(newSeries);
                    added++;
                    RecordHistory(remote.TvdbId, remote.Title, "Added", $"Profile={qualityProfileId}");
                    _logger.Info("Remote series sync: added '{0}' (tvdbId={1})", remote.Title, remote.TvdbId);
                }
                catch (ValidationException vex)
                {
                    failed++;
                    var msg = string.Join("; ", vex.Errors.Select(e => e.ErrorMessage));
                    RecordHistory(remote.TvdbId, remote.Title, "Failed", msg);
                    _logger.Warn("Remote series sync: validation failed for '{0}' (tvdbId={1}): {2}", remote.Title, remote.TvdbId, msg);
                }
                catch (Exception ex)
                {
                    failed++;
                    RecordHistory(remote.TvdbId, remote.Title, "Failed", ex.Message);
                    _logger.Error(ex, "Remote series sync: failed to add '{0}' (tvdbId={1})", remote.Title, remote.TvdbId);
                }
            }

            _logger.Info("Remote series sync completed: added={0}, skipped={1}, failed={2}", added, skipped, failed);

            try
            {
                _historyRepository.DeleteOlderThan(DateTime.UtcNow.AddDays(-HistoryRetentionDays));
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Remote series sync: failed to prune history older than {0} days", HistoryRetentionDays);
            }
        }

        private int ResolveQualityProfileId(RemoteSeries remote)
        {
            if (int.TryParse(Environment.GetEnvironmentVariable("SONARR_WATCH_QUALITY_PROFILE_ID"), out var envProfileId) &&
                _qualityProfileService.Exists(envProfileId))
            {
                return envProfileId;
            }

            if (remote.QualityProfileId > 0 && _qualityProfileService.Exists(remote.QualityProfileId))
            {
                return remote.QualityProfileId;
            }

            var fallbackId = _qualityProfileService.All().FirstOrDefault()?.Id ?? remote.QualityProfileId;
            _logger.Warn(
                "Remote series sync: quality profile {0} for '{1}' does not exist locally; falling back to profile {2}",
                remote.QualityProfileId,
                remote.Title,
                fallbackId);
            return fallbackId;
        }

        private void RecordHistory(int tvdbId, string title, string action, string detail)
        {
            _historyRepository.Insert(new RemoteSyncHistory
            {
                TvdbId = tvdbId,
                Title = title,
                Action = action,
                Detail = detail,
                SyncedAt = DateTime.UtcNow
            });
        }
    }
}
