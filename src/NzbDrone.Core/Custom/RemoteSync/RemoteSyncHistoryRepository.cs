using NzbDrone.Core.Custom.RemoteSync.Models;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Custom.RemoteSync
{
    public interface IRemoteSyncHistoryRepository : IBasicRepository<RemoteSyncHistory>
    {
    }

    public class RemoteSyncHistoryRepository : BasicRepository<RemoteSyncHistory>, IRemoteSyncHistoryRepository
    {
        public RemoteSyncHistoryRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }
}
