using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Core.MediaFiles.TrackImport.Identification;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.Parser.Model
{
    public class LocalAlbumRelease
    {
        public LocalAlbumRelease()
        {
            LocalTracks = new List<LocalTrack>();

            // A dummy distance, will be replaced
            Distance = new Distance();
            Distance.Add("album_id", 1.0);
        }

        public LocalAlbumRelease(List<LocalTrack> tracks)
        {
            LocalTracks = tracks;

            // A dummy distance, will be replaced
            Distance = new Distance();
            Distance.Add("album_id", 1.0);
        }

        public List<LocalTrack> LocalTracks { get; set; }
        public int TrackCount => LocalTracks.Count;

        public TrackMapping TrackMapping { get; set; }
        public Distance Distance { get; set; }
        public AlbumRelease AlbumRelease { get; set; }
        public List<LocalTrack> ExistingTracks { get; set; }
        public bool NewDownload { get; set; }

        public void PopulateMatch()
        {
            if (AlbumRelease != null)
            {
                LocalTracks = LocalTracks.Concat(ExistingTracks).DistinctBy(x => x.Path).ToList();

                var existingPaths = new HashSet<string>(
                    ExistingTracks.Select(e => e.Path),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var localTrack in LocalTracks)
                {
                    localTrack.Release = AlbumRelease;
                    localTrack.Album = AlbumRelease.Album.Value;
                    localTrack.Artist = localTrack.Album.Artist.Value;

                    if (TrackMapping.Mapping.ContainsKey(localTrack))
                    {
                        var track = TrackMapping.Mapping[localTrack].Item1;
                        localTrack.Tracks = new List<Track> { track };
                        localTrack.Distance = TrackMapping.Mapping[localTrack].Item2;
                    }
                }

                // Secondary pass: import files that lost the 1:1 Munkres assignment to a
                // library copy (ExistingFile) end up with Tracks = [].  Re-link them to
                // the corresponding DB Track by absolute track number so that
                // HasExistingFiles is set correctly in the Music Import UI.
                if (AlbumRelease.Tracks.IsLoaded)
                {
                    var tracksWithFiles = AlbumRelease.Tracks.Value
                        .Where(t => t.TrackFileId > 0)
                        .ToList();

                    foreach (var localTrack in LocalTracks)
                    {
                        // Only process unmapped tracks from the import folder (not library copies)
                        if (localTrack.Tracks.Count > 0 || existingPaths.Contains(localTrack.Path))
                        {
                            continue;
                        }

                        var trackNumber = localTrack.FileTrackInfo?.TrackNumbers?.FirstOrDefault() ?? 0;
                        if (trackNumber <= 0)
                        {
                            continue;
                        }

                        var discNumber = localTrack.FileTrackInfo?.DiscNumber ?? 0;

                        // Prefer a disc-aware match; fall back to track number only
                        // (covers single-disc albums where DiscNumber may be 0).
                        var matchingTrack = discNumber > 0
                            ? tracksWithFiles.FirstOrDefault(t => t.MediumNumber == discNumber && t.AbsoluteTrackNumber == trackNumber)
                              ?? tracksWithFiles.FirstOrDefault(t => t.AbsoluteTrackNumber == trackNumber)
                            : tracksWithFiles.FirstOrDefault(t => t.AbsoluteTrackNumber == trackNumber);

                        if (matchingTrack != null)
                        {
                            localTrack.Tracks = new List<Track> { matchingTrack };
                        }
                    }
                }
            }
        }

        public override string ToString()
        {
            return "[" + string.Join(", ", LocalTracks.Select(x => Path.GetDirectoryName(x.Path)).Distinct()) + "]";
        }
    }

    public class TrackMapping
    {
        public TrackMapping()
        {
            Mapping = new Dictionary<LocalTrack, Tuple<Track, Distance>>();
        }

        public Dictionary<LocalTrack, Tuple<Track, Distance>> Mapping { get; set; }
        public List<LocalTrack> LocalExtra { get; set; }
        public List<Track> MBExtra { get; set; }
    }
}
