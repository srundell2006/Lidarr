using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Cache;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music.Events;

namespace NzbDrone.Core.ArtistStats
{
    public interface IArtistStatisticsService
    {
        List<ArtistStatistics> ArtistStatistics();
        ArtistStatistics ArtistStatistics(int artistId);
    }

    public class ArtistStatisticsService : IArtistStatisticsService,
        IHandle<ArtistAddedEvent>,
        IHandle<ArtistEditedEvent>,
        IHandle<ArtistUpdatedEvent>,
        IHandle<ArtistsDeletedEvent>,
        IHandle<AlbumAddedEvent>,
        IHandle<AlbumDeletedEvent>,
        IHandle<AlbumImportedEvent>,
        IHandle<AlbumEditedEvent>,
        IHandle<AlbumUpdatedEvent>,
        IHandle<TrackFileDeletedEvent>
    {
        private const int AllArtistsCacheThrottleSeconds = 60;
        private readonly object _allArtistsCacheLock = new object();
        private DateTime _allArtistsNextInvalidationTime = DateTime.MinValue;

        private readonly IArtistStatisticsRepository _artistStatisticsRepository;
        private readonly ICached<List<AlbumStatistics>> _cache;

        public ArtistStatisticsService(IArtistStatisticsRepository artistStatisticsRepository,
                                       ICacheManager cacheManager)
        {
            _artistStatisticsRepository = artistStatisticsRepository;
            _cache = cacheManager.GetCache<List<AlbumStatistics>>(GetType());
        }

        private void InvalidateAllArtistsCache()
        {
            lock (_allArtistsCacheLock)
            {
                var now = DateTime.UtcNow;
                if (now >= _allArtistsNextInvalidationTime)
                {
                    _cache.Remove("AllArtists");
                    _allArtistsNextInvalidationTime = now.AddSeconds(AllArtistsCacheThrottleSeconds);
                }
            }
        }

        public List<ArtistStatistics> ArtistStatistics()
        {
            var albumStatistics = _cache.Get("AllArtists", () => _artistStatisticsRepository.ArtistStatistics());

            return albumStatistics.GroupBy(s => s.ArtistId).Select(s => MapArtistStatistics(s.ToList())).ToList();
        }

        public ArtistStatistics ArtistStatistics(int artistId)
        {
            var stats = _cache.Get(artistId.ToString(), () => _artistStatisticsRepository.ArtistStatistics(artistId));

            if (stats == null || stats.Count == 0)
            {
                return new ArtistStatistics();
            }

            return MapArtistStatistics(stats);
        }

        private ArtistStatistics MapArtistStatistics(List<AlbumStatistics> albumStatistics)
        {
            var artistStatistics = new ArtistStatistics
            {
                AlbumStatistics = albumStatistics,
                AlbumCount = albumStatistics.Count,
                MissingAlbumCount = albumStatistics.Count(s => s.TrackCount > 0 && s.TrackFileCount < s.TrackCount),
                ArtistId = albumStatistics.First().ArtistId,
                TrackFileCount = albumStatistics.Sum(s => s.TrackFileCount),
                TrackCount = albumStatistics.Sum(s => s.TrackCount),
                TotalTrackCount = albumStatistics.Sum(s => s.TotalTrackCount),
                SizeOnDisk = albumStatistics.Sum(s => s.SizeOnDisk)
            };

            return artistStatistics;
        }

        [EventHandleOrder(EventHandleOrder.First)]
        public void Handle(ArtistAddedEvent message)
        {
            InvalidateAllArtistsCache();
            _cache.Remove(message.Artist.Id.ToString());
        }

        [EventHandleOrder(EventHandleOrder.First)]
        public void Handle(ArtistEditedEvent message)
        {
            InvalidateAllArtistsCache();
            _cache.Remove(message.Artist.Id.ToString());
        }

        [EventHandleOrder(EventHandleOrder.First)]
        public void Handle(ArtistUpdatedEvent message)
        {
            InvalidateAllArtistsCache();
            _cache.Remove(message.Artist.Id.ToString());
        }

        [EventHandleOrder(EventHandleOrder.First)]
        public void Handle(ArtistsDeletedEvent message)
        {
            InvalidateAllArtistsCache();

            foreach (var artist in message.Artists)
            {
                _cache.Remove(artist.Id.ToString());
            }
        }

        [EventHandleOrder(EventHandleOrder.First)]
        public void Handle(AlbumAddedEvent message)
        {
            InvalidateAllArtistsCache();
            _cache.Remove(message.Album.ArtistId.ToString());
        }

        [EventHandleOrder(EventHandleOrder.First)]
        public void Handle(AlbumDeletedEvent message)
        {
            InvalidateAllArtistsCache();
            _cache.Remove(message.Album.ArtistId.ToString());
        }

        [EventHandleOrder(EventHandleOrder.First)]
        public void Handle(AlbumImportedEvent message)
        {
            InvalidateAllArtistsCache();
            _cache.Remove(message.Artist.Id.ToString());
        }

        [EventHandleOrder(EventHandleOrder.First)]
        public void Handle(AlbumEditedEvent message)
        {
            InvalidateAllArtistsCache();
            _cache.Remove(message.Album.ArtistId.ToString());
        }

        [EventHandleOrder(EventHandleOrder.First)]
        public void Handle(AlbumUpdatedEvent message)
        {
            InvalidateAllArtistsCache();
            _cache.Remove(message.Album.ArtistId.ToString());
        }

        [EventHandleOrder(EventHandleOrder.First)]
        public void Handle(TrackFileDeletedEvent message)
        {
            InvalidateAllArtistsCache();
            _cache.Remove(message.TrackFile.Artist.Value.Id.ToString());
        }
    }
}
