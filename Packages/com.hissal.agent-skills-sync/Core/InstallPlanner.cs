using System;
using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Decides what a sync does. Pure: the same lock and project state always give the same plan,
    /// and nothing touches the filesystem.
    /// </summary>
    /// <remarks>
    /// Per locked skill and folder: no entry -> Install (canonical) or Link (link folder); a managed canonical copy that
    /// is not current -> Update; an entry the tool does not manage -> LeaveForeign. Names managed but no longer locked
    /// -> Remove wherever the entry still exists. Entries that are neither locked nor managed are never looked at.
    /// </remarks>
    public static class InstallPlanner
    {
        /// <param name="installedCopyCheck">Whether a managed canonical copy is current; defaults to <see cref="LockedHashCheck"/>.</param>
        public static InstallPlan Plan(Lockfile lockfile, ProjectState project, FolderLayout layout,
            IInstalledCopyCheck installedCopyCheck = null)
        {
            var check = installedCopyCheck ?? LockedHashCheck.Instance;
            var actions = new List<PlanAction>();
            var managed = layout.Folders.ToDictionary(f => f, f => new SortedSet<string>(StringComparer.Ordinal));

            foreach (var skill in lockfile.Skills)
            {
                foreach (var folder in layout.Folders)
                {
                    var state = project.For(folder);
                    var canonical = folder.Role == SkillsFolderRole.Canonical;
                    if (!state.Has(skill.Name))
                    {
                        actions.Add(canonical
                            ? PlanAction.Install(skill, folder)
                            : PlanAction.Link(skill.Name, folder, layout.Canonical));
                        managed[folder].Add(skill.Name);
                    }
                    else if (state.Manages(skill.Name))
                    {
                        if (canonical && !check.IsCurrent(skill, state.InstalledHash(skill.Name)))
                            actions.Add(PlanAction.Update(skill, folder));
                        managed[folder].Add(skill.Name);
                    }
                    else
                    {
                        actions.Add(PlanAction.LeaveForeign(skill, folder));
                    }
                }
            }

            var locked = new HashSet<string>(lockfile.Skills.Select(s => s.Name), StringComparer.Ordinal);
            var stale = layout.Folders
                .SelectMany(f => project.For(f).Managed)
                .Where(name => !locked.Contains(name))
                .Distinct()
                .OrderBy(name => name, StringComparer.Ordinal);
            // Links first, so no link is left pointing at a deleted copy if a removal fails.
            var removalOrder = layout.Folders.OrderBy(f => f.Role == SkillsFolderRole.Canonical ? 1 : 0).ToList();
            foreach (var name in stale)
                foreach (var folder in removalOrder)
                {
                    var state = project.For(folder);
                    if (state.Manages(name) && state.Has(name))
                        actions.Add(PlanAction.Remove(name, folder));
                }

            var managedNamesChange = layout.Folders.Any(f => !managed[f].SetEquals(project.For(f).Managed));
            return new InstallPlan(
                actions,
                managed.ToDictionary(m => m.Key, m => (IReadOnlyList<string>)m.Value.ToList()),
                managedNamesChange);
        }
    }
}
