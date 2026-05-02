using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;

namespace NzbDrone.Common.Disk
{
    public abstract class DiskProviderBase : IDiskProvider
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(DiskProviderBase));
        protected readonly IFileSystem _fileSystem;

        public DiskProviderBase(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
        }

        public static StringComparison PathStringComparison
        {
            get
            {
                if (OsInfo.IsWindows)
                {
                    return StringComparison.OrdinalIgnoreCase;
                }

                return StringComparison.Ordinal;
            }
        }

        public abstract long? GetAvailableSpace(string path);
        public abstract void InheritFolderPermissions(string filename);
        public abstract void SetEveryonePermissions(string filename);
        public abstract void SetFilePermissions(string path, string mask, string group);
        public abstract void SetPermissions(string path, string mask, string group);
        public abstract void CopyPermissions(string sourcePath, string targetPath);
        public abstract long? GetTotalSize(string path);

        public DateTime FolderGetCreationTime(string path)
        {
            CheckFolderExists(path);

            return _fileSystem.DirectoryInfo.FromDirectoryName(path).CreationTimeUtc;
        }

        public DateTime FolderGetLastWrite(string path)
        {
            CheckFolderExists(path);

            var dirFiles = GetFiles(path, true).ToList();

            if (!dirFiles.Any())
            {
                return _fileSystem.DirectoryInfo.FromDirectoryName(path).LastWriteTimeUtc;
            }

            return dirFiles.Select(f => _fileSystem.FileInfo.FromFileName(f)).Max(c => c.LastWriteTimeUtc);
        }

        public DateTime FileGetLastWrite(string path)
        {
            CheckFileExists(path);

            return _fileSystem.FileInfo.FromFileName(path).LastWriteTimeUtc;
        }

        private void CheckFolderExists(string path)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);

            if (!FolderExists(path))
            {
                throw new DirectoryNotFoundException("Directory doesn't exist. " + path);
            }
        }

        private void CheckFileExists(string path)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);

            if (!FileExists(path))
            {
                throw new FileNotFoundException("File doesn't exist: " + path);
            }
        }

        public void EnsureFolder(string path)
        {
            if (!FolderExists(path))
            {
                CreateFolder(path);
            }
        }

        public bool FolderExists(string path)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);
            return _fileSystem.Directory.Exists(path);
        }

        public bool FileExists(string path)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);
            return FileExists(path, PathStringComparison);
        }

        public bool FileExists(string path, StringComparison stringComparison)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);

            switch (stringComparison)
            {
                case StringComparison.CurrentCulture:
                case StringComparison.InvariantCulture:
                case StringComparison.Ordinal:
                    {
                        return _fileSystem.File.Exists(path) && path == path.GetActualCasing();
                    }

                default:
                    {
                        return _fileSystem.File.Exists(path);
                    }
            }
        }

        public bool FolderWritable(string path)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);

            try
            {
                var testPath = Path.Combine(path, "lidarr_write_test.txt");
                var testContent = $"This file was created to verify if '{path}' is writable. It should've been automatically deleted. Feel free to delete it.";
                WriteAllText(testPath, testContent);
                _fileSystem.File.Delete(testPath);
                return true;
            }
            catch (Exception e)
            {
                Logger.Trace("Directory '{0}' isn't writable. {1}", path, e.Message);
                return false;
            }
        }

        public bool FolderEmpty(string path)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);

            return _fileSystem.Directory.EnumerateFileSystemEntries(path).Empty();
        }

        public IEnumerable<string> GetDirectories(string path)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);

            return _fileSystem.Directory.EnumerateDirectories(path);
        }

        public string[] GetDirectories(string path, SearchOption searchOption)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);

            return _fileSystem.Directory.GetDirectories(path, "*", searchOption);
        }

        public IEnumerable<string> GetFiles(string path, bool recursive)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);

            return _fileSystem.Directory.EnumerateFiles(path, "*", new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                IgnoreInaccessible = true
            });
        }

        public long GetFolderSize(string path)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);

            return GetFiles(path, true).Sum(e => _fileSystem.FileInfo.FromFileName(e).Length);
        }

        public long GetFileSize(string path)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);

            if (!FileExists(path))
            {
                throw new FileNotFoundException("File doesn't exist: " + path);
            }

            var fi = _fileSystem.FileInfo.FromFileName(path);
            return fi.Length;
        }

        public void CreateFolder(string path)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);
            _fileSystem.Directory.CreateDirectory(path);
        }

        public void DeleteFile(string path)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);
            Logger.Trace("Deleting file: {0}", path);

            RemoveReadOnly(path);

            _fileSystem.File.Delete(path);
        }

        public void CloneFile(string source, string destination, bool overwrite = false)
        {
            Ensure.That(source, () => source).IsValidPath(PathValidationType.CurrentOs);
            Ensure.That(destination, () => destination).IsValidPath(PathValidationType.CurrentOs);

            if (source.PathEquals(destination))
            {
                throw new IOException(string.Format("Source and destination can't be the same {0}", source));
            }

            CloneFileInternal(source, destination, overwrite);
        }

        protected virtual void CloneFileInternal(string source, string destination, bool overwrite = false)
        {
            CopyFileInternal(source, destination, overwrite);
        }

        public void CopyFile(string source, string destination, bool overwrite = false)
        {
            Ensure.That(source, () => source).IsValidPath(PathValidationType.CurrentOs);
            Ensure.That(destination, () => destination).IsValidPath(PathValidationType.CurrentOs);

            if (source.PathEquals(destination))
            {
                throw new IOException(string.Format("Source and destination can't be the same {0}", source));
            }

            CopyFileInternal(source, destination, overwrite);
        }

        protected virtual void CopyFileInternal(string source, string destination, bool overwrite = false)
        {
            _fileSystem.File.Copy(source, destination, overwrite);
        }

        public void MoveFile(string source, string destination, bool overwrite = false)
        {
            Ensure.That(source, () => source).IsValidPath(PathValidationType.CurrentOs);
            Ensure.That(destination, () => destination).IsValidPath(PathValidationType.CurrentOs);

            if (source.PathEquals(destination))
            {
                throw new IOException(string.Format("Source and destination can't be the same {0}", source));
            }

            if (FileExists(destination) && overwrite)
            {
                DeleteFile(destination);
            }

            RemoveReadOnly(source);
            MoveFileInternal(source, destination);
        }

        public void MoveFolder(string source, string destination)
        {
            Ensure.That(source, () => source).IsValidPath(PathValidationType.CurrentOs);
            Ensure.That(destination, () => destination).IsValidPath(PathValidationType.CurrentOs);

            Directory.Move(source, destination);
        }

        protected virtual void MoveFileInternal(string source, string destination)
        {
            if (File.Exists(destination))
            {
                throw new FileAlreadyExistsException("File already exists", destination);
            }

            _fileSystem.File.Move(source, destination);
        }

        public virtual bool TryRenameFile(string source, string destination)
        {
            return false;
        }

        public abstract bool TryCreateHardLink(string source, string destination);

        public virtual bool TryCreateRefLink(string source, string destination)
        {
            return false;
        }

        public void DeleteFolder(string path, bool recursive)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);

            var files = GetFiles(path, recursive).ToList();

            files.ForEach(RemoveReadOnly);

            var attempts = 0;

            while (attempts < 3 && files.Any())
            {
                EmptyFolder(path);

                if (GetFiles(path, recursive).Any())
                {
                    // Wait for IO operations to complete  after emptying the folder since they aren't always
                    // instantly removed and it can lead to false positives that files are still present.
                    Thread.Sleep(3000);
                }

                attempts++;
                files = GetFiles(path, recursive).ToList();
            }

            _fileSystem.Directory.Delete(path, recursive);
        }

        public string ReadAllText(string filePath)
        {
            Ensure.That(filePath, () => filePath).IsValidPath(PathValidationType.CurrentOs);

            return _fileSystem.File.ReadAllText(filePath);
        }

        public void WriteAllText(string filename, string contents)
        {
            Ensure.That(filename, () => filename).IsValidPath(PathValidationType.CurrentOs);
            RemoveReadOnly(filename);

            // File.WriteAllText is broken on net core when writing to some CIFS mounts
            // This workaround from https://github.com/dotnet/runtime/issues/42790#issuecomment-700362617
            using (var fs = new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(fs))
                {
                    writer.Write(contents);
                }
            }
        }

        public void FolderSetLastWriteTime(string path, DateTime dateTime)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);

            _fileSystem.Directory.SetLastWriteTimeUtc(path, dateTime);
        }

        public void FileSetLastWriteTime(string path, DateTime dateTime)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);

            _fileSystem.File.SetLastWriteTime(path, dateTime);
        }

        public bool IsFileLocked(string file)
        {
            try
            {
                using (_fileSystem.File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    return false;
                }
            }
            catch (IOException)
            {
                return true;
            }
        }

        public virtual string GetPathRoot(string path)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);

            return Path.GetPathRoot(path);
        }

        public string GetParentFolder(string path)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);

            var parent = _fileSystem.Directory.GetParent(path.TrimEnd(Path.DirectorySeparatorChar));

            if (parent == null)
            {
                return null;
            }

            return parent.FullName;
        }

        private static void RemoveReadOnly(string path)
        {
            if (File.Exists(path))
            {
                var attributes = File.GetAttributes(path);

                if (attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    var newAttributes = attributes & ~FileAttributes.ReadOnly;
                    File.SetAttributes(path, newAttributes);
                }
            }
        }

        public FileAttributes GetFileAttributes(string path)
        {
            return _fileSystem.File.GetAttributes(path);
        }

        public void EmptyFolder(string path)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);

            foreach (var file in GetFiles(path, false))
            {
                DeleteFile(file);
            }

            foreach (var directory in GetDirectories(path))
            {
                DeleteFolder(directory, true);
            }
        }

        public string[] GetFixedDrives()
        {
            return GetMounts().Where(x => x.DriveType == DriveType.Fixed).Select(x => x.RootDirectory).ToArray();
        }

        public string GetVolumeLabel(string path)
        {
            var driveInfo = GetMounts().SingleOrDefault(d => d.RootDirectory.PathEquals(path));

            if (driveInfo == null)
            {
                return null;
            }

            return driveInfo.VolumeLabel;
        }

        public FileStream OpenReadStream(string path)
        {
            if (!FileExists(path))
            {
                throw new FileNotFoundException("Unable to find file: " + path, path);
            }

            return (FileStream)_fileSystem.FileStream.Create(path, FileMode.Open, FileAccess.Read);
        }

        public FileStream OpenWriteStream(string path)
        {
            return (FileStream)_fileSystem.FileStream.Create(path, FileMode.Create);
        }

        public List<IMount> GetMounts()
        {
            return GetAllMounts().Where(d => !IsSpecialMount(d)).ToList();
        }

        protected virtual List<IMount> GetAllMounts()
        {
            return GetDriveInfoMounts().Where(d => d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Network || d.DriveType == DriveType.Removable)
                                       .Select(d => new DriveInfoMount(d))
                                       .Cast<IMount>()
                                       .ToList();
        }

        protected virtual bool IsSpecialMount(IMount mount)
        {
            return false;
        }

        public virtual IMount GetMount(string path)
        {
            try
            {
                var mounts = GetAllMounts();

                return mounts.Where(drive => drive.RootDirectory.PathEquals(path) ||
                                             drive.RootDirectory.IsParentPath(path))
                          .MaxBy(drive => drive.RootDirectory.Length);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, $"Failed to get mount for path {path}");
                return null;
            }
        }

        protected List<IDriveInfo> GetDriveInfoMounts()
        {
            return _fileSystem.DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .ToList();
        }

        public List<IDirectoryInfo> GetDirectoryInfos(string path)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);

            var di = _fileSystem.DirectoryInfo.FromDirectoryName(path);

            return di.GetDirectories().ToList();
        }

        public IDirectoryInfo GetDirectoryInfo(string path)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);
            return _fileSystem.DirectoryInfo.FromDirectoryName(path);
        }

        public List<IFileInfo> GetFileInfos(string path, bool recursive = false)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);

            var di = _fileSystem.DirectoryInfo.FromDirectoryName(path);

            var files = di.EnumerateFiles("*", new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                IgnoreInaccessible = true
            }).ToList();

            // On Linux, .NET replaces non-UTF-8 filename bytes (e.g. Windows-1252
            // curly quotes) with '?' when marshalling readdir output, making those
            // files inaccessible through normal System.IO APIs.
            //
            // Two-pass repair strategy:
            //
            // Pass 1 — if any file has '?' inside its *directory* component, the
            //   parent directories themselves have non-UTF-8 bytes.  RepairAndLocatePath
            //   can only fix the leaf filename; it cannot opendir a parent whose bytes
            //   are also mangled (the '?'-substituted path does not exist on disk).
            //   In this case we call RepairFolderEncoding on the scanned root to rename
            //   the whole subtree in one P/Invoke walk, then re-enumerate so all
            //   IFileInfo objects reflect the corrected paths.
            //
            // Pass 2 — repair any files whose leaf name still contains '?' after pass 1
            //   (e.g., files added after the tree repair, or trees where only the
            //   leaf name is non-UTF-8 and pass 1 was skipped).  RepairAndLocatePath
            //   opens the now-valid parent directory and renames just that entry.
            if (OsInfo.IsNotWindows)
            {
                // Pass 1: detect mangled parent directories
                var anyMangledDir = files.Any(f =>
                    Path.GetDirectoryName(f.FullName)?.Contains('?') == true);

                if (anyMangledDir)
                {
                    LinuxNativeFileHelper.RepairFolderEncoding(path);

                    // Re-enumerate so the returned IFileInfo objects have the
                    // corrected UTF-8 paths rather than the original '?'-mangled ones.
                    files = di.EnumerateFiles("*", new EnumerationOptions
                    {
                        RecurseSubdirectories = recursive,
                        IgnoreInaccessible = true
                    }).ToList();
                }

                // Pass 2: repair any leaf filenames that still contain '?'
                for (var i = 0; i < files.Count; i++)
                {
                    var f = files[i];
                    if (!f.FullName.Contains('?'))
                    {
                        continue;
                    }

                    var repairedPath = LinuxNativeFileHelper.RepairAndLocatePath(f.FullName);
                    if (repairedPath != null)
                    {
                        files[i] = _fileSystem.FileInfo.FromFileName(repairedPath);
                    }
                }
            }

            return files;
        }

        public IFileInfo GetFileInfo(string path)
        {
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);
            return _fileSystem.FileInfo.FromFileName(path);
        }

        public void RemoveEmptySubfolders(string path)
        {
            // Depth first search for empty subdirectories
            foreach (var subdir in Directory.EnumerateDirectories(path))
            {
                RemoveEmptySubfolders(subdir);

                if (Directory.EnumerateFileSystemEntries(subdir).Empty())
                {
                    try
                    {
                        Directory.Delete(subdir, false);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "Failed to remove empty directory {0}", subdir);
                    }
                }
            }
        }

        public void SaveStream(Stream stream, string path)
        {
            using (var fileStream = OpenWriteStream(path))
            {
                stream.CopyTo(fileStream);
            }
        }

        public virtual bool IsValidFolderPermissionMask(string mask)
        {
            throw new NotSupportedException();
        }
    }
}
