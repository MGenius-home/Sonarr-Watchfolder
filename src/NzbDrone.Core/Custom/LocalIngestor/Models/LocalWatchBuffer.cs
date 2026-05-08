using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Custom.LocalIngestor.Models
{
    public class LocalWatchBuffer : ModelBase
    {
        public string Path { get; set; }
        public LocalWatchStatus Status { get; set; }
        public DateTime LastSeen { get; set; }
    }
}
