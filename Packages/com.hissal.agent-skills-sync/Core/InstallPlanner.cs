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
    /// is not current -> Update; a managed link that is broken or resolves anywhere but the canonical entry -> Link
    /// again, replacing it; an entry the tool does not manage -> LeaveForeign. Names managed but no longer locked
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
            var linkFolders = layout.Folders.Where(f => f.Role == SkillsFolderRole.Link).ToList();

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
                        else if (!canonical && state.IsStaleLink(skill.Name))
                            actions.Add(PlanAction.Link(skill.Name, folder, layout.Canonical));
                        managed[folder].Add(skill.Name);
                    }
                    else
                    {
                        actions.Add(PlanAction.LeaveForeign(skill, folder));
                    }
                }
            }

            // Project-authored skills: committed in the canonical folder, neither locked nor installed by the tool.
            // They stay tracked there; only the links the tool makes for them are managed.
            var locked = new HashSet<string>(lockfile.Skills.Select(s => s.Name), StringComparer.Ordinal);
            var canonicalState = project.For(layout.Canonical);
            var projectAuthored = canonicalState.Entries
                .Where(name => !locked.Contains(name) && !canonicalState.Manages(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            foreach (var name in projectAuthored)
            {
                foreach (var folder in linkFolders)
                {
                    var state = project.For(folder);
                    if (!state.Has(name))
                    {
                        actions.Add(PlanAction.Link(name, folder, layout.Canonical));
                        managed[folder].Add(name);
                    }
                    else if (state.Manages(name))
                    {
                        managed[folder].Add(name);
                    }
                }
            }

            // Names managed but neither locked nor project-authored. With the canonical entry gone (e.g. a deleted
            // project-authored skill) a managed link is dangling -> Unlink; otherwise the tool's copy and links are
            // deleted -> Remove. Either way the name stops being managed.
            var authored = new HashSet<string>(projectAuthored, StringComparer.Ordinal);
            var stale = layout.Folders
                .SelectMany(f => project.For(f).Managed)
                .Where(name => !locked.Contains(name) && !authored.Contains(name))
                .Distinct()
                .OrderBy(name => name, StringComparer.Ordinal);
            // Links first, so no link is left pointing at a deleted copy if a removal fails.
            var removalOrder = layout.Folders.OrderBy(f => f.Role == SkillsFolderRole.Canonical ? 1 : 0).ToList();
            foreach (var name in stale)
            {
                var canonicalGone = !canonicalState.Has(name);
                foreach (var folder in removalOrder)
                {
                    var state = project.For(folder);
                    if (!state.Manages(name) || !state.Has(name)) continue;
                    actions.Add(canonicalGone ? PlanAction.Unlink(name, folder) : PlanAction.Remove(name, folder));
                }
            }

            var managedNamesChange = layout.Folders.Any(f => !managed[f].SetEquals(project.For(f).Managed));
            return new InstallPlan(
                actions,
                managed.ToDictionary(m => m.Key, m => (IReadOnlyList<string>)m.Value.ToList()),
                managedNamesChange);
        }
    }
}
