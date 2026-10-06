using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>Syncing in a temp project while the folder selection changes.</summary>
    public class SelectionSyncTests
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

        string _root;
        string _project;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            _project = Path.Combine(_root, "project");
            Directory.CreateDirectory(_project);
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), Lock);
        }

        [TearDown]
        public void TearDown() => TempDirectory.Delete(_root);

        SyncSummary Sync(params string[] selected) =>
            new SkillSync(_project, new FixtureFetcher(_root),
                selected: selected.Select(FolderLayout.Default.Find).ToList()).Run();

        string InProject(string relative) => Path.Combine(_project, relative.Replace('/', Path.DirectorySeparatorChar));

        bool Exists(string relative) => Directory.Exists(InProject(relative));

        void MakeFolder(string relative) => Directory.CreateDirectory(InProject(relative));

        string[] ManagedIn(string folder) => ManagedStateFile.Read(InProject(folder)).ToArray();

        [Test]
        public void Run_NoneSelected_InstallsNothing()
        {
            var summary = Sync();

            Assert.That(summary.NothingChanged, Is.True);
            Assert.That(Exists(Agents), Is.False);
            Assert.That(Exists(Claude), Is.False);
        }

        [Test]
        public void Run_ClaudeDeselected_RemovesOnlyTheManagedClaudeLink()
        {
            Sync(Agents, Claude);
            MakeFolder(Claude + "/mine");
            File.AppendAllText(InProject(Claude + "/.gitignore"), "\n/local-notes\n");

            var summary = Sync(Agents);

            Assert.That(summary.Unlinked, Is.EqualTo(new[] { "tdd" }));
            Assert.That(Exists(Claude + "/tdd"), Is.False);
            Assert.That(Exists(Claude + "/mine"), Is.True);
            Assert.That(File.ReadAllText(InProject(Claude + "/.gitignore")), Does.Contain("/local-notes"));
            Assert.That(ManagedIn(Claude), Is.Empty);
            Assert.That(Exists(Agents + "/tdd"), Is.True);
            Assert.That(ManagedIn(Agents), Is.EqualTo(new[] { "tdd" }));
        }

        [Test]
        public void Run_AgentsDeselectedWhileClaudeSelected_KeepsTheCanonicalCopyAndLink()
        {
            Sync(Agents, Claude);

            var summary = Sync(Claude);

            Assert.That(summary.NothingChanged, Is.True);
            Assert.That(Exists(Agents + "/tdd"), Is.True);
            Assert.That(File.Exists(InProject(Claude + "/tdd/SKILL.md")), Is.True);
        }

        [Test]
        public void Run_EverythingDeselected_RemovesManagedEntriesButNotProjectAuthoredOrForeignOnes()
        {
            Sync(Agents, Claude);
            MakeFolder(Agents + "/house-style");
            MakeFolder(Claude + "/mine");
            Sync(Agents, Claude);

            var summary = Sync();

            Assert.That(summary.Removed, Is.EqualTo(new[] { "tdd" }));
            Assert.That(Exists(Agents + "/tdd"), Is.False);
            Assert.That(Exists(Claude + "/tdd"), Is.False);
            Assert.That(Exists(Claude + "/house-style"), Is.False);
            Assert.That(Exists(Agents + "/house-style"), Is.True);
            Assert.That(Exists(Claude + "/mine"), Is.True);
            Assert.That(ManagedIn(Agents), Is.Empty);
            Assert.That(ManagedIn(Claude), Is.Empty);
        }

        [Test]
        public void Run_ClaudeReselected_LinksItAgain()
        {
            Sync(Agents, Claude);
            Sync(Agents);

            var summary = Sync(Agents, Claude);

            Assert.That(summary.Linked, Is.EqualTo(new[] { "tdd" }));
            Assert.That(File.Exists(InProject(Claude + "/tdd/SKILL.md")), Is.True);
            Assert.That(ManagedIn(Claude), Is.EqualTo(new[] { "tdd" }));
        }

        [Test]
        public void Run_ReselectedAfterNone_InstallsAgain()
        {
            Sync(Agents, Claude);
            Sync();

            var summary = Sync(Claude);

            Assert.That(summary.Installed, Is.EqualTo(new[] { "tdd" }));
            Assert.That(File.Exists(InProject(Claude + "/tdd/SKILL.md")), Is.True);
        }
    }
}
