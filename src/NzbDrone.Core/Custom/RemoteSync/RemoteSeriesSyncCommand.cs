using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Custom.RemoteSync
{
    public class RemoteSeriesSyncCommand : Command
    {
        public override bool SendUpdatesToClient => true;
    }
}
