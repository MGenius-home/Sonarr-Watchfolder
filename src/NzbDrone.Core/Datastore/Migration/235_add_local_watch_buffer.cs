using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(235)]
    public class add_local_watch_buffer : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("LocalWatchBuffer")
                  .WithColumn("Path").AsString().Unique()
                  .WithColumn("Status").AsInt32()
                  .WithColumn("LastSeen").AsDateTime();
        }
    }
}
