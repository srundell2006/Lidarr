using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles
{
    public interface IDiskScanService
    {
        void Scan(List<string> folders = null, FilterFilesType filter = FilterFilesType.Known, bool addNewArtists = false, List<int> artistIds = null);
        IFileInfo[] GetAudioFiles(string path, bool allDirectories = true);
        string[] GetNonAudioFiles(string path, bool allDirectories = true);
        List<IFileInfo> FilterFiles(string basePath, IEnumerable<IFileInfo> files);
        List<string> FilterPaths(string basePath, IEnumerable<string> paths);
    }

    public class DiskScanService :
        IDiskScanService,
        IExecute<RescanFoldersCommand>
    {
        public static readonly Regex ExcludedSubFoldersRegex = new Regex(@"(?:\\|\/|^)(?:extras|@eadir|\.@__thumb|extrafanart|plex versions|\.[^\\/]+)(?:\\|\/)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public static readonly Regex ExcludedFilesRegex = new Regex(@"^\._|^Thumbs\.db$|^\.DS_store$|\.partial~$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly IConfigService _configService;
        private readonly IDiskProvider _diskProvider;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMakeImportDecision _importDecisionMaker;
        private readonly IImportApprovedTracks _importApprovedTracks;
        private readonly IArtistService _artistService;
        private readonly IMediaFileTableCleanupService _mediaFileTableCleanupService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public DiskScanService(IConfigService configService,
                               IDiskProvider diskProvider,
                               IMediaFileService mediaFileService,
                               IMakeImportDecision importDecisionMaker,
                               IImportApprovedTracks importApprovedTracks,
                               IArtistService artistService,
                               IRootFolderService rootFolderService,
                               IMediaFileTableCleanupService mediaFileTableCleanupService,
                               IRecycleBinProvider recycleBinProvider,
                               IEventAggregator eventAggregator,
                               Logger logger)
        {
            _configService = configService;
            _diskProvider = diskProvider;
            _mediaFileService = mediaFileService;
            _importDecisionMaker = importDecisionMaker;
            _importApprovedTracks = importApprovedTracks;
            _artistService = artistService;
            _mediaFileTableCleanupService = mediaFileTableCleanupService;
            _rootFolderService = rootFolderService;
            _recycleBinProvider = recycleBinProvider;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public void Scan(List<string> folders = null, FilterFilesType filter = FilterFilesType.Known, bool addNewArtists = false, List<int> artistIds = null)
        {
            if (folders == null)
            {
                folders = _rootFolderService.All().Select(x => x.Path).ToList();
            }

            if (artistIds == null)
            {
                artistIds = new List<int>();
            }

            var artists = _artistService.GetArtists(artistIds);

            // Create missing artist folders when scanning specific artists
            if (artistIds.Any())
            {
                foreach (var artist in artists)
                {
                    if (!_diskProvider.FolderExists(artist.Path))
                    {
                        if (_configService.CreateEmptyArtistFolders)
                        {
                            if (_configService.DeleteEmptyFolders)
                            {
                                _logger.Debug("Not creating missing artist folder: {0} because delete empty folders is enabled", artist.Path);
                            }
                            else
                            {
                                _logger.Debug("Creating missing artist folder: {0}", artist.Path);
                                _diskProvider.CreateFolder(artist.Path);
                            }
                        }
                        else
                        {
                            _logger.Debug("Artist folder doesn't exist: {0}", artist.Path);
                        }
                    }
                }
            }

            var config = new ImportDecisionMakerConfig
            {
                Filter = filter,
                IncludeExisting = true,
                AddNewArtists = addNewArtists
            };

            // Resolve input folders to artist-level scan units.  When a root folder is given
            // (e.g. the daily Rescan Folders task), expand it into its immediate subdirectories
            // so that identification runs one artist folder at a time instead of accumulating
            // the entire library (100k+ files) into a single batch before any work begins.
            // Artist-level folders passed directly (e.g. from RefreshArtistService) are used as-is.
            var rootFolderPaths = _rootFolderService.All()
                .Select(x => x.Path.TrimEnd('/', '\\'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var scanUnits = new List<string>();

            foreach (var folder in folders)
            {
                var normalizedFolder = folder.TrimEnd('/', '\\');

                if (rootFolderPaths.Contains(normalizedFolder))
                {
                    // Root folder — validate existence, then expand to immediate subdirectories.
                    // Files sitting directly under the root (not in an artist subfolder) are
                    // intentionally skipped; Lidarr expects an artist/album/track hierarchy.
                    if (!_diskProvider.FolderExists(folder))
                    {
                        _logger.Warn("Artists' root folder ({0}) doesn't exist.", folder);
                        artists.ForEach(x => _eventAggregator.PublishEvent(new ArtistScanSkippedEvent(x, ArtistScanSkippedReason.RootFolderDoesNotExist)));
                        continue;
                    }

                    if (_diskProvider.FolderEmpty(folder))
                    {
                        _logger.Warn("Artists' root folder ({0}) is empty.", folder);
                        artists.ForEach(x => _eventAggregator.PublishEvent(new ArtistScanSkippedEvent(x, ArtistScanSkippedReason.RootFolderIsEmpty)));
                        continue;
                    }

                    // Reuse existing path filtering to skip @eadir, .@__thumb, hidden dirs, etc.
                    var subDirs = FilterPaths(folder, _diskProvider.GetDirectories(folder))
                        .OrderBy(d => d)
                        .ToList();

                    _logger.Debug("Expanding root folder {0} into {1} artist subdirectories", folder, subDirs.Count);
                    scanUnits.AddRange(subDirs);
                }
                else
                {
                    // Already an artist-level folder (e.g. from RefreshArtistService)
                    scanUnits.Add(folder);
                }
            }

            _logger.ProgressInfo("Scanning {0} folders", scanUnits.Count);

            var totalStopwatch = Stopwatch.StartNew();

            foreach (var folder in scanUnits)
            {
                ScanFolder(folder, config);
            }

            totalStopwatch.Stop();
            _logger.Debug("Scan complete for {0} folders [{1}]", scanUnits.Count, totalStopwatch.Elapsed);

            // When scanning specific artist folders, recycle any track files that still have no
            // track association after this scan cycle.  These are files Lidarr cannot match to
            // anything in its database, so keeping them in the library folder is misleading.
            // Skip this for whole-library rescans (artistIds empty) to avoid mass-recycling on
            // the first run after onboarding.
            if (artistIds.Any())
            {
                RecycleUnmappedFiles(scanUnits);
            }

            foreach (var artist in artists)
            {
                CompletedScanning(artist);
            }
        }

        /// <summary>
        /// Runs the full scan pipeline (enumerate → clean DB → identify → import → insert/update)
        /// for a single artist-level folder.  Keeping this isolated means the maximum number of
        /// files held in memory at once is bounded by one artist's catalogue rather than the
        /// whole library.
        /// </summary>
        private void ScanFolder(string folder, ImportDecisionMakerConfig config)
        {
            var rootFolder = _rootFolderService.GetBestRootFolder(folder);

            if (rootFolder == null)
            {
                _logger.Error("Not scanning {0}, it's not a subdirectory of a defined root folder", folder);
                return;
            }

            if (!_diskProvider.FolderExists(folder))
            {
                _logger.Debug("Specified scan folder ({0}) doesn't exist.", folder);
                CleanMediaFiles(folder, new List<string>());
                return;
            }

            _logger.ProgressInfo("Scanning {0}", folder);

            var files = FilterFiles(folder, GetAudioFiles(folder));

            if (!files.Any())
            {
                _logger.Warn("Scan folder {0} is empty.", folder);
                return;
            }

            CleanMediaFiles(folder, files.Select(x => x.FullName).ToList());

            var decisionsStopwatch = Stopwatch.StartNew();
            var decisions = _importDecisionMaker.GetImportDecisions(files, null, null, config);
            decisionsStopwatch.Stop();
            _logger.Trace("Import decisions for {0} [{1}]", folder, decisionsStopwatch.Elapsed);

            _importApprovedTracks.Import(decisions, false);

            // decisions may have been filtered to just new files.  Anything new and approved will
            // have been inserted.  Now make sure anything new but not approved gets inserted too.
            // Note that knownFiles will include anything imported just now.
            var knownFiles = _mediaFileService.GetFilesWithBasePath(folder);

            var newFiles = decisions
                .ExceptBy(x => x.Item.Path, knownFiles, x => x.Path, PathEqualityComparer.Instance)
                .Select(decision => new TrackFile
                {
                    Path = decision.Item.Path,
                    Size = decision.Item.Size,
                    Modified = decision.Item.Modified,
                    DateAdded = DateTime.UtcNow,
                    Quality = decision.Item.Quality,
                    MediaInfo = decision.Item.FileTrackInfo.MediaInfo
                })
                .ToList();
            _mediaFileService.AddMany(newFiles);

            if (newFiles.Count > 0)
            {
                _logger.Debug($"Inserted {newFiles.Count} new unmatched trackfiles in {folder}");
            }

            // Update size/mtime for known files that have changed on disk
            var updatedFiles = knownFiles
                .Join(decisions,
                      x => x.Path,
                      x => x.Item.Path,
                      (file, decision) => new
                      {
                          File = file,
                          Item = decision.Item
                      },
                      PathEqualityComparer.Instance)
                .Where(x => x.File.Size != x.Item.Size ||
                       Math.Abs((x.File.Modified - x.Item.Modified).TotalSeconds) > 1)
                .Select(x =>
                {
                    x.File.Size = x.Item.Size;
                    x.File.Modified = x.Item.Modified;
                    x.File.MediaInfo = x.Item.FileTrackInfo.MediaInfo;
                    x.File.Quality = x.Item.Quality;
                    return x.File;
                })
                .ToList();

            _mediaFileService.Update(updatedFiles);

            if (updatedFiles.Count > 0)
            {
                _logger.Debug($"Updated info for {updatedFiles.Count} known files in {folder}");
            }
        }

        private void CleanMediaFiles(string folder, List<string> mediaFileList)
        {
            _logger.Debug($"Cleaning up media files in DB [{folder}]");
            _mediaFileTableCleanupService.Clean(folder, mediaFileList);
        }

        private void RecycleUnmappedFiles(List<string> folders)
        {
            foreach (var folder in folders)
            {
                var unmapped = _mediaFileService.GetUnmappedFilesWithBasePath(folder);

                if (!unmapped.Any())
                {
                    continue;
                }

                _logger.Info("Found {0} unmapped track file(s) in {1} — moving to recycle bin", unmapped.Count, folder);

                foreach (var trackFile in unmapped)
                {
                    try
                    {
                        _logger.Debug("Recycling unmapped track file: {0}", trackFile.Path);
                        _recycleBinProvider.DeleteFile(trackFile.Path);
                        _mediaFileService.Delete(trackFile, DeleteMediaFileReason.MissingFromDisk);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Failed to recycle unmapped track file: {0}", trackFile.Path);
                    }
                }
            }
        }

        private void CompletedScanning(Artist artist)
        {
            _logger.Info("Completed scanning disk for {0}", artist.Name);
            _eventAggregator.PublishEvent(new ArtistScannedEvent(artist));
        }

        public IFileInfo[] GetAudioFiles(string path, bool allDirectories = true)
        {
            _logger.Debug("Scanning '{0}' for music files", path);

            var filesOnDisk = _diskProvider.GetFileInfos(path, allDirectories);

            var mediaFileList = filesOnDisk.Where(file => MediaFileExtensions.Extensions.Contains(file.Extension))
                                           .ToList();

            _logger.Trace("{0} files were found in {1}", filesOnDisk.Count, path);
            _logger.Debug("{0} audio files were found in {1}", mediaFileList.Count, path);

            // Rename any path components that contain characters illegal on Windows/NTFS.
            // This runs in-place on disk so Lidarr stores clean paths from the start.
            var sanitized = new List<IFileInfo>(mediaFileList.Count);
            foreach (var file in mediaFileList)
            {
                var cleanPath = SanitizePathComponents(path, file.FullName);
                sanitized.Add(cleanPath != file.FullName
                    ? _diskProvider.GetFileInfo(cleanPath)
                    : file);
            }

            return sanitized.ToArray();
        }

        // Characters that are illegal in filenames on Windows/NTFS and cause
        // interoperability problems.  Slash (/) and backslash (\) are omitted
        // because they are path separators — the OS will never include them in
        // a single filename component returned by readdir.
        private static readonly char[] IllegalPathChars = { ':', '*', '?', '"', '<', '>', '|' };

        /// <summary>
        /// Walks each path component between <paramref name="basePath"/> and
        /// <paramref name="fullPath"/>, renames any component that contains
        /// illegal characters, and returns the resulting clean path.
        /// </summary>
        private string SanitizePathComponents(string basePath, string fullPath)
        {
            // Fast-path: nothing to do if the relative portion is already clean
            var relative = fullPath.Substring(basePath.TrimEnd('/').Length).TrimStart('/');
            if (relative.IndexOfAny(IllegalPathChars) < 0)
            {
                return fullPath;
            }

            var components = relative.Split('/');
            var currentBase = basePath.TrimEnd('/');

            foreach (var component in components)
            {
                var clean = SanitizeFileName(component);
                var originalFull = currentBase + "/" + component;
                var cleanFull = currentBase + "/" + clean;

                if (clean != component)
                {
                    // Skip if the sanitized target already exists (renamed by a previous
                    // file's iteration over the same directory).
                    var targetExists = _diskProvider.FolderExists(cleanFull) ||
                                       _diskProvider.FileExists(cleanFull);

                    if (!targetExists)
                    {
                        try
                        {
                            if (_diskProvider.FolderExists(originalFull))
                            {
                                _diskProvider.MoveFolder(originalFull, cleanFull);
                                _logger.Info(
                                    "Renamed directory: '{0}' → '{1}' (illegal characters removed)",
                                    component,
                                    clean);
                            }
                            else if (_diskProvider.FileExists(originalFull))
                            {
                                _diskProvider.MoveFile(originalFull, cleanFull);
                                _logger.Info(
                                    "Renamed file: '{0}' → '{1}' (illegal characters removed)",
                                    component,
                                    clean);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn(ex, "Failed to rename '{0}' to '{1}' — keeping original name", originalFull, cleanFull);
                            cleanFull = originalFull;
                        }
                    }
                    else
                    {
                        _logger.Debug("Sanitized target already exists, skipping rename: '{0}'", cleanFull);
                    }
                }

                currentBase = cleanFull;
            }

            return currentBase;
        }

        /// <summary>
        /// Returns a copy of <paramref name="name"/> with characters that are
        /// illegal on Windows/NTFS replaced or removed.
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            if (name.IndexOfAny(IllegalPathChars) < 0)
            {
                return name;
            }

            // Colon: smart replacement mirrors FileNameBuilder behaviour.
            // ": " (colon-space) → " - " for readability (e.g. "Artist: Album" → "Artist - Album")
            // remaining ":" → "-"
            var result = name
                .Replace(": ", " - ")
                .Replace(":", "-")
                .Replace("*", "-")
                .Replace("?", string.Empty)
                .Replace("\"", string.Empty)
                .Replace("<", string.Empty)
                .Replace(">", string.Empty)
                .Replace("|", string.Empty);

            // Strip trailing dots/spaces — these are also illegal on Windows
            return result.TrimEnd(' ', '.');
        }

        public string[] GetNonAudioFiles(string path, bool allDirectories = true)
        {
            _logger.Debug("Scanning '{0}' for non-music files", path);

            var filesOnDisk = _diskProvider.GetFiles(path, allDirectories).ToList();

            var mediaFileList = filesOnDisk.Where(file => !MediaFileExtensions.Extensions.Contains(Path.GetExtension(file)))
                                           .ToList();

            _logger.Trace("{0} files were found in {1}", filesOnDisk.Count, path);
            _logger.Debug("{0} non-music files were found in {1}", mediaFileList.Count, path);

            return mediaFileList.ToArray();
        }

        public List<string> FilterPaths(string basePath, IEnumerable<string> paths)
        {
            return paths.Where(file => !ExcludedSubFoldersRegex.IsMatch(basePath.GetRelativePath(file)))
                        .Where(file => !ExcludedFilesRegex.IsMatch(Path.GetFileName(file)))
                        .ToList();
        }

        public List<IFileInfo> FilterFiles(string basePath, IEnumerable<IFileInfo> files)
        {
            return files.Where(file => !ExcludedSubFoldersRegex.IsMatch(basePath.GetRelativePath(file.FullName)))
                        .Where(file => !ExcludedFilesRegex.IsMatch(file.Name))
                        .ToList();
        }

        public void Execute(RescanFoldersCommand message)
        {
            Scan(message.Folders, message.Filter, message.AddNewArtists, message.ArtistIds);
        }
    }
}
