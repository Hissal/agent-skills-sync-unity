using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class WorktreeSetupTests
    {
        const string Lock = @"{ ""version"": 1, ""skills"": {
            ""tdd"": { ""source"": ""owner/skills"", ""sourceType"": ""github"",
                ""skillPath"": ""skills/tdd/SKILL.md"", ""computedHash"": """ + FakeGitHub.MinimalHash + @""" }
        } }";

        string _root;
        GitTestRepo _repo;
        UserEnvironment _environment;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "AgentSkillsWorktreeSetupTests", Guid.NewGuid().ToString("N"));
            var home = Path.Combine(_root, "home");
            Directory.CreateDirectory(Path.Combine(home, ".codex"));
            _environment = new UserEnvironment(home, _ => null);
            _repo = GitTestRepo.Create(Path.Combine(_root, "repo"), (Lockfile.FileName, Lock));
        }

        [TearDown]
        public void TearDown() => TempDirectory.Delete(_root);

        WorktreeSetupStatus Read(string project = null) => WorktreeSetupStatus.Read(project ?? _repo.Root, _environment);
        string Worktree(string name = "wt") => _repo.AddWorktree(Path.Combine(_root, name));

        void Sync(string project)
        {
            var github = new FakeGitHub().Fixture("owner/skills", "skills/tdd", "minimal");
            var service = new SkillsSyncService(project, _environment,
                mode => new GitHubSkillFetcher(Path.Combine(_root, "cache"), github, mode));
            service.Run(service.NewSources);
        }

        static string[] Contents(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => p.Substring(root.Length) + ":" + Convert.ToBase64String(File.ReadAllBytes(p))).ToArray();

        [Test]
        public void Read_NeverSetScope_IdentifiesTheProjectWithoutWritingAnything()
        {
            var before = Contents(_root);
            var directories = Directory.GetDirectories(_root, "*", SearchOption.AllDirectories);

            var status = Read();

            Assert.That(status.Identity, Is.Not.Null);
            Assert.That(status.Identity.ProjectPath, Is.EqualTo("."));
            Assert.That(status.Identity.Checkout, Is.EqualTo(_repo.Root));
            Assert.That(status.Scope, Is.Null);
            Assert.That(status.FollowsShared, Is.False);
            Assert.That(Contents(_root), Is.EqualTo(before));
            Assert.That(Directory.GetDirectories(_root, "*", SearchOption.AllDirectories), Is.EquivalentTo(directories));
        }

        [Test]
        public void Read_OutsideGit_HasNoControlsToOffer()
        {
            Assert.That(Read(Path.Combine(_root, "outside")).Identity, Is.Null);
        }

        [Test]
        public void Read_OwnChoices_ShowsEveryKindOfDifferenceIncludingDeclinedFolders()
        {
            var prefs = LocalPrefs.Load(_repo.Root);
            prefs.SelectedFolders = new[] { ".agents/skills" };
            prefs.Save();
            SharedChoices.SetScope(_repo.Root, ChoiceScope.Shared, _environment);
            var other = Worktree();
            var local = LocalPrefs.Load(other);
            local.ChoiceScope = ChoiceScope.Worktree;
            local.SelectedFolders = new[] { ".claude/skills" };
            local.DeclinedFolders = new[] { ".cursor/skills" };
            local.OptedInSkills = new[] { "extra" };
            local.OptedOutSkills = new[] { "tdd" };
            local.InstallAnywaySkills = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<string>>
                { [".claude/skills"] = new[] { "tdd" } };
            local.Save();

            var status = Read(other);

            Assert.That(status.Scope, Is.EqualTo(ChoiceScope.Worktree));
            Assert.That(status.FollowsShared, Is.False);
            Assert.That(status.HasSharedChoices, Is.True);
            Assert.That(status.Differences.Select(d => d.Kind), Is.EquivalentTo(
                new[] { WorktreeChoiceKind.Folders, WorktreeChoiceKind.DeclinedFolders, WorktreeChoiceKind.OptIns,
                    WorktreeChoiceKind.OptOuts, WorktreeChoiceKind.InstallAnyway }));
            var folders = status.Differences.Single(d => d.Kind == WorktreeChoiceKind.Folders);
            Assert.That(folders.Local, Is.EqualTo(new[] { ".claude/skills" }));
            Assert.That(folders.Shared, Is.EqualTo(new[] { ".agents/skills" }));
        }

        [Test]
        public void Read_SyncedSkill_ShowsItsProvenanceAndLockMatch()
        {
            Sync(_repo.Root);

            var snapshot = Read().Snapshots.Single();

            Assert.That(snapshot.Skill, Is.EqualTo("tdd"));
            Assert.That(snapshot.Available, Is.True);
            Assert.That(snapshot.Resolution.Provenance, Is.EqualTo(SnapshotProvenance.Sync));
            Assert.That(snapshot.MatchesLock, Is.True);
        }

        [Test]
        public void Read_NoSnapshot_NamesTheSkillAndTheUnapprovedSource()
        {
            var status = Read();

            Assert.That(status.Snapshots.Single().Available, Is.False);
            Assert.That(status.NewWorktree.Status, Is.EqualTo(RestoreStatus.SourcesNeedConfirmation));
            Assert.That(status.NewWorktree.UnconfirmedSources, Is.EqualTo(new[] { "owner/skills" }));
        }

        [Test]
        public void Share_PublishesOwnChoicesAndKeepsCheckoutBookkeeping()
        {
            Sync(_repo.Root);
            var prefs = LocalPrefs.Load(_repo.Root);
            prefs.ChoiceScope = ChoiceScope.Worktree;
            prefs.OptedOutSkills = new[] { "tdd" };
            prefs.DeclinedState = "keep this";
            prefs.Save();

            WorktreeSetup.Share(_repo.Root, _environment);

            Assert.That(Read().FollowsShared, Is.True);
            Assert.That(LocalPrefs.Load(_repo.Root).DeclinedState, Is.EqualTo("keep this"));
            Assert.That(new WorktreeRestore(Worktree(), _environment).Run().Summary.Installed, Is.Empty);
        }

        [Test]
        public void UseShared_ReplacesChoicesAndLeavesInstallationForTheNextSync()
        {
            Sync(_repo.Root);
            WorktreeSetup.Share(_repo.Root, _environment);
            var other = Worktree();
            var prefs = LocalPrefs.Load(other);
            prefs.ChoiceScope = ChoiceScope.Worktree;
            prefs.OptedOutSkills = new[] { "tdd" };
            prefs.DeclinedState = "keep this too";
            prefs.Save();

            WorktreeSetup.UseShared(other, _environment);

            Assert.That(Read(other).FollowsShared, Is.True);
            Assert.That(LocalPrefs.Load(other).OptedOutSkills, Is.Empty);
            Assert.That(LocalPrefs.Load(other).DeclinedState, Is.EqualTo("keep this too"));
            Assert.That(Directory.Exists(Path.Combine(other, ".agents", "skills", "tdd")), Is.False);
            Assert.That(new WorktreeRestore(other, _environment).Run().Summary.Installed, Is.EqualTo(new[] { "tdd" }));
        }

        [Test]
        public void SetScope_KeepingOwnChoicesFreezesWhatTheWindowCurrentlyShows()
        {
            Sync(_repo.Root);
            WorktreeSetup.Share(_repo.Root, _environment);
            var other = Worktree();
            WorktreeSetup.UseShared(other, _environment);
            var prefs = SharedChoices.Load(_repo.Root, _environment);
            prefs.OptedOutSkills = new[] { "tdd" };
            SharedChoices.Save(prefs, _repo.Root, _environment);

            WorktreeSetup.SetScope(other, ChoiceScope.Worktree, _environment);
            prefs.OptedOutSkills = new string[0];
            SharedChoices.Save(prefs, _repo.Root, _environment);

            Assert.That(Read(other).Scope, Is.EqualTo(ChoiceScope.Worktree));
            Assert.That(SharedChoices.Load(other, _environment).OptedOutSkills, Is.EqualTo(new[] { "tdd" }));
        }

        [Test]
        public void Read_InstalledPinnedCopyDoesNotHideAMissingSnapshotForANewWorktree()
        {
            Sync(_repo.Root);
            Directory.CreateDirectory(Path.Combine(_repo.Root, "ProjectSettings"));
            File.WriteAllText(Path.Combine(_repo.Root, "ProjectSettings", "AgentSkillsSync.json"), "{\"installMode\":\"pinned\"}");
            var store = Path.Combine(_environment.StateDirectory, "snapshots", FakeGitHub.MinimalHash);
            TempDirectory.Delete(store);

            Assert.That(new WorktreeRestore(_repo.Root, _environment).Plan().Status, Is.EqualTo(RestoreStatus.Ready));
            Assert.That(Read().NewWorktree.Status, Is.EqualTo(RestoreStatus.SnapshotsMissing));
        }

        [Test]
        public void Read_DisagreeingCheckouts_ListsTheirDifferencesAndAdoptsTheChosenBaseline()
        {
            Sync(_repo.Root);
            var other = Worktree();
            var prefs = LocalPrefs.Load(other);
            prefs.SelectedFolders = new[] { ".agents/skills" };
            prefs.OptedOutSkills = new[] { "tdd" };
            prefs.Save();

            var status = Read();

            Assert.That(status.NewWorktree.Status, Is.EqualTo(RestoreStatus.BaselineRequired));
            Assert.That(status.Candidates.Select(c => c.Project), Is.EquivalentTo(new[] { _repo.Root, other }));
            Assert.That(status.Candidates.Single(c => c.Project == other).Differences.Single().Kind, Is.EqualTo(WorktreeChoiceKind.OptOuts));

            WorktreeSetup.AdoptBaseline(_repo.Root, other, _environment);

            Assert.That(Read().HasSharedChoices, Is.True);
            Assert.That(new WorktreeRestore(Worktree("fresh"), _environment).Run().Summary.Installed, Is.Empty);
        }

        [Test]
        public void Read_ImportedOlderInstallation_ReportsItsProvenanceAndRealLockMismatch()
        {
            var oldEnvironment = new UserEnvironment(Path.Combine(_root, "home"), _ => null, Path.Combine(_root, "old-state"));
            var github = new FakeGitHub().Fixture("owner/skills", "skills/tdd", "minimal");
            var service = new SkillsSyncService(_repo.Root, oldEnvironment,
                mode => new GitHubSkillFetcher(Path.Combine(_root, "cache"), github, mode));
            service.Run(service.NewSources);
            File.AppendAllText(Path.Combine(_repo.Root, ".agents", "skills", "tdd", "SKILL.md"), "\nlocal change");
            Assert.That(Read().Snapshots.Single().Available, Is.False);

            Assert.That(SnapshotRecorder.Import(_repo.Root, _environment), Is.EqualTo(new[] { "tdd" }));

            var snapshot = Read().Snapshots.Single();
            Assert.That(snapshot.Available, Is.True);
            Assert.That(snapshot.Resolution.Provenance, Is.EqualTo(SnapshotProvenance.Import));
            Assert.That(snapshot.MatchesLock, Is.False);
            Assert.That(Read().NewWorktree.Status, Is.EqualTo(RestoreStatus.Ready));
        }

        [Test]
        public void Read_WithSharedChoicesAndSnapshots_ChangesNoFilesOrDirectories()
        {
            Sync(_repo.Root);
            WorktreeSetup.Share(_repo.Root, _environment);
            var other = Worktree();
            var before = Contents(_root);
            var directories = Directory.GetDirectories(_root, "*", SearchOption.AllDirectories);

            var status = Read(other);

            Assert.That(status.NewWorktree.Plan.Actions.Where(a => a.NeedsFetch).Select(a => a.SkillName), Is.EqualTo(new[] { "tdd" }));
            Assert.That(Contents(_root), Is.EqualTo(before));
            Assert.That(Directory.GetDirectories(_root, "*", SearchOption.AllDirectories), Is.EquivalentTo(directories));
        }

        [Test]
        public void SetScope_Shared_TakesExistingChoicesAndKeepsThemOutOfOtherClones()
        {
            Sync(_repo.Root);
            WorktreeSetup.Share(_repo.Root, _environment);
            var other = Worktree();
            var prefs = LocalPrefs.Load(other);
            prefs.OptedOutSkills = new[] { "tdd" };
            prefs.Save();

            WorktreeSetup.SetScope(other, ChoiceScope.Shared, _environment);

            Assert.That(Read(other).Scope, Is.EqualTo(ChoiceScope.Shared));
            Assert.That(SharedChoices.Load(other, _environment).OptedOutSkills, Is.Empty);
            var clone = GitTestRepo.Create(Path.Combine(_root, "another-clone"), (Lockfile.FileName, Lock));
            Assert.That(Read(clone.Root).HasSharedChoices, Is.False);
        }

        [Test]
        public void AdoptBaseline_StaleOrForeignCandidate_IsRefusedWithoutChangingChoices()
        {
            Sync(_repo.Root);
            WorktreeSetup.Share(_repo.Root, _environment);
            var before = Contents(_root);

            Assert.Throws<RepositoryException>(() => WorktreeSetup.AdoptBaseline(_repo.Root, Path.Combine(_root, "outside"), _environment));

            Assert.That(Contents(_root), Is.EqualTo(before));
        }

        [Test]
        public void Read_AutomaticFoldersDifferFromAnExplicitEmptySelection()
        {
            Sync(_repo.Root);
            WorktreeSetup.Share(_repo.Root, _environment);
            var other = Worktree();
            var prefs = LocalPrefs.Load(other);
            prefs.SelectedFolders = new string[0];
            prefs.Save();
            var shared = SharedChoices.StateOf(_repo.Root, _environment).LoadChoices();
            shared.SelectedFolders = null;
            shared.Save();

            var folders = Read(other).Differences.Single();

            Assert.That(folders.Kind, Is.EqualTo(WorktreeChoiceKind.Folders));
            Assert.That(folders.Local, Is.Empty);
            Assert.That(folders.Shared, Is.Null);
        }

        [Test]
        public void Read_PinnedCanUseAnOlderMatchingSnapshot_SeparatelyFromTheRecordedLatestResolution()
        {
            Sync(_repo.Root);
            var github = new FakeGitHub().Fixture("owner/skills", "skills/tdd", "nested");
            var service = new SkillsSyncService(_repo.Root, _environment,
                mode => new GitHubSkillFetcher(Path.Combine(_root, "changed-cache"), github, mode));
            service.Run(service.NewSources);
            Directory.CreateDirectory(Path.Combine(_repo.Root, "ProjectSettings"));
            File.WriteAllText(Path.Combine(_repo.Root, "ProjectSettings", "AgentSkillsSync.json"), "{\"installMode\":\"pinned\"}");

            var snapshot = Read().Snapshots.Single();

            Assert.That(snapshot.Available, Is.True);
            Assert.That(snapshot.Resolution.LockVerified, Is.False);
            Assert.That(snapshot.MatchesLock, Is.False);
            Assert.That(snapshot.RestoreSnapshot, Is.EqualTo(FakeGitHub.MinimalHash));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Share_WhenLocalPrefsCannotBeSaved_DoesNotPublishChoices(bool switchScope)
        {
            Sync(_repo.Root);
            if (!switchScope) WorktreeSetup.Share(_repo.Root, _environment);
            var other = Worktree();
            // A directory in place of the destination file refuses the atomic save on every OS.
            Directory.CreateDirectory(LocalPrefs.PathFor(other));
            var before = Contents(_root);

            Assert.Throws<IOException>(() =>
            {
                if (switchScope) WorktreeSetup.SetScope(other, ChoiceScope.Shared, _environment);
                else WorktreeSetup.Share(other, _environment);
            });

            Assert.That(Contents(_root), Is.EqualTo(before));
            Assert.That(Read().HasSharedChoices, Is.EqualTo(!switchScope));
        }

        [Test]
        public void Share_WhenSharedPrefsCannotBeSaved_RestoresTheCheckoutsPreviousBytes()
        {
            Sync(_repo.Root);
            WorktreeSetup.Share(_repo.Root, _environment);
            var other = Worktree();
            var prefs = LocalPrefs.Load(other);
            prefs.OptedOutSkills = new[] { "tdd" };
            prefs.Save();
            var sharedPath = Path.Combine(SharedChoices.StateOf(_repo.Root, _environment).Directory, "choices.json");
            File.Delete(sharedPath);
            Directory.CreateDirectory(sharedPath);
            var before = Contents(_root);

            Assert.Throws<IOException>(() => WorktreeSetup.Share(other, _environment));

            Assert.That(Contents(_root), Is.EqualTo(before));
        }
    }
}
