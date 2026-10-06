using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>
    /// The environment scanner over a faked Claude Code plugin install: a temp home holding
    /// <c>.claude/plugins/installed_plugins.json</c>, the plugin cache and <c>.claude/settings.json</c>, plus a temp project.
    /// </summary>
    public class ClaudePluginSourceTests
    {
        string _root;
        string _home;
        string _project;
        Dictionary<string, string> _variables;
        readonly List<string> _records = new List<string>();
        readonly List<string> _marketplaces = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            _home = Path.Combine(_root, "home");
            _project = Path.Combine(_root, "project");
            Directory.CreateDirectory(_home);
            Directory.CreateDirectory(_project);
            _variables = new Dictionary<string, string>();
            _records.Clear();
            _marketplaces.Clear();
        }

        [TearDown]
        public void TearDown() => TempDirectory.Delete(_root);

        UserEnvironment Environment =>
            new UserEnvironment(_home, name => _variables.TryGetValue(name, out var value) ? value : null);

        static SkillsFolder Agents => FolderLayout.Default.Find(".agents/skills");

        static SkillsFolder Claude => FolderLayout.Default.Find(".claude/skills");

        string PluginsRoot => _variables.TryGetValue("CLAUDE_CODE_PLUGIN_CACHE_DIR", out var root) ? root
            : Path.Combine(_variables.TryGetValue("CLAUDE_CONFIG_DIR", out var config) ? config : Path.Combine(_home, ".claude"), "plugins");

        static string Json(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

        static void MakeSkill(string folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "SKILL.md"), "---\nname: " + Path.GetFileName(folder) + "\n---\n");
        }

        /// <summary>
        /// Creates <c>cache/&lt;marketplace&gt;/&lt;plugin&gt;/&lt;version&gt;</c> with <c>skills/&lt;skill&gt;/SKILL.md</c> per skill and
        /// an optional manifest; returns the install path. Does not record the install.
        /// </summary>
        string MakeCachedPlugin(string id, string version, string[] skills, string manifest = null)
        {
            var parts = id.Split('@');
            var path = Path.Combine(PluginsRoot, "cache", parts[1], parts[0], version);
            foreach (var skill in skills) MakeSkill(Path.Combine(path, "skills", skill));
            Directory.CreateDirectory(path);
            if (manifest != null)
            {
                Directory.CreateDirectory(Path.Combine(path, ".claude-plugin"));
                File.WriteAllText(Path.Combine(path, ".claude-plugin", "plugin.json"), manifest);
            }
            return path;
        }

        /// <summary>Caches the plugin and records it in <c>installed_plugins.json</c>, as Claude Code's install does.</summary>
        string Install(string id, params string[] skills) =>
            InstallAt(id, "user", null, skills, "{ \"name\": \"" + id.Split('@')[0] + "\" }");

        string InstallAt(string id, string scope, string projectPath, string[] skills, string manifest, string version = "1.0.0")
        {
            var path = MakeCachedPlugin(id, version, skills, manifest);
            Record(id, scope, path, projectPath);
            return path;
        }

        void Record(string id, string scope, string installPath, string projectPath = null)
        {
            _records.Add($"\"{id}\": [ {{ \"scope\": \"{scope}\", " +
                         (projectPath == null ? "" : $"\"projectPath\": \"{Json(projectPath)}\", ") +
                         $"\"installPath\": \"{Json(installPath)}\", \"version\": \"1.0.0\" }} ]");
            Directory.CreateDirectory(PluginsRoot);
            File.WriteAllText(Path.Combine(PluginsRoot, "installed_plugins.json"),
                "{ \"version\": 2, \"plugins\": { " + string.Join(", ", _records) + " } }");
        }

        void UserSettings(string json)
        {
            var config = _variables.TryGetValue("CLAUDE_CONFIG_DIR", out var dir) ? dir : Path.Combine(_home, ".claude");
            Directory.CreateDirectory(config);
            File.WriteAllText(Path.Combine(config, "settings.json"), json);
        }

        void ProjectSettings(string fileName, string json)
        {
            Directory.CreateDirectory(Path.Combine(_project, ".claude"));
            File.WriteAllText(Path.Combine(_project, ".claude", fileName), json);
        }

        static string Enabled(string id, bool enabled) =>
            "{ \"enabledPlugins\": { \"" + id + "\": " + (enabled ? "true" : "false") + " } }";

        UserScopeState Scan() =>
            UserScopeScanner.Scan(new[] { Agents, Claude }, Environment, UserScopeScanner.DefaultSourcesFor(_project));

        static string[] FoundIn(UserScopeState state, string skill) =>
            state.CopiesOf(Claude, skill).Select(c => c.FoundIn).ToArray();

        [Test]
        public void Scan_EnabledPluginSkill_IsAClaudeDuplicateNamingThePlugin()
        {
            var path = Install("unity@unity-agent-plugin", "ui", "unity-cli");
            UserSettings(Enabled("unity@unity-agent-plugin", true));

            var state = Scan();

            var copy = state.CopiesOf(Claude, "ui").Single();
            Assert.That(copy.FoundIn, Is.EqualTo("plugin unity@unity-agent-plugin"));
            Assert.That(copy.Plugin, Is.EqualTo("unity@unity-agent-plugin"));
            Assert.That(copy.Path, Is.EqualTo(Path.GetFullPath(Path.Combine(path, "skills", "ui"))));
            Assert.That(copy.Agents, Is.EqualTo("Claude Code, as /unity:ui"));
            Assert.That(state.Has(Claude, "unity-cli"), Is.True);
            Assert.That(state.Has(Agents, "ui"), Is.False);
        }

        [Test]
        public void Scan_PluginNamespace_IsTheManifestName()
        {
            InstallAt("unity@unity-agent-plugin", "user", null, new[] { "ui" }, "{ \"name\": \"unity-official\" }");

            var copy = Scan().CopiesOf(Claude, "ui").Single();

            Assert.That(copy.Plugin, Is.EqualTo("unity@unity-agent-plugin"));
            Assert.That(copy.Agents, Is.EqualTo("Claude Code, as /unity-official:ui"));
        }

        [Test]
        public void Scan_PluginDisabledInUserSettings_IsNotReported()
        {
            Install("superpowers@claude-plugins-official", "tdd");
            UserSettings(Enabled("superpowers@claude-plugins-official", false));

            Assert.That(Scan().Copies, Is.Empty);
        }

        [Test]
        public void Scan_PluginNotInAnySettings_FollowsTheManifestDefaultEnabled()
        {
            Install("on@market", "tdd");
            InstallAt("off@market", "user", null, new[] { "grill" }, "{ \"name\": \"off\", \"defaultEnabled\": false }");

            var state = Scan();

            Assert.That(FoundIn(state, "tdd"), Is.EqualTo(new[] { "plugin on@market" }));
            Assert.That(state.Has(Claude, "grill"), Is.False);
        }

        /// <summary>
        /// Records the marketplace in <c>known_marketplaces.json</c> with the given source and install location, and
        /// writes its <c>marketplace.json</c> (plugin entries as JSON objects) at <paramref name="marketplaceFile"/>.
        /// </summary>
        void Marketplace(string name, string source, string installLocation, string marketplaceFile, params string[] entries)
        {
            _marketplaces.Add($"\"{name}\": {{ \"source\": {source}, \"installLocation\": \"{Json(installLocation)}\" }}");
            Directory.CreateDirectory(PluginsRoot);
            File.WriteAllText(Path.Combine(PluginsRoot, "known_marketplaces.json"), "{ " + string.Join(", ", _marketplaces) + " }");
            Directory.CreateDirectory(Path.GetDirectoryName(marketplaceFile));
            File.WriteAllText(marketplaceFile,
                $"{{ \"name\": \"{name}\", \"owner\": {{ \"name\": \"me\" }}, \"plugins\": [ {string.Join(", ", entries)} ] }}");
        }

        /// <summary>A GitHub marketplace cloned to <c>marketplaces/&lt;name&gt;</c>, its file at the default path.</summary>
        void GitHubMarketplace(string name, params string[] entries)
        {
            var clone = Path.Combine(PluginsRoot, "marketplaces", name);
            Marketplace(name, "{ \"source\": \"github\", \"repo\": \"owner/" + name + "\" }", clone,
                Path.Combine(clone, ".claude-plugin", "marketplace.json"), entries);
        }

        static string Entry(string name, string fields = "") =>
            "{ \"name\": \"" + name + "\", \"source\": \"./plugins/" + name + "\"" + (fields.Length > 0 ? ", " + fields : "") + " }";

        [Test]
        public void Scan_MarketplaceEntryDefaultEnabled_OverridesTheManifest()
        {
            Install("on@market", "alpha");
            InstallAt("off@market", "user", null, new[] { "beta" }, "{ \"name\": \"off\", \"defaultEnabled\": false }");
            Install("plain@market", "gamma");
            GitHubMarketplace("market",
                Entry("on", "\"defaultEnabled\": false"),
                Entry("off", "\"defaultEnabled\": true"),
                Entry("plain"));

            var state = Scan();

            Assert.That(state.Has(Claude, "alpha"), Is.False);
            Assert.That(state.Has(Claude, "beta"), Is.True);
            Assert.That(state.Has(Claude, "gamma"), Is.True);
        }

        [Test]
        public void Scan_MarketplaceEntryDefault_StillYieldsToSettings()
        {
            Install("on@market", "alpha");
            GitHubMarketplace("market", Entry("on", "\"defaultEnabled\": false"));
            UserSettings(Enabled("on@market", true));

            Assert.That(Scan().Has(Claude, "alpha"), Is.True);
        }

        [Test]
        public void Scan_MarketplaceFile_IsFoundForEachLocalSourceType()
        {
            Install("a@custom-path", "alpha");
            Install("b@folder", "beta");
            Install("c@file", "gamma");
            var clone = Path.Combine(PluginsRoot, "marketplaces", "custom-path");
            Marketplace("custom-path", "{ \"source\": \"git\", \"url\": \"https://example.com/m.git\", \"path\": \"meta/market.json\" }",
                clone, Path.Combine(clone, "meta", "market.json"), Entry("a", "\"defaultEnabled\": false"));
            var folder = Path.Combine(_root, "folder-market");
            Marketplace("folder", "{ \"source\": \"directory\", \"path\": \"" + Json(folder) + "\" }",
                folder, Path.Combine(folder, ".claude-plugin", "marketplace.json"), Entry("b", "\"defaultEnabled\": false"));
            var file = Path.Combine(_root, "file-market", ".claude-plugin", "marketplace.json");
            Marketplace("file", "{ \"source\": \"file\", \"path\": \"" + Json(file) + "\" }",
                file, file, Entry("c", "\"defaultEnabled\": false"));

            Assert.That(Scan().Copies, Is.Empty);
        }

        [Test]
        public void Scan_ProjectAndLocalSettings_OverrideTheUserSetting()
        {
            Install("a@market", "alpha");
            Install("b@market", "beta");
            UserSettings("{ \"enabledPlugins\": { \"a@market\": false, \"b@market\": true } }");
            ProjectSettings("settings.json", "{ \"enabledPlugins\": { \"a@market\": true, \"b@market\": true } }");
            ProjectSettings("settings.local.json", Enabled("b@market", false));

            var state = Scan();

            Assert.That(state.Has(Claude, "alpha"), Is.True);
            Assert.That(state.Has(Claude, "beta"), Is.False);
        }

        [Test]
        public void Scan_SettingsFileThatOmitsAPlugin_LeavesTheLowerSettingInEffect()
        {
            // Claude Code merges enabledPlugins id by id, not as one whole value (plugins/loading, "Find where a
            // plugin is enabled"): a higher file that doesn't mention an id keeps the lower file's value for it.
            Install("a@market", "alpha");
            Install("b@market", "beta");
            UserSettings(Enabled("a@market", false));
            ProjectSettings("settings.json", Enabled("b@market", true));
            ProjectSettings("settings.local.json", "{ \"enabledPlugins\": {} }");

            var state = Scan();

            Assert.That(state.Has(Claude, "alpha"), Is.False);
            Assert.That(state.Has(Claude, "beta"), Is.True);
        }

        [Test]
        public void Scan_WithoutAProject_ReadsUserSettingsOnly()
        {
            Install("a@market", "alpha");
            UserSettings(Enabled("a@market", false));
            ProjectSettings("settings.json", Enabled("a@market", true));

            var state = UserScopeScanner.Scan(new[] { Claude }, Environment);

            Assert.That(state.Copies, Is.Empty);
        }

        [Test]
        public void Scan_ProjectOrLocalScopeInstall_CountsOnlyInItsOwnProject()
        {
            InstallAt("here@market", "project", _project, new[] { "alpha" }, "{ \"name\": \"here\" }");
            InstallAt("there@market", "local", Path.Combine(_root, "other-project"), new[] { "beta" }, "{ \"name\": \"there\" }");

            var state = Scan();

            Assert.That(state.Has(Claude, "alpha"), Is.True);
            Assert.That(state.Has(Claude, "beta"), Is.False);
        }

        [Test]
        public void Scan_ManifestSkillsPaths_AddToTheDefaultSkillsFolder()
        {
            var path = InstallAt("kit@market", "user", null, new[] { "alpha" },
                "{ \"name\": \"kit\", \"skills\": [\"./extra\", \"./single\"] }");
            MakeSkill(Path.Combine(path, "extra", "beta"));
            MakeSkill(Path.Combine(path, "single"));
            MakeSkill(Path.Combine(path, "unlisted", "gamma"));

            var state = Scan();

            Assert.That(state.Copies.Select(c => c.SkillName), Is.EquivalentTo(new[] { "alpha", "beta", "single" }));
        }

        [Test]
        public void Scan_ManifestSkillsPathAsAString_IsScannedToo()
        {
            var path = InstallAt("kit@market", "user", null, new string[0], "{ \"name\": \"kit\", \"skills\": \"./extra/\" }");
            MakeSkill(Path.Combine(path, "extra", "beta"));

            Assert.That(Scan().Has(Claude, "beta"), Is.True);
        }

        [Test]
        public void Scan_OnlyTheRecordedVersionIsRead()
        {
            MakeCachedPlugin("superpowers@market", "6.3.0", new[] { "old-skill" });
            InstallAt("superpowers@market", "user", null, new[] { "tdd" }, "{ \"name\": \"superpowers\" }", "6.4.1");

            var state = Scan();

            Assert.That(state.Copies.Select(c => c.SkillName), Is.EqualTo(new[] { "tdd" }));
        }

        [Test]
        public void Scan_ClaudeConfigDir_MovesThePluginsRootAndUserSettings()
        {
            _variables["CLAUDE_CONFIG_DIR"] = Path.Combine(_root, "claude-config");
            Install("a@market", "alpha");
            Install("b@market", "beta");
            UserSettings(Enabled("b@market", false));

            var state = Scan();

            Assert.That(state.Has(Claude, "alpha"), Is.True);
            Assert.That(state.Has(Claude, "beta"), Is.False);
        }

        [Test]
        public void Scan_PluginCacheDirVariable_MovesThePluginsRoot()
        {
            _variables["CLAUDE_CODE_PLUGIN_CACHE_DIR"] = Path.Combine(_root, "plugins-root");
            Install("a@market", "alpha");

            Assert.That(Scan().Has(Claude, "alpha"), Is.True);
        }

        [Test]
        public void Scan_NoOrBrokenInstallRecord_FindsNothing()
        {
            Assert.That(Scan().Copies, Is.Empty);

            Install("a@market", "alpha");
            File.WriteAllText(Path.Combine(PluginsRoot, "installed_plugins.json"), "{ not json");
            UserSettings("{ also not json");

            Assert.That(Scan().Copies, Is.Empty);
        }

        [Test]
        public void Scan_ManifestPresentButInvalid_SkipsThePlugin()
        {
            InstallAt("broken@market", "user", null, new[] { "alpha" }, "{ not json");
            InstallAt("array@market", "user", null, new[] { "beta" }, "[]");
            InstallAt("bare@market", "user", null, new[] { "gamma" }, manifest: null);

            var state = Scan();

            Assert.That(state.Copies.Select(c => c.SkillName), Is.EqualTo(new[] { "gamma" }));
        }

        [Test]
        public void Scan_InstallPathGone_IsSkipped()
        {
            Record("gone@market", "user", Path.Combine(_root, "nowhere"));
            Install("a@market", "alpha");

            Assert.That(Scan().Copies.Select(c => c.SkillName), Is.EqualTo(new[] { "alpha" }));
        }

        [Test]
        public void Scan_PluginAndClaudeHomeCopies_AreBothReported()
        {
            MakeSkill(Path.Combine(_home, ".claude", "skills", "tdd"));
            Install("superpowers@market", "tdd");

            Assert.That(FoundIn(Scan(), "tdd"), Is.EqualTo(new[] { "~/.claude/skills", "plugin superpowers@market" }));
        }

        [Test]
        public void Plan_SkipStoredForAPluginProvidedSkill_SkipsOnlyTheClaudeLink()
        {
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), @"{
  ""version"": 1,
  ""skills"": {
    ""tdd"": { ""source"": ""owner/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/tdd/SKILL.md"", ""computedHash"": """ + FakeGitHub.MinimalHash + @""" }
  }
}");
            Install("superpowers@market", "tdd");
            var prefs = LocalPrefs.Load(_project);
            SkipChoices.Set(prefs, Claude, "tdd", true);
            var both = new[] { Agents, Claude };

            var plan = new SkillSync(_project, fetcher: null, selected: both, userScope: Scan(), skips: SkipChoices.From(prefs)).Plan();

            var skip = plan.Actions.Single(a => a.Kind == PlanActionKind.SkipUserScope);
            Assert.That(skip.Folder, Is.SameAs(Claude));
            Assert.That(skip.UserScopeCopies.Select(c => c.Plugin), Is.EqualTo(new[] { "superpowers@market" }));
            Assert.That(plan.Actions.Where(a => a.Folder == Claude).Select(a => a.Kind), Is.EqualTo(new[] { PlanActionKind.SkipUserScope }));
            Assert.That(plan.Actions.Any(a => a.Kind == PlanActionKind.Install && a.Folder == Agents), Is.True);
        }

        [Test]
        public void DefaultSources_IncludeTheClaudePlugins()
        {
            Install("a@market", "alpha");

            Assert.That(UserScopeScanner.Scan(new[] { Claude }, Environment).Has(Claude, "alpha"), Is.True);
        }
    }
}
