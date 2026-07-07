using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Music.Commands;

namespace NzbDrone.Core.Music.Services
{
    /// <summary>
    /// Unmonitors singles whose tracks are already present in a full album,
    /// matched by MusicBrainz ForeignRecordingId.
    ///
    /// When ArtistIds is empty (e.g. triggered from the System task list) the
    /// check runs across every artist in the library.  When ArtistIds is
    /// non-empty (e.g. triggered from the Artist detail page) only those
    /// specific artists are processed.
    /// </summary>
    public class UnmonitorSinglesService : IExecute<UnmonitorSinglesCommand>
    {
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly ITrackService _trackService;
        private readonly Logger _logger;

        public UnmonitorSinglesService(IArtistService artistService,
                                       IAlbumService albumService,
                                       ITrackService trackService,
                                       Logger logger)
        {
            _artistService = artistService;
            _albumService = albumService;
            _trackService = trackService;
            _logger = logger;
        }

        public void Execute(UnmonitorSinglesCommand message)
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

            _logger.ProgressInfo("Unmonitoring redundant singles for {0} artist(s)", artists.Count);

            foreach (var artist in artists)
            {
                ProcessArtist(artist);
            }

            _logger.ProgressInfo("Finished unmonitoring redundant singles");
        }

        private void ProcessArtist(Artist artist)
        {
            var allAlbums = _albumService.GetAlbumsByArtist(artist.Id);

            // Monitored singles only — nothing to do if none exist
            var singles = allAlbums
                .Where(a => a.AlbumType == "Single" && a.Monitored)
                .ToList();

            if (!singles.Any())
            {
                _logger.Debug("No monitored singles found for artist {0}", artist.Name);
                return;
            }

            // Full albums (any monitored state) — the reference pool
            var fullAlbums = allAlbums
                .Where(a => a.AlbumType == "Album")
                .ToList();

            if (!fullAlbums.Any())
            {
                _logger.Debug("No full albums found for artist {0}; nothing to unmonitor", artist.Name);
                return;
            }

            // Collect every ForeignRecordingId that appears on a full-album track
            var fullAlbumRecordingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var album in fullAlbums)
            {
                var tracks = _trackService.GetTracksByAlbum(album.Id);
                foreach (var track in tracks)
                {
                    if (track.ForeignRecordingId.IsNotNullOrWhiteSpace())
                    {
                        fullAlbumRecordingIds.Add(track.ForeignRecordingId);
                    }
                }
            }

            // A single is redundant when every one of its tracks is in the full-album pool
            var toUnmonitor = new List<int>();
            foreach (var single in singles)
            {
                var singleTracks = _trackService.GetTracksByAlbum(single.Id);

                if (!singleTracks.Any())
                {
                    // No tracks indexed yet — skip; we can't make a reliable decision
                    continue;
                }

                var allTracksPresent = singleTracks.All(t =>
                    t.ForeignRecordingId.IsNotNullOrWhiteSpace() &&
                    fullAlbumRecordingIds.Contains(t.ForeignRecordingId));

                if (allTracksPresent)
                {
                    _logger.Debug("Single '{0}' (id {1}) is fully covered by a full album — will unmonitor", single.Title, single.Id);
                    toUnmonitor.Add(single.Id);
                }
            }

            if (toUnmonitor.Any())
            {
                _albumService.SetMonitored(toUnmonitor, false);
                _logger.Info("Unmonitored {0} single(s) for artist '{1}' that are already present in full albums", toUnmonitor.Count, artist.Name);
            }
            else
            {
                _logger.Debug("No redundant singles found for artist '{0}'", artist.Name);
            }
        }
    }
}
