using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>Syncing a temp project against a faked user home while install-anyway choices are toggled.</summary>
    public class UserScopeSkipSyncTests
    {
        sealed class FixtureFetcher : ISkillFetcher
        {
            static readonly string Minimal =
                Path.GetFullPath("Packages/com.hissal.agent-skills-sync/Tests/Editor/Fixtures~/SkillFolderHash/minimal");

            readonly string _root;

            public FixtureFetcher(string root) => _root = root;

            public string Fetch(LockedSkill skill)
            {
                var folder = Path.Combine(_root, "fetched", skill.Name, Guid.NewGuid().ToString("N"));
                Paths.CopyDirectory(Minimal, folder);
                return folder;
            }
        }

        const string Lock = @"{
  ""version"": 1,
  ""skills"": {
    ""tdd"": { ""source"": ""owner/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/tdd/SKILL.md"", ""computedHash"": """ + FakeGitHub.MinimalHash + @""" }
  }
}";

        const string Agents = ".agents/skills";
        const string Claude = ".claude/skills";

        static FolderLayout Layout => FolderLayout.Default;

        string _root;
        string _project;
        string _home;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            _project = Path.Combine(_root, "project");
            _home = Path.Combine(_root, "home");
            Directory.CreateDirectory(_project);
            Directory.CreateDirectory(_home);
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), Lock);
        }

        [TearDown]
        public void TearDown() => TempDirectory.Delete(_root);

        UserEnvironment Environment => new UserEnvironment(_home, _ => null);

        static IReadOnlyList<SkillsFolder> Both => new[] { Layout.Find(Agents), Layout.Find(Claude) };

        void MakeUserScopeSkill(string skillsFolder, string skill)
        {
            var folder = Path.Combine(_home, skillsFolder.Replace('/', Path.DirectorySeparatorChar), skill);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "SKILL.md"), "mine");
        }

        void SetInstallAnyway(string folder, bool installAnyway)
        {
            var prefs = LocalPrefs.Load(_project);
            InstallAnywayChoices.Set(prefs, Layout.Find(folder), "tdd", installAnyway);
            prefs.Save();
        }

        SyncSummary Sync() =>
            new SkillSync(_project, new FixtureFetcher(_root), selected: Both,
                userScope: UserScopeScanner.Scan(Both, Environment),
                installAnyway: InstallAnywayChoices.From(LocalPrefs.Load(_project))).Run();

        SyncStatus Status() =>
            SyncStatus.Read(_project, Layout, Both, UserScopeScanner.Scan(Both, Environment), InstallAnywayChoices.From(LocalPrefs.Load(_project)));

        string InProject(string relative) => Path.Combine(_project, relative.Replace('/', Path.DirectorySeparatorChar));

        bool Exists(string relative) => Directory.Exists(InProject(relative));

        /// <summary>What this machine manages in the folder, from its local prefs.</summary>
        string[] ManagedIn(string folder) =>
            LocalPrefs.Load(_project).ManagedSkills is var managed && managed != null && managed.TryGetValue(folder, out var names)
                ? names.ToArray()
                : new string[0];

        /// <summary>The names the folder's committed .gitignore block lists; installAnyway never change it.</summary>
        string[] IgnoredIn(string folder) => ManagedStateFile.Read(InProject(folder)).ToArray();

        [Test]
        public void Run_ClaudeOverrideRemovedThenAdded_UnlinksThenLinksAgain()
        {
            MakeUserScopeSkill(".claude/skills", "tdd");
            SetInstallAnyway(Claude, true);
            Sync();
            Directory.CreateDirectory(InProject(Claude + "/mine"));

            SetInstallAnyway(Claude, false);
            var skipped = Sync();

            Assert.That(skipped.Unlinked, Is.EqualTo(new[] { "tdd" }));
            Assert.That(skipped.SkippedForUserScope, Is.EqualTo(new[] { "tdd" }));
            Assert.That(Exists(Claude + "/tdd"), Is.False);
            Assert.That(Exists(Claude + "/mine"), Is.True);
            Assert.That(ManagedIn(Claude), Is.Empty);
            Assert.That(IgnoredIn(Claude), Is.EqualTo(new[] { "tdd" }));
            Assert.That(Exists(Agents + "/tdd"), Is.True);
            Assert.That(Exists(Path.Combine(_home, ".claude", "skills", "tdd")), Is.True);
            Assert.That(Status().OutOfSyncSkills, Is.Empty);

            SetInstallAnyway(Claude, true);
            var unskipped = Sync();

            Assert.That(unskipped.Linked, Is.EqualTo(new[] { "tdd" }));
            Assert.That(File.Exists(InProject(Claude + "/tdd/SKILL.md")), Is.True);
            Assert.That(ManagedIn(Claude), Is.EqualTo(new[] { "tdd" }));
            Assert.That(Exists(Claude + "/mine"), Is.True);
        }

        [Test]
        public void Run_BothOverridesRemovedThenAgentsOverrideAdded_WithdrawsThenInstallsAgentsOnly()
        {
            MakeUserScopeSkill(".claude/skills", "tdd");
            MakeUserScopeSkill(".codex/skills", "tdd");
            SetInstallAnyway(Claude, true);
            SetInstallAnyway(Agents, true);
            Sync();

            SetInstallAnyway(Claude, false);
            SetInstallAnyway(Agents, false);
            var skipped = Sync();

            Assert.That(skipped.Removed, Is.EqualTo(new[] { "tdd" }));
            Assert.That(Exists(Agents + "/tdd"), Is.False);
            Assert.That(Exists(Claude + "/tdd"), Is.False);
            Assert.That(ManagedIn(Agents), Is.Empty);
            Assert.That(IgnoredIn(Agents), Is.EqualTo(new[] { "tdd" }));
            Assert.That(Exists(Path.Combine(_home, ".codex", "skills", "tdd")), Is.True);

            SetInstallAnyway(Agents, true);
            var unskipped = Sync();

            Assert.That(unskipped.Installed, Is.EqualTo(new[] { "tdd" }));
            Assert.That(Exists(Agents + "/tdd"), Is.True);
            Assert.That(Exists(Claude + "/tdd"), Is.False);
        }

        [Test]
        public void Run_UpgradeWithdrawsManagedCopiesByDefault_LeavesForeignEntriesAndIgnoreBlocks()
        {
            Sync();
            var agentsIgnore = File.ReadAllText(InProject(Agents + "/.gitignore"));
            var claudeIgnore = File.ReadAllText(InProject(Claude + "/.gitignore"));
            Directory.CreateDirectory(InProject(Claude + "/foreign"));
            File.WriteAllText(InProject(Claude + "/foreign/SKILL.md"), "hand-made");
            MakeUserScopeSkill(".claude/skills", "tdd");
            MakeUserScopeSkill(".codex/skills", "tdd");

            Assert.That(Status().OutOfSyncSkills, Is.EqualTo(new[] { "tdd" }));
            var summary = Sync();

            Assert.That(summary.Unlinked, Is.EqualTo(new[] { "tdd" }));
            Assert.That(summary.Removed, Is.EqualTo(new[] { "tdd" }));
            Assert.That(Exists(Claude + "/tdd"), Is.False);
            Assert.That(Exists(Agents + "/tdd"), Is.False);
            Assert.That(File.ReadAllText(InProject(Claude + "/foreign/SKILL.md")), Is.EqualTo("hand-made"));
            Assert.That(File.ReadAllText(InProject(Agents + "/.gitignore")), Is.EqualTo(agentsIgnore));
            Assert.That(File.ReadAllText(InProject(Claude + "/.gitignore")), Is.EqualTo(claudeIgnore));
            Assert.That(Status().OutOfSyncSkills, Is.Empty);
        }

        [Test]
        public void Run_SkipOverAForeignEntry_LeavesItUntouched()
        {
            MakeUserScopeSkill(".claude/skills", "tdd");
            Directory.CreateDirectory(InProject(Claude + "/tdd"));
            File.WriteAllText(InProject(Claude + "/tdd/SKILL.md"), "hand-made");

            Sync();

            Assert.That(File.ReadAllText(InProject(Claude + "/tdd/SKILL.md")), Is.EqualTo("hand-made"));
            Assert.That(ManagedIn(Claude), Is.Empty);
        }

        [Test]
        public void Status_SkippedButUserScopeCopyGone_ReportsTheSkillMissing()
        {
            MakeUserScopeSkill(".claude/skills", "tdd");
            Sync();
            Directory.Delete(Path.Combine(_home, ".claude", "skills", "tdd"), recursive: true);

            Assert.That(Status().OutOfSyncSkills, Is.EqualTo(new[] { "tdd" }));
        }
    }
}
