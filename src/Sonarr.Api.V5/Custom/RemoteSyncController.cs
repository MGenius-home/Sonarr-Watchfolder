using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Custom.RemoteSync;
using NzbDrone.Core.Custom.RemoteSync.Models;
using Sonarr.Http;

namespace Sonarr.Api.V5.Custom
{
    [V5ApiController("remotesync")]
    public class RemoteSyncController : Controller
    {
        private readonly IRemoteSyncHistoryRepository _repository;

        public RemoteSyncController(IRemoteSyncHistoryRepository repository)
        {
            _repository = repository;
        }

        [HttpGet]
        public List<RemoteSyncHistory> GetHistory()
        {
            return _repository.All()
                              .OrderByDescending(h => h.SyncedAt)
                              .Take(100)
                              .ToList();
        }
    }
}
