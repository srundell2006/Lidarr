using System.Collections.Generic;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.TrackImport.Manual
{
    public class ManualImportCommand : Command
    {
        public List<ManualImportFile> Files { get; set; }

        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;

        public ImportMode ImportMode { get; set; }
        public bool ReplaceExistingFiles { get; set; }

        /// <summary>
        /// When true, any source files that were submitted but not successfully
        /// imported (rejected or quality-blocked) are deleted from disk after the
        /// import completes.  Used by the Music Import page to clean up the
        /// import folder automatically.
        /// </summary>
        public bool DeleteRejectedFiles { get; set; }
    }
}
