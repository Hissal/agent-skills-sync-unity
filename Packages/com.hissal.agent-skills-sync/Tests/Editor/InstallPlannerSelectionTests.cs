using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>The planner over which skills folders this machine selected.</summary>
    public class InstallPlannerSelectionTests
    {
        const string Hash = "locked-hash";

        /// <summary>The default layout plus one more link folder, as a future layout entry would add.</summary>
        static readonly FolderLayout ExtendedLayout = new FolderLayout(FolderLayout.Default.Folders
            .Concat(new[] { new SkillsFolder("junie", ".junie/skills", SkillsFolderRole.Link, "Junie") }));

        static Lockfile Lock(params string[] names) =>
            new Lockfile(names.Select(n => new LockedSkill(n, "owner/repo", "github", $"skills/{n}/SKILL.md", Hash)).ToList());

        static string Describe(PlanAction action) =>
            action.Kind == PlanActionKind.Link
                ? $"Link {action.Folder.RelativePath}/{action.SkillName} -> {action.LinkTarget.RelativePath}/{action.SkillName}"
                : $"{action.Kind} {action.Folder.RelativePath}/{action.SkillName}";

        static IEnumerable<SkillsFolder> Select(FolderLayout layout, string selected) =>
            selected.Split(',').Where(p => p.Length > 0).Select(p => layout.Find(p.Trim()));

        /// <summary>
        /// Each folder spec is <c>path=entries</c>; entries are comma-separated, <c>name</c> = managed (at the locked
        /// hash in the canonical folder), <c>name?</c> = present but not managed.
        /// </summary>
        static ProjectState Project(FolderLayout layout, params string[] folders)
        {
            var states = new List<FolderState>();
            foreach (var spec in folders)
            {
                var parts = spec.Split('=');
                var folder = layout.Find(parts[0]);
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

        static TestCaseData Fresh(string name, FolderLayout layout, string selected, params string[] expected) =>
            new TestCaseData(layout, selected, expected).SetName("Plan_FreshProject_" + name);

        static IEnumerable<TestCaseData> FreshCases()
        {
            var layout = FolderLayout.Default;
            yield return Fresh("NoneSelected_InstallsNothing", layout, "");
            yield return Fresh("ClaudeOnly_CopiesIntoAgentsAndLinksClaude", layout, ".claude/skills",
                "Install .agents/skills/tdd", "Link .claude/skills/tdd -> .agents/skills/tdd");
            yield return Fresh("AgentsOnly_CopiesIntoAgentsOnly", layout, ".agents/skills",
                "Install .agents/skills/tdd");
            yield return Fresh("Both_CopiesAndLinks", layout, ".agents/skills,.claude/skills",
                "Install .agents/skills/tdd", "Link .claude/skills/tdd -> .agents/skills/tdd");
            yield return Fresh("ExtraEntryOnly_CopiesIntoAgentsAndLinksTheExtraFolder", ExtendedLayout, ".junie/skills",
                "Install .agents/skills/tdd", "Link .junie/skills/tdd -> .agents/skills/tdd");
            yield return Fresh("ExtraEntryAndClaude_LinksBoth", ExtendedLayout, ".claude/skills,.junie/skills",
                "Install .agents/skills/tdd", "Link .claude/skills/tdd -> .agents/skills/tdd",
                "Link .junie/skills/tdd -> .agents/skills/tdd");
        }

        [TestCaseSource(nameof(FreshCases))]
        public void Plan_FreshProject(FolderLayout layout, string selected, string[] expected)
        {
            var plan = InstallPlanner.Plan(Lock("tdd"), ProjectState.Empty, layout, selected: Select(layout, selected));

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
            var layout = FolderLayout.Default;
            var project = Project(layout, ".agents/skills=tdd", ".claude/skills=tdd");

            var plan = InstallPlanner.Plan(Lock("tdd"), project, layout, selected: Select(layout, selected));

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(expected));
            Assert.That(plan.ManagedNames[layout.Canonical], Is.EqualTo(managedAgents));
            Assert.That(plan.ManagedNames[layout.Find(".claude/skills")], Is.EqualTo(managedClaude));
        }

        [Test]
        public void Plan_DeselectedFolderHoldsAForeignEntry_LeavesItWithoutAnyAction()
        {
            var layout = FolderLayout.Default;
            var project = Project(layout, ".agents/skills=tdd", ".claude/skills=tdd?");

            var plan = InstallPlanner.Plan(Lock("tdd"), project, layout, selected: Select(layout, ".agents/skills"));

            Assert.That(plan.Actions, Is.Empty);
        }

        [Test]
        public void Plan_NoneSelected_KeepsForeignCanonicalEntries()
        {
            var layout = FolderLayout.Default;
            var project = Project(layout, ".agents/skills=tdd?");

            var plan = InstallPlanner.Plan(Lock("tdd"), project, layout, selected: Select(layout, ""));

            Assert.That(plan.Actions, Is.Empty);
        }

        [Test]
        public void Plan_ProjectAuthoredSkill_LinksOnlyIntoSelectedFolders()
        {
            var project = Project(ExtendedLayout, ".agents/skills=house-style?");

            var plan = InstallPlanner.Plan(Lock(), project, ExtendedLayout, selected: Select(ExtendedLayout, ".junie/skills"));

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(new[] { "Link .junie/skills/house-style -> .agents/skills/house-style" }));
        }

        [Test]
        public void Plan_ProjectAuthoredSkillLinkedIntoDeselectedFolder_UnlinksItButNeverTouchesTheSkill()
        {
            var layout = FolderLayout.Default;
            var project = Project(layout, ".agents/skills=house-style?", ".claude/skills=house-style");

            var plan = InstallPlanner.Plan(Lock(), project, layout, selected: Select(layout, ""));

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(new[] { "Unlink .claude/skills/house-style" }));
        }

        [Test]
        public void Plan_NoSelectionGiven_UsesEveryFolderInTheLayout()
        {
            var plan = InstallPlanner.Plan(Lock("tdd"), ProjectState.Empty, ExtendedLayout);

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(new[]
            {
                "Install .agents/skills/tdd", "Link .claude/skills/tdd -> .agents/skills/tdd", "Link .junie/skills/tdd -> .agents/skills/tdd",
            }));
        }
    }
}
