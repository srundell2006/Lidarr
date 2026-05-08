using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Music.Commands;

namespace NzbDrone.Core.Music.Services
{
    /// <summary>
    /// Executes a full "Rebuild Database" cycle for one or more artists:
    ///   1. Rescan the artist's folder on disk (picks up any new/removed files)
    ///   2. Rename all track files to match the current Lidarr naming convention
    ///
    /// When ArtistIds is empty the whole library is processed.
    /// This command is manual-only and never runs automatically.
    /// </summary>
    public class RebuildArtistService : IExecute<RebuildArtistCommand>
    {
        private readonly IArtistService _artistService;
        private readonly IDiskScanService _diskScanService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public RebuildArtistService(IArtistService artistService,
                                    IDiskScanService diskScanService,
                                    IManageCommandQueue commandQueueManager,
                                    Logger logger)
        {
            _artistService = artistService;
            _diskScanService = diskScanService;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public void Execute(RebuildArtistCommand message)
        {
            List<Artist> artists;

            if (message.ArtistIds.Any())
            {
                artists = _artistService.GetArtists(message.ArtistIds);
            }
            else
            {
                artists = _artistService.GetAllArtists();
            }

            _logger.ProgressInfo("Rebuilding database for {0} artist(s)", artists.Count);

            var artistIndex = 1;

            foreach (var artist in artists)
            {
                _logger.ProgressInfo("Rebuilding {0} ({1}/{2})", artist.Name, artistIndex++, artists.Count);

                // Step 1 – rescan the artist folder so the database reflects what's on disk
                _diskScanService.Scan(
                    folders: new List<string> { artist.Path },
                    filter: FilterFilesType.None,
                    addNewArtists: false,
                    artistIds: new List<int> { artist.Id });

                // Step 2 – rename all track files to match the current naming convention.
                // We push a RenameArtistCommand into the command queue for this artist
                // so the full rename pipeline (move + DB update + events) runs correctly.
                _commandQueueManager.Push(new RenameArtistCommand
                {
                    ArtistIds = new List<int> { artist.Id }
                }, trigger: CommandTrigger.Manual);
            }

            _logger.ProgressInfo("Rebuild database completed for {0} artist(s)", artists.Count);
        }
    }
}
