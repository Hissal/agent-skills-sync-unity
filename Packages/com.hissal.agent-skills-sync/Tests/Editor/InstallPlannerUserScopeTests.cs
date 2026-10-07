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

        static FolderLayout Table => FolderLayout.Default;

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
                states.Add(new FolderState(Table.Find(parts[0]), present, managed, hashes));
            }
            return new ProjectState(states);
        }

        /// <summary>Folders (comma-separated) whose agents have <c>tdd</c> at user scope; empty = none.</summary>
        static UserScopeState UserScope(string folders) =>
            new UserScopeState(Paths(folders).Select(p =>
                new UserScopeCopy(Table.Find(p), "tdd", "/home/" + p + "/tdd", p == ClaudePath ? "~/.claude/skills" : "~/.codex/skills")));

        /// <summary>Folders (comma-separated) where the contributor skips <c>tdd</c>; empty = none.</summary>
        static SkipChoices Skips(string folders) =>
            new SkipChoices(Paths(folders).ToDictionary(p => p, p => (IReadOnlyList<string>)new[] { "tdd" }));

        static IEnumerable<string> Paths(string list) => list.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0);

        static IEnumerable<SkillsFolder> Selected(string list) => Paths(list).Select(Table.Find);

        const string Both = AgentsPath + "," + ClaudePath;

        static TestCaseData Case(string name, string project, string selected, string found, string skipped, params string[] expected) =>
            new TestCaseData(project, selected, found, skipped, expected).SetName("Plan_SkipUserScope_" + name);

        static IEnumerable<TestCaseData> Cases()
        {
            // Fresh project.
            yield return Case("NoCopyFound_SkipStored_InstallsAsUsual", "", Both, "", Both,
                "Install .agents/skills/tdd", "Link .claude/skills/tdd");
            yield return Case("CopyFound_NotSkipped_InstallsAsUsual", "", Both, Both, "",
                "Install .agents/skills/tdd", "Link .claude/skills/tdd");
            yield return Case("ClaudeSkipped_CopiesForAgentsOnly", "", Both, ClaudePath, ClaudePath,
                "Install .agents/skills/tdd", "SkipUserScope .claude/skills/tdd");
            yield return Case("AgentsSkipped_KeepsTheCopyTheClaudeLinkNeeds", "", Both, AgentsPath, AgentsPath,
                "Install .agents/skills/tdd", "Link .claude/skills/tdd");
            yield return Case("AgentsSkippedClaudeNotSelected_InstallsNothing", "", AgentsPath, AgentsPath, AgentsPath,
                "SkipUserScope .agents/skills/tdd");
            yield return Case("ClaudeSkippedAgentsNotSelected_InstallsNothing", "", ClaudePath, ClaudePath, ClaudePath,
                "SkipUserScope .claude/skills/tdd");
            yield return Case("BothSkipped_InstallsNothing", "", Both, Both, Both,
                "SkipUserScope .claude/skills/tdd", "SkipUserScope .agents/skills/tdd");
            yield return Case("SkipStoredForAnUnselectedFolder_Ignored", "", ClaudePath, Both, AgentsPath,
                "Install .agents/skills/tdd", "Link .claude/skills/tdd");

            // Installed in both folders.
            const string installed = AgentsPath + "=tdd;" + ClaudePath + "=tdd";
            yield return Case("Installed_ClaudeSkipped_UnlinksOnlyClaude", installed, Both, Both, ClaudePath,
                "Unlink .claude/skills/tdd", "SkipUserScope .claude/skills/tdd");
            yield return Case("Installed_AgentsSkipped_KeepsEverything", installed, Both, Both, AgentsPath);
            yield return Case("Installed_BothSkipped_UnlinksThenRemoves", installed, Both, Both, Both,
                "Unlink .claude/skills/tdd", "SkipUserScope .claude/skills/tdd",
                "Remove .agents/skills/tdd", "SkipUserScope .agents/skills/tdd");
            yield return Case("Installed_ClaudeSkippedButCopyGone_KeepsTheLink", installed, Both, "", ClaudePath);

            // Skipped earlier, now un-skipped (or the user-scope copy is gone): the project copy comes back.
            yield return Case("ClaudeUnskipped_LinksAgain", AgentsPath + "=tdd", Both, ClaudePath, "",
                "Link .claude/skills/tdd");
            yield return Case("BothUnskipped_InstallsAgain", "", Both, Both, "",
                "Install .agents/skills/tdd", "Link .claude/skills/tdd");

            // Entries the tool does not manage are never touched.
            yield return Case("ClaudeSkippedOverAForeignEntry_LeavesItAlone", AgentsPath + "=tdd;" + ClaudePath + "=tdd?", Both, ClaudePath, ClaudePath,
                "SkipUserScope .claude/skills/tdd");
            yield return Case("BothSkippedOverAForeignCanonicalEntry_LeavesItAlone", AgentsPath + "=tdd?", Both, Both, Both,
                "SkipUserScope .claude/skills/tdd", "SkipUserScope .agents/skills/tdd");
        }

        [TestCaseSource(nameof(Cases))]
        public void Plan_SkipUserScope(string project, string selected, string found, string skipped, string[] expected)
        {
            var plan = InstallPlanner.Plan(Lock("tdd"), Project(project.Split(';')), Table, selected: Selected(selected),
                userScope: UserScope(found), skips: Skips(skipped));

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(expected));
        }

        [Test]
        public void Plan_ClaudeSkipped_DropsTheNameFromClaudesManagedListOnly()
        {
            var plan = InstallPlanner.Plan(Lock("tdd"), Project(AgentsPath + "=tdd", ClaudePath + "=tdd"), Table,
                userScope: UserScope(ClaudePath), skips: Skips(ClaudePath));

            Assert.That(plan.ManagedNames[Table.Find(ClaudePath)], Is.Empty);
            Assert.That(plan.ManagedNames[Table.Canonical], Is.EqualTo(new[] { "tdd" }));
        }

        [Test]
        public void Plan_SkipUserScope_NamesWhereTheCopyWasFoundAndChangesNothing()
        {
            var plan = InstallPlanner.Plan(Lock("tdd"), ProjectState.Empty, Table,
                userScope: UserScope(ClaudePath), skips: Skips(ClaudePath));

            var skip = plan.Actions.Single(a => a.Kind == PlanActionKind.SkipUserScope);
            Assert.That(skip.UserScopeCopies.Select(c => c.FoundIn), Is.EqualTo(new[] { "~/.claude/skills" }));
            Assert.That(skip.UserScopeCopies.Single().Path, Is.EqualTo("/home/.claude/skills/tdd"));
            Assert.That(skip.Skill.Name, Is.EqualTo("tdd"));
            Assert.That(skip.ChangesProject, Is.False);
        }

        [Test]
        public void Plan_OnlySkippedSkillIsSkipped()
        {
            var plan = InstallPlanner.Plan(Lock("other", "tdd"), ProjectState.Empty, Table,
                userScope: UserScope(ClaudePath), skips: Skips(ClaudePath));

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
            var plan = InstallPlanner.Plan(Lock(), Project(AgentsPath + "=tdd?"), Table,
                userScope: UserScope(ClaudePath), skips: Skips(ClaudePath));

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(new[] { "Link .claude/skills/tdd" }));
        }
    }
}
