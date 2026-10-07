using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class InstallPlannerTests
    {
        static LockedSkill Skill(string name) =>
            new LockedSkill(name, "owner/repo", "github", $"skills/{name}/SKILL.md", "hash");

        static Lockfile Lock(params string[] names) => new Lockfile(names.Select(Skill).ToList());

        /// <summary>Renders an action the way the expectations below are written.</summary>
        static string Describe(PlanAction action) =>
            action.Kind == PlanActionKind.Link
                ? $"Link {action.Folder.RelativePath}/{action.SkillName} -> {action.LinkTarget.RelativePath}/{action.SkillName}"
                : $"{action.Kind} {action.Folder.RelativePath}/{action.SkillName}";

        static IEnumerable<TestCaseData> EmptyProjectCases()
        {
            yield return new TestCaseData((object)new string[0], new string[0])
                .SetName("Plan_EmptyProject_EmptyLock_DoesNothing");
            yield return new TestCaseData(
                    new[] { "tdd" },
                    new[]
                    {
                        "Install .agents/skills/tdd",
                        "Link .claude/skills/tdd -> .agents/skills/tdd",
                    })
                .SetName("Plan_EmptyProject_OneSkill_InstallsCanonicalCopyAndLinksClaude");
            yield return new TestCaseData(
                    new[] { "tdd", "code-review" },
                    new[]
                    {
                        "Install .agents/skills/tdd",
                        "Link .claude/skills/tdd -> .agents/skills/tdd",
                        "Install .agents/skills/code-review",
                        "Link .claude/skills/code-review -> .agents/skills/code-review",
                    })
                .SetName("Plan_EmptyProject_TwoSkills_InstallsAndLinksEach");
        }

        [TestCaseSource(nameof(EmptyProjectCases))]
        public void Plan_EmptyProject(string[] locked, string[] expectedActions)
        {
            var plan = InstallPlanner.Plan(Lock(locked), ProjectState.Empty, FolderLayout.Default);

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(expectedActions));
        }

        [Test]
        public void Plan_EmptyProject_ManagesEveryLockedSkillInBothFolders()
        {
            var plan = InstallPlanner.Plan(Lock("tdd", "code-review"), ProjectState.Empty, FolderLayout.Default);

            var managed = plan.ManagedNames.ToDictionary(m => m.Key.RelativePath, m => m.Value);
            Assert.That(managed.Keys, Is.EquivalentTo(new[] { ".agents/skills", ".claude/skills" }));
            Assert.That(managed[".agents/skills"], Is.EqualTo(new[] { "code-review", "tdd" }));
            Assert.That(managed[".claude/skills"], Is.EqualTo(new[] { "code-review", "tdd" }));
        }

        [Test]
        public void Plan_SkillAlreadyInstalledAndLinked_DoesNothingAndKeepsManagingIt()
        {
            var layout = FolderLayout.Default;
            var project = new ProjectState(layout.Folders.Select(folder =>
                new FolderState(folder, entries: new[] { "tdd" }, managed: new[] { "tdd" })));

            var plan = InstallPlanner.Plan(Lock("tdd"), project, layout);

            Assert.That(plan.Actions, Is.Empty);
            Assert.That(plan.ManagedNames.Select(m => m.Value), Has.All.EqualTo(new[] { "tdd" }));
        }
    }
}
