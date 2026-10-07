using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class StartupCheckTests
    {
        // Locked at the real hashes of the `minimal` and `nested` fixtures, so installed copies count as current.
        const string Lock = @"{
  ""version"": 1,
  ""skills"": {
    ""tdd"": { ""source"": ""owner/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/tdd/SKILL.md"", ""computedHash"": """ + FakeGitHub.MinimalHash + @""" },
    ""code-review"": { ""source"": ""owner/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/code-review/SKILL.md"", ""computedHash"": """ + FakeGitHub.NestedHash + @""" }
  }
}";

        static readonly string FixturesRoot =
            Path.GetFullPath("Packages/com.hissal.agent-skills-sync/Tests/Editor/Fixtures~/SkillFolderHash");

        /// <summary>The lock with tdd's hash replaced, as after `npx skills update`.</summary>
        static string LockWithTddHash(string hash) => Lock.Replace(FakeGitHub.MinimalHash, hash);

        string _project;

        [SetUp]
        public void SetUp()
        {
            _project = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_project);
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), Lock);
        }

        [TearDown]
        public void TearDown()
        {
            TempDirectory.Delete(_project);
        }

        /// <summary>Syncs the locked skills from the hash fixtures: managed canonical copies plus links.</summary>
        void InstallAll()
        {
            var layout = FolderLayout.Default;
            var plan = InstallPlanner.Plan(Lockfile.Load(_project), ProjectScanner.Scan(_project, layout), layout);
            new PlanExecutor().Execute(_project, plan, new Dictionary<string, string>
            {
                ["tdd"] = Path.Combine(FixturesRoot, "minimal"),
                ["code-review"] = Path.Combine(FixturesRoot, "nested"),
            });
        }

        void Unlink(string relativePath) => DirectoryLink.Remove(Path.Combine(_project, relativePath));

        LocalPrefs Prefs => LocalPrefs.Load(_project);

        bool ShouldNotify() => StartupCheck.ShouldNotify(SyncStatus.Read(_project), LocalPrefs.Load(_project));

        void RecordSynced()
        {
            var prefs = Prefs;
            StartupCheck.RecordSynced(prefs, SyncStatus.Read(_project));
            prefs.Save();
        }

        void RecordDeclined()
        {
            var prefs = Prefs;
            StartupCheck.RecordDeclined(prefs, SyncStatus.Read(_project));
            prefs.Save();
        }

        [Test]
        public void ShouldNotify_NeverSynced_Notifies()
        {
            InstallAll();

            Assert.That(ShouldNotify(), Is.True);
        }

        [Test]
        public void ShouldNotify_LockUnchangedAndAllSkillsPresent_IsQuiet()
        {
            InstallAll();
            RecordSynced();

            Assert.That(ShouldNotify(), Is.False);
        }

        [Test]
        public void ShouldNotify_LockChangedSinceLastSync_Notifies()
        {
            InstallAll();
            RecordSynced();

            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), LockWithTddHash("changed-2"));

            Assert.That(ShouldNotify(), Is.True);
        }

        [Test]
        public void ShouldNotify_OnlyLineEndingsChanged_IsQuiet()
        {
            var lf = Lock.Replace("\r\n", "\n");
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), lf);
            InstallAll();
            RecordSynced();

            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), lf.Replace("\n", "\r\n"));

            Assert.That(ShouldNotify(), Is.False);
        }

        [Test]
        public void ShouldNotify_ManagedSkillMissing_Notifies()
        {
            InstallAll();
            RecordSynced();

            Unlink(".claude/skills/tdd");

            Assert.That(ShouldNotify(), Is.True);
            Assert.That(SyncStatus.Read(_project).MissingSkills, Is.EqualTo(new[] { "tdd" }));
        }

        [Test]
        public void Read_DoesNotReadInstalledSkillContents()
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("Exclusive file locks are enforced only on Windows.");
            InstallAll();
            RecordSynced();
            Unlink(".claude/skills/tdd");

            // Hashing would have to read this file; the startup check must only look at names and the lock.
            using (new FileStream(Path.Combine(_project, ".agents/skills/code-review/SKILL.md"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.That(SyncStatus.Read(_project).MissingSkills, Is.EqualTo(new[] { "tdd" }));
            }
        }

        [Test]
        public void Read_InstalledCopyEditedLocally_IsNotCountedAsMissing()
        {
            InstallAll();
            RecordSynced();

            File.WriteAllText(Path.Combine(_project, ".agents/skills/tdd/SKILL.md"), "# edited");

            Assert.That(SyncStatus.Read(_project).MissingSkills, Is.Empty);
            Assert.That(ShouldNotify(), Is.False);
        }

        [Test]
        public void ShouldNotify_DeclinedAndNothingChanged_IsQuiet()
        {
            InstallAll();
            RecordSynced();
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), LockWithTddHash("changed-2"));
            RecordDeclined();

            Assert.That(ShouldNotify(), Is.False);
        }

        [Test]
        public void ShouldNotify_DeclinedThenAnotherSkillGoesMissing_Notifies()
        {
            InstallAll();
            RecordSynced();
            Unlink(".claude/skills/tdd");
            RecordDeclined();

            Unlink(".claude/skills/code-review");

            Assert.That(ShouldNotify(), Is.True);
        }

        [Test]
        public void ShouldNotify_DeclinedThenLockChangedAgain_Notifies()
        {
            InstallAll();
            RecordSynced();
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), LockWithTddHash("changed-2"));
            RecordDeclined();

            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), LockWithTddHash("changed-3"));

            Assert.That(ShouldNotify(), Is.True);
        }

        [Test]
        public void ShouldNotify_SyncedAfterDeclining_ClearsTheDecline()
        {
            InstallAll();
            RecordSynced();
            Unlink(".claude/skills/tdd");
            RecordDeclined();
            InstallAll();
            RecordSynced();

            Assert.That(Prefs.DeclinedState, Is.Null);
            Unlink(".claude/skills/tdd");
            Assert.That(ShouldNotify(), Is.True);
        }

        [Test]
        public void SyncStatus_LockedSkillsPresentAsForeignFolders_AreNotMissing()
        {
            foreach (var folder in new[] { ".agents/skills", ".claude/skills" })
            foreach (var name in new[] { "tdd", "code-review" })
                Directory.CreateDirectory(Path.Combine(_project, folder, name));

            Assert.That(SyncStatus.Read(_project).MissingSkills, Is.Empty);
        }

        [Test]
        public void ShouldNotify_NoLockfile_IsQuiet()
        {
            File.Delete(Path.Combine(_project, Lockfile.FileName));

            Assert.That(SyncStatus.Read(_project), Is.Null);
            Assert.That(ShouldNotify(), Is.False);
        }

        [Test]
        public void ShouldNotify_InvalidLockfileThatChanged_Notifies()
        {
            InstallAll();
            RecordSynced();

            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), "{ broken");

            Assert.That(ShouldNotify(), Is.True);
        }

        static SkillsFolder[] Only(params string[] paths) => paths.Select(FolderLayout.Default.Find).ToArray();

        [Test]
        public void ShouldNotify_NoFolderSelected_NeverNotifies()
        {
            var status = SyncStatus.Read(_project, selected: Only());

            Assert.That(status.NoFolderSelected, Is.True);
            Assert.That(StartupCheck.ShouldNotify(status, Prefs), Is.False);
        }

        [Test]
        public void ShouldNotify_NoFolderSelectedWithManagedEntriesLeft_NeverNotifies()
        {
            InstallAll();

            Assert.That(StartupCheck.ShouldNotify(SyncStatus.Read(_project, selected: Only()), Prefs), Is.False);
        }

        [Test]
        public void ShouldNotify_DeclinedThenSelectionChangedWithSameSkillMissing_Notifies()
        {
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), @"{
  ""version"": 1,
  ""skills"": {
    ""tdd"": { ""source"": ""owner/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/tdd/SKILL.md"", ""computedHash"": """ + FakeGitHub.MinimalHash + @""" }
  }
}");
            var layout = FolderLayout.Default;
            var agentsOnly = Only(".agents/skills");
            var plan = InstallPlanner.Plan(Lockfile.Load(_project), ProjectScanner.Scan(_project, layout), layout, selected: agentsOnly);
            new PlanExecutor().Execute(_project, plan, new Dictionary<string, string> { ["tdd"] = Path.Combine(FixturesRoot, "minimal") });
            var prefs = Prefs;
            StartupCheck.RecordSynced(prefs, SyncStatus.Read(_project, selected: agentsOnly));
            prefs.Save();
            TempDirectory.Delete(Path.Combine(_project, ".agents/skills/tdd"));
            prefs = Prefs;
            StartupCheck.RecordDeclined(prefs, SyncStatus.Read(_project, selected: agentsOnly));
            prefs.Save();

            var widened = SyncStatus.Read(_project, selected: Only(".agents/skills", ".claude/skills"));

            Assert.That(widened.MissingSkills, Is.EqualTo(new[] { "tdd" }));
            Assert.That(StartupCheck.ShouldNotify(widened, Prefs), Is.True);
        }

        [Test]
        public void Read_OnlyAgentsSelectedAndInstalledThere_ReportsNothingMissing()
        {
            var layout = FolderLayout.Default;
            var selected = Only(".agents/skills");
            var plan = InstallPlanner.Plan(Lockfile.Load(_project), ProjectScanner.Scan(_project, layout), layout, selected: selected);
            new PlanExecutor().Execute(_project, plan, new Dictionary<string, string>
            {
                ["tdd"] = Path.Combine(FixturesRoot, "minimal"),
                ["code-review"] = Path.Combine(FixturesRoot, "nested"),
            });

            Assert.That(SyncStatus.Read(_project, selected: selected).MissingSkills, Is.Empty);
            Assert.That(SyncStatus.Read(_project).MissingSkills, Is.EqualTo(new[] { "code-review", "tdd" }));
        }
    }
}
