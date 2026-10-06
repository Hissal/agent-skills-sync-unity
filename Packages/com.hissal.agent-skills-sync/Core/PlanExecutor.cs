using System.Collections.Generic;
using System.IO;
using System.Linq;

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
                        DirectoryLink.Remove(entry);
                        Paths.CopyDirectory(source, entry);
                        RefreshCopiedLinks(projectRoot, plan, action.SkillName, entry);
                        break;
                    case PlanActionKind.Link:
                        Directory.CreateDirectory(folder);
                        var target = Path.Combine(Paths.InProject(projectRoot, action.LinkTarget.RelativePath), action.SkillName);
                        linkMethods[action] = _linker.CreateDirectoryLink(entry, target);
                        break;
                    case PlanActionKind.Remove:
                        DirectoryLink.Remove(entry);
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

        /// <summary>
        /// A symlink or junction follows an updated canonical copy by itself; a link made by the Copy fallback does
        /// not, so re-copy it from the new canonical copy.
        /// </summary>
        static void RefreshCopiedLinks(string projectRoot, InstallPlan plan, string skillName, string canonicalEntry)
        {
            foreach (var managed in plan.ManagedNames)
            {
                if (managed.Key.Role != SkillsFolderRole.Link || !managed.Value.Contains(skillName)) continue;
                var link = Path.Combine(Paths.InProject(projectRoot, managed.Key.RelativePath), skillName);
                if (!Directory.Exists(link) || File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint)) continue;
                DirectoryLink.Remove(link);
                Paths.CopyDirectory(canonicalEntry, link);
            }
        }

        static string Fetched(IReadOnlyDictionary<string, string> fetchedFolders, PlanAction action) =>
            fetchedFolders.TryGetValue(action.SkillName, out var source)
                ? source
                : throw new KeyNotFoundException($"No fetched folder for skill \"{action.SkillName}\".");
    }
}
