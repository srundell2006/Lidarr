using System.Collections.Generic;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Music.Commands
{
    public class UnmonitorSinglesCommand : Command
    {
        public List<int> ArtistIds { get; set; }

        public UnmonitorSinglesCommand()
        {
            ArtistIds = new List<int>();
        }

        public UnmonitorSinglesCommand(List<int> artistIds)
        {
            ArtistIds = artistIds;
        }

        // Only update the scheduled task timestamp when triggered for the whole library
        // (i.e. ArtistIds is empty, meaning run across all artists).
        public override bool UpdateScheduledTask => ArtistIds.Count == 0;

        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => false;

        public override string CompletionMessage => "Finished unmonitoring singles already in albums";
    }
}
