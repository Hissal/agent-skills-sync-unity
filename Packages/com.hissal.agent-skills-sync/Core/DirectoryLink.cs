using System.IO;

namespace Hissal.AgentSkillsSync
{
    /// <summary>Operations on a link folder entry, whichever <see cref="LinkMethod"/> made it.</summary>
    public static class DirectoryLink
    {
        /// <summary>
        /// Removes the link at <paramref name="linkPath"/>. A symlink or junction is removed without touching
        /// its target; a plain copy is deleted with its contents. The canonical copy is never touched.
        /// </summary>
        public static void Remove(string linkPath)
        {
            if (File.GetAttributes(linkPath).HasFlag(FileAttributes.ReparsePoint))
                Directory.Delete(linkPath, recursive: false);
            else
                Directory.Delete(linkPath, recursive: true);
        }
    }
}
