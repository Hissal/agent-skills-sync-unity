using System;
using System.IO;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    internal static class Paths
    {
        static readonly char[] Separators = { '/', '\\' };

        static readonly bool CaseInsensitive = Path.DirectorySeparatorChar == '\\';

        /// <summary>Compares paths as this machine's file system does: ignoring case on Windows, exactly elsewhere.</summary>
        public static StringComparer Comparer { get; } = CaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        /// <summary>The <see cref="StringComparison"/> of <see cref="Comparer"/>.</summary>
        public static StringComparison Comparison { get; } = CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>Absolute path of a forward-slash project-relative path.</summary>
        public static string InProject(string projectRoot, string relativePath) =>
            Path.GetFullPath(Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        /// <summary>Path of <paramref name="target"/> relative to the directory <paramref name="fromDirectory"/>.</summary>
        public static string Relative(string fromDirectory, string target)
        {
            var from = Path.GetFullPath(fromDirectory).Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            var to = Path.GetFullPath(target).Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            var common = 0;
            while (common < from.Length && common < to.Length && string.Equals(from[common], to[common], Comparison))
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
