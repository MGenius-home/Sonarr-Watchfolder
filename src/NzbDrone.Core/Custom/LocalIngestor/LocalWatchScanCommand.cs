using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Custom.LocalIngestor
{
    public class LocalWatchScanCommand : Command
    {
        public override bool SendUpdatesToClient => true;
    }
}
