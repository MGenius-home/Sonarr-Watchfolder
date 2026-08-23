using System;
using NzbDrone.Core.Custom.RemoteSync.Models;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Custom.RemoteSync
{
    public interface IRemoteSyncHistoryRepository : IBasicRepository<RemoteSyncHistory>
    {
        void DeleteOlderThan(DateTime cutoff);
    }

    public class RemoteSyncHistoryRepository : BasicRepository<RemoteSyncHistory>, IRemoteSyncHistoryRepository
    {
        public RemoteSyncHistoryRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public void DeleteOlderThan(DateTime cutoff)
        {
            Delete(h => h.SyncedAt < cutoff);
        }
    }
}
