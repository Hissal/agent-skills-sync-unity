using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>The environment scanner over faked user-scope locations (a temp home and fake environment variables).</summary>
    public class UserScopeScannerTests
    {
        string _root;
        string _home;
        Dictionary<string, string> _variables;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            _home = Path.Combine(_root, "home");
            Directory.CreateDirectory(_home);
            _variables = new Dictionary<string, string>();
        }

        [TearDown]
        public void TearDown() => TempDirectory.Delete(_root);

        UserEnvironment Environment =>
            new UserEnvironment(_home, name => _variables.TryGetValue(name, out var value) ? value : null);

        static FolderLayout Layout => FolderLayout.Default;

        static SkillsFolder Agents => Layout.Find(".agents/skills");

        static SkillsFolder Claude => Layout.Find(".claude/skills");

        /// <summary>Creates <c>skill/SKILL.md</c> under <paramref name="skillsFolder"/>, relative to the home.</summary>
        string MakeSkill(string skillsFolder, string skill)
        {
            var folder = Path.Combine(_home, skillsFolder.Replace('/', Path.DirectorySeparatorChar), skill);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "SKILL.md"), "---\nname: " + skill + "\n---\n");
            return folder;
        }

        UserScopeState Scan(params SkillsFolder[] folders) => UserScopeScanner.Scan(folders, Environment);

        static string[] FoundIn(UserScopeState state, SkillsFolder folder, string skill) =>
            state.CopiesOf(folder, skill).Select(c => c.FoundIn).ToArray();

        [Test]
        public void Scan_NoUserScopeSkills_FindsNothing()
        {
            var state = Scan(Agents, Claude);

            Assert.That(state.Copies, Is.Empty);
            Assert.That(state.Has(Agents, "tdd"), Is.False);
        }

        [Test]
        public void Scan_SkillOnlyInCodexHome_IsADuplicateForTheAgentsFolderOnly()
        {
            var path = MakeSkill(".codex/skills", "tdd");

            var state = Scan(Agents, Claude);

            Assert.That(FoundIn(state, Agents, "tdd"), Is.EqualTo(new[] { "~/.codex/skills" }));
            Assert.That(state.CopiesOf(Agents, "tdd").Single().Path, Is.EqualTo(Path.GetFullPath(path)));
            Assert.That(state.CopiesOf(Agents, "tdd").Single().Folder, Is.SameAs(Agents));
            Assert.That(state.Has(Claude, "tdd"), Is.False);
        }

        [Test]
        public void Scan_SkillInClaudeHome_IsADuplicateForTheClaudeFolderOnly()
        {
            MakeSkill(".claude/skills", "tdd");

            var state = Scan(Agents, Claude);

            Assert.That(FoundIn(state, Claude, "tdd"), Is.EqualTo(new[] { "~/.claude/skills" }));
            Assert.That(state.Has(Agents, "tdd"), Is.False);
        }

        [Test]
        public void Scan_SkillSyncedByClaudeAi_IsADuplicateForTheClaudeFolder()
        {
            var path = MakeSkill(".claude/skills/synced", "tdd");

            var state = Scan(Agents, Claude);

            Assert.That(FoundIn(state, Claude, "tdd"), Is.EqualTo(new[] { "~/.claude/skills/synced" }));
            Assert.That(state.CopiesOf(Claude, "tdd").Single().Path, Is.EqualTo(Path.GetFullPath(path)));
            Assert.That(state.Has(Claude, "synced"), Is.False);
            Assert.That(state.Has(Agents, "tdd"), Is.False);
        }

        [Test]
        public void Scan_ClaudeConfigDirSet_LooksForSyncedSkillsUnderIt()
        {
            var configDir = Path.Combine(_root, "claude-config");
            _variables["CLAUDE_CONFIG_DIR"] = configDir;
            MakeSkill(".claude/skills/synced", "ignored");
            var moved = Path.Combine(configDir, "skills", "synced", "tdd");
            Directory.CreateDirectory(moved);
            File.WriteAllText(Path.Combine(moved, "SKILL.md"), "x");

            var state = Scan(Claude);

            Assert.That(FoundIn(state, Claude, "tdd"), Is.EqualTo(new[] { Path.Combine(configDir, "skills", "synced") }));
            Assert.That(state.Has(Claude, "ignored"), Is.False);
        }

        [Test]
        public void Scan_SkillWithNestedSkillFolders_ReportsOnlyTheSkill()
        {
            MakeSkill(".claude/skills", "tdd");
            MakeSkill(".claude/skills/tdd", "inner");
            MakeSkill(".claude/skills/synced/review", "inner");

            var state = Scan(Claude);

            Assert.That(state.Copies.Select(c => c.SkillName), Is.EqualTo(new[] { "tdd" }));
        }

        [Test]
        public void Scan_SkillInSeveralLocations_ReportsEachInLayoutOrder()
        {
            MakeSkill(".cursor/skills", "tdd");
            MakeSkill(".agents/skills", "tdd");
            MakeSkill(".config/opencode/skills", "tdd");

            var state = Scan(Agents);

            Assert.That(FoundIn(state, Agents, "tdd"),
                Is.EqualTo(new[] { "~/.agents/skills", "~/.cursor/skills", "~/.config/opencode/skills" }));
        }

        [Test]
        public void Scan_ChecksEveryListedLocation()
        {
            foreach (var location in Agents.UserScopeLocations)
                MakeSkill(location.ResolveSkillsFolder(Environment).Substring(_home.Length + 1), "tdd");

            var state = Scan(Agents);

            Assert.That(state.CopiesOf(Agents, "tdd").Select(c => c.Path),
                Is.EqualTo(Agents.UserScopeSkillsFolders(Environment).Select(f => Path.Combine(f, "tdd"))));
        }

        [Test]
        public void Scan_EnvVarMovesTheHome_LooksThereAndNamesTheResolvedFolder()
        {
            var codexHome = Path.Combine(_root, "elsewhere", "codex");
            _variables["CODEX_HOME"] = codexHome;
            MakeSkill(".codex/skills", "ignored");
            var moved = Path.Combine(codexHome, "skills", "tdd");
            Directory.CreateDirectory(moved);
            File.WriteAllText(Path.Combine(moved, "SKILL.md"), "x");

            var state = Scan(Agents);

            Assert.That(FoundIn(state, Agents, "tdd"), Is.EqualTo(new[] { Path.Combine(codexHome, "skills") }));
            Assert.That(state.Has(Agents, "ignored"), Is.False);
        }

        [Test]
        public void Scan_FolderWithoutSkillFile_IsNotASkill()
        {
            Directory.CreateDirectory(Path.Combine(_home, ".claude", "skills", "tdd"));
            File.WriteAllText(Path.Combine(_home, ".claude", "skills", "notes.md"), "x");

            var state = Scan(Claude);

            Assert.That(state.Copies, Is.Empty);
        }

        [Test]
        public void Scan_OnlyTheGivenFolders()
        {
            MakeSkill(".claude/skills", "tdd");
            MakeSkill(".agents/skills", "tdd");

            var state = Scan(Agents);

            Assert.That(state.Has(Agents, "tdd"), Is.True);
            Assert.That(state.Has(Claude, "tdd"), Is.False);
        }

        [Test]
        public void Scan_TwoLocationsResolvingToTheSameFolder_ReportItOnce()
        {
            // Gemini's $GEMINI_CLI_HOME/.agents/skills is ~/.agents/skills while the variable is unset.
            MakeSkill(".agents/skills", "tdd");

            var state = Scan(Agents);

            Assert.That(FoundIn(state, Agents, "tdd"), Is.EqualTo(new[] { "~/.agents/skills" }));
        }

        sealed class FakeSource : IUserScopeSource
        {
            public IEnumerable<UserScopeCopy> Find(SkillsFolder folder, UserEnvironment environment) =>
                folder.RelativePath == ".claude/skills"
                    ? new[] { new UserScopeCopy(folder, "tdd", "/plugins/acme/skills/tdd", "plugin acme", "Claude Code") }
                    : Enumerable.Empty<UserScopeCopy>();
        }

        [Test]
        public void Scan_ExtraSource_ReportsItsCopiesBesideTheLocations()
        {
            MakeSkill(".claude/skills", "tdd");

            var state = UserScopeScanner.Scan(new[] { Agents, Claude }, Environment,
                UserScopeScanner.DefaultSources.Concat(new[] { new FakeSource() }));

            Assert.That(FoundIn(state, Claude, "tdd"), Is.EqualTo(new[] { "~/.claude/skills", "plugin acme" }));
            Assert.That(state.Has(Agents, "tdd"), Is.False);
        }
    }
}
