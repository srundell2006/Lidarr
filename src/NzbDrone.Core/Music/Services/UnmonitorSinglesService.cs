using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Music.Commands;

namespace NzbDrone.Core.Music.Services
{
    /// <summary>
    /// Unmonitors singles for an artist whose tracks are already present in a full album
    /// by matching on MusicBrainz ForeignRecordingId.  Only monitored singles are
    /// considered; a single is unmonitored only when every one of its tracks appears
    /// in at least one full album track for the same artist.
    /// </summary>
    public class UnmonitorSinglesService : IExecute<UnmonitorSinglesCommand>
    {
        private readonly IAlbumService _albumService;
        private readonly ITrackService _trackService;
        private readonly Logger _logger;

        public UnmonitorSinglesService(IAlbumService albumService,
                                       ITrackService trackService,
                                       Logger logger)
        {
            _albumService = albumService;
            _trackService = trackService;
            _logger = logger;
        }

        public void Execute(UnmonitorSinglesCommand message)
        {
            var allAlbums = _albumService.GetAlbumsByArtist(message.ArtistId);

            // Monitored singles only — nothing to do if none exist
            var singles = allAlbums
                .Where(a => a.AlbumType == "Single" && a.Monitored)
                .ToList();

            if (!singles.Any())
            {
                _logger.Debug("No monitored singles found for artist {0}", message.ArtistId);
                return;
            }

            // Full albums (any monitored state) — the reference pool
            var fullAlbums = allAlbums
                .Where(a => a.AlbumType == "Album")
                .ToList();

            if (!fullAlbums.Any())
            {
                _logger.Debug("No full albums found for artist {0}; nothing to unmonitor", message.ArtistId);
                return;
            }

            // Collect every ForeignRecordingId that appears on a full-album track
            var fullAlbumRecordingIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
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
                _logger.Info("Unmonitored {0} single(s) for artist {1} that are already present in full albums", toUnmonitor.Count, message.ArtistId);
            }
            else
            {
                _logger.Info("No redundant singles found for artist {0}", message.ArtistId);
            }
        }
    }
}
