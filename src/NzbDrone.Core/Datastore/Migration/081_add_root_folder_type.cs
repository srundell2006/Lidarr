using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(81)]
    public class add_root_folder_type : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("RootFolders")
                 .AddColumn("FolderType").AsInt32().Nullable();

            Execute.Sql("UPDATE RootFolders SET FolderType = 0 WHERE FolderType IS NULL");
        }
    }
}