using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.TrackImport.Specifications
{
    // Disabled: always accept regardless of track count vs existing release.
    // MoreTracksSpecification was blocking different-edition imports when the
    // monitored release already had more files — conflicts with ReleaseWantedSpecification
    // being disabled to allow any identified release edition to be imported.
    public class MoreTracksSpecification : IImportDecisionEngineSpecification<LocalAlbumRelease>
    {
        public Decision IsSatisfiedBy(LocalAlbumRelease item, DownloadClientItem downloadClientItem)
        {
            return Decision.Accept();
        }
    }
}
