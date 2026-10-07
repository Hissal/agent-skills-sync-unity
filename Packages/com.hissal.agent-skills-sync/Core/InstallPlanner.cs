using System;
using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Decides what a sync does. The same lock, project state and user-scope copies always give the same plan, and
    /// nothing changes the filesystem (the only read is hashing a skipped skill's user-scope copy).
    /// </summary>
    /// <remarks>
    /// Per locked skill and folder: no entry -> Install (canonical) or Link (link folder); a managed canonical copy that
    /// is not current -> Update; a managed link that no longer shows the canonical entry (a broken symlink or junction,
    /// one resolving elsewhere, or a Copy-fallback copy whose content differs) -> Link again, replacing it; an entry
    /// the tool does not manage -> LeaveForeign. An unmanaged, unlocked folder with a SKILL.md in the
    /// canonical folder is project-authored -> Link (link folders only). Names managed but neither locked nor
    /// project-authored, wherever the entry still exists: canonical entry gone -> Unlink (the link is dangling);
    /// otherwise -> Remove. Other unlocked, unmanaged entries are never looked at.
    /// <para>
    /// Folder selection: only selected link folders get links; the canonical folder gets copies while any folder is
    /// selected (every selected folder needs the project copy, whether or not the canonical folder itself is selected).
    /// In a folder that is not needed, the tool's managed entries go (Unlink in a link folder, Remove of the canonical
    /// copy, links first) and its managed names are dropped; entries it does not manage are never touched.
    /// </para>
    /// <para>
    /// User-scope skips: a locked skill is skipped in a selected folder when the contributor chose to skip it there
    /// (<see cref="SkipChoices"/>) <b>and</b> a user-scope copy the folder's agents read was found
    /// (<see cref="UserScopeState"/>) -> SkipUserScope, and the folder is not needed for that skill (its managed entry
    /// goes as above). The canonical copy stays while any selected folder still needs it for that skill, so a skip of
    /// the canonical folder only takes effect once no selected link folder links to it. Project-authored skills are
    /// never skipped. Where a skipped folder's user-scope copy verifiably differs from the lock
    /// (<see cref="UserScopeCopy.DiffersFromLock"/>) -> WarnUserScopeDiffers right after its SkipUserScope. That check
    /// hashes the copy, the planner's only read of the filesystem.
    /// </para>
    /// </remarks>
    public static class InstallPlanner
    {
        /// <summary>Shorthand for <see cref="Plan(Lockfile, ProjectState, MachineChoices, IInstalledCopyCheck)"/> with the choices given one by one.</summary>
        /// <param name="installedCopyCheck">Whether a managed canonical copy is current; defaults to <see cref="LockedHashCheck"/>.</param>
        public static InstallPlan Plan(Lockfile lockfile, ProjectState project, FolderLayout layout,
            IInstalledCopyCheck installedCopyCheck = null, IEnumerable<SkillsFolder> selected = null,
            UserScopeState userScope = null, SkipChoices skips = null) =>
            Plan(lockfile, project, new MachineChoices(layout, selected, userScope, skips), installedCopyCheck);

        /// <param name="choices">This machine's layout, folder selection, user-scope copies and skips.</param>
        /// <param name="installedCopyCheck">Whether a managed canonical copy is current; defaults to <see cref="LockedHashCheck"/>.</param>
        public static InstallPlan Plan(Lockfile lockfile, ProjectState project, MachineChoices choices,
            IInstalledCopyCheck installedCopyCheck = null)
        {
            var layout = choices.Layout;
            var userScope = choices.UserScope;
            var skips = choices.Skips;
            var check = installedCopyCheck ?? LockedHashCheck.Instance;
            var actions = new List<PlanAction>();
            var managed = layout.Folders.ToDictionary(f => f, f => new SortedSet<string>(StringComparer.Ordinal));
            var linkFolders = layout.Folders.Where(f => f.Role == SkillsFolderRole.Link).ToList();
            // Links first, so no link is left pointing at a deleted copy if a removal fails.
            var removalOrder = layout.Folders.OrderBy(f => f.Role == SkillsFolderRole.Canonical ? 1 : 0).ToList();

            var selectedPaths = new HashSet<string>(
                (choices.Selected ?? layout.Folders).Where(f => f != null).Select(f => f.RelativePath), StringComparer.Ordinal);
            var anySelected = layout.Folders.Any(f => selectedPaths.Contains(f.RelativePath));
            // Whether the tool keeps entries in the folder: a selected link folder, or the canonical folder while any
            // selected folder needs the project copy.
            bool Needed(SkillsFolder folder) =>
                folder.Role == SkillsFolderRole.Canonical ? anySelected : selectedPaths.Contains(folder.RelativePath);

            // A selected folder whose agents already have the skill at user scope, and where the contributor skips it.
            bool Skipped(SkillsFolder folder, string skill) =>
                selectedPaths.Contains(folder.RelativePath) && skips.IsSkipped(folder, skill) && userScope.Has(folder, skill);

            // Per locked skill: a selected link folder that does not skip it, or the canonical folder while any selected
            // folder (canonical or link) still needs the project copy of it.
            bool NeededFor(SkillsFolder folder, string skill) =>
                folder.Role == SkillsFolderRole.Canonical
                    ? layout.Folders.Any(f => selectedPaths.Contains(f.RelativePath) && !Skipped(f, skill))
                    : Needed(folder) && !Skipped(folder, skill);

            // Takes a managed entry out of a folder the tool no longer keeps entries in.
            void Withdraw(string name, SkillsFolder folder, FolderState state)
            {
                if (!state.Manages(name) || !state.Has(name)) return;
                actions.Add(folder.Role == SkillsFolderRole.Canonical ? PlanAction.Remove(name, folder) : PlanAction.Unlink(name, folder));
            }

            foreach (var skill in lockfile.Skills)
            {
                foreach (var folder in NeededFor(layout.Canonical, skill.Name) ? layout.Folders : removalOrder)
                {
                    var state = project.For(folder);
                    var canonical = folder.Role == SkillsFolderRole.Canonical;
                    if (!NeededFor(folder, skill.Name))
                    {
                        Withdraw(skill.Name, folder, state);
                        if (Skipped(folder, skill.Name))
                        {
                            var copies = userScope.CopiesOf(folder, skill.Name);
                            actions.Add(PlanAction.SkipUserScope(skill, folder, copies));
                            var differing = copies.Where(c => c.DiffersFromLock(skill)).ToList();
                            if (differing.Count > 0) actions.Add(PlanAction.WarnUserScopeDiffers(skill, folder, differing));
                        }
                    }
                    else if (!state.Has(skill.Name))
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

            // Project-authored skills: folders with a SKILL.md committed in the canonical folder, neither locked nor
            // installed by the tool.
            // They stay tracked there; only the links the tool makes for them are managed.
            var locked = new HashSet<string>(lockfile.Skills.Select(s => s.Name), StringComparer.Ordinal);
            var canonicalState = project.For(layout.Canonical);
            var projectAuthored = canonicalState.Entries
                .Where(name => !locked.Contains(name) && !canonicalState.Manages(name) && canonicalState.IsSkillFolder(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            foreach (var name in projectAuthored)
            {
                foreach (var folder in linkFolders)
                {
                    var state = project.For(folder);
                    if (!Needed(folder))
                    {
                        Withdraw(name, folder, state);
                    }
                    else if (!state.Has(name))
                    {
                        actions.Add(PlanAction.Link(name, folder, layout.Canonical));
                        managed[folder].Add(name);
                    }
                    else if (state.Manages(name))
                    {
                        if (state.IsStaleLink(name)) actions.Add(PlanAction.Link(name, folder, layout.Canonical));
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

            // The committed .gitignore blocks: the same on every machine, whatever it selected or manages.
            var ignored = new Dictionary<SkillsFolder, IReadOnlyList<string>>();
            foreach (var folder in layout.Folders)
            {
                if (!Needed(folder) && project.For(folder).Ignored.Count == 0) continue;
                var names = new SortedSet<string>(locked, StringComparer.Ordinal);
                if (folder.Role == SkillsFolderRole.Link) names.UnionWith(projectAuthored);
                ignored[folder] = names.ToList();
            }

            var managedNamesChange = layout.Folders.Any(f => !managed[f].SetEquals(project.For(f).Managed)) ||
                                     ignored.Any(i => !new HashSet<string>(project.For(i.Key).Ignored).SetEquals(i.Value));
            return new InstallPlan(
                actions,
                managed.ToDictionary(m => m.Key, m => (IReadOnlyList<string>)m.Value.ToList()),
                managedNamesChange,
                ignored);
        }
    }
}
