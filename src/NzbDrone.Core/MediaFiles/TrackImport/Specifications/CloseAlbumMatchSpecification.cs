using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.TrackImport.Specifications
{
    public class CloseAlbumMatchSpecification : IImportDecisionEngineSpecification<LocalAlbumRelease>
    {
        private const double _albumThreshold = 0.99;

        private const double _trackThreshold = 0.99;
        private readonly Logger _logger;

        public CloseAlbumMatchSpecification(Logger logger)
        {
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalAlbumRelease item, DownloadClientItem downloadClientItem)
        {
            // Always exclude missing_tracks and unmatched_tracks from the album-level distance
            // so that partial albums (where only some tracks are present) are not rejected at
            // this stage. Individual track quality is still enforced by CloseTrackMatchSpecification.
            var dist = item.Distance.NormalizedDistanceExcluding(new List<string> { "missing_tracks", "unmatched_tracks" });
            var reasons = item.Distance.Reasons;

            if (dist > _albumThreshold)
            {
                _logger.Debug($"Album match is not close enough: {dist} vs {_albumThreshold} {reasons}. Skipping {item}");
                return Decision.Reject($"Album match is not close enough: {1 - dist:P1} vs {1 - _albumThreshold:P0} {reasons}");
            }

            if (item.NewDownload)
            {
                var worstTrackMatch = item.LocalTracks.Where(x => x.Distance != null).MaxBy(x => x.Distance.NormalizedDistance());
                if (worstTrackMatch == null)
                {
                    _logger.Debug($"No tracks matched");
                    return Decision.Reject("No tracks matched");
                }

                var maxTrackDist = worstTrackMatch.Distance.NormalizedDistance();
                var trackReasons = worstTrackMatch.Distance.Reasons;
                if (maxTrackDist > _trackThreshold)
                {
                    _logger.Debug($"Worst track match: {maxTrackDist} vs {_trackThreshold} {trackReasons}. Skipping {item}");
                    return Decision.Reject($"Worst track match: {1 - maxTrackDist:P1} vs {1 - _trackThreshold:P0} {trackReasons}");
                }
            }

            _logger.Debug($"Accepting release {item}: dist {dist} vs {_albumThreshold} {reasons}");
            return Decision.Accept();
        }
    }
}
