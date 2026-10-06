using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Creates a Windows directory junction (mount point) to an absolute target. Needs no privileges,
    /// but only works on Windows and on local volumes.
    /// </summary>
    public sealed class JunctionCreator : ILinkCreator
    {
        const uint GenericWrite = 0x40000000;
        const uint OpenExisting = 3;
        const uint FileFlagBackupSemantics = 0x02000000;
        const uint FileFlagOpenReparsePoint = 0x00200000;
        const uint FsctlSetReparsePoint = 0x000900A4;
        const uint IoReparseTagMountPoint = 0xA0000003;

        public LinkMethod CreateDirectoryLink(string linkPath, string targetPath)
        {
            if (Path.DirectorySeparatorChar != '\\')
                throw new IOException("Directory junctions exist only on Windows.");
            if (Directory.Exists(linkPath) || File.Exists(linkPath))
                throw new IOException($"Could not create junction {linkPath}: it already exists.");

            var target = Path.GetFullPath(targetPath).TrimEnd('\\');
            Directory.CreateDirectory(linkPath);
            try
            {
                using (var handle = CreateFileW(linkPath, GenericWrite, 0, IntPtr.Zero, OpenExisting,
                           FileFlagBackupSemantics | FileFlagOpenReparsePoint, IntPtr.Zero))
                {
                    if (handle.IsInvalid) throw Failure(linkPath, target);
                    var buffer = MountPointReparseData(target);
                    if (!DeviceIoControl(handle, FsctlSetReparsePoint, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
                        throw Failure(linkPath, target);
                }
            }
            catch
            {
                Directory.Delete(linkPath);
                throw;
            }

            return LinkMethod.Junction;
        }

        static IOException Failure(string linkPath, string target) =>
            new IOException($"Could not create junction {linkPath} -> {target}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");

        // REPARSE_DATA_BUFFER for IO_REPARSE_TAG_MOUNT_POINT: tag, data length, reserved, then the
        // substitute name ("\??\C:\...") and print name offsets/lengths and the two null-terminated names.
        static byte[] MountPointReparseData(string target)
        {
            var substitute = Encoding.Unicode.GetBytes(@"\??\" + target);
            var print = Encoding.Unicode.GetBytes(target);
            var pathBufferLength = substitute.Length + 2 + print.Length + 2;

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(IoReparseTagMountPoint);
                writer.Write((ushort)(8 + pathBufferLength));
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write((ushort)substitute.Length);
                writer.Write((ushort)(substitute.Length + 2));
                writer.Write((ushort)print.Length);
                writer.Write(substitute);
                writer.Write((ushort)0);
                writer.Write(print);
                writer.Write((ushort)0);
                writer.Flush();
                return stream.ToArray();
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, byte[] lpInBuffer, int nInBufferSize,
            IntPtr lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);
    }
}
