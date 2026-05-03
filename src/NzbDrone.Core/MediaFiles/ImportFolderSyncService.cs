using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.Messaging.Commands;
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
                var directoryPath = group.Key;

                try
                {
                    ProcessDirectory(directoryPath, group.ToList());

                    // After processing, send the whole subdirectory to the recycle bin.
                    // (Files that were successfully imported have already been moved to
                    // the library; everything remaining — unmatched, rejected, etc. —
                    // goes with the folder.)
                    // We never recycle the import root itself, only its subdirectories.
                    if (!directoryPath.Equals(importRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        MoveDirectoryToRecycleBin(directoryPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error processing import directory: {0}", directoryPath);
                }
            }
        }

        // ── per-directory ─────────────────────────────────────────────────────────

        private void ProcessDirectory(string directoryPath, List<IFileInfo> audioFiles)
        {
            _logger.ProgressInfo("Importing from: {0} ({1} file{2})",
                directoryPath, audioFiles.Count, audioFiles.Count == 1 ? "" : "s");

            var idOverrides = new IdentificationOverrides();
            var itemInfo = new ImportDecisionMakerInfo
            {
                // Give the identification service a hint from the folder name
                // (e.g. "Artist - Album (Year)") to improve match quality
                ParsedAlbumInfo = Parser.Parser.ParseAlbumTitle(new DirectoryInfo(directoryPath).Name)
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
        }

        // ── helpers ───────────────────────────────────────────────────────────────

        private void MoveDirectoryToRecycleBin(string directoryPath)
        {
            try
            {
                if (!_diskProvider.FolderExists(directoryPath))
                {
                    return;
                }

                _logger.Info("Moving processed directory to recycle bin: {0}", directoryPath);
                _recycleBinProvider.DeleteFolder(directoryPath);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to move directory to recycle bin: {0}", directoryPath);
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
    }
}
