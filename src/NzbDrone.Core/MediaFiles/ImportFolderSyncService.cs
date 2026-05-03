using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Parser;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles
{
    public class ImportFolderSyncService : IExecute<ImportFolderSyncCommand>
    {
        private readonly IRootFolderService _rootFolderService;
        private readonly IDiskScanService _diskScanService;
        private readonly IMakeImportDecision _importDecisionMaker;
        private readonly IImportApprovedTracks _importApprovedTracks;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public ImportFolderSyncService(
            IRootFolderService rootFolderService,
            IDiskScanService diskScanService,
            IMakeImportDecision importDecisionMaker,
            IImportApprovedTracks importApprovedTracks,
            IRecycleBinProvider recycleBinProvider,
            IDiskProvider diskProvider,
            Logger logger)
        {
            _rootFolderService = rootFolderService;
            _diskScanService = diskScanService;
            _importDecisionMaker = importDecisionMaker;
            _importApprovedTracks = importApprovedTracks;
            _recycleBinProvider = recycleBinProvider;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public void Execute(ImportFolderSyncCommand message)
        {
            var importFolders = _rootFolderService.AllImportFolders();

            if (!importFolders.Any())
            {
                _logger.Trace("No Import Folders configured — skipping import folder sync");
                return;
            }

            foreach (var folder in importFolders)
            {
                if (!_diskProvider.FolderExists(folder.Path))
                {
                    _logger.Warn("Import folder does not exist: {0}", folder.Path);
                    continue;
                }

                try
                {
                    SyncFolder(folder.Path);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unhandled error syncing import folder: {0}", folder.Path);
                }
            }
        }

        // ── per-folder ───────────────────────────────────────────────────────────

        private void SyncFolder(string importRoot)
        {
            _logger.ProgressInfo("Scanning import folder: {0}", importRoot);

            // GetAudioFiles filters by extension; FilterFiles removes excluded paths
            // (extras, @eadir, dotfiles, partial downloads, etc.)
            var allFiles = _diskScanService
                .FilterFiles(importRoot, _diskScanService.GetAudioFiles(importRoot))
                .ToList();

            if (!allFiles.Any())
            {
                _logger.Debug("No audio files found in {0}", importRoot);
                return;
            }

            // Group by top-level subdirectory so each artist/album folder is processed
            // as an independent batch.  Files sitting directly in the root are handled
            // as their own group under the root path.
            var groups = allFiles
                .GroupBy(f => GetTopLevelSubdirectory(importRoot, f.FullName))
                .ToList();

            _logger.Debug("{0} file(s) across {1} director{2} in {3}",
                allFiles.Count, groups.Count, groups.Count == 1 ? "y" : "ies", importRoot);

            foreach (var group in groups)
            {
                try
                {
                    ProcessDirectory(importRoot, group.Key, group.ToList());
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error processing import directory: {0}", group.Key);
                }
            }

            // Remove any empty subdirectories left behind after moving files
            CleanupEmptySubdirectories(importRoot);
        }

        // ── per-directory ─────────────────────────────────────────────────────────

        private void ProcessDirectory(string importRoot, string directoryPath, List<IFileInfo> audioFiles)
        {
            _logger.ProgressInfo("Importing from: {0} ({1} file{2})",
                directoryPath, audioFiles.Count, audioFiles.Count == 1 ? "" : "s");

            var idOverrides = new IdentificationOverrides();
            var itemInfo = new ImportDecisionMakerInfo
            {
                // Give the identification service a hint from the folder name
                // (e.g. "Artist - Album (Year)") to improve match quality
                ParsedAlbumInfo = Parser.ParseAlbumTitle(new DirectoryInfo(directoryPath).Name)
            };
            var config = new ImportDecisionMakerConfig
            {
                Filter          = FilterFilesType.None,
                NewDownload     = true,
                SingleRelease   = false,
                IncludeExisting = false,
                AddNewArtists   = false,
                // Allow the album to be incomplete — we may be importing a single
                // or a partial rip, and the missing tracks should not block the
                // ones that are present.
                AllowPartialAlbum = true
            };

            var decisions = _importDecisionMaker.GetImportDecisions(audioFiles, idOverrides, itemInfo, config);
            var results   = _importApprovedTracks.Import(decisions,
                                                         replaceExisting: false,
                                                         downloadClientItem: null,
                                                         importMode: ImportMode.Move);

            var importedCount = results.Count(r => r.Result == ImportResultType.Imported);
            _logger.Info("Imported {0}/{1} file{2} from {3}",
                importedCount, audioFiles.Count, audioFiles.Count == 1 ? "" : "s", directoryPath);

            // Move every file that was not successfully imported to the Recycle Bin.
            // Imported files have already been moved to the library by ImportApprovedTracks.
            var notImportedPaths = results
                .Where(r => r.Result != ImportResultType.Imported)
                .Select(r => r.ImportDecision.Item.Path)
                .Distinct()
                .ToList();

            foreach (var filePath in notImportedPaths)
            {
                MoveToRecycleBin(importRoot, filePath);
            }
        }

        // ── helpers ───────────────────────────────────────────────────────────────

        private void MoveToRecycleBin(string importRoot, string filePath)
        {
            try
            {
                if (!_diskProvider.FileExists(filePath))
                {
                    // Already moved (e.g. a successful import relocated it) — nothing to do
                    return;
                }

                // Preserve the relative sub-path inside the recycle bin so the user
                // can tell at a glance which import folder the file came from.
                var root     = importRoot.TrimEnd('/', '\\');
                var relative = filePath.Length > root.Length
                    ? filePath.Substring(root.Length).TrimStart('/', '\\')
                    : Path.GetFileName(filePath);
                var subfolder = Path.GetDirectoryName(relative) ?? string.Empty;

                _logger.Info("Moving unimported file to recycle bin: {0}", filePath);
                _recycleBinProvider.DeleteFile(filePath, subfolder);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to move file to recycle bin: {0}", filePath);
            }
        }

        /// <summary>
        /// Returns the path of the first directory component under
        /// <paramref name="importRoot"/> for <paramref name="filePath"/>.
        /// If the file is directly in the root, the root path is returned.
        /// </summary>
        private static string GetTopLevelSubdirectory(string importRoot, string filePath)
        {
            var root         = importRoot.TrimEnd('/', '\\');
            var relative     = filePath.Substring(root.Length).TrimStart('/', '\\');
            var firstSegment = relative.Split(new[] { '/', '\\' }, 2,
                                              StringSplitOptions.RemoveEmptyEntries)
                                       .FirstOrDefault();

            return firstSegment.IsNullOrWhiteSpace()
                ? importRoot
                : Path.Combine(importRoot, firstSegment);
        }

        private void CleanupEmptySubdirectories(string rootPath)
        {
            try
            {
                // Sort by descending path length so leaf directories are removed first,
                // which allows their now-empty parents to be removed in the same pass.
                var subdirs = Directory
                    .GetDirectories(rootPath, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length);

                foreach (var dir in subdirs)
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        {
                            _logger.Debug("Removing empty directory: {0}", dir);
                            _diskProvider.DeleteFolder(dir, false);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Could not remove directory: {0}", dir);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error enumerating subdirectories of: {0}", rootPath);
            }
        }
    }
}
