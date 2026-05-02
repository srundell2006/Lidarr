using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(82)]
    public class add_automatically_import_to_import_folder : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("RootFolders")
                 .AddColumn("AutomaticallyImport").AsBoolean().NotNullable().WithDefaultValue(false);
        }
    }
}
