using NLog;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.TrackImport.Specifications
{
    public class NoMissingOrUnmatchedTracksSpecification : IImportDecisionEngineSpecification<LocalAlbumRelease>
    {
        private readonly Logger _logger;

        public NoMissingOrUnmatchedTracksSpecification(Logger logger)
        {
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalAlbumRelease item, DownloadClientItem downloadClientItem)
        {
            // Manual-import paths set AllowPartialAlbum so the user can import a
            // subset of an album's tracks (e.g. a single, or a partial rip) without
            // the whole decision being blocked because other tracks are absent.
            if (item.AllowPartialAlbum)
            {
                return Decision.Accept();
            }

            if (item.NewDownload && item.TrackMapping.LocalExtra.Count > 0)
            {
                _logger.Debug("This release has track files that have not been matched. Skipping {0}", item);
                return Decision.Reject("Has unmatched tracks");
            }

            if (item.NewDownload && item.TrackMapping.MBExtra.Count > 0)
            {
                _logger.Debug("This release is missing tracks. Skipping {0}", item);
                return Decision.Reject("Has missing tracks");
            }

            return Decision.Accept();
        }
    }
}
