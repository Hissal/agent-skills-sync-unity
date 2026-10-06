using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class FolderSelectionTests
    {
        string _root;
        string _home;
        string _project;
        Dictionary<string, string> _variables;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            _home = Path.Combine(_root, "home");
            _project = Path.Combine(_root, "project");
            Directory.CreateDirectory(_home);
            Directory.CreateDirectory(_project);
            _variables = new Dictionary<string, string>();
        }

        [TearDown]
        public void TearDown() => TempDirectory.Delete(_root);

        UserEnvironment Environment =>
            new UserEnvironment(_home, name => _variables.TryGetValue(name, out var value) ? value : null);

        void MakeHome(string relativePath) => Directory.CreateDirectory(Path.Combine(_home, relativePath));

        static FolderLayout Table => FolderLayout.Default;

        IEnumerable<string> Autofill() => FolderSelection.Autofill(Table, Environment).Select(f => f.RelativePath);

        [Test]
        public void Autofill_NoAgentHomes_SelectsNothing()
        {
            Assert.That(Autofill(), Is.Empty);
        }

        [Test]
        public void Autofill_CodexOnlyMachine_SelectsOnlyTheAgentsFolder()
        {
            MakeHome(".codex");

            Assert.That(Autofill(), Is.EqualTo(new[] { ".agents/skills" }));
        }

        [Test]
        public void Autofill_ClaudeOnlyMachine_SelectsOnlyTheClaudeFolder()
        {
            MakeHome(".claude");

            Assert.That(Autofill(), Is.EqualTo(new[] { ".claude/skills" }));
        }

        [TestCase(".agents")]
        [TestCase(".cursor")]
        [TestCase(".copilot")]
        public void Autofill_AnyAgentsFolderHome_SelectsTheAgentsFolder(string home)
        {
            MakeHome(home);

            Assert.That(Autofill(), Is.EqualTo(new[] { ".agents/skills" }));
        }

        [Test]
        public void Autofill_BothHomes_SelectsBothInTableOrder()
        {
            MakeHome(".claude");
            MakeHome(".agents");

            Assert.That(Autofill(), Is.EqualTo(new[] { ".agents/skills", ".claude/skills" }));
        }

        [Test]
        public void Autofill_CodexHomeMovedByEnvVar_FindsItThere()
        {
            var codexHome = Path.Combine(_root, "elsewhere", "codex");
            Directory.CreateDirectory(codexHome);
            _variables["CODEX_HOME"] = codexHome;

            Assert.That(Autofill(), Is.EqualTo(new[] { ".agents/skills" }));
        }

        [Test]
        public void Autofill_ClaudeConfigDirSet_IgnoresTheDefaultClaudeHome()
        {
            MakeHome(".claude");
            _variables["CLAUDE_CONFIG_DIR"] = Path.Combine(_root, "missing-claude-config");

            Assert.That(Autofill(), Is.Empty);
        }

        [Test]
        public void UserScopeSkillsFolders_ResolveEnvVarOverridesAndDefaults()
        {
            _variables["CLAUDE_CONFIG_DIR"] = Path.Combine(_root, "claude-config");
            var claude = Table.Folders.Single(f => f.RelativePath == ".claude/skills");
            var agents = Table.Folders.Single(f => f.RelativePath == ".agents/skills");

            Assert.That(claude.UserScopeSkillsFolders(Environment),
                Is.EqualTo(new[] { Path.Combine(_root, "claude-config", "skills") }));
            Assert.That(agents.UserScopeSkillsFolders(Environment), Is.SupersetOf(new[]
            {
                Path.Combine(_home, ".agents", "skills"),
                Path.Combine(_home, ".codex", "skills"),
                Path.Combine(_home, ".cursor", "skills"),
                Path.Combine(_home, ".copilot", "skills"),
            }));
        }

        LocalPrefs Prefs => LocalPrefs.Load(_project);

        static SkillsFolder Agents => Table.Find(".agents/skills");
        static SkillsFolder Claude => Table.Find(".claude/skills");

        void Choose(params SkillsFolder[] folders)
        {
            var prefs = Prefs;
            FolderSelection.Save(prefs, Table, folders, Environment);
            prefs.Save();
        }

        IEnumerable<string> Effective() => FolderSelection.Effective(Prefs, Table, Environment).Select(f => f.RelativePath);

        IEnumerable<string> Offers() => FolderSelection.Offers(Prefs, Table, Environment).Select(f => f.RelativePath);

        [Test]
        public void Effective_NeverChosen_IsTheAutofill()
        {
            MakeHome(".codex");

            Assert.That(FolderSelection.IsChosen(Prefs), Is.False);
            Assert.That(Effective(), Is.EqualTo(new[] { ".agents/skills" }));
        }

        [Test]
        public void Effective_ChosenNone_StaysNoneWhateverHomesExist()
        {
            Choose();
            MakeHome(".claude");
            MakeHome(".agents");

            Assert.That(FolderSelection.IsChosen(Prefs), Is.True);
            Assert.That(Effective(), Is.Empty);
        }

        [Test]
        public void Effective_HomeVanished_KeepsTheChosenFolder()
        {
            MakeHome(".claude");
            MakeHome(".agents");
            Choose(Agents, Claude);
            Directory.Delete(Path.Combine(_home, ".agents"));

            Assert.That(Effective(), Is.EqualTo(new[] { ".agents/skills", ".claude/skills" }));
            Assert.That(Offers(), Is.Empty);
        }

        [Test]
        public void Effective_StoredFolderNoLongerInTheTable_IsIgnored()
        {
            var prefs = Prefs;
            prefs.SelectedFolders = new[] { ".windsurf/skills", ".claude/skills" };
            prefs.Save();

            Assert.That(Effective(), Is.EqualTo(new[] { ".claude/skills" }));
        }

        [Test]
        public void Offers_NeverChosen_OffersNothing()
        {
            MakeHome(".claude");
            MakeHome(".codex");

            Assert.That(Offers(), Is.Empty);
        }

        [Test]
        public void Offers_NewlyAppearedHome_OffersItsFolderUntilDeclined()
        {
            MakeHome(".claude");
            Choose(Claude);
            Assert.That(Offers(), Is.Empty);

            MakeHome(".codex");
            Assert.That(Offers(), Is.EqualTo(new[] { ".agents/skills" }));

            var prefs = Prefs;
            FolderSelection.Decline(prefs, Agents);
            prefs.Save();
            Assert.That(Offers(), Is.Empty);
            Assert.That(Effective(), Is.EqualTo(new[] { ".claude/skills" }));
        }

        [Test]
        public void Offers_NoneChosenThenAgentsHomeAppears_OffersTheAgentsFolder()
        {
            Choose();
            MakeHome(".agents");

            Assert.That(Offers(), Is.EqualTo(new[] { ".agents/skills" }));
        }

        [Test]
        public void Accept_AddsTheOfferedFolderToTheSelection()
        {
            MakeHome(".claude");
            Choose(Claude);
            MakeHome(".agents");

            var prefs = Prefs;
            FolderSelection.Accept(prefs, Table, Agents, Environment);
            prefs.Save();

            Assert.That(Effective(), Is.EqualTo(new[] { ".agents/skills", ".claude/skills" }));
            Assert.That(Offers(), Is.Empty);
        }

        [Test]
        public void Save_LeavingOutAFolderWhoseHomeExists_CountsAsDecliningIt()
        {
            MakeHome(".claude");
            MakeHome(".agents");

            Choose(Claude);

            Assert.That(Prefs.DeclinedFolders, Is.EqualTo(new[] { ".agents/skills" }));
            Assert.That(Offers(), Is.Empty);
        }

        [Test]
        public void Save_SelectingADeclinedFolder_ClearsTheDecline()
        {
            MakeHome(".agents");
            Choose();
            Assert.That(Prefs.DeclinedFolders, Is.EqualTo(new[] { ".agents/skills" }));

            Choose(Agents);

            Assert.That(Prefs.DeclinedFolders, Is.Empty);
        }
    }
}
