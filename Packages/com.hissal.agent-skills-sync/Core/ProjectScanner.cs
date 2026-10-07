using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>Reads the project's skills folders into a <see cref="ProjectState"/> for the planner.</summary>
    public static class ProjectScanner
    {
        const string SkillFile = "SKILL.md";

        /// <param name="readContents">
        /// False for a quick scan that reads names and links only: no installed hashes and no check of copied links'
        /// content, so the planner can't see outdated copies (see <see cref="SyncStatus.Read"/>).
        /// </param>
        /// <param name="prefsRoot">The folder holding this machine's <see cref="LocalPrefs"/>; null = <paramref name="projectRoot"/>.</param>
        public static ProjectState Scan(string projectRoot, FolderLayout layout, bool readContents = true, string prefsRoot = null)
        {
            // What this machine manages lives in its local prefs. Until a sync first records it, the names in the
            // committed .gitignore blocks count as managed (before, the block listed exactly this machine's entries).
            // So do they when the record is for another skills root, as after the lock moved.
            var recorded = LocalPrefs.Load(prefsRoot ?? projectRoot).ManagedSkillsFor(projectRoot);
            return new ProjectState(layout.Folders.Select(folder =>
                ScanFolder(projectRoot, folder, layout.Canonical, readContents, recorded)));
        }

        static FolderState ScanFolder(string projectRoot, SkillsFolder folder, SkillsFolder canonicalFolder, bool readContents,
            IReadOnlyDictionary<string, IReadOnlyList<string>> recorded)
        {
            var path = Paths.InProject(projectRoot, folder.RelativePath);
            if (!Directory.Exists(path))
                return new FolderState(folder, null, recorded == null ? null : Recorded(recorded, folder));

            var entries = Directory.GetFileSystemEntries(path)
                .Where(entry => Path.GetFileName(entry) != ManagedStateFile.FileName)
                .ToList();
            var names = entries.Select(Path.GetFileName).ToList();
            var files = entries.Where(entry => !Directory.Exists(entry) && !IsLink(entry)).Select(Path.GetFileName).ToList();
            var withoutSkillFile = entries
                .Where(entry => !files.Contains(Path.GetFileName(entry)) && !File.Exists(Path.Combine(entry, SkillFile)))
                .Select(Path.GetFileName)
                .ToList();
            var ignored = ManagedStateFile.Read(path);
            var managed = recorded == null ? ignored : Recorded(recorded, folder);

            // Only canonical copies are compared with the lock; link folders point at them.
            var hashes = new Dictionary<string, string>();
            if (readContents && folder.Role == SkillsFolderRole.Canonical)
                foreach (var name in managed.Where(names.Contains))
                {
                    var copy = Path.Combine(path, name);
                    if (Directory.Exists(copy)) hashes[name] = SkillFolderHash.Compute(copy);
                }

            // A managed link must show this project's canonical entry. A symlink or junction that is broken or points
            // anywhere else (e.g. a folder copied from another checkout) is re-linked, and so is a link made by the
            // Copy fallback, a real folder that does not follow the canonical copy, once its content differs.
            var staleLinks = new List<string>();
            if (folder.Role == SkillsFolderRole.Link)
            {
                var canonicalPath = Paths.InProject(projectRoot, canonicalFolder.RelativePath);
                foreach (var name in managed.Where(names.Contains))
                {
                    var link = Path.Combine(path, name);
                    var canonical = Path.Combine(canonicalPath, name);
                    if (DirectoryLink.IsLink(link))
                    {
                        if (!DirectoryLink.ResolvesTo(link, canonical)) staleLinks.Add(name);
                    }
                    else if (readContents && Directory.Exists(link) && Directory.Exists(canonical) &&
                             SkillFolderHash.Compute(link) != SkillFolderHash.Compute(canonical))
                    {
                        staleLinks.Add(name);
                    }
                }
            }

            return new FolderState(folder, names, managed, installedHashes: hashes, files: files, staleLinks: staleLinks,
                withoutSkillFile: withoutSkillFile, ignored: ignored);
        }

        static IReadOnlyList<string> Recorded(IReadOnlyDictionary<string, IReadOnlyList<string>> recorded, SkillsFolder folder) =>
            recorded.TryGetValue(folder.RelativePath, out var names) ? names : new string[0];

        // A dangling link fails Directory.Exists but is still a link entry, not a plain file.
        static bool IsLink(string entry) => File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint);
    }
}
