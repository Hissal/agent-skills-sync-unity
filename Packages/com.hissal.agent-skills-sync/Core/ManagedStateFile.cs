using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// The <c>.gitignore</c> the tool owns inside each skills folder. It ignores exactly the names the tool
    /// manages there, and doubles as the record of which entries are the tool's (everything else is foreign).
    /// </summary>
    public static class ManagedStateFile
    {
        public const string FileName = ".gitignore";

        const string Header =
            "# Managed by Agent Skills Sync from skills-lock.json; do not edit.\n" +
            "# Lists exactly the skills the tool installed here. Anything else in this folder stays tracked.\n";

        /// <summary>The managed names recorded in <paramref name="folderPath"/>; empty when there is no file.</summary>
        public static IReadOnlyList<string> Read(string folderPath)
        {
            var path = Path.Combine(folderPath, FileName);
            if (!File.Exists(path)) return Array.Empty<string>();
            return File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
                .Select(line => line.Trim('/'))
                .Where(name => name.Length > 0)
                .ToList();
        }

        /// <summary>Records <paramref name="names"/> as managed, creating the folder if needed. Leaves an unchanged file untouched.</summary>
        public static void Write(string folderPath, IEnumerable<string> names)
        {
            var content = new StringBuilder(Header);
            // No trailing slash: git sees a symlink as a file, and "/name/" would not match it.
            foreach (var name in names.OrderBy(n => n, StringComparer.Ordinal))
                content.Append('/').Append(name).Append('\n');

            var path = Path.Combine(folderPath, FileName);
            var text = content.ToString();
            if (File.Exists(path) && File.ReadAllText(path) == text) return;
            Directory.CreateDirectory(folderPath);
            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }
}
