using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>Reads the project's skills folders into a <see cref="ProjectState"/> for the planner.</summary>
    public static class ProjectScanner
    {
        public static ProjectState Scan(string projectRoot, FolderLayout layout) =>
            new ProjectState(layout.Folders.Select(folder => ScanFolder(projectRoot, folder)));

        static FolderState ScanFolder(string projectRoot, SkillsFolder folder)
        {
            var path = Paths.InProject(projectRoot, folder.RelativePath);
            if (!Directory.Exists(path)) return new FolderState(folder, null, null);

            var entries = Directory.GetFileSystemEntries(path)
                .Select(Path.GetFileName)
                .Where(name => name != ManagedStateFile.FileName)
                .ToList();
            var managed = ManagedStateFile.Read(path);

            // Only canonical copies are compared with the lock; link folders point at them.
            var hashes = new Dictionary<string, string>();
            if (folder.Role == SkillsFolderRole.Canonical)
                foreach (var name in managed.Where(entries.Contains))
                {
                    var copy = Path.Combine(path, name);
                    if (Directory.Exists(copy)) hashes[name] = SkillFolderHash.Compute(copy);
                }

            return new FolderState(folder, entries, managed, hashes);
        }
    }
}
