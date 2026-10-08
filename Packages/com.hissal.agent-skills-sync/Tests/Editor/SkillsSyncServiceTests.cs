using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hissal.AgentSkillsSync.Editor;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class SkillsSyncServiceTests
    {
        const string Lock = @"{
  ""version"": 1,
  ""skills"": {
    ""tdd"": { ""source"": ""owner/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/tdd/SKILL.md"", ""computedHash"": """ + FakeGitHub.MinimalHash + @""" },
    ""review"": { ""source"": ""other/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/review/SKILL.md"", ""computedHash"": """ + FakeGitHub.MinimalHash + @""" }
  }
}";

        string _root;
        string _project;
        UserEnvironment _environment;
        FakeGitHub _github;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncServiceTests", Guid.NewGuid().ToString("N"));
            _project = Path.Combine(_root, "project");
            Directory.CreateDirectory(_project);
            var home = Path.Combine(_root, "home");
            Directory.CreateDirectory(Path.Combine(home, ".codex"));
            _environment = new UserEnvironment(home, _ => null);
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), Lock);
            _github = new FakeGitHub()
                .Fixture("owner/skills", "skills/tdd", "minimal")
                .Fixture("other/skills", "skills/review", "minimal");
        }

        [TearDown]
        public void TearDown() => TempDirectory.Delete(_root);

        SkillsSyncService Service(InstallMode? mode = null) => new SkillsSyncService(_project, _environment,
            m => new GitHubSkillFetcher(Path.Combine(_root, "cache"), _github, m), mode);

        static string[] Contents(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => p.Substring(root.Length) + ":" + Convert.ToBase64String(File.ReadAllBytes(p))).ToArray();

        [Test]
        public void Plan_UnconfirmedSources_IsOfflineAndChangesNoFilesOrDirectories()
        {
            var before = Contents(_root);
            var directories = Directory.GetDirectories(_root, "*", SearchOption.AllDirectories);
            var service = Service();
            var plan = service.Plan();

            Assert.That(plan.Actions.Where(a => a.Kind == PlanActionKind.Install).Select(a => a.SkillName),
                Is.EquivalentTo(new[] { "tdd", "review" }));
            Assert.That(service.NewSources, Is.EqualTo(new[] { "other/skills", "owner/skills" }));
            Assert.That(_github.Requested, Is.Empty);
            Assert.That(Contents(_root), Is.EqualTo(before));
            Assert.That(Directory.GetDirectories(_root, "*", SearchOption.AllDirectories), Is.EqualTo(directories));
        }

        [Test]
        public void Run_MissingConsent_ListsEverySourceWithoutFetchingOrWriting()
        {
            var before = Contents(_root);
            var error = Assert.Throws<SourceConsentException>(() => Service().Run(Array.Empty<string>()));

            Assert.That(error.Sources, Is.EqualTo(new[] { "other/skills", "owner/skills" }));
            Assert.That(error.Message, Does.Contain("owner/skills").And.Contain("other/skills"));
            Assert.That(_github.Requested, Is.Empty);
            Assert.That(Contents(_root), Is.EqualTo(before));
        }

        [Test]
        public void Run_PartialConsent_RefusesTheRemainingSource()
        {
            var error = Assert.Throws<SourceConsentException>(() => Service().Run(new[] { "OWNER/skills" }));
            Assert.That(error.Sources, Is.EqualTo(new[] { "other/skills" }));
            Assert.That(_github.Requested, Is.Empty);
        }

        [Test]
        public void Run_ConfirmedSources_InstallsAndRecordsConsentAutofillAndOwnership()
        {
            var service = Service();
            var summary = service.Run(service.NewSources);
            var prefs = LocalPrefs.Load(_project);

            Assert.That(summary.Installed, Is.EquivalentTo(new[] { "tdd", "review" }));
            Assert.That(prefs.SyncedSources, Is.EqualTo(service.NewSources));
            Assert.That(prefs.SelectedFolders, Is.EqualTo(new[] { ".agents/skills" }));
            Assert.That(prefs.ManagedSkills[".agents/skills"], Is.EquivalentTo(summary.Installed));
            Assert.That(prefs.LastSyncedLockHash, Is.EqualTo(LockfileHash.Compute(_project)));
            Assert.That(StartupCheck.ShouldNotify(SyncStatus.Read(_project, service.Choices), prefs), Is.False);
            Assert.That(Service().Run(Array.Empty<string>()).NothingChanged, Is.True);
        }

        [Test]
        public void Run_Offline_AbortsWithoutRecordingConsentOrChangingProject()
        {
            _github.Offline = true;
            var before = Contents(_project);
            var service = Service();
            var error = Assert.Throws<SyncAbortedException>(() => service.Run(service.NewSources));

            Assert.That(error.Failures.Keys, Is.EquivalentTo(new[] { "tdd", "review" }));
            Assert.That(Contents(_project), Is.EqualTo(before));
            Assert.That(File.Exists(LocalPrefs.PathFor(_project)), Is.False);
        }

        [Test]
        public void Run_ModeChangedSinceWindowPreview_RequiresReviewBeforeFetching()
        {
            new ProjectSyncSettings(InstallMode.Pinned).Save(_project);
            var service = Service();
            Assert.Throws<InstallModeChangedException>(() => service.Run(service.NewSources, InstallMode.Latest));
            Assert.That(_github.Requested, Is.Empty);
        }

        [Test]
        public void Run_ModeOverride_AppliesOnlyToThisInvocation()
        {
            new ProjectSyncSettings(InstallMode.Pinned).Save(_project);
            _github.File("owner/skills", "skills/tdd/SKILL.md", "# changed upstream");
            var service = Service(InstallMode.Latest);
            var summary = service.Run(service.NewSources);

            Assert.That(summary.DiffersFromLock, Is.EqualTo(new[] { "tdd" }));
            Assert.That(ProjectSyncSettings.Load(_project).InstallMode, Is.EqualTo(InstallMode.Pinned));
            Assert.That(File.ReadAllText(Path.Combine(_project, Lockfile.FileName)), Is.EqualTo(Lock));
        }

        [Test]
        public void Run_PinnedSourceChanged_RefusesItWithoutSavingConsent()
        {
            new ProjectSyncSettings(InstallMode.Pinned).Save(_project);
            _github.File("owner/skills", "skills/tdd/SKILL.md", "# changed upstream");
            var service = Service();
            Assert.Throws<SyncAbortedException>(() => service.Run(service.NewSources));
            Assert.That(File.Exists(LocalPrefs.PathFor(_project)), Is.False);
        }

        [Test]
        public void Run_NoSelectedFolders_RespectsTheStoredOptOut()
        {
            var prefs = LocalPrefs.Load(_project);
            prefs.SelectedFolders = Array.Empty<string>();
            prefs.Save();
            var service = Service();
            var summary = service.Run(service.NewSources);

            Assert.That(summary.Installed, Is.Empty);
            Assert.That(_github.Requested, Is.Empty);
            Assert.That(LocalPrefs.Load(_project).SelectedFolders, Is.Empty);
        }

        [Test]
        public void Run_UserScopeCopyAndInstallAnyway_UsesStoredChoices()
        {
            var userSkill = Path.Combine(_environment.HomeDirectory, ".codex/skills/tdd");
            Directory.CreateDirectory(userSkill);
            File.WriteAllText(Path.Combine(userSkill, "SKILL.md"), "# user's tdd");
            var service = Service();
            var summary = service.Run(service.NewSources);
            Assert.That(summary.SkippedForUserScope, Is.EqualTo(new[] { "tdd" }));
            Assert.That(summary.Installed, Is.EqualTo(new[] { "review" }));

            var prefs = LocalPrefs.Load(_project);
            InstallAnywayChoices.Set(prefs, FolderLayout.Default.Canonical, "tdd", true);
            prefs.Save();
            Assert.That(Service().Run(Array.Empty<string>()).Installed, Is.EqualTo(new[] { "tdd" }));
        }

        [Test]
        public void Run_ParentLock_KeepsPrefsInUnityProjectAndSkillsBesideLock()
        {
            File.Move(Path.Combine(_project, Lockfile.FileName), Path.Combine(_root, Lockfile.FileName));
            var service = Service();
            service.Run(service.NewSources);

            Assert.That(File.Exists(Path.Combine(_root, ".agents/skills/tdd/SKILL.md")), Is.True);
            Assert.That(File.Exists(LocalPrefs.PathFor(_root)), Is.False);
            Assert.That(LocalPrefs.Load(_project).SyncedSources, Is.EqualTo(service.NewSources));
            Assert.That(LocalPrefs.Load(_project).ManagedSkillsFor(_root), Is.Not.Null);
        }

        [Test]
        public void Run_LockChangedAfterSnapshot_NeverRunsTheUncheckedLock()
        {
            var service = Service();
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), Lock.Replace("other/skills", "unconfirmed/skills"));
            service.Run(service.NewSources);

            Assert.That(_github.Requested.Any(url => url.Contains("unconfirmed/skills")), Is.False);
            Assert.That(LocalPrefs.Load(_project).SyncedSources, Does.Not.Contain("unconfirmed/skills"));
            Assert.Throws<SourceConsentException>(() => Service().Run(Array.Empty<string>()));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Load_InvalidProjectSettings_RejectsItEvenWithModeOverride(bool withOverride)
        {
            Directory.CreateDirectory(Path.Combine(_project, "ProjectSettings"));
            File.WriteAllText(Path.Combine(_project, ProjectSyncSettings.RelativePath), "{\"installMode\":\"unknown\"}");
            Assert.Throws<ProjectSyncSettingsException>(() => Service(withOverride ? InstallMode.Latest : (InstallMode?)null));
        }

        [Test]
        public void Load_InvalidLock_RejectsItBeforePlanning()
        {
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), "{ invalid JSON");
            Assert.Throws<LockfileException>(() => Service());
            Assert.That(_github.Requested, Is.Empty);
        }

        [Test]
        public void Load_MissingLock_NamesTheSearchedFolders()
        {
            File.Delete(Path.Combine(_project, Lockfile.FileName));
            var error = Assert.Throws<LockfileException>(() => Service());
            Assert.That(error.Message, Does.Contain(_project).And.Contain(_root));
        }
    }
}
