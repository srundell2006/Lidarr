using System.Collections.Generic;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Music.Commands
{
    public class RebuildArtistCommand : Command
    {
        public List<int> ArtistIds { get; set; }

        public RebuildArtistCommand()
        {
            ArtistIds = new List<int>();
        }

        public RebuildArtistCommand(List<int> artistIds)
        {
            ArtistIds = artistIds;
        }

        // Only update the scheduled task timestamp when run from the task scheduler
        // (i.e. ArtistIds is empty, meaning whole-library run).
        public override bool UpdateScheduledTask => ArtistIds.Count == 0;

        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
        public override bool IsLongRunning => true;

        public override string CompletionMessage => "Rebuild database completed";
    }
}
