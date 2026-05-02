using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using NLog;

namespace NzbDrone.Common.Disk
{
    /// <summary>
    /// Repairs non-UTF-8 filenames in import folders using Linux P/Invoke.
    ///
    /// Background: music files ripped on Windows often have filenames encoded in
    /// Windows-1252 (e.g. curly apostrophe U+2019 stored as byte 0x92).  .NET's
    /// managed IO assumes UTF-8 on Linux and replaces invalid bytes with '?',
    /// making the file inaccessible through normal System.IO APIs.  This helper
    /// reads raw filename bytes via opendir/readdir, detects invalid UTF-8,
    /// decodes as Windows-1252, and renames the file to its proper UTF-8 form.
    /// After repair all normal .NET file operations work correctly.
    /// </summary>
    public static class LinuxNativeFileHelper
    {
        // d_type values in struct dirent
        private const byte DtUnknown = 0;
        private const byte DtDir = 4;
        private const byte DtReg = 8;
        private const byte DtLnk = 10;

        // Byte offsets inside struct dirent on 64-bit Linux (glibc & musl):
        //   d_ino  (uint64) offset  0  size 8
        //   d_off  (int64)  offset  8  size 8
        //   d_reclen (u16)  offset 16  size 2
        //   d_type  (u8)    offset 18  size 1
        //   d_name  (char[])offset 19  (up to 256 bytes)
        private const int DirentTypeOffset = 18;
        private const int DirentNameOffset = 19;
        private const int DirentNameMaxLen = 256;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        [DllImport("libc", EntryPoint = "opendir", SetLastError = true)]
        private static extern IntPtr opendir(byte[] name);

        [DllImport("libc", EntryPoint = "closedir")]
        private static extern int closedir(IntPtr dirp);

        [DllImport("libc", EntryPoint = "readdir")]
        private static extern IntPtr readdir(IntPtr dirp);

        [DllImport("libc", EntryPoint = "rename", SetLastError = true)]
        private static extern int rename(byte[] oldpath, byte[] newpath);

        private static readonly Encoding Win1252;

        static LinuxNativeFileHelper()
        {
            Win1252 = Encoding.GetEncoding(1252);
        }

        /// <summary>
        /// Recursively walks <paramref name="folder"/> and renames any file or
        /// directory whose name is not valid UTF-8 to the Windows-1252–decoded
        /// UTF-8 equivalent.  No-op on non-Linux platforms.
        /// </summary>
        public static void RepairFolderEncoding(string folder)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return;
            }

