using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Hissal.AgentSkillsSync
{
    /// <summary>How a link folder entry was made to show the canonical copy.</summary>
    public enum LinkMethod
    {
        Symlink,
        Junction,
        Copy,
    }

    /// <summary>Creates a folder link. The seam where the symlink, junction and copy fallbacks plug in.</summary>
    public interface ILinkCreator
    {
        /// <summary>Makes <paramref name="linkPath"/> show the contents of the existing directory <paramref name="targetPath"/>.</summary>
        /// <returns>The method that made the link.</returns>
        /// <exception cref="IOException">The link could not be created.</exception>
        LinkMethod CreateDirectoryLink(string linkPath, string targetPath);
    }

    /// <summary>
    /// Creates a relative directory symlink. On Windows this needs Developer Mode or elevation;
    /// without either it throws.
    /// </summary>
    public sealed class SymlinkCreator : ILinkCreator
    {
        const int SymbolicLinkFlagDirectory = 0x1;
        const int SymbolicLinkFlagAllowUnprivilegedCreate = 0x2;

        public LinkMethod CreateDirectoryLink(string linkPath, string targetPath)
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(linkPath));
            var relativeTarget = Paths.Relative(parent, targetPath);

            if (Path.DirectorySeparatorChar == '\\')
            {
                if (!CreateSymbolicLinkW(linkPath, relativeTarget, SymbolicLinkFlagDirectory | SymbolicLinkFlagAllowUnprivilegedCreate))
                    throw new IOException(
                        $"Could not create symlink {linkPath} -> {relativeTarget}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            }
            else if (symlink(relativeTarget, linkPath) != 0)
            {
                throw new IOException($"Could not create symlink {linkPath} -> {relativeTarget} (errno {Marshal.GetLastWin32Error()}).");
            }

            return LinkMethod.Symlink;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        static extern bool CreateSymbolicLinkW(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);

        [DllImport("libc", SetLastError = true)]
        static extern int symlink(string target, string linkPath);
    }
}
