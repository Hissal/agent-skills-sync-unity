using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// The tool's block inside the <c>.gitignore</c> of each skills folder. The block is committed, so it lists the
    /// same names on every machine: every locked skill, plus project-authored skills in link folders (see
    /// <see cref="InstallPlan.IgnoredNames"/>). Which entries this machine actually manages is kept in its
    /// <see cref="LocalPrefs.ManagedSkills"/>; the block is read as that record only before a sync first wrote it.
    /// Lines outside the block belong to the user: they are kept as they are and never read as the tool's.
    /// </summary>
    public static class ManagedStateFile
    {
        public const string FileName = ".gitignore";

        const string BeginMarker = "# >>> Agent Skills Sync: managed from skills-lock.json; do not edit this block.";
        const string EndMarker = "# <<< Agent Skills Sync";

        const string BlockIntro =
            "# Lists every skill the tool may install or link here, on any machine. Anything else stays tracked.\n";

        /// <summary>
        /// The names listed in <paramref name="folderPath"/>'s block; empty when there is no file or
        /// the file has no block (a <c>.gitignore</c> the tool never wrote to).
        /// </summary>
        public static IReadOnlyList<string> Read(string folderPath)
        {
            var path = Path.Combine(folderPath, FileName);
            if (!File.Exists(path)) return Array.Empty<string>();

            var text = File.ReadAllText(path);
            if (!TryFindBlock(text, out var start, out var end)) return Array.Empty<string>();

            return text.Substring(start, end - start)
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
                .Select(line => line.Trim('/'))
                .Where(name => name.Length > 0)
                .ToList();
        }

        /// <summary>
        /// Lists <paramref name="names"/> in the block, creating the folder if needed. Replaces only the tool's
        /// block (appending one to a file that has none) and leaves an unchanged file untouched.
        /// </summary>
        public static void Write(string folderPath, IEnumerable<string> names)
        {
            var block = new StringBuilder(BeginMarker).Append('\n').Append(BlockIntro);
            // No trailing slash: git sees a symlink as a file, and "/name/" would not match it.
            foreach (var name in names.OrderBy(n => n, StringComparer.Ordinal))
                block.Append('/').Append(name).Append('\n');
            block.Append(EndMarker).Append('\n');

            var path = Path.Combine(folderPath, FileName);
            var existing = File.Exists(path) ? File.ReadAllText(path) : "";

            string text;
            if (TryFindBlock(existing, out var start, out var end))
            {
                text = existing.Substring(0, start) + block + existing.Substring(end);
            }
            else
            {
                // Nothing to record and no block yet: an empty block would only add noise to the user's file.
                if (existing.Length > 0 && !names.Any()) return;
                var before = existing;
                if (before.Length > 0 && !before.EndsWith("\n", StringComparison.Ordinal)) before += "\n";
                if (before.Length > 0) before += "\n";
                text = before + block;
            }

            if (text == existing) return;
            Directory.CreateDirectory(folderPath);
            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        /// <summary>
        /// Finds the tool's block: <paramref name="start"/> is the begin marker line's first char, and
        /// <paramref name="end"/> is just past the end marker line (or the end of the text when it is missing).
        /// </summary>
        static bool TryFindBlock(string text, out int start, out int end)
        {
            start = end = -1;
            var lineStart = 0;
            while (lineStart < text.Length)
            {
                var newline = text.IndexOf('\n', lineStart);
                var lineEnd = newline < 0 ? text.Length : newline + 1;
                var line = text.Substring(lineStart, lineEnd - lineStart).Trim();

                if (start < 0 && line == BeginMarker)
                {
                    start = lineStart;
                }
                else if (start >= 0 && line == EndMarker)
                {
                    end = lineEnd;
                    return true;
                }

                lineStart = lineEnd;
            }

            if (start < 0) return false;
            end = text.Length;
            return true;
        }
    }
}
