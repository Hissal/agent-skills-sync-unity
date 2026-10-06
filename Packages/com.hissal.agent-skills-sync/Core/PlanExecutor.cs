using System.Collections.Generic;
using System.IO;

namespace Hissal.AgentSkillsSync
{
    /// <summary>Applies an <see cref="InstallPlan"/> to the project's skills folders.</summary>
    public sealed class PlanExecutor
    {
        readonly ILinkCreator _linker;

        public PlanExecutor(ILinkCreator linker = null) => _linker = linker ?? FallbackLinkCreator.Default;

        /// <param name="projectRoot">The folder holding <c>skills-lock.json</c>.</param>
        /// <param name="plan">The plan to apply.</param>
        /// <param name="fetchedFolders">Skill name to a local folder with the skill's contents, for every Install and Update action.</param>
        /// <returns>What was done, for the summary.</returns>
        public SyncSummary Execute(string projectRoot, InstallPlan plan, IReadOnlyDictionary<string, string> fetchedFolders)
        {
            var applied = new List<PlanAction>();
            var linkMethods = new Dictionary<PlanAction, LinkMethod>();
            foreach (var action in plan.Actions)
            {
                var folder = Paths.InProject(projectRoot, action.Folder.RelativePath);
                var entry = Path.Combine(folder, action.SkillName);

                switch (action.Kind)
                {
                    case PlanActionKind.Install:
                        Directory.CreateDirectory(folder);
                        Paths.CopyDirectory(Fetched(fetchedFolders, action), entry);
                        break;
                    case PlanActionKind.Update:
                        var source = Fetched(fetchedFolders, action);
                        RemoveEntry(entry);
                        Paths.CopyDirectory(source, entry);
                        break;
                    case PlanActionKind.Link:
                        Directory.CreateDirectory(folder);
                        var target = Path.Combine(Paths.InProject(projectRoot, action.LinkTarget.RelativePath), action.SkillName);
                        linkMethods[action] = _linker.CreateDirectoryLink(entry, target);
                        break;
                    case PlanActionKind.Remove:
                        RemoveEntry(entry);
                        break;
                    case PlanActionKind.LeaveForeign:
                        break;
                }
                applied.Add(action);
            }

            foreach (var managed in plan.ManagedNames)
            {
                var folder = Paths.InProject(projectRoot, managed.Key.RelativePath);
                if (managed.Value.Count > 0 || Directory.Exists(folder))
                    ManagedStateFile.Write(folder, managed.Value);
            }

            return new SyncSummary(applied, linkMethods);
        }

        static string Fetched(IReadOnlyDictionary<string, string> fetchedFolders, PlanAction action) =>
            fetchedFolders.TryGetValue(action.SkillName, out var source)
                ? source
                : throw new KeyNotFoundException($"No fetched folder for skill \"{action.SkillName}\".");

        /// <summary>
        /// Deletes a managed entry: a link (symlink or junction) is removed without touching what it points at; a real
        /// folder (a canonical copy, or a copied link) is deleted with its contents. A missing entry is fine.
        /// </summary>
        // The one place entries are deleted; landing #6 swaps the body for DirectoryLink.Remove.
        static void RemoveEntry(string path)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(path);
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
                if (isDirectory && Path.DirectorySeparatorChar == '\\') Directory.Delete(path);
                else File.Delete(path);
            }
            else if (isDirectory)
            {
                Directory.Delete(path, recursive: true);
            }
            else
            {
                File.Delete(path);
            }
        }
    }
}
