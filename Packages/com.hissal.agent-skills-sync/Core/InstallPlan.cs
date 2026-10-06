using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    public enum PlanActionKind
    {
        /// <summary>Copy the fetched skill into the canonical folder.</summary>
        Install,

        /// <summary>Create a link in a link folder pointing at the canonical copy.</summary>
        Link,

        /// <summary>Replace a managed canonical copy that is no longer current with the fetched skill.</summary>
        Update,

        /// <summary>Delete a managed entry (canonical copy or link) whose skill is no longer locked, or a managed canonical copy no selected folder needs.</summary>
        Remove,

        /// <summary>Leave an entry the tool does not manage untouched, though a locked skill has its name. Changes nothing.</summary>
        LeaveForeign,

        /// <summary>Delete a managed link whose canonical entry is gone, or that sits in a folder no longer selected.</summary>
        Unlink,

        /// <summary>
        /// Leave the project copy or link out of a folder because the contributor skips it there in favour of a
        /// user-scope copy the folder's agents already read. Changes nothing by itself; a managed entry there goes
        /// through its own Unlink or Remove.
        /// </summary>
        SkipUserScope,

        /// <summary>
        /// Warn that the user-scope copy the folder's agents read instead of a skipped project copy verifiably differs
        /// from the locked hash, so they don't run what teammates run. Only "differs": never outdated or ahead.
        /// Changes nothing; follows the folder's SkipUserScope.
        /// </summary>
        WarnUserScopeDiffers,
    }

    /// <summary>One thing the executor does to one skill in one folder.</summary>
    public sealed class PlanAction
    {
        PlanAction(PlanActionKind kind, LockedSkill skill, string skillName, SkillsFolder folder, SkillsFolder linkTarget,
            IReadOnlyList<UserScopeCopy> userScopeCopies = null)
        {
            UserScopeCopies = userScopeCopies ?? new UserScopeCopy[0];
            Kind = kind;
            Skill = skill;
            SkillName = skillName;
            Folder = folder;
            LinkTarget = linkTarget;
        }

        public static PlanAction Install(LockedSkill skill, SkillsFolder folder) =>
            new PlanAction(PlanActionKind.Install, skill, skill.Name, folder, null);

        public static PlanAction Link(string skillName, SkillsFolder folder, SkillsFolder target) =>
            new PlanAction(PlanActionKind.Link, null, skillName, folder, target);

        public static PlanAction Update(LockedSkill skill, SkillsFolder folder) =>
            new PlanAction(PlanActionKind.Update, skill, skill.Name, folder, null);

        public static PlanAction Remove(string skillName, SkillsFolder folder) =>
            new PlanAction(PlanActionKind.Remove, null, skillName, folder, null);

        public static PlanAction LeaveForeign(LockedSkill skill, SkillsFolder folder) =>
            new PlanAction(PlanActionKind.LeaveForeign, skill, skill.Name, folder, null);

        public static PlanAction Unlink(string skillName, SkillsFolder folder) =>
            new PlanAction(PlanActionKind.Unlink, null, skillName, folder, null);

        /// <param name="copies">Where the folder's agents already have the skill at user scope.</param>
        public static PlanAction SkipUserScope(LockedSkill skill, SkillsFolder folder, IReadOnlyList<UserScopeCopy> copies) =>
            new PlanAction(PlanActionKind.SkipUserScope, skill, skill.Name, folder, null, copies);

        /// <param name="differing">The user-scope copies whose content differs from the lock.</param>
        public static PlanAction WarnUserScopeDiffers(LockedSkill skill, SkillsFolder folder, IReadOnlyList<UserScopeCopy> differing) =>
            new PlanAction(PlanActionKind.WarnUserScopeDiffers, skill, skill.Name, folder, null, differing);

        public PlanActionKind Kind { get; }

        /// <summary>The locked skill, for Install, Update (to fetch), LeaveForeign, SkipUserScope and WarnUserScopeDiffers; null otherwise.</summary>
        public LockedSkill Skill { get; }

        /// <summary>Whether the action needs the skill fetched first (Install, Update).</summary>
        public bool NeedsFetch => Kind == PlanActionKind.Install || Kind == PlanActionKind.Update;

        /// <summary>Whether applying the action changes the project (false for LeaveForeign, SkipUserScope and WarnUserScopeDiffers).</summary>
        public bool ChangesProject => Kind != PlanActionKind.LeaveForeign && Kind != PlanActionKind.SkipUserScope &&
                                      Kind != PlanActionKind.WarnUserScopeDiffers;

        /// <summary>
        /// For SkipUserScope, every user-scope copy found for the folder (where, and its path); for WarnUserScopeDiffers,
        /// those whose content differs from the lock; empty otherwise.
        /// </summary>
        public IReadOnlyList<UserScopeCopy> UserScopeCopies { get; }

        public string SkillName { get; }

        /// <summary>The folder the action changes.</summary>
        public SkillsFolder Folder { get; }

        /// <summary>The folder holding the entry the link points at, for <see cref="PlanActionKind.Link"/>; null otherwise.</summary>
        public SkillsFolder LinkTarget { get; }

        public override string ToString() => $"{Kind} {Folder.RelativePath}/{SkillName}";
    }

    /// <summary>The planner's output: the ordered actions, and what each folder's managed-state file lists afterwards.</summary>
    public sealed class InstallPlan
    {
        /// <param name="managedNamesChange">
        /// Whether <paramref name="managedNames"/> differs from what this machine records now, or
        /// <paramref name="ignoredNames"/> from a folder's <c>.gitignore</c> block.
        /// </param>
        /// <param name="ignoredNames">Per folder whose <c>.gitignore</c> block the sync writes, the names it lists; null = none.</param>
        public InstallPlan(IReadOnlyList<PlanAction> actions, IReadOnlyDictionary<SkillsFolder, IReadOnlyList<string>> managedNames,
            bool managedNamesChange = false, IReadOnlyDictionary<SkillsFolder, IReadOnlyList<string>> ignoredNames = null)
        {
            Actions = actions;
            ManagedNames = managedNames;
            ManagedNamesChange = managedNamesChange;
            IgnoredNames = ignoredNames ?? new Dictionary<SkillsFolder, IReadOnlyList<string>>();
        }

        /// <summary>
        /// Actions in execution order: per locked skill, the canonical folder first (link folders first when the
        /// skill's canonical copy goes, each skip after the withdrawal in that folder and any differs warning right after its skip); then links for project-authored
        /// skills; then, per name no longer locked, unlinks of dangling links and removals, link folders before the
        /// canonical copy.
        /// </summary>
        public IReadOnlyList<PlanAction> Actions { get; }

        /// <summary>
        /// Whether applying the plan changes anything: an action other than LeaveForeign, or managed names or a
        /// <c>.gitignore</c> block to rewrite (e.g. dropping a no-longer-locked name whose entry is already gone from disk).
        /// </summary>
        public bool HasChanges => ManagedNamesChange || Actions.Any(a => a.ChangesProject);

        /// <summary>Whether some folder's managed names or <c>.gitignore</c> block differ from what is recorded now.</summary>
        public bool ManagedNamesChange { get; }

        /// <summary>
        /// Per folder in the layout, the sorted names this machine manages there once the plan is applied. Recorded in
        /// <see cref="LocalPrefs.ManagedSkills"/>; it depends on this machine's folder selection.
        /// </summary>
        public IReadOnlyDictionary<SkillsFolder, IReadOnlyList<string>> ManagedNames { get; }

        /// <summary>
        /// Per folder whose <c>.gitignore</c> block the sync writes (a folder this machine keeps entries in, or one
        /// whose block exists), the sorted names the block lists: every locked skill, plus project-authored skills in
        /// link folders. The block is committed, so it never depends on this machine's selection or skips.
        /// </summary>
        public IReadOnlyDictionary<SkillsFolder, IReadOnlyList<string>> IgnoredNames { get; }
    }
}
