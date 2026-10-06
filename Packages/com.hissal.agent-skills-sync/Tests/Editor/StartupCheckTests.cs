using System;
using System.IO;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class StartupCheckTests
    {
        const string Lock = @"{
  ""version"": 1,
  ""skills"": {
    ""tdd"": { ""source"": ""owner/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/tdd/SKILL.md"", ""computedHash"": ""a"" },
    ""code-review"": { ""source"": ""owner/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/code-review/SKILL.md"", ""computedHash"": ""b"" }
  }
}";

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
            if (Directory.Exists(_project)) Directory.Delete(_project, recursive: true);
        }

        void InstallAll()
        {
            foreach (var folder in new[] { ".agents/skills", ".claude/skills" })
            foreach (var name in new[] { "tdd", "code-review" })
                Directory.CreateDirectory(Path.Combine(_project, folder, name));
        }

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

            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), Lock.Replace(@"""a""", @"""a2"""));

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

            Directory.Delete(Path.Combine(_project, ".claude/skills/tdd"));

            Assert.That(ShouldNotify(), Is.True);
            Assert.That(SyncStatus.Read(_project).MissingSkills, Is.EqualTo(new[] { "tdd" }));
        }

        [Test]
        public void ShouldNotify_DeclinedAndNothingChanged_IsQuiet()
        {
            InstallAll();
            RecordSynced();
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), Lock.Replace(@"""a""", @"""a2"""));
            RecordDeclined();

            Assert.That(ShouldNotify(), Is.False);
        }

        [Test]
        public void ShouldNotify_DeclinedThenAnotherSkillGoesMissing_Notifies()
        {
            InstallAll();
            RecordSynced();
            Directory.Delete(Path.Combine(_project, ".claude/skills/tdd"));
            RecordDeclined();

            Directory.Delete(Path.Combine(_project, ".claude/skills/code-review"));

            Assert.That(ShouldNotify(), Is.True);
        }

        [Test]
        public void ShouldNotify_DeclinedThenLockChangedAgain_Notifies()
        {
            InstallAll();
            RecordSynced();
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), Lock.Replace(@"""a""", @"""a2"""));
            RecordDeclined();

            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), Lock.Replace(@"""a""", @"""a3"""));

            Assert.That(ShouldNotify(), Is.True);
        }

        [Test]
        public void ShouldNotify_SyncedAfterDeclining_ClearsTheDecline()
        {
            InstallAll();
            RecordSynced();
            Directory.Delete(Path.Combine(_project, ".claude/skills/tdd"));
            RecordDeclined();
            Directory.CreateDirectory(Path.Combine(_project, ".claude/skills/tdd"));
            RecordSynced();

            Assert.That(Prefs.DeclinedState, Is.Null);
            Directory.Delete(Path.Combine(_project, ".claude/skills/tdd"));
            Assert.That(ShouldNotify(), Is.True);
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
    }
}
