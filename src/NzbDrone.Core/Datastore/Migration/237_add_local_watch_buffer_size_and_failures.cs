using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(237)]
    public class add_local_watch_buffer_size_and_failures : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("LocalWatchBuffer")
                 .AddColumn("Size").AsInt64().Nullable()
                 .AddColumn("Failures").AsInt32().WithDefaultValue(0);
        }
    }
}
