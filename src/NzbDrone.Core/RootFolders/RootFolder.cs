using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.RootFolders
{
    public enum RootFolderType
    {
        RootFolder = 0,
        ImportFolder = 1
    }

    public class RootFolder : ModelBase
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public RootFolderType FolderType { get; set; }
        public int DefaultMetadataProfileId { get; set; }
        public int DefaultQualityProfileId { get; set; }
        public MonitorTypes DefaultMonitorOption { get; set; }
        public NewItemMonitorTypes DefaultNewItemMonitorOption { get; set; }
        public HashSet<int> DefaultTags { get; set; } = new ();

        public bool AutomaticallyImport { get; set; }

        public bool Accessible { get; set; }
        public long? FreeSpace { get; set; }
        public long? TotalSpace { get; set; }
    }
}
