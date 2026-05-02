using System.Collections.Generic;
using System.Linq;
using Lidarr.Api.V1.Albums;
using Lidarr.Api.V1.Artist;
using Lidarr.Api.V1.Tracks;
using Lidarr.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TrackImport.Manual;
using NzbDrone.Core.Music;
using NzbDrone.Core.Qualities;

namespace Lidarr.Api.V1.ManualImport
{
    [V1ApiController]
    public class ManualImportController : Controller
    {
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly IReleaseService _releaseService;
        private readonly ITrackService _trackService;
        private readonly IManualImportService _manualImportService;
        private readonly Logger _logger;

        public ManualImportController(IManualImportService manualImportService,
                                  IArtistService artistService,
                                  IAlbumService albumService,
                                  IReleaseService releaseService,
                                  ITrackService trackService,
                                  Logger logger)
        {
            _artistService = artistService;
            _albumService = albumService;
            _releaseService = releaseService;
            _trackService = trackService;
            _manualImportService = manualImportService;
            _logger = logger;
        }

        [HttpPost]
        public IActionResult UpdateItems([FromBody] List<ManualImportUpdateResource> resource)
        {
            return Accepted(UpdateImportItems(resource));
        }

        [HttpGet]
        public List<ManualImportResource> GetMediaFiles(string folder, string downloadId, int? artistId, bool filterExistingFiles = true, bool replaceExistingFiles = true)
        {
            NzbDrone.Core.Music.Artist artist = null;

            if (artistId > 0)
            {
                artist = _artistService.GetArtist(artistId.Value);
            }

            var filter = filterExistingFiles ? FilterFilesType.Matched : FilterFilesType.None;

            return _manualImportService.GetMediaFiles(folder, downloadId, artist, filter, replaceExistingFiles).ToResource().Select(AddQualityWeight).ToList();
        }

        // Looks up artist, album and track by a MusicBrainz recording ID.
        // Returns a slim object so the Music Import page can populate unmatched rows.
        [HttpGet("lookup")]
        public IActionResult LookupByRecordingId(string recordingId)
        {
            if (string.IsNullOrWhiteSpace(recordingId))
            {
                return BadRequest("recordingId is required");
            }

            var track = _trackService.GetTrackByForeignRecordingId(recordingId);

            if (track == null)
            {
                return NotFound();
            }

            // Load the release to get album and artist
            var release = _releaseService.GetRelease(track.AlbumReleaseId);
            if (release == null)
            {
                return NotFound();
            }

            var album = _albumService.GetAlbum(release.AlbumId);
            if (album == null)
            {
                return NotFound();
            }

            var artist = _artistService.GetArtistByMetadataId(album.ArtistMetadataId);
            if (artist == null)
            {
                return NotFound();
            }

            return Ok(new RecordingLookupResource
            {
                Artist = artist.ToResource(),
                Album = album.ToResource(),
                AlbumReleaseId = release.Id,
                Track = track.ToResource()
            });
        }

        private ManualImportResource AddQualityWeight(ManualImportResource item)
        {
            if (item.Quality != null)
            {
                item.QualityWeight = Quality.DefaultQualityDefinitions.Single(q => q.Quality == item.Quality.Quality).Weight;
                item.QualityWeight += item.Quality.Revision.Real * 10;
                item.QualityWeight += item.Quality.Revision.Version;
            }

            return item;
        }

        private List<ManualImportResource> UpdateImportItems(List<ManualImportUpdateResource> resources)
        {
            var items = new List<ManualImportItem>();
            foreach (var resource in resources)
            {
                items.Add(new ManualImportItem
                {
                    Id = resource.Id,
                    Path = resource.Path,
                    Name = resource.Name,
                    Artist = resource.ArtistId.HasValue ? _artistService.GetArtist(resource.ArtistId.Value) : null,
                    Album = resource.AlbumId.HasValue ? _albumService.GetAlbum(resource.AlbumId.Value) : null,
                    Release = resource.AlbumReleaseId.HasValue ? _releaseService.GetRelease(resource.AlbumReleaseId.Value) : null,
                    Quality = resource.Quality,
                    ReleaseGroup = resource.ReleaseGroup,
                    IndexerFlags = resource.IndexerFlags,
                    DownloadId = resource.DownloadId,
                    AdditionalFile = resource.AdditionalFile,
                    ReplaceExistingFiles = resource.ReplaceExistingFiles,
                    DisableReleaseSwitching = resource.DisableReleaseSwitching
                });
            }

            return _manualImportService.UpdateItems(items).Select(x => x.ToResource()).ToList();
        }
    }

    public class RecordingLookupResource
    {
        public ArtistResource Artist { get; set; }
        public AlbumResource Album { get; set; }
        public int AlbumReleaseId { get; set; }
        public TrackResource Track { get; set; }
    }
}
