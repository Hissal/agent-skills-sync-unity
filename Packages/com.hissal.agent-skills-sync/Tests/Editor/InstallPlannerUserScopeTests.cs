using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>The planner over user-scope duplicates and the contributor's per-folder skip choices.</summary>
    public class InstallPlannerUserScopeTests
    {
        const string Hash = "locked-hash";
        const string AgentsPath = ".agents/skills";
        const string ClaudePath = ".claude/skills";

        static FolderLayout Layout => FolderLayout.Default;

        static Lockfile Lock(params string[] names) =>
            new Lockfile(names.Select(n => new LockedSkill(n, "owner/repo", "github", $"skills/{n}/SKILL.md", Hash)).ToList());

        static string Describe(PlanAction action) =>
            action.Kind == PlanActionKind.Link
                ? $"Link {action.Folder.RelativePath}/{action.SkillName}"
                : $"{action.Kind} {action.Folder.RelativePath}/{action.SkillName}";

        /// <summary>Each spec is <c>path=entries</c>; <c>name</c> = managed (at the locked hash), <c>name?</c> = present, not managed.</summary>
        static ProjectState Project(params string[] folders)
        {
            var states = new List<FolderState>();
            foreach (var spec in folders.Where(s => s.Length > 0))
            {
                var parts = spec.Split('=');
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
                states.Add(new FolderState(Layout.Find(parts[0]), present, managed, hashes));
            }
            return new ProjectState(states);
        }

        /// <summary>Folders (comma-separated) whose agents have <c>tdd</c> at user scope; empty = none.</summary>
        static UserScopeState UserScope(string folders) =>
            new UserScopeState(Paths(folders).Select(p =>
                new UserScopeCopy(Layout.Find(p), "tdd", "/home/" + p + "/tdd", p == ClaudePath ? "~/.claude/skills" : "~/.codex/skills")));

        /// <summary>Folders (comma-separated) where the contributor installs anyway <c>tdd</c>; empty = none.</summary>
        static InstallAnywayChoices Overrides(string folders) =>
            new InstallAnywayChoices(Paths(folders).ToDictionary(p => p, p => (IReadOnlyList<string>)new[] { "tdd" }));

        static IEnumerable<string> Paths(string list) => list.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0);

        static IEnumerable<SkillsFolder> Selected(string list) => Paths(list).Select(Layout.Find);

        const string Both = AgentsPath + "," + ClaudePath;

        static TestCaseData Case(string name, string project, string selected, string found, string installAnyway, params string[] expected) =>
            new TestCaseData(project, selected, found, installAnyway, expected).SetName("Plan_SkipUserScope_" + name);

        static IEnumerable<TestCaseData> Cases()
        {
            yield return Case("NoCopy_NoChoice_Installs", "", Both, "", "",
                "Install .agents/skills/tdd", "Link .claude/skills/tdd");
            yield return Case("NoCopy_OverridesStored_Installs", "", Both, "", Both,
                "Install .agents/skills/tdd", "Link .claude/skills/tdd");
            yield return Case("BothFound_NoChoice_UsesMine", "", Both, Both, "",
                "SkipUserScope .claude/skills/tdd", "SkipUserScope .agents/skills/tdd");
            yield return Case("BothFound_BothInstallAnyway_Installs", "", Both, Both, Both,
                "Install .agents/skills/tdd", "Link .claude/skills/tdd");
            yield return Case("ClaudeFound_NoChoice_CopiesForAgents", "", Both, ClaudePath, "",
                "Install .agents/skills/tdd", "SkipUserScope .claude/skills/tdd");
            yield return Case("AgentsFound_ClaudeNeedsCanonical_KeepsCopy", "", Both, AgentsPath, "",
                "Install .agents/skills/tdd", "SkipUserScope .agents/skills/tdd", "Link .claude/skills/tdd");
            yield return Case("AgentsOnly_Found_UsesMine", "", AgentsPath, AgentsPath, "",
                "SkipUserScope .agents/skills/tdd");
            yield return Case("ClaudeOnly_Found_UsesMine", "", ClaudePath, ClaudePath, "",
                "SkipUserScope .claude/skills/tdd");
            yield return Case("OverrideInUnselectedFolder_Ignored", "", ClaudePath, Both, AgentsPath,
                "SkipUserScope .claude/skills/tdd");
            yield return Case("ClaudeOverride_InstallsBoth", "", Both, Both, ClaudePath,
                "Install .agents/skills/tdd", "SkipUserScope .agents/skills/tdd", "Link .claude/skills/tdd");
            yield return Case("AgentsOverride_OnlyInstallsAgents", "", Both, Both, AgentsPath,
                "Install .agents/skills/tdd", "SkipUserScope .claude/skills/tdd");

            const string installed = AgentsPath + "=tdd;" + ClaudePath + "=tdd";
            yield return Case("Installed_ClaudeFound_UnlinksClaude", installed, Both, ClaudePath, "",
                "Unlink .claude/skills/tdd", "SkipUserScope .claude/skills/tdd");
            yield return Case("Installed_BothFound_WithdrawsBoth", installed, Both, Both, "",
                "Unlink .claude/skills/tdd", "SkipUserScope .claude/skills/tdd",
                "Remove .agents/skills/tdd", "SkipUserScope .agents/skills/tdd");
            yield return Case("Installed_CopyGone_KeepsBoth", installed, Both, "", "");
            yield return Case("ClaudeInstallAnyway_LinksAgain", AgentsPath + "=tdd", Both, ClaudePath, ClaudePath,
                "Link .claude/skills/tdd");
            yield return Case("ForeignClaude_LeavesUntouched", AgentsPath + "=tdd;" + ClaudePath + "=tdd?", Both, ClaudePath, "",
                "SkipUserScope .claude/skills/tdd");
            yield return Case("ForeignCanonical_LeavesUntouched", AgentsPath + "=tdd?", Both, Both, "",
                "SkipUserScope .claude/skills/tdd", "SkipUserScope .agents/skills/tdd");
        }

        [TestCaseSource(nameof(Cases))]
        public void Plan_SkipUserScope(string project, string selected, string found, string installAnyway, string[] expected)
        {
            var plan = InstallPlanner.Plan(Lock("tdd"), Project(project.Split(';')), Layout, selected: Selected(selected),
                userScope: UserScope(found), installAnyway: Overrides(installAnyway));

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(expected));
        }

        [Test]
        public void Plan_UserScopeCopyFoundWithoutStoredChoice_UsesMineByDefault()
        {
            var plan = InstallPlanner.Plan(Lock("tdd"), ProjectState.Empty, Layout,
                selected: Selected(Both), userScope: UserScope(Both));

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(new[]
            {
                "SkipUserScope .claude/skills/tdd", "SkipUserScope .agents/skills/tdd",
            }));
        }

        [Test]
        public void Plan_ClaudeSkipped_DropsTheNameFromClaudesManagedListOnly()
        {
            var plan = InstallPlanner.Plan(Lock("tdd"), Project(AgentsPath + "=tdd", ClaudePath + "=tdd"), Layout,
                userScope: UserScope(ClaudePath), installAnyway: InstallAnywayChoices.None);

            Assert.That(plan.ManagedNames[Layout.Find(ClaudePath)], Is.Empty);
            Assert.That(plan.ManagedNames[Layout.Canonical], Is.EqualTo(new[] { "tdd" }));
        }

        [Test]
        public void Plan_SkipUserScope_NamesWhereTheCopyWasFoundAndChangesNothing()
        {
            var plan = InstallPlanner.Plan(Lock("tdd"), ProjectState.Empty, Layout,
                userScope: UserScope(ClaudePath), installAnyway: InstallAnywayChoices.None);

            var skip = plan.Actions.Single(a => a.Kind == PlanActionKind.SkipUserScope);
            Assert.That(skip.UserScopeCopies.Select(c => c.FoundIn), Is.EqualTo(new[] { "~/.claude/skills" }));
            Assert.That(skip.UserScopeCopies.Single().Path, Is.EqualTo("/home/.claude/skills/tdd"));
            Assert.That(skip.Skill.Name, Is.EqualTo("tdd"));
            Assert.That(skip.ChangesProject, Is.False);
        }

        [Test]
        public void Plan_OnlySkillFoundAtUserScopeIsSkipped()
        {
            var plan = InstallPlanner.Plan(Lock("other", "tdd"), ProjectState.Empty, Layout,
                userScope: UserScope(ClaudePath), installAnyway: InstallAnywayChoices.None);

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(new[]
            {
                "Install .agents/skills/other", "Link .claude/skills/other",
                "Install .agents/skills/tdd", "SkipUserScope .claude/skills/tdd",
            }));
        }

        [Test]
        public void Plan_ProjectAuthoredSkillWithTheSkippedName_IsStillLinked()
        {
            // Skips cover locked skills only; a project-authored skill is the project's own.
            var plan = InstallPlanner.Plan(Lock(), Project(AgentsPath + "=tdd?"), Layout,
                userScope: UserScope(ClaudePath), installAnyway: InstallAnywayChoices.None);

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(new[] { "Link .claude/skills/tdd" }));
        }
    }
}
