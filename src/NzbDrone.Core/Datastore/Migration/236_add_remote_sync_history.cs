using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(236)]
    public class add_remote_sync_history : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("RemoteSyncHistory")
                  .WithColumn("TvdbId").AsInt32()
                  .WithColumn("Title").AsString()
                  .WithColumn("Action").AsString()
                  .WithColumn("Detail").AsString().Nullable()
                  .WithColumn("SyncedAt").AsDateTime();
        }
    }
}
