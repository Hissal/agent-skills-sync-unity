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
