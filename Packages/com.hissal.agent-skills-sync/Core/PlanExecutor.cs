using System.Collections.Generic;
using System.IO;

namespace Hissal.AgentSkillsSync
{
    /// <summary>Applies an <see cref="InstallPlan"/> to the project's skills folders.</summary>
    public sealed class PlanExecutor
    {
        readonly ILinkCreator _linker;

        public PlanExecutor(ILinkCreator linker = null) => _linker = linker ?? new SymlinkCreator();

        /// <param name="projectRoot">The folder holding <c>skills-lock.json</c>.</param>
        /// <param name="plan">The plan to apply.</param>
        /// <param name="fetchedFolders">Skill name to a local folder with the skill's contents, for every Install action.</param>
        /// <returns>What was done, for the summary.</returns>
        public SyncSummary Execute(string projectRoot, InstallPlan plan, IReadOnlyDictionary<string, string> fetchedFolders)
        {
            var applied = new List<PlanAction>();
            foreach (var action in plan.Actions)
            {
                var folder = Paths.InProject(projectRoot, action.Folder.RelativePath);
                var entry = Path.Combine(folder, action.SkillName);
                Directory.CreateDirectory(folder);

                switch (action.Kind)
                {
                    case PlanActionKind.Install:
                        if (!fetchedFolders.TryGetValue(action.SkillName, out var source))
                            throw new KeyNotFoundException($"No fetched folder for skill \"{action.SkillName}\".");
                        Paths.CopyDirectory(source, entry);
                        break;
                    case PlanActionKind.Link:
                        var target = Path.Combine(Paths.InProject(projectRoot, action.LinkTarget.RelativePath), action.SkillName);
                        _linker.CreateDirectoryLink(entry, target);
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

            return new SyncSummary(applied);
        }
    }
}
