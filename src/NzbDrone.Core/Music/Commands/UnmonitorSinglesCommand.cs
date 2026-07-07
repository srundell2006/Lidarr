using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Music.Commands
{
    public class UnmonitorSinglesCommand : Command
    {
        public int ArtistId { get; set; }

        public UnmonitorSinglesCommand()
        {
        }

        public UnmonitorSinglesCommand(int artistId)
        {
            ArtistId = artistId;
        }

        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => false;

        public override string CompletionMessage => "Finished unmonitoring singles already in albums";
    }
}
