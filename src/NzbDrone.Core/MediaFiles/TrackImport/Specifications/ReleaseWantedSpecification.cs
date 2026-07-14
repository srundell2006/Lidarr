using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.TrackImport.Specifications
{
    // Disabled: always accept regardless of whether the specific release edition is
    // monitored, so that any identified release can be imported into the library.
    public class ReleaseWantedSpecification : IImportDecisionEngineSpecification<LocalAlbumRelease>
    {
        public Decision IsSatisfiedBy(LocalAlbumRelease item, DownloadClientItem downloadClientItem)
        {
            return Decision.Accept();
        }
    }
}
