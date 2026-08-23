namespace NzbDrone.Core.Custom.LocalIngestor.Models
{
    public enum LocalWatchStatus
    {
        Pending = 0,
        Unmapped = 1,
        Imported = 2,
        Ignored = 3,
        Failed = 4
    }
}
