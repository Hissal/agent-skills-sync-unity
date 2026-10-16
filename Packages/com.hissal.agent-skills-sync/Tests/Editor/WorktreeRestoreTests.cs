using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class WorktreeRestoreTests
    {
        const string Lock = @"{
  ""version"": 1,
  ""skills"": {
    ""tdd"": { ""source"": ""owner/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/tdd/SKILL.md"", ""computedHash"": """ + FakeGitHub.MinimalHash + @""" },
    ""review"": { ""source"": ""other/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/review/SKILL.md"", ""computedHash"": """ + FakeGitHub.MinimalHash + @""" }
  }
}";

        const string ReviewOptional = @"{ ""groups"": {
            ""recommended"": { ""skills"": [""tdd""] },
            ""optional"": { ""selectedByDefault"": false, ""skills"": [""review""] } } }";

        string _root;
        GitTestRepo _repo;
        string _home;
        UserEnvironment _environment;
        FakeGitHub _github;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "AgentSkillsWorktreeRestoreTests", Guid.NewGuid().ToString("N"));
            _home = Path.Combine(_root, "home");
            Directory.CreateDirectory(Path.Combine(_home, ".codex"));
            Directory.CreateDirectory(Path.Combine(_home, ".claude"));
            _environment = new UserEnvironment(_home, _ => null);
            _github = new FakeGitHub()
                .Fixture("owner/skills", "skills/tdd", "minimal")
                .Fixture("other/skills", "skills/review", "minimal");
            _repo = GitTestRepo.Create(Path.Combine(_root, "repo"), (Lockfile.FileName, Lock));
        }

        [TearDown]
        public void TearDown() => TempDirectory.Delete(_root);

        SkillsSyncService Service(string project, UserEnvironment environment = null) =>
            new SkillsSyncService(project, environment ?? _environment,
                m => new GitHubSkillFetcher(Path.Combine(_root, "cache"), _github, m));

        /// <summary>Syncs a checkout from (fake) GitHub, as the sync window does, confirming every new source.</summary>
        SyncSummary Sync(string project, UserEnvironment environment = null)
        {
            var service = Service(project, environment);
            return service.Run(service.NewSources);
        }

        /// <summary>A linked worktree away from the main checkout, not beside it.</summary>
        string NewWorktree(string name = "wt") => _repo.AddWorktree(Path.Combine(_root, "elsewhere", "deep", name));

        WorktreeRestore Restore(string project, UserEnvironment environment = null, string baseline = null) =>
            new WorktreeRestore(project, environment ?? _environment, baseline);

        static string Canonical(string project, string skill) => Path.Combine(project, ".agents", "skills", skill);

        static string Linked(string project, string skill) => Path.Combine(project, ".claude", "skills", skill);

        static string[] Contents(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar))
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => p.Substring(root.Length) + ":" + Convert.ToBase64String(File.ReadAllBytes(p))).ToArray();

        static string[] Directories(string root) => Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal).ToArray();

        static bool ShouldNotify(string project, UserEnvironment environment)
        {
            var prefs = LocalPrefs.Load(project);
            var groups = ProjectSyncSettings.Load(project).Groups;
            var status = SyncStatus.Read(project, MachineChoices.Read(project, prefs, environment, groups: groups), project);
            return StartupCheck.ShouldNotify(status, prefs);
        }

        [Test]
        public void Run_FreshWorktreeOutsideTheRepo_InstallsTheRecordedSkillsOfflineAndUnitySeesItSynced()
        {
            Sync(_repo.Root);
            var mainPrefs = File.ReadAllText(LocalPrefs.PathFor(_repo.Root));
            var worktree = NewWorktree();
            _github.Offline = true;
            _github.Requested.Clear();

            var report = Restore(worktree).Run();

            Assert.That(report.Status, Is.EqualTo(RestoreStatus.Restored));
            Assert.That(report.Summary.Installed, Is.EquivalentTo(new[] { "tdd", "review" }));
            Assert.That(_github.Requested, Is.Empty);
            Assert.That(File.ReadAllBytes(Path.Combine(Canonical(worktree, "tdd"), "SKILL.md")),
                Is.EqualTo(File.ReadAllBytes(Path.Combine(Canonical(_repo.Root, "tdd"), "SKILL.md"))));
            var link = Linked(worktree, "tdd");
            Assert.That(File.Exists(Path.Combine(link, "SKILL.md")), Is.True);
            if (DirectoryLink.IsLink(link)) Assert.That(DirectoryLink.ResolvesTo(link, Canonical(worktree, "tdd")), Is.True);
            Assert.That(LocalPrefs.Load(worktree).ManagedSkills[".agents/skills"], Is.EquivalentTo(new[] { "tdd", "review" }));
            Assert.That(ShouldNotify(worktree, _environment), Is.False);
            Assert.That(File.ReadAllText(LocalPrefs.PathFor(_repo.Root)), Is.EqualTo(mainPrefs));
        }

        [Test]
        public void Plan_ChangesNoFilesOrDirectoriesAndListsWhatARunWouldDo()
        {
            Sync(_repo.Root);
            var worktree = NewWorktree();
            var before = Contents(_root);
            var directories = Directories(_root);
            _github.Requested.Clear();

            var report = Restore(worktree).Plan();

            Assert.That(report.Status, Is.EqualTo(RestoreStatus.Ready));
            Assert.That(report.DryRun, Is.True);
            Assert.That(report.Plan.Actions.Where(a => a.Kind == PlanActionKind.Install).Select(a => a.SkillName),
                Is.EquivalentTo(new[] { "tdd", "review" }));
            Assert.That(_github.Requested, Is.Empty);
            Assert.That(Contents(_root), Is.EqualTo(before));
            Assert.That(Directories(_root), Is.EqualTo(directories));
        }

        [Test]
        public void Run_Again_ChangesNothing()
        {
            Sync(_repo.Root);
            var worktree = NewWorktree();
            Restore(worktree).Run();
            var before = Contents(_root);

            var report = Restore(worktree).Run();

            Assert.That(report.Status, Is.EqualTo(RestoreStatus.Restored));
            Assert.That(report.Summary.NothingChanged, Is.True);
            Assert.That(Contents(_root), Is.EqualTo(before));
        }

        const string ThirdSkill =
            @"    ""extra"": { ""source"": ""third/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/extra/SKILL.md"", ""computedHash"": """ + FakeGitHub.MinimalHash + @""" },
";

        static void AddThirdSkill(string project) =>
            File.WriteAllText(Path.Combine(project, Lockfile.FileName), Lock.Replace("\"skills\": {\n", "\"skills\": {\n" + ThirdSkill));

        [Test]
        public void Run_SourceNeverApprovedInThisRepository_ListsItAndChangesNothing()
        {
            Sync(_repo.Root);
            var worktree = NewWorktree();
            AddThirdSkill(worktree);
            var before = Contents(_root);
            var directories = Directories(_root);

            var report = Restore(worktree).Run();

            Assert.That(report.Status, Is.EqualTo(RestoreStatus.SourcesNeedConfirmation));
            Assert.That(report.UnconfirmedSources, Is.EqualTo(new[] { "third/skills" }));
            Assert.That(Contents(_root), Is.EqualTo(before));
            Assert.That(Directories(_root), Is.EqualTo(directories));
        }

        [Test]
        public void Run_ConfirmedSourceWithNoRecordedSnapshot_RequiresAnExplicitSyncAndChangesNothing()
        {
            Sync(_repo.Root);
            var worktree = NewWorktree();
            AddThirdSkill(worktree);
            var before = Contents(_root);

            var report = Restore(worktree).Run(new[] { "third/skills" });

            Assert.That(report.Status, Is.EqualTo(RestoreStatus.SnapshotsMissing));
            Assert.That(report.MissingSnapshots.Select(m => m.Skill), Is.EqualTo(new[] { "extra" }));
            Assert.That(report.MissingSnapshots[0].Reason, Does.Contain("sync"));
            Assert.That(Contents(_root), Is.EqualTo(before));
        }

        /// <summary>Makes choices in a checkout's prefs, as the sync window does.</summary>
        static void Choose(string project, Action<LocalPrefs, SkillGroups> choose)
        {
            var prefs = LocalPrefs.Load(project);
            choose(prefs, ProjectSyncSettings.Load(project).Groups);
            prefs.Save();
        }

        static readonly SkillsFolder Agents = FolderLayout.Default.Find(".agents/skills");

        void CommitReviewOptional() =>
            _repo.Commit("groups", ("ProjectSettings/AgentSkillsSync.json", ReviewOptional));

        [Test]
        public void Run_ChoicesMadeInTheMainCheckout_AreInheritedAndShared()
        {
            CommitReviewOptional();
            Choose(_repo.Root, (prefs, groups) =>
            {
                SkillSelection.Set(prefs, new[] { "review" }, true, groups);
                SkillSelection.Set(prefs, new[] { "tdd" }, false, groups);
                FolderSelection.Save(prefs, FolderLayout.Default, new[] { Agents }, _environment);
            });
            Sync(_repo.Root);
            var worktree = NewWorktree();

            var report = Restore(worktree).Run();

            Assert.That(report.Status, Is.EqualTo(RestoreStatus.Restored));
            Assert.That(report.Scope, Is.EqualTo(ChoiceScope.Shared));
            Assert.That(report.BaselineFrom, Is.EqualTo(_repo.Root));
            Assert.That(report.Summary.Installed, Is.EqualTo(new[] { "review" }));
            Assert.That(Directory.Exists(Canonical(worktree, "tdd")), Is.False);
            Assert.That(Directory.Exists(Linked(worktree, "review")), Is.False);
            var prefs = LocalPrefs.Load(worktree);
            Assert.That(prefs.OptedInSkills, Is.EqualTo(new[] { "review" }));
            Assert.That(prefs.OptedOutSkills, Is.EqualTo(new[] { "tdd" }));
            Assert.That(prefs.SelectedFolders, Is.EqualTo(new[] { ".agents/skills" }));
            Assert.That(ShouldNotify(worktree, _environment), Is.False);

            // A later choice in a checkout following the shared choices reaches the others at their next restore.
            var prefsThere = SharedChoices.Load(worktree, _environment);
            SkillSelection.Set(prefsThere, new[] { "review" }, false, ProjectSyncSettings.Load(worktree).Groups);
            SharedChoices.Save(prefsThere, worktree, _environment);
            var second = NewWorktree("second");
            Assert.That(Restore(second).Run().Summary.Installed, Is.Empty);
            Assert.That(LocalPrefs.Load(second).OptedOutSkills, Is.EqualTo(new[] { "tdd" }));
        }

        [Test]
        public void Run_ChoiceMadeLaterInTheBaselineCheckout_ReachesNewWorktrees()
        {
            Choose(_repo.Root, (prefs, groups) => SkillSelection.Set(prefs, new[] { "review" }, false, groups));
            Sync(_repo.Root);
            Assert.That(Restore(NewWorktree()).Run().BaselineFrom, Is.EqualTo(_repo.Root));

            // The main checkout's sync window, which never restored, opts out of tdd too.
            var prefs = SharedChoices.Load(_repo.Root, _environment);
            SkillSelection.Set(prefs, new[] { "tdd" }, false);
            SharedChoices.Save(prefs, _repo.Root, _environment);
            var report = Restore(NewWorktree("second")).Run();

            Assert.That(report.Summary.Installed, Is.Empty);
            Assert.That(LocalPrefs.Load(report.Project).OptedOutSkills, Is.EqualTo(new[] { "review", "tdd" }));
        }

        [Test]
        public void Run_InstallFailsPartway_LeavesTheCheckoutAndSharedStateAsTheyWere()
        {
            Sync(_repo.Root);
            var worktree = NewWorktree();
            File.WriteAllText(Path.Combine(worktree, ".agents"), "not a folder");
            var before = Contents(_root);
            var directories = Directories(_root);

            Assert.Throws<IOException>(() => Restore(worktree).Run());

            Assert.That(Contents(_root), Is.EqualTo(before));
            Assert.That(Directories(_root), Is.EqualTo(directories));
        }

        [Test]
        public void Restore_GitFileNamingNoRepository_IsNotACheckout()
        {
            var project = Path.Combine(_root, "broken");
            Directory.CreateDirectory(project);
            File.WriteAllText(Path.Combine(project, ".git"), "not a gitdir line");
            File.WriteAllText(Path.Combine(project, Lockfile.FileName), Lock);

            Assert.Throws<RepositoryException>(() => Restore(project));
        }

        [Test]
        public void Run_UnreadableResolutions_CountAsNoneRecorded()
        {
            Sync(_repo.Root);
            var state = SharedChoices.StateOf(_repo.Root, _environment);
            File.WriteAllText(Path.Combine(state.Directory, "resolutions.json"), "{ not json");

            var report = Restore(NewWorktree()).Run();

            Assert.That(report.Status, Is.EqualTo(RestoreStatus.SnapshotsMissing));
            Assert.That(report.MissingSnapshots.Select(m => m.Skill), Is.EquivalentTo(new[] { "tdd", "review" }));
        }

        [Test]
        public void SetScopeShared_RepositoryWithNoSharedChoices_SharesThisCheckoutsChoices()
        {
            Choose(_repo.Root, (prefs, groups) => SkillSelection.Set(prefs, new[] { "review" }, false, groups));

            SharedChoices.SetScope(_repo.Root, ChoiceScope.Shared, _environment);

            Assert.That(SharedChoices.StateOf(_repo.Root, _environment).LoadChoices().OptedOutSkills, Is.EqualTo(new[] { "review" }));
            Assert.That(LocalPrefs.Load(_repo.Root).ChoiceScope, Is.EqualTo(ChoiceScope.Shared));
        }

        [Test]
        public void Run_InstallAnywayChoice_IsInheritedAndUseMineFollowsTheDestinationsOwnScan()
        {
            var userScope = Path.Combine(_home, ".codex", "skills", "tdd");
            Directory.CreateDirectory(userScope);
            File.WriteAllText(Path.Combine(userScope, "SKILL.md"), "mine");
            Choose(_repo.Root, (prefs, _) => InstallAnywayChoices.Set(prefs, Agents, "tdd", true));
            Sync(_repo.Root);
            var worktree = NewWorktree();
            var first = Restore(worktree).Run().Summary;
            Assert.That(first.Installed, Is.EquivalentTo(new[] { "tdd", "review" }));
            Assert.That(first.SkippedForUserScope, Is.Empty);

            // Another machine's home: review is at user scope there and nothing was chosen for it, so use mine.
            var otherHome = Path.Combine(_root, "other-home");
            foreach (var agentHome in new[] { ".codex", ".claude" })
            {
                var otherCopy = Path.Combine(otherHome, agentHome, "skills", "review");
                Directory.CreateDirectory(otherCopy);
                File.WriteAllText(Path.Combine(otherCopy, "SKILL.md"), "mine");
            }
            var other = new UserEnvironment(otherHome, _ => null, _environment.StateDirectory);
            var report = Restore(NewWorktree("other"), other).Run();

            Assert.That(report.Summary.Installed, Is.EqualTo(new[] { "tdd" }));
            Assert.That(report.Summary.SkippedForUserScope, Is.EqualTo(new[] { "review" }));
        }

        [Test]
        public void Run_UnrelatedRepository_SharesNoChoicesOrApprovals()
        {
            Choose(_repo.Root, (prefs, groups) => SkillSelection.Set(prefs, new[] { "review" }, false, groups));
            Sync(_repo.Root);
            var unrelated = GitTestRepo.Create(Path.Combine(_root, "unrelated"), (Lockfile.FileName, Lock));
            var worktree = unrelated.AddWorktree(Path.Combine(_root, "unrelated-wt"));

            var report = Restore(worktree).Run();

            Assert.That(report.Status, Is.EqualTo(RestoreStatus.SourcesNeedConfirmation));
            Assert.That(report.UnconfirmedSources, Is.EqualTo(new[] { "other/skills", "owner/skills" }));
            Assert.That(report.BaselineFrom, Is.Null);
        }

        [Test]
        public void Run_TwoUnityProjectsInOneRepository_ShareNothing()
        {
            Sync(_repo.Root);
            _repo.Commit("second project", ("Second/" + Lockfile.FileName, Lock));
            var worktree = NewWorktree();

            var report = Restore(Path.Combine(worktree, "Second")).Plan();

            Assert.That(report.Status, Is.EqualTo(RestoreStatus.SourcesNeedConfirmation));
            Assert.That(report.UnconfirmedSources, Is.EqualTo(new[] { "other/skills", "owner/skills" }));
            Assert.That(report.BaselineFrom, Is.Null);
        }

        [Test]
        public void Run_CheckoutsDisagreeAndNothingIsShared_RequiresABaselineThenAdoptsTheOneNamed()
        {
            var first = NewWorktree("first");
            Choose(_repo.Root, (prefs, groups) => SkillSelection.Set(prefs, new[] { "review" }, false, groups));
            Choose(first, (prefs, groups) => SkillSelection.Set(prefs, new[] { "tdd" }, false, groups));
            var worktree = NewWorktree();
            var before = Contents(_root);

            var report = Restore(worktree).Run();

            Assert.That(report.Status, Is.EqualTo(RestoreStatus.BaselineRequired));
            Assert.That(report.BaselineCandidates, Is.EquivalentTo(new[] { _repo.Root, first }));
            Assert.That(Contents(_root), Is.EqualTo(before));

            Sync(first);
            var adopted = Restore(worktree, baseline: first).Run();

            Assert.That(adopted.Status, Is.EqualTo(RestoreStatus.Restored));
            Assert.That(adopted.BaselineFrom, Is.EqualTo(first));
            Assert.That(adopted.Summary.Installed, Is.EqualTo(new[] { "review" }));
            Assert.That(LocalPrefs.Load(_repo.Root).OptedOutSkills, Is.EqualTo(new[] { "review" }));
        }

        static string SkillText(string project, string skill) => File.ReadAllText(Path.Combine(Canonical(project, skill), "SKILL.md"));

        [Test]
        public void Run_LatestModeAfterUpstreamMoved_InstallsTheRecordedSnapshotUntilAnExplicitSync()
        {
            Sync(_repo.Root);
            var synced = SkillText(_repo.Root, "tdd");
            _github.File("owner/skills", "skills/tdd/SKILL.md", "moved upstream");
            _github.Requested.Clear();
            var worktree = NewWorktree();

            Restore(worktree).Run();

            Assert.That(SkillText(worktree, "tdd"), Is.EqualTo(synced));
            Assert.That(_github.Requested, Is.Empty);

            // An explicit sync updates to upstream and records it; restores from then on install the new content.
            Assert.That(Sync(worktree).Updated, Is.EqualTo(new[] { "tdd" }));
            var next = NewWorktree("next");
            Restore(next).Run();
            Assert.That(SkillText(next, "tdd"), Is.EqualTo("moved upstream"));
            Assert.That(SkillText(_repo.Root, "tdd"), Is.EqualTo(synced));
        }

        [Test]
        public void Run_DifferentLockEntryOnTheBranch_HasNoResolutionUntilSynced()
        {
            Sync(_repo.Root);
            var worktree = NewWorktree();
            File.WriteAllText(Path.Combine(worktree, Lockfile.FileName), Lock.Replace("skills/tdd/SKILL.md", "other/tdd/SKILL.md"));

            var report = Restore(worktree).Run();

            Assert.That(report.Status, Is.EqualTo(RestoreStatus.SnapshotsMissing));
            Assert.That(report.MissingSnapshots.Select(m => m.Skill), Is.EqualTo(new[] { "tdd" }));
        }

        static void WritePinned(string project)
        {
            var path = Path.Combine(project, ProjectSyncSettings.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, @"{ ""installMode"": ""pinned"" }");
        }

        [Test]
        public void Run_PinnedMode_InstallsLockVerifiedSnapshotsOffline()
        {
            WritePinned(_repo.Root);
            Sync(_repo.Root);
            var worktree = NewWorktree();
            WritePinned(worktree);
            _github.Offline = true;

            var report = Restore(worktree).Run();

            Assert.That(report.Status, Is.EqualTo(RestoreStatus.Restored));
            Assert.That(report.Mode, Is.EqualTo(InstallMode.Pinned));
            Assert.That(SkillFolderHash.Compute(Canonical(worktree, "tdd")), Is.EqualTo(FakeGitHub.MinimalHash));
        }

        [Test]
        public void Run_PinnedModeWithARecordedLatestSnapshotThatDiffersFromTheLock_RefusesIt()
        {
            // Upstream moved for both skills: no snapshot hashes to the lock.
            _github = new FakeGitHub()
                .Fixture("owner/skills", "skills/tdd", "nested")
                .Fixture("other/skills", "skills/review", "nested");
            Sync(_repo.Root);
            var worktree = NewWorktree();
            WritePinned(worktree);

            var report = Restore(worktree).Run();

            Assert.That(report.Status, Is.EqualTo(RestoreStatus.SnapshotsMissing));
            Assert.That(report.MissingSnapshots.Select(m => m.Skill), Is.EquivalentTo(new[] { "tdd", "review" }));
            Assert.That(report.MissingSnapshots[0].Reason, Does.Contain("differs from the lock"));
        }

        [Test]
        public void Run_InstallationFromBeforeSnapshots_NeedsAnImportThatRecordsItsProvenance()
        {
            // Synced while nothing was recorded where this machine now keeps its state.
            Sync(_repo.Root, new UserEnvironment(_home, _ => null, Path.Combine(_root, "old-state")));
            var worktree = NewWorktree();
            Assert.That(Restore(worktree).Plan().Status, Is.EqualTo(RestoreStatus.SnapshotsMissing));

            Assert.That(SnapshotRecorder.Import(_repo.Root, _environment), Is.EqualTo(new[] { "tdd", "review" }));
            var resolution = SharedChoices.StateOf(_repo.Root, _environment).LoadResolutions()
                .Find(Lockfile.Load(_repo.Root).Skills[0]);

            Assert.That(resolution.Provenance, Is.EqualTo(SnapshotProvenance.Import));
            Assert.That(resolution.LockVerified, Is.True);
            Assert.That(Restore(worktree).Run().Status, Is.EqualTo(RestoreStatus.Restored));
        }

        [Test]
        public void Run_InParallelInTwoWorktrees_BothRestore()
        {
            Sync(_repo.Root);
            var first = NewWorktree("first");
            var second = NewWorktree("second");

            var reports = new[] { first, second }.AsParallel().Select(w => Restore(w).Run()).ToList();

            Assert.That(reports.Select(r => r.Status), Is.All.EqualTo(RestoreStatus.Restored));
            Assert.That(SkillText(first, "review"), Is.EqualTo(SkillText(second, "review")));
        }

        [Test]
        public void RemoveWorktree_LeavesAnotherWorktreesSkillsWorking()
        {
            Sync(_repo.Root);
            var first = NewWorktree("first");
            var second = NewWorktree("second");
            Restore(first).Run();
            Restore(second).Run();
            var before = Contents(second);

            _repo.RemoveWorktree(first);

            Assert.That(Contents(second), Is.EqualTo(before));
            Assert.That(File.Exists(Path.Combine(Linked(second, "tdd"), "SKILL.md")), Is.True);
            Assert.That(Restore(second).Run().Summary.NothingChanged, Is.True);
        }

        [Test]
        public void Run_ProjectAuthoredUnsupportedAndForeignEntries_AreKeptAndLinkedWithinTheWorktree()
        {
            _repo.Commit("project skill",
                (".agents/skills/house/SKILL.md", "# house"),
                (Lockfile.FileName, Lock.Replace("\"skills\": {\n",
                    "\"skills\": {\n    \"local-one\": { \"source\": \"./local\", \"sourceType\": \"local\" },\n")));
            Sync(_repo.Root);
            var worktree = NewWorktree();
            Directory.CreateDirectory(Linked(worktree, "review"));
            File.WriteAllText(Path.Combine(Linked(worktree, "review"), "SKILL.md"), "# my own");

            var report = Restore(worktree).Run();

            Assert.That(report.Status, Is.EqualTo(RestoreStatus.Restored));
            Assert.That(report.Summary.Skipped, Is.EqualTo(new[] { "review" }));
            Assert.That(File.ReadAllText(Path.Combine(Linked(worktree, "review"), "SKILL.md")), Is.EqualTo("# my own"));
            Assert.That(File.Exists(Path.Combine(Canonical(worktree, "local-one"), "SKILL.md")), Is.False);
            var house = Linked(worktree, "house");
            Assert.That(File.ReadAllText(Path.Combine(house, "SKILL.md")), Is.EqualTo("# house"));
            if (DirectoryLink.IsLink(house)) Assert.That(DirectoryLink.ResolvesTo(house, Canonical(worktree, "house")), Is.True);
        }

        [Test]
        public void Run_CheckoutKeepingItsOwnChoices_KeepsThemOverTheSharedOnes()
        {
            Sync(_repo.Root);
            var worktree = NewWorktree();
            Restore(worktree).Run();
            SharedChoices.SetScope(worktree, ChoiceScope.Worktree);
            Choose(worktree, (prefs, groups) => SkillSelection.Set(prefs, new[] { "review" }, false, groups));

            var report = Restore(worktree).Run();

            Assert.That(report.Scope, Is.EqualTo(ChoiceScope.Worktree));
            Assert.That(report.Summary.Removed, Is.EqualTo(new[] { "review" }));
            Assert.That(Restore(NewWorktree("second")).Run().Summary.Installed, Is.EquivalentTo(new[] { "tdd", "review" }));
        }
    }
}
