using System.Collections.Generic;

namespace Hissal.AgentSkillsSync
{
    public enum PlanActionKind
    {
        /// <summary>Copy the fetched skill into the canonical folder.</summary>
        Install,

        /// <summary>Create a link in a link folder pointing at the canonical copy.</summary>
        Link,
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

        public PlanActionKind Kind { get; }

        /// <summary>The locked skill to fetch, for <see cref="PlanActionKind.Install"/>; null otherwise.</summary>
        public LockedSkill Skill { get; }

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
        public InstallPlan(IReadOnlyList<PlanAction> actions, IReadOnlyDictionary<SkillsFolder, IReadOnlyList<string>> managedNames)
        {
            Actions = actions;
            ManagedNames = managedNames;
        }

        /// <summary>Actions in execution order: per skill, the canonical folder first.</summary>
        public IReadOnlyList<PlanAction> Actions { get; }

        /// <summary>Per folder in the layout, the sorted names the tool manages there once the plan is applied.</summary>
        public IReadOnlyDictionary<SkillsFolder, IReadOnlyList<string>> ManagedNames { get; }
    }
}
