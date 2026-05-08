using System.Linq;
using NzbDrone.Core.Custom.LocalIngestor.Models;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
namespace NzbDrone.Core.Custom.LocalIngestor
{
    public interface ILocalWatchBufferRepository : IBasicRepository<LocalWatchBuffer>
    {
        LocalWatchBuffer GetByPath(string path);
    }

    public class LocalWatchBufferRepository : BasicRepository<LocalWatchBuffer>, ILocalWatchBufferRepository
    {
        public LocalWatchBufferRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public LocalWatchBuffer GetByPath(string path)
        {
            return Query(c => c.Path == path).SingleOrDefault();
        }
    }
}
