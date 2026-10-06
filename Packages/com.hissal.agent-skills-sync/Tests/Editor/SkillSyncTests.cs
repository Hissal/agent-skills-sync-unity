using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class SkillSyncTests
    {
        /// <summary>Fakes the network: serves each skill as a copy of the hash fixture its locked hash names; listed names fail.</summary>
        sealed class FakeFetcher : ISkillFetcher
        {
            static readonly string FixturesRoot =
                Path.GetFullPath("Packages/com.hissal.agent-skills-sync/Tests/Editor/Fixtures~/SkillFolderHash");

            readonly string _root;
            public readonly HashSet<string> Failing = new HashSet<string>();
            public readonly List<string> Fetched = new List<string>();

            public FakeFetcher(string root) => _root = root;

            public string Fetch(LockedSkill skill)
            {
                Fetched.Add(skill.Name);
                if (Failing.Contains(skill.Name)) throw new SkillFetchException($"Could not download {skill.Source}.");
                var fixture = skill.ComputedHash == FakeGitHub.NestedHash ? "nested" : "minimal";
                var folder = Path.Combine(_root, "fetched", skill.Name, Guid.NewGuid().ToString("N"));
                Paths.CopyDirectory(Path.Combine(FixturesRoot, fixture), folder);
                return folder;
            }
        }

        const string Lock = @"{
  ""version"": 1,
  ""skills"": {
    ""tdd"": { ""source"": ""owner/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/tdd/SKILL.md"", ""computedHash"": """ + FakeGitHub.MinimalHash + @""" },
    ""code-review"": { ""source"": ""owner/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/code-review/SKILL.md"", ""computedHash"": """ + FakeGitHub.MinimalHash + @""" }
  }
}";

        /// <summary>The same lock after a pull: tdd's hash changed, code-review dropped.</summary>
        const string PulledLock = @"{
  ""version"": 1,
  ""skills"": {
    ""tdd"": { ""source"": ""owner/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/tdd/SKILL.md"", ""computedHash"": """ + FakeGitHub.NestedHash + @""" }
  }
}";

        string _root;
        string _project;
        FakeFetcher _fetcher;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            _project = Path.Combine(_root, "project");
            Directory.CreateDirectory(_project);
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), Lock);
            _fetcher = new FakeFetcher(_root);
        }

        [TearDown]
        public void TearDown()
        {
            TempDirectory.Delete(_root);
        }

        [Test]
        public void Run_EmptyProject_InstallsAndLinksEveryLockedSkill()
        {
            var summary = new SkillSync(_project, _fetcher).Run();

            Assert.That(summary.Installed, Is.EqualTo(new[] { "tdd", "code-review" }));
            Assert.That(summary.Linked, Is.EqualTo(new[] { "tdd", "code-review" }));
            Assert.That(File.ReadAllText(Path.Combine(_project, ".claude/skills/code-review/SKILL.md")), Does.Contain("# minimal"));
        }

        [Test]
        public void Run_FetchFails_ChangesNothingInTheProject()
        {
            _fetcher.Failing.Add("code-review");

            Assert.Throws<SyncAbortedException>(() => new SkillSync(_project, _fetcher).Run());

            Assert.That(Directory.Exists(Path.Combine(_project, ".agents")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(_project, ".claude")), Is.False);
        }

        [Test]
        public void Run_SeveralFetchesFail_ReportsEveryFailedSkill()
        {
            _fetcher.Failing.Add("tdd");
            _fetcher.Failing.Add("code-review");

            var error = Assert.Throws<SyncAbortedException>(() => new SkillSync(_project, _fetcher).Run());

            Assert.That(error.Failures.Keys, Is.EquivalentTo(new[] { "tdd", "code-review" }));
        }

        // Verification end to end: the real fetcher over a faked GitHub.

        const string VerifiedLock = @"{
  ""version"": 1,
  ""skills"": {
    ""installed"": { ""source"": ""owner/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/installed/SKILL.md"", ""computedHash"": """ + FakeGitHub.MinimalHash + @""" },
    ""tdd"": { ""source"": ""owner/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/tdd/SKILL.md"", ""computedHash"": """ + FakeGitHub.MinimalHash + @""" },
    ""code-review"": { ""source"": ""other/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/code-review/SKILL.md"", ""computedHash"": """ + FakeGitHub.NestedHash + @""" }
  }
}";

        FakeGitHub VerifiedProject()
        {
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), VerifiedLock);
            var existing = Path.Combine(_project, ".agents/skills/installed");
            Directory.CreateDirectory(existing);
            File.WriteAllText(Path.Combine(existing, "SKILL.md"), "# installed earlier");
            return new FakeGitHub()
                .Fixture("owner/skills", "skills/tdd", "minimal")
                .Fixture("other/skills", "skills/code-review", "nested");
        }

        SkillSync VerifyingSync(FakeGitHub github) =>
            new SkillSync(_project, new GitHubSkillFetcher(Path.Combine(_root, "cache"), github));

        static IEnumerable<string> Tree(string folder) =>
            Directory.GetFileSystemEntries(folder, "*", SearchOption.AllDirectories).Select(p => p.Substring(folder.Length)).OrderBy(p => p);

        [Test]
        public void Run_SourcesMatchTheLock_Installs()
        {
            var summary = VerifyingSync(VerifiedProject()).Run();

            Assert.That(summary.Installed, Is.EquivalentTo(new[] { "tdd", "code-review" }));
        }

        [Test]
        public void Run_Offline_LeavesTheProjectUntouchedAndReportsADownloadFailurePerSkill()
        {
            var github = VerifiedProject();
            github.Offline = true;
            var before = Tree(_project).ToList();

            var error = Assert.Throws<SyncAbortedException>(() => VerifyingSync(github).Run());

            Assert.That(Tree(_project), Is.EqualTo(before));
            Assert.That(error.Failures.Keys, Is.EquivalentTo(new[] { "tdd", "code-review" }));
            Assert.That(error.Failures.Values.Select(f => f.Failure), Is.All.EqualTo(SkillFetchFailure.Download));
        }

        [Test]
        public void Run_SourceChangedSinceLocked_LeavesTheProjectUntouchedAndReportsAHashMismatch()
        {
            var github = VerifiedProject().File("other/skills", "skills/code-review/SKILL.md", "# changed upstream");
            var before = Tree(_project).ToList();

            var error = Assert.Throws<SyncAbortedException>(() => VerifyingSync(github).Run());

            Assert.That(Tree(_project), Is.EqualTo(before));
            Assert.That(error.Failures.Keys, Is.EqualTo(new[] { "code-review" }));
            Assert.That(error.Failures["code-review"].Failure, Is.EqualTo(SkillFetchFailure.HashMismatch));
            Assert.That(error.Message, Does.Contain("npx skills update"));
        }

        [Test]
        public void Run_Twice_SecondRunChangesNothing()
        {
            new SkillSync(_project, _fetcher).Run();

            var summary = new SkillSync(_project, _fetcher).Run();

            Assert.That(summary.NothingChanged, Is.True);
        }

        const string UserGitignore = "# my own rules\n*.tmp\n/my-own-skill\n";

        string WriteUserGitignore(string folder)
        {
            var path = Path.Combine(_project, folder, ManagedStateFile.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, UserGitignore);
            return path;
        }

        [TestCase(".agents/skills")]
        [TestCase(".claude/skills")]
        public void Run_ExistingUserGitignore_KeepsTheUserRulesAndAddsTheManagedNames(string folder)
        {
            var path = WriteUserGitignore(folder);

            new SkillSync(_project, _fetcher).Run();

            var text = File.ReadAllText(path);
            Assert.That(text, Does.StartWith(UserGitignore));
            Assert.That(text.Split('\n'), Does.Contain("/tdd").And.Contain("/code-review"));
        }

        [Test]
        public void Run_ExistingUserGitignore_SecondRunChangesNothing()
        {
            var path = WriteUserGitignore(".agents/skills");
            new SkillSync(_project, _fetcher).Run();
            var afterFirst = File.ReadAllText(path);

            var summary = new SkillSync(_project, _fetcher).Run();

            Assert.That(summary.NothingChanged, Is.True);
            Assert.That(File.ReadAllText(path), Is.EqualTo(afterFirst));
        }

        [Test]
        public void Scan_UserGitignoreRules_AreNotTreatedAsManaged()
        {
            WriteUserGitignore(".agents/skills");
            Directory.CreateDirectory(Path.Combine(_project, ".agents/skills/my-own-skill"));

            var state = ProjectScanner.Scan(_project, FolderLayout.Default).For(FolderLayout.Default.Canonical);

            Assert.That(state.Managed, Is.Empty);
        }

        [Test]
        public void Scan_AfterSyncIntoUserGitignore_ManagesOnlyTheSyncedSkills()
        {
            WriteUserGitignore(".agents/skills");
            new SkillSync(_project, _fetcher).Run();

            var state = ProjectScanner.Scan(_project, FolderLayout.Default).For(FolderLayout.Default.Canonical);

            Assert.That(state.Managed, Is.EquivalentTo(new[] { "tdd", "code-review" }));
        }

        [Test]
        public void Run_CheckedLockfile_LockChangedOnDiskAfterTheCheck_RunsTheCheckedLock()
        {
            var checkedLock = Lockfile.Load(_project);
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), @"{
  ""version"": 1,
  ""skills"": {
    ""unconfirmed"": { ""source"": ""stranger/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/unconfirmed/SKILL.md"", ""computedHash"": """ + FakeGitHub.MinimalHash + @""" }
  }
}");

            var summary = new SkillSync(_project, _fetcher).Run(checkedLock);

            Assert.That(summary.Installed, Is.EquivalentTo(new[] { "tdd", "code-review" }));
            Assert.That(_fetcher.Fetched, Has.No.Member("unconfirmed"));
            Assert.That(Directory.Exists(Path.Combine(_project, ".agents/skills/unconfirmed")), Is.False);
        }

        [Test]
        public void Run_AfterAPull_UpdatesAndRemoves()
        {
            new SkillSync(_project, _fetcher).Run();
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), PulledLock);

            var summary = new SkillSync(_project, _fetcher).Run();

            Assert.That(summary.Updated, Is.EqualTo(new[] { "tdd" }));
            Assert.That(summary.Removed, Is.EqualTo(new[] { "code-review" }));
            Assert.That(SkillFolderHash.Compute(Path.Combine(_project, ".agents/skills/tdd")), Is.EqualTo(FakeGitHub.NestedHash));
        }

        [Test]
        public void Run_UpdateFetchFails_ChangesNothingInTheProject()
        {
            new SkillSync(_project, _fetcher).Run();
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), PulledLock);
            _fetcher.Failing.Add("tdd");
            var before = Tree(_project).ToList();

            var error = Assert.Throws<SyncAbortedException>(() => new SkillSync(_project, _fetcher).Run());

            Assert.That(error.Failures.Keys, Is.EqualTo(new[] { "tdd" }));
            Assert.That(Tree(_project), Is.EqualTo(before));
        }

        [Test]
        public void Run_Twice_SecondRunFetchesNothing()
        {
            new SkillSync(_project, _fetcher).Run();
            _fetcher.Fetched.Clear();

            new SkillSync(_project, _fetcher).Run();

            Assert.That(_fetcher.Fetched, Is.Empty);
        }

        [Test]
        public void Run_EmptyLockAndStaleManagedNameMissingOnDisk_DropsTheNameFromTheManagedState()
        {
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), @"{ ""version"": 1, ""skills"": {} }");
            var canonical = Path.Combine(_project, ".agents/skills");
            ManagedStateFile.Write(canonical, new[] { "tdd" });
            var sync = new SkillSync(_project, _fetcher);
            Assert.That(sync.Plan().HasChanges, Is.True, "the window only offers Sync when the plan has changes");

            sync.Run();

            Assert.That(ManagedStateFile.Read(canonical), Is.Empty);
        }
    }
}
