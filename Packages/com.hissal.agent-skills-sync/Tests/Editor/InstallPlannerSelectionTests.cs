using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>The planner over which skills folders this machine selected.</summary>
    public class InstallPlannerSelectionTests
    {
        const string Hash = "locked-hash";

        /// <summary>The default table plus one more link folder, as a future table entry would add.</summary>
        static readonly FolderLayout ExtendedTable = new FolderLayout(FolderLayout.Default.Folders
            .Concat(new[] { new SkillsFolder("junie", ".junie/skills", SkillsFolderRole.Link, "Junie") }));

        static Lockfile Lock(params string[] names) =>
            new Lockfile(names.Select(n => new LockedSkill(n, "owner/repo", "github", $"skills/{n}/SKILL.md", Hash)).ToList());

        static string Describe(PlanAction action) =>
            action.Kind == PlanActionKind.Link
                ? $"Link {action.Folder.RelativePath}/{action.SkillName} -> {action.LinkTarget.RelativePath}/{action.SkillName}"
                : $"{action.Kind} {action.Folder.RelativePath}/{action.SkillName}";

        static IEnumerable<SkillsFolder> Select(FolderLayout table, string selected) =>
            selected.Split(',').Where(p => p.Length > 0).Select(p => table.Find(p.Trim()));

        /// <summary>
        /// Each folder spec is <c>path=entries</c>; entries are comma-separated, <c>name</c> = managed (at the locked
        /// hash in the canonical folder), <c>name?</c> = present but not managed.
        /// </summary>
        static ProjectState Project(FolderLayout table, params string[] folders)
        {
            var states = new List<FolderState>();
            foreach (var spec in folders)
            {
                var parts = spec.Split('=');
                var folder = table.Find(parts[0]);
                var present = new List<string>();
                var managed = new List<string>();
                var hashes = new Dictionary<string, string>();
                foreach (var entry in parts[1].Split(',').Where(e => e.Length > 0))
                {
                    var name = entry.TrimEnd('?');
                    present.Add(name);
                    if (entry.EndsWith("?")) continue;
                    managed.Add(name);
                    hashes[name] = Hash;
                }
                states.Add(new FolderState(folder, present, managed, hashes));
            }
            return new ProjectState(states);
        }

        static TestCaseData Fresh(string name, FolderLayout table, string selected, params string[] expected) =>
            new TestCaseData(table, selected, expected).SetName("Plan_FreshProject_" + name);

        static IEnumerable<TestCaseData> FreshCases()
        {
            var table = FolderLayout.Default;
            yield return Fresh("NoneSelected_InstallsNothing", table, "");
            yield return Fresh("ClaudeOnly_CopiesIntoAgentsAndLinksClaude", table, ".claude/skills",
                "Install .agents/skills/tdd", "Link .claude/skills/tdd -> .agents/skills/tdd");
            yield return Fresh("AgentsOnly_CopiesIntoAgentsOnly", table, ".agents/skills",
                "Install .agents/skills/tdd");
            yield return Fresh("Both_CopiesAndLinks", table, ".agents/skills,.claude/skills",
                "Install .agents/skills/tdd", "Link .claude/skills/tdd -> .agents/skills/tdd");
            yield return Fresh("ExtraEntryOnly_CopiesIntoAgentsAndLinksTheExtraFolder", ExtendedTable, ".junie/skills",
                "Install .agents/skills/tdd", "Link .junie/skills/tdd -> .agents/skills/tdd");
            yield return Fresh("ExtraEntryAndClaude_LinksBoth", ExtendedTable, ".claude/skills,.junie/skills",
                "Install .agents/skills/tdd", "Link .claude/skills/tdd -> .agents/skills/tdd",
                "Link .junie/skills/tdd -> .agents/skills/tdd");
        }

        [TestCaseSource(nameof(FreshCases))]
        public void Plan_FreshProject(FolderLayout table, string selected, string[] expected)
        {
            var plan = InstallPlanner.Plan(Lock("tdd"), ProjectState.Empty, table, selected: Select(table, selected));

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(expected));
        }

        static TestCaseData Installed(string name, string selected, string[] expected, string[] managedAgents, string[] managedClaude) =>
            new TestCaseData(selected, expected, managedAgents, managedClaude).SetName("Plan_InstalledInBoth_" + name);

        static IEnumerable<TestCaseData> InstalledCases()
        {
            yield return Installed("BothStillSelected_DoesNothing", ".agents/skills,.claude/skills",
                new string[0], new[] { "tdd" }, new[] { "tdd" });
            yield return Installed("ClaudeDeselected_UnlinksOnlyClaude", ".agents/skills",
                new[] { "Unlink .claude/skills/tdd" }, new[] { "tdd" }, new string[0]);
            yield return Installed("AgentsDeselected_KeepsTheCopyClaudeNeeds", ".claude/skills",
                new string[0], new[] { "tdd" }, new[] { "tdd" });
            yield return Installed("NoneSelected_UnlinksThenRemovesTheCopy", "",
                new[] { "Unlink .claude/skills/tdd", "Remove .agents/skills/tdd" }, new string[0], new string[0]);
        }

        [TestCaseSource(nameof(InstalledCases))]
        public void Plan_InstalledInBoth(string selected, string[] expected, string[] managedAgents, string[] managedClaude)
        {
            var table = FolderLayout.Default;
            var project = Project(table, ".agents/skills=tdd", ".claude/skills=tdd");

            var plan = InstallPlanner.Plan(Lock("tdd"), project, table, selected: Select(table, selected));

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(expected));
            Assert.That(plan.ManagedNames[table.Canonical], Is.EqualTo(managedAgents));
            Assert.That(plan.ManagedNames[table.Find(".claude/skills")], Is.EqualTo(managedClaude));
        }

        [Test]
        public void Plan_DeselectedFolderHoldsAForeignEntry_LeavesItWithoutAnyAction()
        {
            var table = FolderLayout.Default;
            var project = Project(table, ".agents/skills=tdd", ".claude/skills=tdd?");

            var plan = InstallPlanner.Plan(Lock("tdd"), project, table, selected: Select(table, ".agents/skills"));

            Assert.That(plan.Actions, Is.Empty);
        }

        [Test]
        public void Plan_NoneSelected_KeepsForeignCanonicalEntries()
        {
            var table = FolderLayout.Default;
            var project = Project(table, ".agents/skills=tdd?");

            var plan = InstallPlanner.Plan(Lock("tdd"), project, table, selected: Select(table, ""));

            Assert.That(plan.Actions, Is.Empty);
        }

        [Test]
        public void Plan_ProjectAuthoredSkill_LinksOnlyIntoSelectedFolders()
        {
            var project = Project(ExtendedTable, ".agents/skills=house-style?");

            var plan = InstallPlanner.Plan(Lock(), project, ExtendedTable, selected: Select(ExtendedTable, ".junie/skills"));

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(new[] { "Link .junie/skills/house-style -> .agents/skills/house-style" }));
        }

        [Test]
        public void Plan_ProjectAuthoredSkillLinkedIntoDeselectedFolder_UnlinksItButNeverTouchesTheSkill()
        {
            var table = FolderLayout.Default;
            var project = Project(table, ".agents/skills=house-style?", ".claude/skills=house-style");

            var plan = InstallPlanner.Plan(Lock(), project, table, selected: Select(table, ""));

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(new[] { "Unlink .claude/skills/house-style" }));
        }

        [Test]
        public void Plan_NoSelectionGiven_UsesEveryFolderInTheTable()
        {
            var plan = InstallPlanner.Plan(Lock("tdd"), ProjectState.Empty, ExtendedTable);

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(new[]
            {
                "Install .agents/skills/tdd", "Link .claude/skills/tdd -> .agents/skills/tdd", "Link .junie/skills/tdd -> .agents/skills/tdd",
            }));
        }
    }
}
