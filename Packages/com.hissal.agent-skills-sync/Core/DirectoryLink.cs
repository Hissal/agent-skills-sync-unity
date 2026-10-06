using System.IO;

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
    }
}
