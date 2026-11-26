using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(900001)]
    public class add_local_watch_buffer_reason : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("LocalWatchBuffer")
                 .AddColumn("Reason").AsString().Nullable();
        }
    }
}