            try
            {
                RepairDirectory(Encoding.UTF8.GetBytes(folder));
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Unexpected error during import folder encoding repair for '{0}'", folder);
            }
        }

        /// <summary>
        /// Given a path that .NET has mangled by substituting '?' for invalid UTF-8
        /// bytes (e.g. Windows-1252 curly quotes), finds the real file in the parent
        /// directory via raw P/Invoke readdir, renames it to proper UTF-8 on disk,
        /// and returns the corrected path string.  Returns null if no match is found.
        /// No-op / returns null on non-Linux platforms.
        /// </summary>
        public static string RepairAndLocatePath(string corruptedPath)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return null;
            }

            try
            {
                var directory = Path.GetDirectoryName(corruptedPath);
                var corruptedName = Path.GetFileName(corruptedPath);

                if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(corruptedName))
                {
                    return null;
                }

                var dirBytes = Encoding.UTF8.GetBytes(directory);
                var dirHandle = opendir(AppendNul(dirBytes));
                if (dirHandle == IntPtr.Zero)
                {
                    return null;
                }

                try
                {
                    while (true)
                    {
                        var entryPtr = readdir(dirHandle);
                        if (entryPtr == IntPtr.Zero)
                        {
                            break;
                        }

                        var rawName = ReadDirentName(entryPtr);
                        if (rawName == null || IsDotEntry(rawName))
                        {
                            continue;
                        }

                        // Only consider files whose bytes are NOT valid UTF-8
                        // (valid UTF-8 files won't have been mangled to '?')
                        if (IsValidUtf8(rawName))
                        {
                            continue;
                        }

                        // Decode as Windows-1252 to recover the intended Unicode name
                        var decodedName = Win1252.GetString(rawName);

                        // Replace every non-ASCII character with '?' and compare.
                        // This is exactly what .NET did when it mangled the path.
                        if (!MatchesMangled(decodedName, corruptedName))
                        {
                            continue;
                        }

                        // Found it — rename to proper UTF-8
                        var utf8NameBytes = Encoding.UTF8.GetBytes(decodedName);
                        var oldFullPath = CombinePath(dirBytes, rawName);
                        var newFullPath = CombinePath(dirBytes, utf8NameBytes);

                        if (rename(AppendNul(oldFullPath), AppendNul(newFullPath)) == 0)
                        {
                            var correctedPath = directory + "/" + decodedName;
                            Logger.Info(
                                "Repaired filename encoding on demand: '{0}' → '{1}'",
                                corruptedName,
                                decodedName);
                            return correctedPath;
                        }
                        else
                        {
                            Logger.Warn(
                                "P/Invoke rename failed for '{0}' (errno {1})",
                                decodedName,
                                Marshal.GetLastWin32Error());
                            return null;
                        }
                    }
                }
                finally
                {
                    closedir(dirHandle);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Unexpected error in RepairAndLocatePath for '{0}'", corruptedPath);
            }

            return null;
        }

        /// <summary>
        /// Returns true when <paramref name="actual"/> (Unicode) matches
        /// <paramref name="mangled"/> after replacing every non-ASCII character
        /// in <paramref name="actual"/> with '?'.
        /// </summary>
        private static bool MatchesMangled(string actual, string mangled)
        {
            if (actual.Length != mangled.Length)
            {
                return false;
            }

            for (var i = 0; i < actual.Length; i++)
            {
                var a = actual[i];
                var m = mangled[i];

                if (a == m)
                {
                    continue;
                }

                // non-ASCII in actual → should appear as '?' in mangled
                if (a > 127 && m == '?')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static void RepairDirectory(byte[] dirPathBytes)
        {
            var dirHandle = opendir(AppendNul(dirPathBytes));
            if (dirHandle == IntPtr.Zero)
            {
                return;
            }

            // Collect subdirectories to recurse into after the directory handle
            // is closed (avoids holding the handle open during recursion).
            var subdirs = new List<byte[]>();

            try
            {
                while (true)
                {
                    var entryPtr = readdir(dirHandle);
                    if (entryPtr == IntPtr.Zero)
                    {
                        break;
                    }

                    var rawName = ReadDirentName(entryPtr);
                    if (rawName == null || IsDotEntry(rawName))
                    {
                        continue;
                    }

                    var dtype = Marshal.ReadByte(entryPtr, DirentTypeOffset);

                    // After potential rename, record the canonical path
                    var finalPath = ProcessEntry(dirPathBytes, rawName, dtype);

                    if (finalPath != null && (dtype == DtDir || dtype == DtUnknown))
                    {
                        subdirs.Add(finalPath);
                    }
                }
            }
            finally
            {
                closedir(dirHandle);
            }

            foreach (var subdir in subdirs)
            {
                RepairDirectory(subdir);
            }
        }

        /// <summary>
        /// Checks whether the entry's name is valid UTF-8.  If not, renames it
        /// to the Windows-1252 → UTF-8 equivalent.
        /// Returns the final full path bytes (UTF-8, no NUL terminator).
        /// </summary>
        private static byte[] ProcessEntry(byte[] dirBytes, byte[] rawName, byte dtype)
        {
            var rawFullPath = CombinePath(dirBytes, rawName);

            if (IsValidUtf8(rawName))
            {
                // Name is already good — just return the path for recursion
                return rawFullPath;
            }

            // Decode from Windows-1252 to get the intended Unicode name
            string correctedName;
            try
            {
                correctedName = Win1252.GetString(rawName);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Cannot decode filename as Windows-1252, skipping repair");
                return rawFullPath;
            }

            var utf8NameBytes = Encoding.UTF8.GetBytes(correctedName);
            var newFullPath = CombinePath(dirBytes, utf8NameBytes);

            var ret = rename(AppendNul(rawFullPath), AppendNul(newFullPath));
            if (ret == 0)
            {
                Logger.Info(
                    "Repaired filename encoding: '{0}' → '{1}'",
                    Win1252.GetString(rawName),
                    correctedName);
                return newFullPath;
            }
            else
            {
                Logger.Warn(
                    "Failed to rename '{0}' to UTF-8 equivalent (errno {1})",
                    correctedName,
                    Marshal.GetLastWin32Error());
                return rawFullPath;
            }
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static byte[] ReadDirentName(IntPtr entryPtr)
        {
            // Use stackalloc for the read buffer to avoid a heap allocation on the
            // hot path.  DirentNameMaxLen is 256 bytes — well within stack budget.
            // We still allocate the returned byte[] because callers take ownership.
            Span<byte> buf = stackalloc byte[DirentNameMaxLen];
            var len = 0;
            for (var i = 0; i < DirentNameMaxLen; i++)
            {
                var b = Marshal.ReadByte(entryPtr, DirentNameOffset + i);
                if (b == 0)
                {
                    break;
                }

                buf[len++] = b;
            }

            return len > 0 ? buf[..len].ToArray() : null;
        }

        private static bool IsDotEntry(byte[] name)
        {
            if (name.Length == 1 && name[0] == (byte)'.')
            {
                return true;
            }

            if (name.Length == 2 && name[0] == (byte)'.' && name[1] == (byte)'.')
            {
                return true;
            }

            return false;
        }

        private static bool IsValidUtf8(byte[] bytes)
        {
            // Validate UTF-8 by inspecting bytes directly rather than by throwing and
            // catching DecoderFallbackException.  Exception-based control flow is
            // orders of magnitude slower and creates GC pressure on libraries that
            // contain non-UTF-8 filenames (the case we care most about).
            var i = 0;
            while (i < bytes.Length)
            {
                var b = bytes[i];
                var extra = 0;
                if (b < 0x80)
                {
                    // ASCII — single byte
                    i++;
                    continue;
                }
                else if ((b & 0xE0) == 0xC0)
                {
                    extra = 1; // 2-byte sequence
                }
                else if ((b & 0xF0) == 0xE0)
                {
                    extra = 2; // 3-byte sequence
                }
                else if ((b & 0xF8) == 0xF0)
                {
                    extra = 3; // 4-byte sequence
                }
                else
                {
                    return false; // Invalid lead byte
                }

                if (i + extra >= bytes.Length)
                {
                    return false; // Truncated
                }

                for (var j = 1; j <= extra; j++)
                {
                    if ((bytes[i + j] & 0xC0) != 0x80)
                    {
                        return false; // Invalid continuation
                    }
                }

                i += 1 + extra;
            }

            return true;
        }

        /// <summary>Returns dir + '/' + name (no NUL terminator).</summary>
        private static byte[] CombinePath(byte[] dir, byte[] name)
        {
            var result = new byte[dir.Length + 1 + name.Length];
            Buffer.BlockCopy(dir, 0, result, 0, dir.Length);
            result[dir.Length] = (byte)'/';
            Buffer.BlockCopy(name, 0, result, dir.Length + 1, name.Length);
            return result;
        }

        /// <summary>Returns a copy of <paramref name="bytes"/> with a NUL appended.</summary>
        private static byte[] AppendNul(byte[] bytes)
        {
            var result = new byte[bytes.Length + 1];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            return result; // last byte is already 0
        }
    }
}
