using System;

namespace NzbDrone.Core.Custom.RemoteSync
{
    public class RemoteSeries
    {
        public int TvdbId { get; set; }
        public string TitleSlug { get; set; }
        public string Title { get; set; }
        public int QualityProfileId { get; set; }
        public bool Monitored { get; set; }
        public DateTime Added { get; set; }
        public string RootFolderPath { get; set; }
    }
}
