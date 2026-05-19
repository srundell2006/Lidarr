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

                // Step 1 – rescan the artist folder so the database reflects what's on disk.
                //
                // FilterFilesType.Known skips files already in the DB whose size and
                // modified-time haven't changed — meaning ReadTags() is only called for
                // new or changed files, not for every file in the collection.  For a large
                // library over SMB this is the single biggest time saving.
                //
                // Matched files that are already correct in the DB will still be picked up
                // by the RenameArtistCommand below, so no information is lost.  Unmatched
                // unchanged files are handled by RecycleUnmappedFiles inside DiskScanService.
                _diskScanService.Scan(
                    folders: new List<string> { artist.Path },
                    filter: FilterFilesType.Known,
                    addNewArtists: false,
                    artistIds: new List<int> { artist.Id });
            }

            // Step 2 – rename all track files for every rebuilt artist in a single command.
            // Pushing one RenameArtistCommand with all IDs is far more efficient than
            // queuing a separate command per artist, which would interleave renames with
            // any other work entering the queue between iterations.
            _commandQueueManager.Push(new RenameArtistCommand
            {
                ArtistIds = artists.Select(x => x.Id).ToList()
            }, trigger: CommandTrigger.Manual);

            _logger.ProgressInfo("Rebuild database completed for {0} artist(s)", artists.Count);
        }
    }
}
