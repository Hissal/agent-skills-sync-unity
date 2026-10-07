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

            /// <summary>Skill name to the fixture upstream serves now, overriding the one its locked hash names.</summary>
            public readonly Dictionary<string, string> Upstream = new Dictionary<string, string>();

            public FakeFetcher(string root) => _root = root;

            public string Fetch(LockedSkill skill)
            {
                Fetched.Add(skill.Name);
                if (Failing.Contains(skill.Name)) throw new SkillFetchException($"Could not download {skill.Source}.");
                if (!Upstream.TryGetValue(skill.Name, out var fixture))
                    fixture = skill.ComputedHash == FakeGitHub.NestedHash ? "nested" : "minimal";
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

        // Install modes.

        SkillSync Sync(InstallMode mode) => new SkillSync(_project, _fetcher, mode: mode);

        [Test]
        public void Run_Latest_UpstreamDiffersFromLock_InstallsUpstreamAndReportsIt()
        {
            _fetcher.Upstream["tdd"] = "nested";

            var summary = Sync(InstallMode.Latest).Run();

            Assert.That(summary.Installed, Is.EqualTo(new[] { "tdd", "code-review" }));
            Assert.That(summary.DiffersFromLock, Is.EqualTo(new[] { "tdd" }));
            Assert.That(SkillFolderHash.Compute(Path.Combine(_project, ".agents/skills/tdd")), Is.EqualTo(FakeGitHub.NestedHash));
        }

        [Test]
        public void StartupCheck_AfterLatestSyncOfUpstreamThatDiffersFromLock_StaysQuiet()
        {
            _fetcher.Upstream["tdd"] = "nested";
            Sync(InstallMode.Latest).Run();
            var prefs = LocalPrefs.Load(_project);
            StartupCheck.RecordSynced(prefs, SyncStatus.Read(_project));
            prefs.Save();

            var status = SyncStatus.Read(_project);

            Assert.That(status.OutOfSyncSkills, Is.Empty, "a Latest copy that differs from the lock is not out of sync");
            Assert.That(StartupCheck.ShouldNotify(status, LocalPrefs.Load(_project)), Is.False);
        }

        [Test]
        public void Run_Pinned_UpstreamDiffersFromLock_RefusesAndChangesNothing()
        {
            _fetcher.Upstream["tdd"] = "nested";

            var error = Assert.Throws<SyncAbortedException>(() => Sync(InstallMode.Pinned).Run());

            Assert.That(error.Failures.Keys, Is.EqualTo(new[] { "tdd" }));
            Assert.That(error.Failures["tdd"].Failure, Is.EqualTo(SkillFetchFailure.HashMismatch));
            Assert.That(error.Message, Does.Contain("npx skills update"));
            Assert.That(Directory.Exists(Path.Combine(_project, ".agents")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(_project, ".claude")), Is.False);
        }

        [Test]
        public void Run_Latest_NeverModifiesTheLockfile()
        {
            _fetcher.Upstream["tdd"] = "nested";
            var lockPath = Path.Combine(_project, Lockfile.FileName);
            var before = File.ReadAllBytes(lockPath);
            var written = File.GetLastWriteTimeUtc(lockPath);

            Sync(InstallMode.Latest).Run();
            Sync(InstallMode.Latest).Run();

            Assert.That(File.ReadAllBytes(lockPath), Is.EqualTo(before));
            Assert.That(File.GetLastWriteTimeUtc(lockPath), Is.EqualTo(written));
        }

        [Test]
        public void Run_Latest_TwiceWithUnchangedUpstream_SecondRunChangesNothing()
        {
            _fetcher.Upstream["tdd"] = "nested";
            Sync(InstallMode.Latest).Run();

            var summary = Sync(InstallMode.Latest).Run();

            Assert.That(summary.NothingChanged, Is.True);
            Assert.That(summary.DiffersFromLock, Is.EqualTo(new[] { "tdd" }));
        }

        [Test]
        public void Run_Latest_UpstreamMovedSinceTheLastSync_UpdatesToTheNewUpstream()
        {
            Sync(InstallMode.Latest).Run();
            _fetcher.Upstream["code-review"] = "nested";

            var summary = Sync(InstallMode.Latest).Run();

            Assert.That(summary.Updated, Is.EqualTo(new[] { "code-review" }));
            Assert.That(SkillFolderHash.Compute(Path.Combine(_project, ".agents/skills/code-review")), Is.EqualTo(FakeGitHub.NestedHash));
        }

        [Test]
        public void Run_Latest_FetchFails_ChangesNothingInTheProject()
        {
            Sync(InstallMode.Latest).Run();
            _fetcher.Upstream["tdd"] = "nested";
            _fetcher.Failing.Add("code-review");
            var before = Tree(_project).ToList();

            var error = Assert.Throws<SyncAbortedException>(() => Sync(InstallMode.Latest).Run());

            Assert.That(error.Failures.Keys, Is.EqualTo(new[] { "code-review" }));
            Assert.That(Tree(_project), Is.EqualTo(before));
        }

        [Test]
        public void Run_Latest_RealFetcherOverChangedSource_InstallsUpstream()
        {
            var github = VerifiedProject().File("other/skills", "skills/code-review/SKILL.md", "# changed upstream");
            var fetcher = new GitHubSkillFetcher(Path.Combine(_root, "cache"), github, InstallMode.Latest);

            var summary = new SkillSync(_project, fetcher, mode: InstallMode.Latest).Run();

            Assert.That(summary.Installed, Is.EquivalentTo(new[] { "tdd", "code-review" }));
            Assert.That(summary.DiffersFromLock, Does.Contain("code-review"));
        }

        [Test]
        public void InstalledDiffersFromLock_ListsManagedCopiesWhoseHashIsNotTheLocked()
        {
            _fetcher.Upstream["tdd"] = "nested";
            Sync(InstallMode.Latest).Run();

            Assert.That(Sync(InstallMode.Latest).InstalledDiffersFromLock(), Is.EqualTo(new[] { "tdd" }));
        }

        [Test]
        public void Plan_Latest_InstalledCopyDiffersFromLock_PlansNoUpdateWithoutFetching()
        {
            _fetcher.Upstream["tdd"] = "nested";
            Sync(InstallMode.Latest).Run();
            _fetcher.Fetched.Clear();

            var plan = Sync(InstallMode.Latest).Plan();

            Assert.That(plan.HasChanges, Is.False);
            Assert.That(_fetcher.Fetched, Is.Empty);
        }
    

        // Latest installs current upstream, so Pinned's lock-verification refusals and CRLF rewrite do not apply.

        /// <summary>The lock with one skill, served by the real fetcher over a faked GitHub repo.</summary>
        (SkillSync Sync, FakeGitHub GitHub) OneSkill(InstallMode mode, string name, string source, string hash)
        {
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), @"{
  ""version"": 1,
  ""skills"": {
    """ + name + @""": { ""source"": """ + source + @""", ""sourceType"": ""github"", ""skillPath"": ""skills/" + name + @"/SKILL.md"", ""computedHash"": """ + hash + @""" }
  }
}");
            var github = new FakeGitHub();
            return (new SkillSync(_project, new GitHubSkillFetcher(Path.Combine(_root, "cache"), github, mode), mode: mode), github);
        }

        const string ByteExactCrlfCheckoutHash = "1ef11b466bb0ad8cb6eec71a1a735f1851045762121f50f124f21d66c7ceee8f";

        [Test]
        public void Run_Latest_SkillsShHashedSource_InstallsWithoutReportingItAsDiffering()
        {
            var (sync, github) = OneSkill(InstallMode.Latest, "tdd", "vercel-labs/agent-skills", "server-hash-of-another-algorithm");
            github.Fixture("vercel-labs/agent-skills", "skills/tdd", "minimal");

            var summary = sync.Run();

            Assert.That(summary.Installed, Is.EqualTo(new[] { "tdd" }));
            Assert.That(summary.DiffersFromLock, Is.Empty, "a skills.sh hash can't show whether upstream differs");
            Assert.That(SkillFolderHash.Compute(Path.Combine(_project, ".agents/skills/tdd")), Is.EqualTo(FakeGitHub.MinimalHash));
        }

        [Test]
        public void Run_Pinned_SkillsShHashedSource_RefusesAsUnverifiable()
        {
            var (sync, github) = OneSkill(InstallMode.Pinned, "tdd", "vercel-labs/agent-skills", "server-hash-of-another-algorithm");
            github.Fixture("vercel-labs/agent-skills", "skills/tdd", "minimal");

            var error = Assert.Throws<SyncAbortedException>(() => sync.Run());

            Assert.That(error.Failures["tdd"].Failure, Is.EqualTo(SkillFetchFailure.Unverifiable));
            Assert.That(Directory.Exists(Path.Combine(_project, ".agents")), Is.False);
        }

        [Test]
        public void Run_Latest_NonAsciiSkillWhoseHashDiffers_InstallsWithoutReportingItAsDiffering()
        {
            var (sync, github) = OneSkill(InstallMode.Latest, "intl", "owner/skills", FakeGitHub.MinimalHash);
            github.File("owner/skills", "skills/intl/SKILL.md", "# intl").File("owner/skills", "skills/intl/résumé.md", "cv");

            var summary = sync.Run();

            Assert.That(summary.Installed, Is.EqualTo(new[] { "intl" }));
            Assert.That(summary.DiffersFromLock, Is.Empty, "with non-ASCII names a mismatch can't show whether upstream differs");
            Assert.That(File.ReadAllText(Path.Combine(_project, ".agents/skills/intl/résumé.md")), Is.EqualTo("cv"));
        }

        [Test]
        public void Run_Pinned_NonAsciiSkillWhoseHashDiffers_RefusesAsUnverifiable()
        {
            var (sync, github) = OneSkill(InstallMode.Pinned, "intl", "owner/skills", FakeGitHub.MinimalHash);
            github.File("owner/skills", "skills/intl/SKILL.md", "# intl").File("owner/skills", "skills/intl/résumé.md", "cv");

            var error = Assert.Throws<SyncAbortedException>(() => sync.Run());

            Assert.That(error.Failures["intl"].Failure, Is.EqualTo(SkillFetchFailure.Unverifiable));
        }

        [Test]
        public void Run_Pinned_CrlfLockedSkill_InstallsTheLockedCheckoutAndReRunsAsNoOp()
        {
            var (sync, github) = OneSkill(InstallMode.Pinned, "byte-exact", "owner/skills", ByteExactCrlfCheckoutHash);
            github.Fixture("owner/skills", "skills/byte-exact", "byte-exact");

            sync.Run();
            var second = sync.Run();

            Assert.That(SkillFolderHash.Compute(Path.Combine(_project, ".agents/skills/byte-exact")), Is.EqualTo(ByteExactCrlfCheckoutHash));
            Assert.That(second.NothingChanged, Is.True);
        }

        [Test]
        public void Run_Latest_CrlfLockedSkill_InstallsUpstreamBytesWithoutReportingItAsDiffering()
        {
            var (sync, github) = OneSkill(InstallMode.Latest, "byte-exact", "owner/skills", ByteExactCrlfCheckoutHash);
            github.Fixture("owner/skills", "skills/byte-exact", "byte-exact");

            var summary = sync.Run();
            var second = sync.Run();

            var installed = Path.Combine(_project, ".agents/skills/byte-exact");
            Assert.That(File.ReadAllBytes(Path.Combine(installed, "SKILL.md")), Has.No.Member((byte)'\r'));
            Assert.That(summary.DiffersFromLock, Is.Empty, "the lock is this upstream's CRLF checkout");
            Assert.That(second.NothingChanged, Is.True);
            Assert.That(sync.InstalledDiffersFromLock(), Is.Empty);
        }

        [Test]
        public void Run_Latest_CheckedLockfile_LockChangedOnDiskAfterTheCheck_RunsTheCheckedLock()
        {
            var checkedLock = Lockfile.Load(_project);
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), @"{
  ""version"": 1,
  ""skills"": {
    ""unconfirmed"": { ""source"": ""stranger/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/unconfirmed/SKILL.md"", ""computedHash"": """ + FakeGitHub.MinimalHash + @""" }
  }
}");

            var summary = Sync(InstallMode.Latest).Run(checkedLock);

            Assert.That(summary.Installed, Is.EquivalentTo(new[] { "tdd", "code-review" }));
            Assert.That(_fetcher.Fetched, Has.No.Member("unconfirmed"));
        }

        [Test]
        public void InstalledDiffersFromLock_CheckedLockfile_ComparesWithTheGivenLock()
        {
            _fetcher.Upstream["tdd"] = "nested";
            var checkedLock = Lockfile.Load(_project);
            Sync(InstallMode.Latest).Run(checkedLock);
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), PulledLock); // locks tdd at the nested hash

            Assert.That(Sync(InstallMode.Latest).InstalledDiffersFromLock(checkedLock), Is.EqualTo(new[] { "tdd" }));
        }

        [Test]
        public void Run_Latest_OnlyTheCanonicalFolderSelected_LinksNothing()
        {
            var agents = FolderLayout.Default.Find(".agents/skills");

            var summary = new SkillSync(_project, _fetcher, selected: new[] { agents }, mode: InstallMode.Latest).Run();

            Assert.That(summary.Installed, Is.EqualTo(new[] { "tdd", "code-review" }));
            Assert.That(summary.Linked, Is.Empty);
            Assert.That(Directory.Exists(Path.Combine(_project, ".claude")), Is.False);
        }

        [Test]
        public void Run_Latest_SkipStoredWithAUserScopeCopy_LeavesThatFolderOut()
        {
            var agents = FolderLayout.Default.Find(".agents/skills");
            var claude = FolderLayout.Default.Find(".claude/skills");
            var userScope = new UserScopeState(new[] { new UserScopeCopy(claude, "tdd", Path.Combine(_root, "home", "tdd"), "~/.claude/skills") });
            var skips = new SkipChoices(new Dictionary<string, IReadOnlyList<string>> { [claude.RelativePath] = new[] { "tdd" } });

            var summary = new SkillSync(_project, _fetcher, selected: new[] { agents, claude }, userScope: userScope, skips: skips,
                mode: InstallMode.Latest).Run();

            Assert.That(summary.Installed, Is.EqualTo(new[] { "tdd", "code-review" }));
            Assert.That(summary.Linked, Is.EqualTo(new[] { "code-review" }));
            Assert.That(summary.SkippedForUserScope, Is.EqualTo(new[] { "tdd" }));
            Assert.That(Directory.Exists(Path.Combine(_project, ".claude/skills/tdd")), Is.False);
        }

        [Test]
        public void Run_WithPrefsRoot_RecordsManagedSkillsThereAndNotBesideTheLock()
        {
            var unityProject = Path.Combine(_project, "UnityProject");

            new SkillSync(_project, _fetcher, MachineChoices.Default, prefsRoot: unityProject).Run();

            Assert.That(LocalPrefs.Load(unityProject).ManagedSkills[".agents/skills"], Is.EquivalentTo(new[] { "tdd", "code-review" }));
            Assert.That(File.Exists(LocalPrefs.PathFor(_project)), Is.False);
        }

        [Test]
        public void Plan_WithPrefsRoot_ReadsManagedSkillsFromThere()
        {
            var unityProject = Path.Combine(_project, "UnityProject");
            new SkillSync(_project, _fetcher, MachineChoices.Default, prefsRoot: unityProject).Run();
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), PulledLock);
            // Prefs there say this machine manages nothing, so code-review is left alone; the .gitignore blocks beside
            // the lock (the fallback without recorded prefs) would have it removed.
            var prefs = LocalPrefs.Load(unityProject);
            prefs.ManagedSkills = new Dictionary<string, IReadOnlyList<string>>
            {
                [".agents/skills"] = new string[0],
                [".claude/skills"] = new string[0],
            };
            prefs.Save();

            var plan = new SkillSync(_project, _fetcher, MachineChoices.Default, prefsRoot: unityProject).Plan();

            Assert.That(plan.Actions.Where(a => a.Kind == PlanActionKind.Remove), Is.Empty);
        }

        [Test]
        public void Plan_LockMovedAboveTheUnityProject_LeavesSameNamedSkillsThereAlone()
        {
            // Synced while the lock sat in the Unity project, so the prefs record tdd and code-review as managed there.
            var unityProject = Path.Combine(_project, "UnityProject");
            Directory.CreateDirectory(unityProject);
            File.Move(Path.Combine(_project, Lockfile.FileName), Path.Combine(unityProject, Lockfile.FileName));
            new SkillSync(unityProject, _fetcher, MachineChoices.Default).Run();
            // The lock moves up a folder, where someone's own tdd skill already lives.
            File.Move(Path.Combine(unityProject, Lockfile.FileName), Path.Combine(_project, Lockfile.FileName));
            Directory.CreateDirectory(Path.Combine(_project, ".agents/skills/tdd"));
            File.WriteAllText(Path.Combine(_project, ".agents/skills/tdd/SKILL.md"), "# someone else's tdd");

            var plan = new SkillSync(_project, _fetcher, MachineChoices.Default, prefsRoot: unityProject).Plan();

            var tdd = plan.Actions.Where(a => a.SkillName == "tdd" && a.Folder.RelativePath == ".agents/skills").ToList();
            Assert.That(tdd.Select(a => a.Kind), Is.EqualTo(new[] { PlanActionKind.LeaveForeign }));
        }

        [Test]
        public void Run_RecordsTheSkillsRootRelativeToThePrefs()
        {
            var unityProject = Path.Combine(_project, "UnityProject");

            new SkillSync(_project, _fetcher, MachineChoices.Default, prefsRoot: unityProject).Run();

            Assert.That(LocalPrefs.Load(unityProject).ManagedSkillsRoot, Is.EqualTo(".."));
            Assert.That(LocalPrefs.Load(unityProject).ManagedSkillsFor(_project), Is.Not.Null);
            Assert.That(LocalPrefs.Load(unityProject).ManagedSkillsFor(unityProject), Is.Null);
        }
    }
}
