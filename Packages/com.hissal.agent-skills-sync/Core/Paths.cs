using System;
using System.IO;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    internal static class Paths
    {
        static readonly char[] Separators = { '/', '\\' };

        static bool CaseInsensitive => Path.DirectorySeparatorChar == '\\';

        /// <summary>Absolute path of a forward-slash project-relative path.</summary>
        public static string InProject(string projectRoot, string relativePath) =>
            Path.GetFullPath(Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        /// <summary>Path of <paramref name="target"/> relative to the directory <paramref name="fromDirectory"/>.</summary>
        public static string Relative(string fromDirectory, string target)
        {
            var from = Path.GetFullPath(fromDirectory).Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            var to = Path.GetFullPath(target).Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            var comparison = CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            var common = 0;
            while (common < from.Length && common < to.Length && string.Equals(from[common], to[common], comparison))
                common++;
            if (common == 0) return Path.GetFullPath(target);

            var up = Enumerable.Repeat("..", from.Length - common);
            return string.Join(Path.DirectorySeparatorChar.ToString(), up.Concat(to.Skip(common)));
        }

        /// <summary>
        /// Deletes a directory link without touching what it points at; works on dangling links.
        /// A plain folder (a copy standing in for a link) is deleted recursively. A missing entry is fine.
        /// </summary>
        public static void DeleteLink(string path)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(path); }
            catch (FileNotFoundException) { return; }
            catch (DirectoryNotFoundException) { return; }

            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                // Windows removes a directory symlink or junction with RemoveDirectory; Unix unlinks a symlink as a file.
                if (CaseInsensitive && attributes.HasFlag(FileAttributes.Directory)) Directory.Delete(path, recursive: false);
                else File.Delete(path);
            }
            else if (attributes.HasFlag(FileAttributes.Directory))
            {
                Directory.Delete(path, recursive: true);
            }
            else
            {
                File.Delete(path);
            }
        }

        public static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            foreach (var directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
