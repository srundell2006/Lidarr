using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.Commands
{
    public class ScanAlbumCommand : Command
    {
        public int AlbumId { get; set; }

        public ScanAlbumCommand()
        {
        }

        public ScanAlbumCommand(int albumId)
        {
            AlbumId = albumId;
        }

        public override string CompletionMessage => "Completed";
        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
    }
}
