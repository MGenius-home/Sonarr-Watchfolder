using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Custom.RemoteSync.Models
{
    public class RemoteSyncHistory : ModelBase
    {
        public int TvdbId { get; set; }
        public string Title { get; set; }
        public string Action { get; set; }
        public string Detail { get; set; }
        public DateTime SyncedAt { get; set; }
    }
}
