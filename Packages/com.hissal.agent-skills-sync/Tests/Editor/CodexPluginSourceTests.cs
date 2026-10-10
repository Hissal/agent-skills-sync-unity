using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class CodexPluginSourceTests
    {
        string _root;
        string _codex;
        static SkillsFolder Agents => FolderLayout.Default.Find(".agents/skills");

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            _codex = Path.Combine(_root, "custom-codex");
            Directory.CreateDirectory(_codex);
        }

        [TearDown]
        public void TearDown() => TempDirectory.Delete(_root);

        [Test]
        public void Scan_EnabledCodexPlugin_ReportsOnlyTheAgentsCopy()
        {
            var skill = Path.Combine(_codex, "plugins", "cache", "market", "unity", "1.0.0", "skills", "ui");
            Directory.CreateDirectory(skill);
            File.WriteAllText(Path.Combine(skill, "SKILL.md"), "plugin skill");
            var manifest = Path.Combine(_codex, "plugins", "cache", "market", "unity", "1.0.0", ".codex-plugin");
            Directory.CreateDirectory(manifest);
            File.WriteAllText(Path.Combine(manifest, "plugin.json"), "{\"name\":\"unity\",\"version\":\"1.0.0\",\"skills\":\"./skills\"}");
            File.WriteAllText(Path.Combine(_codex, "config.toml"), "[plugins.\"unity@market\"]\nenabled = true\n");
            var environment = new UserEnvironment(Path.Combine(_root, "home"), name => name == "CODEX_HOME" ? _codex : null);

            var copies = UserScopeScanner.Scan(FolderLayout.Default.Folders, environment).Copies;

            Assert.That(copies.Count, Is.EqualTo(1));
            Assert.That(copies.Single().Folder, Is.SameAs(Agents));
            Assert.That(copies.Single().SkillName, Is.EqualTo("ui"));
            Assert.That(copies.Single().Plugin, Is.EqualTo("unity@market"));
            Assert.That(copies.Single().Agents, Is.EqualTo("Codex"));
            Assert.That(Hissal.AgentSkillsSync.Editor.SyncText.Where(copies.Single()),
                Is.EqualTo("provided by Codex plugin unity@market"));
        }

        UserEnvironment Environment => new UserEnvironment(Path.Combine(_root, "home"), name => name == "CODEX_HOME" ? _codex : null);
        UserScopeState Scan(string project = null) => UserScopeScanner.Scan(FolderLayout.Default.Folders, Environment,
            project == null ? null : UserScopeScanner.DefaultSourcesFor(project));
        void Config(string text) => File.WriteAllText(Path.Combine(_codex, "config.toml"), text);

        string Install(string version = "1.0.0", string name = "ui", bool manifest = true)
        {
            var root = Path.Combine(_codex, "plugins", "cache", "market", "unity", version);
            var skill = Path.Combine(root, "skills", name);
            Directory.CreateDirectory(skill);
            File.WriteAllText(Path.Combine(skill, "SKILL.md"), "plugin skill");
            if (manifest)
            {
                Directory.CreateDirectory(Path.Combine(root, ".codex-plugin"));
                File.WriteAllText(Path.Combine(root, ".codex-plugin", "plugin.json"),
                    "{\"name\":\"unity\",\"version\":\"" + version + "\",\"skills\":\"./skills\"}");
            }
            return skill;
        }

        [TestCase("[plugins.\"unity@market\"]\nenabled = false", 0)]
        [TestCase("[plugins.\"unity@market\"]", 1)]
        [TestCase("model = \"test\"", 0)]
        [TestCase("[plugins.\"other@market\"]\nenabled = true", 0)]
        public void Scan_ConfiguredPluginDefaultsToEnabled_AbsentPluginDoesNotLoad(string config, int count)
        {
            Install();
            Config(config);
            Assert.That(Scan().Copies.Count, Is.EqualTo(count));
        }

        [TestCase("1.9.0", "1.10.0", "1.10.0")]
        [TestCase("1.0.0-beta.2", "1.0.0-beta.10", "1.0.0-beta.10")]
        [TestCase("1.0.0-beta", "1.0.0", "1.0.0")]
        [TestCase("2.0.0", "local", "local")]
        [TestCase("abc123", "def456", "def456")]
        [TestCase("1.0.0+build.2", "1.0.0+build.10", "1.0.0+build.10")]
        public void Scan_SeveralCachedVersions_OnlyTheCodexSelectedVersionCounts(string first, string second, string active)
        {
            Install(first, "old");
            Install(second, "new");
            Config("[plugins.\"unity@market\"]\nenabled = true");
            var copies = Scan().Copies;
            Assert.That(copies.Count, Is.EqualTo(1));
            Assert.That(copies.Single().SkillName, Is.EqualTo(active == first ? "old" : "new"));
        }

        [TestCase("0.9.0", true)]
        [TestCase("9.0.0", false)]
        [TestCase("local", false)]
        public void Scan_EmptyLeftoverVersion_DoesNotCountOrCauseFallback(string emptyVersion, bool detected)
        {
            Install();
            Directory.CreateDirectory(Path.Combine(_codex, "plugins", "cache", "market", "unity", emptyVersion));
            Config("[plugins.\"unity@market\"]");
            Assert.That(Scan().Has(Agents, "ui"), Is.EqualTo(detected));
        }

        [TestCase("[plugins.\"unity@market\"]\nenabled = \"true\"")]
        [TestCase("[plugins.\"unity@market\"\nenabled = true")]
        [TestCase("[plugins.\"unity@market\"]\nenabled = true\nenabled = false")]
        [TestCase("[plugins.\"unity@market\"]\nenabled = true\nbroken")]
        [TestCase("[plugins.\"unity@market\"]\nenabled = true\nother = [")]
        [TestCase("[plugins.\"unity@market\"]\nenabled = true\nother = \"unterminated")]
        public void Scan_MalformedConfig_FindsNothing(string config)
        {
            Install();
            Config(config);
            Assert.That(Scan().Copies, Is.Empty);
        }

        [Test]
        public void Scan_UnreadableConfig_FindsNothing()
        {
            Install();
            Config("[plugins.\"unity@market\"]");
            using (File.Open(Path.Combine(_codex, "config.toml"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.That(Scan().Copies, Is.Empty);
        }

        [Test]
        public void Scan_ConfiguredButUninstalledPlugin_FindsNothing()
        {
            Config("[plugins.\"unity@market\"]\nenabled = true");
            Assert.That(Scan().Copies, Is.Empty);
        }

        [TestCase(null)]
        [TestCase("not json")]
        public void Scan_MissingOrMalformedManifest_FindsNothing(string manifest)
        {
            var skill = Install(manifest: false);
            if (manifest != null)
            {
                var directory = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(skill)), ".codex-plugin");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "plugin.json"), manifest);
            }
            Config("[plugins.\"unity@market\"]");
            Assert.That(Scan().Copies, Is.Empty);
        }

        [Test]
        public void Scan_MalformedProjectConfig_FindsNothing()
        {
            Install();
            Config("[plugins.\"unity@market\"]");
            var project = Path.Combine(_root, "project");
            Directory.CreateDirectory(Path.Combine(project, ".codex"));
            File.WriteAllText(Path.Combine(project, ".codex", "config.toml"), "[plugins.\"unity@market\"]\nenabled = nope");
            Assert.That(Scan(project).Copies, Is.Empty);
        }

        [Test]
        public void Scan_ProjectTableWithoutEnabled_PreservesTheUserDisable()
        {
            Install();
            Config("[plugins.\"unity@market\"]\nenabled = false");
            var project = Path.Combine(_root, "project");
            Directory.CreateDirectory(Path.Combine(project, ".codex"));
            File.WriteAllText(Path.Combine(project, ".codex", "config.toml"), "[plugins.\"unity@market\"]");
            Assert.That(Scan(project).Copies, Is.Empty);
        }

        [Test]
        public void Scan_UnrelatedMultilineValues_DoNotActAsPluginTables()
        {
            Install();
            Config("description = '''\n[plugins.\"unity@market\"]\nenabled = true\n'''\n" +
                   "tools = [\n { name = \"test\", enabled = true },\n]\n");
            Assert.That(Scan().Copies, Is.Empty);
            Config("options = { list = [1, 2], text = \"# comment\" }\n[plugins.'unity@market'] # comment\nenabled = true # comment");
            Assert.That(Scan().Has(Agents, "ui"), Is.True);
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void Scan_ProjectConfig_OverridesUserEnabledSetting(bool userEnabled, bool projectEnabled)
        {
            Install();
            Config("[plugins.\"unity@market\"]\nenabled = " + userEnabled.ToString().ToLowerInvariant());
            var project = Path.Combine(_root, "project");
            Directory.CreateDirectory(Path.Combine(project, ".codex"));
            File.WriteAllText(Path.Combine(project, ".codex", "config.toml"),
                "[plugins.\"unity@market\"]\nenabled = " + projectEnabled.ToString().ToLowerInvariant());
            Assert.That(Scan(project).Has(Agents, "ui"), Is.EqualTo(projectEnabled));
        }

        [Test]
        public void Scan_DefaultCodexHome_IsUnderTheFakeHome()
        {
            _codex = Path.Combine(_root, "home", ".codex");
            Directory.CreateDirectory(_codex);
            Install();
            Config("[plugins.\"unity@market\"]");
            Assert.That(UserScopeScanner.Scan(new[] { Agents }, new UserEnvironment(Path.Combine(_root, "home"), _ => null))
                .Has(Agents, "ui"), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Plan_CodexPluginCopy_SkipsAndWarnsEvenWhenCanonicalCopyIsNeeded(bool claudeSelected)
        {
            Install(name: "tdd");
            Config("[plugins.\"unity@market\"]");
            var locked = new Lockfile(new[] { new LockedSkill("tdd", "owner/repo", "github", "skills/tdd/SKILL.md", FakeGitHub.MinimalHash) });
            var selected = claudeSelected ? FolderLayout.Default.Folders : new[] { Agents };
            var plan = InstallPlanner.Plan(locked, ProjectState.Empty, FolderLayout.Default, selected: selected, userScope: Scan());
            Assert.That(plan.Actions.Any(a => a.Kind == PlanActionKind.SkipUserScope && a.Folder == Agents), Is.True);
            var warning = plan.Actions.Single(a => a.Kind == PlanActionKind.WarnUserScopeDiffers);
            Assert.That(warning.UserScopeCopies.Single().Plugin, Is.EqualTo("unity@market"));
            Assert.That(Hissal.AgentSkillsSync.Editor.SyncText.DiffersMessage(warning), Does.Contain("Codex plugin unity@market"));
            Assert.That(plan.ManagedNames[Agents].Contains("tdd"), Is.EqualTo(claudeSelected));
        }
    }
}
