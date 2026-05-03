using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.Commands
{
    /// <summary>
    /// Triggers a scheduled scan of every configured Import Folder.
    /// Each top-level subdirectory is processed independently: matched tracks are
    /// moved into the library and anything that cannot be imported is sent to the
    /// application Recycle Bin.
    /// </summary>
    public class ImportFolderSyncCommand : Command
    {
        public override bool RequiresDiskAccess => true;
        public override bool IsLongRunning => true;
    }
}
