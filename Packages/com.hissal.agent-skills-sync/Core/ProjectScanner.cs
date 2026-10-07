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
                .Where(name => name != ManagedStateFile.FileName);
            return new FolderState(folder, entries, ManagedStateFile.Read(path));
        }
    }
}
