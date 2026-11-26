using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Custom.RemoteSync;
using NzbDrone.Core.Custom.RemoteSync.Models;
using NzbDrone.Core.Messaging.Commands;
using Sonarr.Http;

namespace Sonarr.Api.V5.Custom
{
    [V5ApiController("remotesync")]
    public class RemoteSyncController : Controller
    {
        private readonly IRemoteSyncHistoryRepository _repository;
        private readonly IManageCommandQueue _commandQueueManager;

        public RemoteSyncController(IRemoteSyncHistoryRepository repository,
                                    IManageCommandQueue commandQueueManager)
        {
            _repository = repository;
            _commandQueueManager = commandQueueManager;
        }

        [HttpGet]
        public List<RemoteSyncHistory> GetHistory()
        {
            return _repository.All()
                              .OrderByDescending(h => h.SyncedAt)
                              .Take(100)
                              .ToList();
        }

        [HttpPost("sync")]
        public IActionResult TriggerSync()
        {
            _commandQueueManager.Push(new RemoteSeriesSyncCommand());
            return Accepted(new { queued = true });
        }
    }
}
