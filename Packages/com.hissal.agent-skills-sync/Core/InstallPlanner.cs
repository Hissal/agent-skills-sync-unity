using System;
using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Decides what a sync does. Pure: the same lock and project state always give the same plan,
    /// and nothing touches the filesystem.
    /// </summary>
    public static class InstallPlanner
    {
        public static InstallPlan Plan(Lockfile lockfile, ProjectState project, FolderLayout layout)
        {
            var actions = new List<PlanAction>();
            var managed = layout.Folders.ToDictionary(f => f, f => new SortedSet<string>(StringComparer.Ordinal));

            foreach (var skill in lockfile.Skills)
            {
                foreach (var folder in layout.Folders)
                {
                    var state = project.For(folder);
                    if (!state.Has(skill.Name))
                    {
                        actions.Add(folder.Role == SkillsFolderRole.Canonical
                            ? PlanAction.Install(skill, folder)
                            : PlanAction.Link(skill.Name, folder, layout.Canonical));
                        managed[folder].Add(skill.Name);
                    }
                    else if (state.Manages(skill.Name))
                    {
                        managed[folder].Add(skill.Name);
                    }
                }
            }

            // Names managed before but no longer locked stay recorded until removal is planned.
            foreach (var folder in layout.Folders)
                managed[folder].UnionWith(project.For(folder).Managed);

            return new InstallPlan(
                actions,
                managed.ToDictionary(m => m.Key, m => (IReadOnlyList<string>)m.Value.ToList()));
        }
    }
}
