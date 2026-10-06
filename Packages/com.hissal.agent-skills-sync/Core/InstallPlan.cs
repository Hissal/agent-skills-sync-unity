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

        /// <summary>Delete a managed entry (canonical copy or link) whose skill is no longer locked.</summary>
        Remove,

        /// <summary>Leave an entry the tool does not manage untouched, though a locked skill has its name. Changes nothing.</summary>
        LeaveForeign,
    }

    /// <summary>One thing the executor does to one skill in one folder.</summary>
    public sealed class PlanAction
    {
        PlanAction(PlanActionKind kind, LockedSkill skill, string skillName, SkillsFolder folder, SkillsFolder linkTarget)
        {
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

        public PlanActionKind Kind { get; }

        /// <summary>The locked skill, for Install, Update (to fetch) and LeaveForeign; null otherwise.</summary>
        public LockedSkill Skill { get; }

        /// <summary>Whether the action needs the skill fetched first (Install, Update).</summary>
        public bool NeedsFetch => Kind == PlanActionKind.Install || Kind == PlanActionKind.Update;

        /// <summary>Whether applying the action changes the project (false for LeaveForeign).</summary>
        public bool ChangesProject => Kind != PlanActionKind.LeaveForeign;

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
        /// <param name="managedNamesChange">Whether <paramref name="managedNames"/> differs from what the folders record now.</param>
        public InstallPlan(IReadOnlyList<PlanAction> actions, IReadOnlyDictionary<SkillsFolder, IReadOnlyList<string>> managedNames,
            bool managedNamesChange = false)
        {
            Actions = actions;
            ManagedNames = managedNames;
            ManagedNamesChange = managedNamesChange;
        }

        /// <summary>
        /// Actions in execution order: per locked skill, the canonical folder first; then removals of skills no longer
        /// locked, by name, link folders before the canonical copy.
        /// </summary>
        public IReadOnlyList<PlanAction> Actions { get; }

        /// <summary>
        /// Whether applying the plan changes anything: an action other than LeaveForeign, or a managed-state file to
        /// rewrite (e.g. dropping a no-longer-locked name whose entry is already gone from disk).
        /// </summary>
        public bool HasChanges => ManagedNamesChange || Actions.Any(a => a.ChangesProject);

        /// <summary>Whether some folder's managed names differ from what its managed-state file records now.</summary>
        public bool ManagedNamesChange { get; }

        /// <summary>Per folder in the layout, the sorted names the tool manages there once the plan is applied.</summary>
        public IReadOnlyDictionary<SkillsFolder, IReadOnlyList<string>> ManagedNames { get; }
    }
}
