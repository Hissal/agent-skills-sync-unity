using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Hissal.AgentSkillsSync
{
    /// <summary>Operations on a link folder entry, whichever <see cref="LinkMethod"/> made it.</summary>
    public static class DirectoryLink
    {
        /// <summary>
        /// Removes the entry at <paramref name="linkPath"/>. A symlink or junction is removed without touching
        /// its target; a real folder (a copied link, or a canonical copy) is deleted with its contents. A missing
        /// entry is fine. Never recurses into a symlink or junction.
        /// </summary>
        public static void Remove(string linkPath)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(linkPath);
            }
            catch (FileNotFoundException)
            {
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }

            var isDirectory = (attributes & FileAttributes.Directory) != 0;
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                // Windows removes a directory link with a non-recursive RemoveDirectory; elsewhere a symlink is a file.
                if (isDirectory && Path.DirectorySeparatorChar == '\\') Directory.Delete(linkPath);
                else File.Delete(linkPath);
            }
            else if (isDirectory)
            {
                Directory.Delete(linkPath, recursive: true);
            }
            else
            {
                File.Delete(linkPath);
            }
        }

        /// <summary>True when <paramref name="path"/> is a symlink or junction (reparse point), dangling or not.</summary>
        public static bool IsLink(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// True when the symlink or junction at <paramref name="linkPath"/> resolves to the existing folder
        /// <paramref name="targetPath"/>. False when the link is broken, points anywhere else, or the target is missing.
        /// </summary>
        public static bool ResolvesTo(string linkPath, string targetPath)
        {
            var link = FinalPath(linkPath);
            var target = FinalPath(targetPath);
            if (link == null || target == null) return false;
            return string.Equals(link, target, Paths.Comparison);
        }

        /// <summary>The fully resolved path of an existing folder (every link followed), or null when it can't be resolved.</summary>
        static string FinalPath(string path)
        {
            try
            {
                if (Path.DirectorySeparatorChar == '\\') return WindowsFinalPath(path);
                var resolved = realpath(path, IntPtr.Zero);
                if (resolved == IntPtr.Zero) return null;
                try
                {
                    return Marshal.PtrToStringAnsi(resolved);
                }
                finally
                {
                    free(resolved);
                }
            }
            catch (DllNotFoundException)
            {
                return null;
            }
            catch (EntryPointNotFoundException)
            {
                return null;
            }
        }

        const uint FileShareAll = 0x7;
        const uint OpenExisting = 3;
        const uint FileFlagBackupSemantics = 0x02000000;

        static string WindowsFinalPath(string path)
        {
            using (var handle = CreateFileW(path, 0, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero))
            {
                if (handle.IsInvalid) return null;
                var buffer = new StringBuilder(1024);
                var length = GetFinalPathNameByHandleW(handle, buffer, buffer.Capacity, 0);
                if (length == 0) return null;
                if (length >= buffer.Capacity)
                {
                    buffer = new StringBuilder((int)length + 1);
                    length = GetFinalPathNameByHandleW(handle, buffer, buffer.Capacity, 0);
                    if (length == 0 || length >= buffer.Capacity) return null;
                }
                return buffer.ToString().TrimEnd('\\');
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetFinalPathNameByHandleW(SafeFileHandle hFile, StringBuilder lpszFilePath, int cchFilePath, uint dwFlags);

        [DllImport("libc", SetLastError = true)]
        static extern IntPtr realpath(string path, IntPtr resolvedPath);

        [DllImport("libc")]
        static extern void free(IntPtr pointer);
    }
}
