using System;
using Lidarr.Http.REST;
using NzbDrone.Core.Music;

namespace Lidarr.Api.V1.Albums
{
    /// <summary>
    /// Lightweight album representation used in the artist list endpoint.
    /// Contains only the fields needed by the artist index views — avoids the
    /// lazy-loaded AlbumReleases join that the full AlbumResource triggers.
    /// </summary>
    public class AlbumSummaryResource : RestResource
    {
        public string Title { get; set; }
        public string Disambiguation { get; set; }
        public string ForeignAlbumId { get; set; }
        public DateTime? ReleaseDate { get; set; }
    }

    public static class AlbumSummaryResourceMapper
    {
        public static AlbumSummaryResource ToSummaryResource(this Album model)
        {
            if (model == null)
            {
                return null;
            }

            return new AlbumSummaryResource
            {
                Id = model.Id,
                Title = model.Title,
                Disambiguation = model.Disambiguation,
                ForeignAlbumId = model.ForeignAlbumId,
                ReleaseDate = model.ReleaseDate
            };
        }
    }
}
