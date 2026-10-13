using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>The planner over which locked skills this machine opted out of.</summary>
    public class InstallPlannerOptOutTests
    {
        const string Hash = "locked-hash";

        static readonly FolderLayout Layout = FolderLayout.Default;

        static Lockfile Lock(params string[] names) =>
            new Lockfile(names.Select(n => new LockedSkill(n, "owner/repo", "github", $"skills/{n}/SKILL.md", Hash)).ToList());

        static string Describe(PlanAction action) =>
            action.Kind == PlanActionKind.Link
                ? $"Link {action.Folder.RelativePath}/{action.SkillName} -> {action.LinkTarget.RelativePath}/{action.SkillName}"
                : $"{action.Kind} {action.Folder.RelativePath}/{action.SkillName}";

        /// <summary>Entries are <c>name</c> = managed (at the locked hash), <c>name?</c> = present but not managed.</summary>
        static FolderState Folder(SkillsFolder folder, string[] entries, string[] ignored = null)
        {
            var present = entries.Select(e => e.TrimEnd('?')).ToList();
            var managed = entries.Where(e => !e.EndsWith("?")).ToList();
            return new FolderState(folder, present, managed, managed.ToDictionary(n => n, _ => Hash), ignored: ignored);
        }

        static ProjectState Project(string[] canonical, string[] claude, string[] ignored = null) => new ProjectState(new[]
        {
            Folder(Layout.Canonical, canonical, ignored),
            Folder(Layout.Find(".claude/skills"), claude, ignored),
        });

        /// <summary>Plans as Latest mode decides what to fetch: every managed copy of a selected skill counts as outdated.</summary>
        static InstallPlan PlanFor(string[] locked, string[] optedOut, ProjectState project, UserScopeState userScope = null) =>
            InstallPlanner.Plan(Lock(locked), project,
                new MachineChoices(Layout, userScope: userScope, skills: new SkillSelection(optedOut)), FixedCheck.NeverCurrent);

        static TestCaseData Case(string name, string[] locked, string[] optedOut, string[] canonical, string[] claude,
            params string[] expected) =>
            new TestCaseData(locked, optedOut, canonical, claude, expected).SetName("Plan_" + name);

        static readonly string[] None = new string[0];

        static IEnumerable<TestCaseData> Cases()
        {
            yield return Case("OptedOutOfTheOnlySkill_InstallsNothing",
                new[] { "tdd" }, new[] { "tdd" }, None, None);
            yield return Case("OptedOutOfOneSkill_InstallsTheOther",
                new[] { "tdd", "code-review" }, new[] { "tdd" }, None, None,
                "Install .agents/skills/code-review",
                "Link .claude/skills/code-review -> .agents/skills/code-review");
            yield return Case("OptedOutOfAnInstalledSkill_WithdrawsItLinksFirstAndNeverUpdatesIt",
                new[] { "tdd", "code-review" }, new[] { "tdd" }, new[] { "tdd", "code-review" }, new[] { "tdd", "code-review" },
                "Unlink .claude/skills/tdd",
                "Remove .agents/skills/tdd",
                "Update .agents/skills/code-review");
            yield return Case("OptedOutSkillLinkedOnly_UnlinksIt",
                new[] { "tdd" }, new[] { "tdd" }, None, new[] { "tdd" },
                "Unlink .claude/skills/tdd");
            yield return Case("OptedOutSkillPresentAsForeignEntries_LeavesThemUntouchedAndUnreported",
                new[] { "tdd" }, new[] { "tdd" }, new[] { "tdd?" }, new[] { "tdd?" });
            yield return Case("OptedOutSkillForeignCopyWithManagedLink_UnlinksOnly",
                new[] { "tdd" }, new[] { "tdd" }, new[] { "tdd?" }, new[] { "tdd" },
                "Unlink .claude/skills/tdd");
            yield return Case("OptOutOfASkillThatIsNotLocked_ChangesNothing",
                new[] { "tdd" }, new[] { "gone" }, None, None,
                "Install .agents/skills/tdd",
                "Link .claude/skills/tdd -> .agents/skills/tdd");
        }

        [TestCaseSource(nameof(Cases))]
        public void Plan(string[] locked, string[] optedOut, string[] canonical, string[] claude, string[] expected)
        {
            var plan = PlanFor(locked, optedOut, Project(canonical, claude));

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(expected));
        }

        [Test]
        public void Plan_OptedOutSkill_StaysInTheGitignoreBlockButIsNoLongerManaged()
        {
            var plan = PlanFor(new[] { "tdd", "code-review" }, new[] { "tdd" },
                Project(new[] { "tdd", "code-review" }, new[] { "tdd", "code-review" }, ignored: new[] { "code-review", "tdd" }));

            foreach (var folder in Layout.Folders)
            {
                Assert.That(plan.IgnoredNames[folder], Is.EqualTo(new[] { "code-review", "tdd" }));
                Assert.That(plan.ManagedNames[folder], Is.EqualTo(new[] { "code-review" }));
            }
        }

        [Test]
        public void Plan_OptedOutSkillNeverInstalled_ChangesNoManagedNamesOrGitignoreBlock()
        {
            var plan = PlanFor(new[] { "tdd", "code-review" }, new[] { "tdd" },
                Project(new[] { "code-review" }, new[] { "code-review" }, ignored: new[] { "code-review", "tdd" }));

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(new[] { "Update .agents/skills/code-review" }));
            Assert.That(plan.ManagedNamesChange, Is.False);
        }

        [Test]
        public void Plan_OptedOutSkillWithAUserScopeCopy_ReportsNoSkip()
        {
            var userScope = new UserScopeState(Layout.Folders.Select(f =>
                new UserScopeCopy(f, "tdd", "/home/me/.claude/skills/tdd", "~/.claude/skills")));

            var plan = PlanFor(new[] { "tdd" }, new[] { "tdd" }, Project(None, None), userScope);

            Assert.That(plan.Actions, Is.Empty);
        }
    }
}
