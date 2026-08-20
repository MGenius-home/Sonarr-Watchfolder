using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using NLog;
using NzbDrone.Core.Custom.RemoteSync.Models;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Custom.RemoteSync
{
    public class RemoteSeriesSyncService : IExecute<RemoteSeriesSyncCommand>
    {
        private const int LookbackDays = 7;

        private readonly IRemoteSonarrClient _remoteClient;
        private readonly ISeriesService _seriesService;
        private readonly IAddSeriesService _addSeriesService;
        private readonly IRemoteSyncHistoryRepository _historyRepository;
        private readonly Logger _logger;

        public RemoteSeriesSyncService(IRemoteSonarrClient remoteClient,
                                      ISeriesService seriesService,
                                      IAddSeriesService addSeriesService,
                                      IRemoteSyncHistoryRepository historyRepository,
                                      Logger logger)
        {
            _remoteClient = remoteClient;
            _seriesService = seriesService;
            _addSeriesService = addSeriesService;
            _historyRepository = historyRepository;
            _logger = logger;
        }

        public void Execute(RemoteSeriesSyncCommand message)
        {
            _logger.Info("Starting remote series sync (lookback {0} days)", LookbackDays);

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

            var cutoff = DateTime.UtcNow.AddDays(-LookbackDays);
            var recent = remoteSeries.Where(r => r.Added >= cutoff).ToList();

            _logger.Info("Remote series sync: upstream returned {0} series, {1} added within last {2} days",
                remoteSeries.Count, recent.Count, LookbackDays);

            var rootFolder = Environment.GetEnvironmentVariable("SONARR_WATCH_ROOT_FOLDER") ?? "/tv";

            var added = 0;
            var skipped = 0;
            var failed = 0;

            foreach (var remote in recent)
            {
                try
                {
                    var existing = _seriesService.FindByTvdbId(remote.TvdbId);
                    if (existing != null)
                    {
                        skipped++;
                        RecordHistory(remote.TvdbId, remote.Title, "Skipped", "Already present locally");
                        continue;
                    }

                    var newSeries = new Series
                    {
                        TvdbId = remote.TvdbId,
                        TitleSlug = remote.TitleSlug,
                        Title = remote.Title,
                        QualityProfileId = remote.QualityProfileId,
                        Monitored = remote.Monitored,
                        RootFolderPath = rootFolder,
                        AddOptions = new AddSeriesOptions
                        {
                            SearchForMissingEpisodes = false,
                            Monitor = remote.Monitored ? MonitorTypes.All : MonitorTypes.None
                        }
                    };

                    _addSeriesService.AddSeries(newSeries);
                    added++;
                    RecordHistory(remote.TvdbId, remote.Title, "Added", $"Profile={remote.QualityProfileId}");
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
