using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Crypto;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles.TrackImport.Manual
{
    public interface IManualImportService
    {
        List<ManualImportItem> GetMediaFiles(string path, string downloadId, Artist artist, FilterFilesType filter, bool replaceExistingFiles);
        List<ManualImportItem> UpdateItems(List<ManualImportItem> item);
    }

    public class ManualImportService : IExecute<ManualImportCommand>, IManualImportService
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IParsingService _parsingService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IDiskScanService _diskScanService;
        private readonly IMakeImportDecision _importDecisionMaker;
        private readonly ICustomFormatCalculationService _formatCalculator;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly IReleaseService _releaseService;
        private readonly ITrackService _trackService;
        private readonly IAudioTagService _audioTagService;
        private readonly IImportApprovedTracks _importApprovedTracks;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly IDownloadedTracksImportService _downloadedTracksImportService;
        private readonly IProvideImportItemService _provideImportItemService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public ManualImportService(IDiskProvider diskProvider,
                                   IParsingService parsingService,
                                   IRootFolderService rootFolderService,
                                   IDiskScanService diskScanService,
                                   IMakeImportDecision importDecisionMaker,
                                   ICustomFormatCalculationService formatCalculator,
                                   IArtistService artistService,
                                   IAlbumService albumService,
                                   IReleaseService releaseService,
                                   ITrackService trackService,
                                   IAudioTagService audioTagService,
                                   IImportApprovedTracks importApprovedTracks,
                                   ITrackedDownloadService trackedDownloadService,
                                   IDownloadedTracksImportService downloadedTracksImportService,
                                   IProvideImportItemService provideImportItemService,
                                   IEventAggregator eventAggregator,
                                   Logger logger)
        {
            _diskProvider = diskProvider;
            _parsingService = parsingService;
            _rootFolderService = rootFolderService;
            _diskScanService = diskScanService;
            _importDecisionMaker = importDecisionMaker;
            _formatCalculator = formatCalculator;
            _artistService = artistService;
            _albumService = albumService;
            _releaseService = releaseService;
            _trackService = trackService;
            _audioTagService = audioTagService;
            _importApprovedTracks = importApprovedTracks;
            _trackedDownloadService = trackedDownloadService;
            _downloadedTracksImportService = downloadedTracksImportService;
            _provideImportItemService = provideImportItemService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public List<ManualImportItem> GetMediaFiles(string path, string downloadId, Artist artist, FilterFilesType filter, bool replaceExistingFiles)
        {
            // Before scanning, repair any filenames whose bytes are not valid UTF-8
            // (common with music ripped on Windows using encodings like Windows-1252).
            // .NET replaces invalid UTF-8 bytes with '?' making those files unreachable;
            // renaming them to their proper UTF-8 form fixes the issue permanently.
            if (_diskProvider.FolderExists(path))
            {
                LinuxNativeFileHelper.RepairFolderEncoding(path);
            }

            if (downloadId.IsNotNullOrWhiteSpace())
            {
                var trackedDownload = _trackedDownloadService.Find(downloadId);

                if (trackedDownload == null)
                {
                    return new List<ManualImportItem>();
                }

                if (trackedDownload.ImportItem == null)
                {
                    trackedDownload.ImportItem = _provideImportItemService.ProvideImportItem(trackedDownload.DownloadItem, trackedDownload.ImportItem);
                }

                path = trackedDownload.ImportItem.OutputPath.FullPath;
            }

            if (!_diskProvider.FolderExists(path))
            {
                if (!_diskProvider.FileExists(path))
                {
                    return new List<ManualImportItem>();
                }

                var files = new List<IFileInfo> { _diskProvider.GetFileInfo(path) };

                var config = new ImportDecisionMakerConfig
                {
                    Filter = FilterFilesType.None,
                    NewDownload = true,
                    SingleRelease = false,
                    IncludeExisting = !replaceExistingFiles,
                    AddNewArtists = false
                };

                var decisions = _importDecisionMaker.GetImportDecisions(files, null, null, config);

                if (decisions.Any())
                {
                    var result = MapItem(decisions.First(), downloadId, replaceExistingFiles, false);
                    return new List<ManualImportItem> { result };
                }

                return new List<ManualImportItem>
                {
                    new ManualImportItem()
                    {
                        Id = HashConverter.GetHashInt31(path),
                        DownloadId = downloadId,
                        Path = path,
                        Name = Path.GetFileNameWithoutExtension(path),
                        Size = _diskProvider.GetFileSize(path),
                        Rejections = new List<Rejection> { new Rejection("Unable to process file") },
                        ReplaceExistingFiles = replaceExistingFiles
                    }
                };
            }

            return ProcessFolder(path, downloadId, artist, filter, replaceExistingFiles);
        }

        private List<ManualImportItem> ProcessFolder(string folder, string downloadId, Artist artist, FilterFilesType filter, bool replaceExistingFiles)
        {
            DownloadClientItem downloadClientItem = null;
            var directoryInfo = new DirectoryInfo(folder);

            try
            {
                artist ??= _parsingService.GetArtist(directoryInfo.Name);
            }
            catch (MultipleArtistsFoundException e)
            {
                _logger.Warn(e, "Unable to find artist from title");
            }

            if (downloadId.IsNotNullOrWhiteSpace())
            {
                var trackedDownload = _trackedDownloadService.Find(downloadId);
                downloadClientItem = trackedDownload.DownloadItem;

                if (artist == null)
                {
                    artist = trackedDownload.RemoteAlbum?.Artist;
                }
            }

            var artistFiles = _diskScanService.GetAudioFiles(folder).ToList();

            if (artist == null && artistFiles.Count > 3000)
            {
                _logger.Warn("Unable to determine artist from folder name and found more than 3000 files. Skipping parsing");
                return ProcessDownloadDirectory(folder, artistFiles);
            }

            var idOverrides = new IdentificationOverrides
            {
                Artist = artist
            };
            var itemInfo = new ImportDecisionMakerInfo
            {
                DownloadClientItem = downloadClientItem,
                ParsedAlbumInfo = Parser.Parser.ParseAlbumTitle(directoryInfo.Name)
            };
            var config = new ImportDecisionMakerConfig
            {
                Filter = filter,
                NewDownload = true,
                SingleRelease = false,
                IncludeExisting = !replaceExistingFiles,

                // Allow the identification service to fetch remote (Skyhook) candidates
                // so that artists not yet in the library appear as proper matches on the
                // Music Import page.  EnsureArtistAdded / EnsureAlbumAdded in
                // ImportApprovedTracks will add them to the library when the user
                // triggers the import.
                AddNewArtists = true
            };

            var decisions = _importDecisionMaker.GetImportDecisions(artistFiles, idOverrides, itemInfo, config);

            // paths will be different for new and old files which is why we need to map separately
            var newFiles = artistFiles.Join(decisions,
                                            f => f.FullName,
                                            d => d.Item.Path,
                                            (f, d) => new { File = f, Decision = d },
                                            PathEqualityComparer.Instance);

            var newItems = newFiles.Select(x => MapItem(x.Decision, downloadId, replaceExistingFiles, false));
            var existingDecisions = decisions.Except(newFiles.Select(x => x.Decision));
            var existingItems = existingDecisions.Select(x => MapItem(x, null, replaceExistingFiles, false));

            return newItems.Concat(existingItems).ToList();
        }

        private List<ManualImportItem> ProcessDownloadDirectory(string folder, List<IFileInfo> audioFiles)
        {
            var items = new List<ManualImportItem>();

            foreach (var file in audioFiles)
            {
                var localTrack = new LocalTrack();
                localTrack.Path = file.FullName;
                localTrack.Quality = new QualityModel(Quality.Unknown);
                localTrack.Size = file.Length;

                items.Add(MapItem(new ImportDecision<LocalTrack>(localTrack), null, false, false));
            }

            return items;
        }

        public List<ManualImportItem> UpdateItems(List<ManualImportItem> items)
        {
            var replaceExistingFiles = items.All(x => x.ReplaceExistingFiles);
            var groupedItems = items.Where(x => !x.AdditionalFile).GroupBy(x => x.Album?.Id);
            _logger.Debug($"UpdateItems, {groupedItems.Count()} groups, replaceExisting {replaceExistingFiles}");

            var result = new List<ManualImportItem>();

            foreach (var group in groupedItems)
            {
                _logger.Debug("UpdateItems, group key: {0}", group.Key);

                var disableReleaseSwitching = group.First().DisableReleaseSwitching;

                var files = group.Select(x => _diskProvider.GetFileInfo(x.Path)).ToList();
                var idOverride = new IdentificationOverrides
                {
                    Artist = group.First().Artist,
                    Album = group.First().Album,
                    AlbumRelease = group.First().Release
                };
                var config = new ImportDecisionMakerConfig
                {
                    Filter = FilterFilesType.None,
                    NewDownload = true,
                    SingleRelease = true,
                    IncludeExisting = !replaceExistingFiles,
                    AddNewArtists = false
                };
                var decisions = _importDecisionMaker.GetImportDecisions(files, idOverride, null, config);

                var existingItems = group.Join(decisions,
                                               i => i.Path,
                                               d => d.Item.Path,
                                               (i, d) => new { Item = i, Decision = d },
                                               PathEqualityComparer.Instance);

                foreach (var pair in existingItems)
                {
                    var item = pair.Item;
                    var decision = pair.Decision;

                    if (decision.Item.Artist != null)
                    {
                        item.Artist = decision.Item.Artist;
                    }

                    if (decision.Item.Album != null)
                    {
                        item.Album = decision.Item.Album;
                        item.Release = decision.Item.Release;
                    }

                    if (decision.Item.Tracks.Any())
                    {
                        item.Tracks = decision.Item.Tracks;
                    }

                    if (item.Quality?.Quality == Quality.Unknown)
                    {
                        item.Quality = decision.Item.Quality;
                    }

                    if (item.ReleaseGroup.IsNullOrWhiteSpace())
                    {
                        item.ReleaseGroup = decision.Item.ReleaseGroup;
                    }

                    item.Rejections = decision.Rejections;
                    item.Size = decision.Item.Size;

                    result.Add(item);
                }

                var newDecisions = decisions.Except(existingItems.Select(x => x.Decision));
                result.AddRange(newDecisions.Select(x => MapItem(x, null, replaceExistingFiles, disableReleaseSwitching)));
            }

            return result;
        }

        private ManualImportItem MapItem(ImportDecision<LocalTrack> decision, string downloadId, bool replaceExistingFiles, bool disableReleaseSwitching)
        {
            var item = new ManualImportItem();

            item.Id = HashConverter.GetHashInt31(decision.Item.Path);
            item.Path = decision.Item.Path;
            item.Name = Path.GetFileNameWithoutExtension(decision.Item.Path);
            item.DownloadId = downloadId;

            if (decision.Item.Artist != null)
            {
                item.Artist = decision.Item.Artist;

                item.CustomFormats = _formatCalculator.ParseCustomFormat(decision.Item);
            }

            if (decision.Item.Album != null)
            {
                item.Album = decision.Item.Album;
                item.Release = decision.Item.Release;
            }

            if (decision.Item.Tracks.Any())
            {
                item.Tracks = decision.Item.Tracks;
            }

            item.Quality = decision.Item.Quality;
            item.IndexerFlags = (int)decision.Item.IndexerFlags;

            // Flag the item when any matched track already has a library file so the
            // Music Import UI can warn the user before they trigger an import.
            item.HasExistingFiles = decision.Item.Tracks.Any(t => t.TrackFileId > 0);

            try
            {
                item.Size = _diskProvider.GetFileSize(decision.Item.Path);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to get file size for '{0}' — non-ASCII filename encoding issue? Size will be reported as 0.", decision.Item.Path);
                item.Size = 0;
            }
            item.Rejections = decision.Rejections;
            item.Tags = decision.Item.FileTrackInfo;
            item.AdditionalFile = decision.Item.AdditionalFile;
            item.ReplaceExistingFiles = replaceExistingFiles;
            item.DisableReleaseSwitching = disableReleaseSwitching;

            return item;
        }

        public void Execute(ManualImportCommand message)
        {
            _logger.ProgressTrace("Manually importing {0} files using mode {1}", message.Files.Count, message.ImportMode);

            var imported = new List<ImportResult>();
            var importedTrackedDownload = new List<ManuallyImportedFile>();
            var fileCount = 0;

            // Files for artists already in the library AND whose album is already in the
            // library use DB IDs and the standard path.
            //
            // All other files go through the re-identification path (AddNewArtists = true)
            // so that EnsureArtistAdded / EnsureAlbumAdded can add missing artists or albums
            // synchronously before the import runs.
            //
            // The "known artist, new album" case arises when the Music Import scan matched
            // the artist against the DB but identified the album against a remote Skyhook
            // result that isn't yet in the library (album.Id == 0 in the scan result).
            // The frontend sends ArtistId > 0, AlbumId == 0 — those files must also go
            // through re-identification so the album gets added before import.
            var newArtistFiles = message.Files
                .Where(f => f.ArtistId == 0 || f.AlbumId <= 0 || f.AlbumReleaseId <= 0)
                .ToList();

            var knownArtistFiles = message.Files
                .Where(f => f.ArtistId > 0 && f.AlbumId > 0 && f.AlbumReleaseId > 0)
                .ToList();

            var albumIds = knownArtistFiles.GroupBy(e => e.AlbumId).ToList();

            foreach (var importAlbumId in albumIds)
            {
                var albumImportDecisions = new List<ImportDecision<LocalTrack>>();

                // turn off anyReleaseOk if specified
                if (importAlbumId.First().DisableReleaseSwitching)
                {
                    var album = _albumService.GetAlbum(importAlbumId.First().AlbumId);
                    album.AnyReleaseOk = false;
                    _albumService.UpdateAlbum(album);
                }

                foreach (var file in importAlbumId)
                {
                    _logger.ProgressTrace("Processing file {0} of {1}", fileCount + 1, message.Files.Count);

                    var artist = _artistService.GetArtist(file.ArtistId);
                    var album = _albumService.GetAlbum(file.AlbumId);
                    var release = _releaseService.GetRelease(file.AlbumReleaseId);
                    var tracks = _trackService.GetTracks(file.TrackIds);
                    var fileTrackInfo = _audioTagService.ReadTags(file.Path) ?? new ParsedTrackInfo();
                    var fileInfo = _diskProvider.GetFileInfo(file.Path);

                    var localTrack = new LocalTrack
                    {
                        ExistingFile = artist.Path.IsParentPath(file.Path),
                        Tracks = tracks,
                        FileTrackInfo = fileTrackInfo,
                        Path = file.Path,
                        Size = fileInfo.Length,
                        Modified = fileInfo.LastWriteTimeUtc,
                        Quality = file.Quality,
                        IndexerFlags = (IndexerFlags)file.IndexerFlags,
                        Artist = artist,
                        Album = album,
                        Release = release
                    };

                    var importDecision = new ImportDecision<LocalTrack>(localTrack);
                    if (_rootFolderService.GetBestRootFolder(artist.Path) == null)
                    {
                        _logger.Warn($"Destination artist folder {artist.Path} not in a Root Folder, skipping import");
                        importDecision.Reject(new Rejection($"Destination artist folder {artist.Path} is not in a Root Folder"));
                    }

                    albumImportDecisions.Add(importDecision);
                    fileCount += 1;
                }

                var downloadId = importAlbumId.Select(x => x.DownloadId).FirstOrDefault(x => x.IsNotNullOrWhiteSpace());
                if (downloadId.IsNullOrWhiteSpace())
                {
                    imported.AddRange(_importApprovedTracks.Import(albumImportDecisions, message.ReplaceExistingFiles, null, message.ImportMode));
                }
                else
                {
                    var trackedDownload = _trackedDownloadService.Find(downloadId);
                    var importResults = _importApprovedTracks.Import(albumImportDecisions, message.ReplaceExistingFiles, trackedDownload.DownloadItem, message.ImportMode);

                    imported.AddRange(importResults);

                    foreach (var importResult in importResults)
                    {
                        importedTrackedDownload.Add(new ManuallyImportedFile
                        {
                            TrackedDownload = trackedDownload,
                            ImportResult = importResult
                        });
                    }
                }
            }

            // Import files whose artist or album is not yet in the library (or whose DB IDs
            // were not resolved during the scan, e.g. a Skyhook-identified album for an
            // artist already in the library).  Re-run identification with AddNewArtists = true
            // so the full EnsureArtistAdded / EnsureAlbumAdded pipeline fires: it fetches
            // artist and album metadata from Skyhook, inserts them into the DB (including a
            // synchronous RefreshAlbumInfo to populate tracks), then proceeds with the normal
            // file import and move.  At the end it queues a background BulkRefreshArtistCommand
            // to fill in covers, biography, etc.
            if (newArtistFiles.Any())
            {
                _logger.ProgressInfo("Importing {0} file(s) requiring re-identification (new artist or album)", newArtistFiles.Count);

                var fileInfos = newArtistFiles.Select(f => _diskProvider.GetFileInfo(f.Path)).ToList();
                var newArtistConfig = new ImportDecisionMakerConfig
                {
                    Filter = FilterFilesType.None,
                    NewDownload = true,
                    SingleRelease = false,
                    IncludeExisting = !message.ReplaceExistingFiles,
                    AddNewArtists = true
                };

                var newArtistDecisions = _importDecisionMaker.GetImportDecisions(fileInfos, null, null, newArtistConfig);
                var newArtistResults = _importApprovedTracks.Import(newArtistDecisions, message.ReplaceExistingFiles, null, message.ImportMode);
                imported.AddRange(newArtistResults);
                fileCount += newArtistFiles.Count;
            }

            _logger.ProgressTrace("Manually imported {0} files", imported.Count);

            // When requested, delete source files that were submitted but not
            // successfully imported (rejected by quality check, unrecognised, etc.).
            // Only files that still exist at their original path are removed — a
            // successful Move import will already have relocated the file.
            if (message.DeleteRejectedFiles)
            {
                var rejectedPaths = imported
                    .Where(r => r.Result != ImportResultType.Imported)
                    .Select(r => r.ImportDecision.Item.Path)
                    .Distinct()
                    .ToList();

                foreach (var path in rejectedPaths)
                {
                    try
                    {
                        if (_diskProvider.FileExists(path))
                        {
                            _logger.Info("Deleting rejected/unimported source file: {0}", path);
                            _diskProvider.DeleteFile(path);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Failed to delete rejected source file '{0}'", path);
                    }
                }
            }

            foreach (var groupedTrackedDownload in importedTrackedDownload.GroupBy(i => i.TrackedDownload.DownloadItem.DownloadId).ToList())
            {
                var trackedDownload = groupedTrackedDownload.First().TrackedDownload;
                var importArtist = groupedTrackedDownload.First().ImportResult.ImportDecision.Item.Artist;
                var outputPath = trackedDownload.ImportItem.OutputPath.FullPath;

                if (_diskProvider.FolderExists(outputPath))
                {
                    if (_downloadedTracksImportService.ShouldDeleteFolder(
                            _diskProvider.GetDirectoryInfo(outputPath), importArtist) &&
                        trackedDownload.DownloadItem.CanMoveFiles)
                    {
                        _diskProvider.DeleteFolder(outputPath, true);
                    }
                }

                var remoteTrackCount = Math.Max(1, trackedDownload.RemoteAlbum?.Albums.Sum(x => x.AlbumReleases.Value.Where(y => y.Monitored).Sum(z => z.TrackCount)) ?? 1);

                var importResults = groupedTrackedDownload.Select(c => c.ImportResult).ToList();
                var importedTrackCount = importResults.Where(c => c.Result == ImportResultType.Imported)
                    .SelectMany(c => c.ImportDecision.Item.Tracks)
                    .Count();

                var allTracksImported = (importResults.Any() && importResults.All(c => c.Result == ImportResultType.Imported)) || importedTrackCount >= remoteTrackCount;

                if (allTracksImported)
                {
                    trackedDownload.State = TrackedDownloadState.Imported;
                    _eventAggregator.PublishEvent(new DownloadCompletedEvent(trackedDownload, importArtist.Id));
                }
            }
        }
    }
}
