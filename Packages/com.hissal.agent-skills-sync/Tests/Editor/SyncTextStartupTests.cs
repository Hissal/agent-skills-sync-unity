using Hissal.AgentSkillsSync.Editor;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>What the startup notification says is out of sync.</summary>
    public class SyncTextStartupTests
    {
        static LocalPrefs PrefsSyncedAt(string lockHash)
        {
            var prefs = LocalPrefs.Load(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AgentSkillsSyncTests", System.Guid.NewGuid().ToString("N")));
            prefs.LastSyncedLockHash = lockHash;
            return prefs;
        }

        [Test]
        public void StartupMessage_NeverSynced_SaysSo()
        {
            var message = SyncText.StartupMessage(new SyncStatus("lock", new[] { "tdd" }), PrefsSyncedAt(null));

            Assert.That(message, Does.StartWith("This project's agent skills have not been synced on this machine yet."));
        }

        [Test]
        public void StartupMessage_LockChanged_NamesTheLockfile()
        {
            var message = SyncText.StartupMessage(new SyncStatus("new", new string[0]), PrefsSyncedAt("old"));

            Assert.That(message, Does.StartWith("skills-lock.json changed since the last sync."));
        }

        [Test]
        public void StartupMessage_SkillsMissing_NamesThem()
        {
            var message = SyncText.StartupMessage(new SyncStatus("lock", new[] { "tdd", "code-review" }), PrefsSyncedAt("lock"));

            Assert.That(message, Does.StartWith("Some locked skills are missing: code-review, tdd.\n\n"));
            Assert.That(message, Does.Not.Contain("removed"));
        }

        [Test]
        public void StartupMessage_OnlySkillsWaitingForRemoval_DoesNotCallThemMissing()
        {
            var message = SyncText.StartupMessage(
                new SyncStatus("lock", new[] { "ui-uitk" }, toRemove: new[] { "ui-uitk" }), PrefsSyncedAt("lock"));

            Assert.That(message, Does.StartWith("Some installed skills are no longer selected and will be removed on the next sync: ui-uitk.\n\n"));
            Assert.That(message, Does.Not.Contain("missing"));
        }

        /// <summary>Prefs last synced at <paramref name="lockHash"/> that have seen these skills.</summary>
        static LocalPrefs PrefsSeen(string lockHash, params string[] seen)
        {
            var prefs = PrefsSyncedAt(lockHash);
            prefs.SeenSkills = seen;
            return prefs;
        }

        [Test]
        public void StartupMessage_OnlyANewOptionalSkill_NamesItAndDoesNotCallTheProjectOutOfSync()
        {
            var message = SyncText.StartupMessage(
                new SyncStatus("lock", new string[0], optionalSkills: new[] { "ui-uitk", "audio" }), PrefsSeen("lock", "audio"));

            Assert.That(message, Is.EqualTo(
                "A new optional skill was locked: ui-uitk. This machine does not install it unless you select it.\n\n" +
                "Open the sync window to review it? If you choose Not Now, you won't be told about it again. " +
                "You can turn this notice off in the sync window."));
        }

        [Test]
        public void StartupMessage_SeveralNewOptionalSkills_NamesThemAll()
        {
            var message = SyncText.StartupMessage(
                new SyncStatus("lock", new string[0], optionalSkills: new[] { "ui-uitk", "audio" }), PrefsSeen("lock"));

            Assert.That(message, Is.EqualTo(
                "New optional skills were locked: audio, ui-uitk. This machine does not install them unless you select them.\n\n" +
                "Open the sync window to review them? If you choose Not Now, you won't be told about them again. " +
                "You can turn this notice off in the sync window."));
        }

        [Test]
        public void StartupMessage_OutOfSyncAndANewOptionalSkill_SaysBothInOneMessage()
        {
            var message = SyncText.StartupMessage(
                new SyncStatus("lock", new[] { "tdd" }, optionalSkills: new[] { "ui-uitk" }), PrefsSeen("lock"));

            Assert.That(message, Is.EqualTo(
                "Some locked skills are missing: tdd. " +
                "A new optional skill was locked: ui-uitk. This machine does not install it unless you select it.\n\n" +
                "Open the sync window to review and sync them? If you choose Not Now, " +
                "you won't be asked again until something changes."));
        }

        [Test]
        public void StartupMessage_OutOfSyncAlreadyDeclinedAndANewOptionalSkill_NamesOnlyTheNewSkill()
        {
            var status = new SyncStatus("lock", new[] { "tdd" }, optionalSkills: new[] { "ui-uitk" });
            var prefs = PrefsSeen("lock");
            prefs.DeclinedState = status.Fingerprint;

            Assert.That(SyncText.StartupMessage(status, prefs), Does.StartWith("A new optional skill was locked: ui-uitk."));
        }

        [Test]
        public void StartupMessage_NewSkillNoticeTurnedOff_DoesNotMentionNewSkills()
        {
            var prefs = PrefsSeen("lock");
            prefs.NotifyNewSkills = false;

            var message = SyncText.StartupMessage(new SyncStatus("lock", new[] { "tdd" }, optionalSkills: new[] { "ui-uitk" }), prefs);

            Assert.That(message, Does.StartWith("Some locked skills are missing: tdd.\n\n"));
            Assert.That(message, Does.Not.Contain("ui-uitk"));
        }

        [Test]
        public void StartupMessage_SkillsMissingAndWaitingForRemoval_NamesEachKind()
        {
            var message = SyncText.StartupMessage(
                new SyncStatus("lock", new[] { "tdd", "ui-uitk" }, toRemove: new[] { "ui-uitk" }), PrefsSyncedAt("lock"));

            Assert.That(message, Does.StartWith("Some locked skills are missing: tdd. " +
                "Some installed skills are no longer selected and will be removed on the next sync: ui-uitk.\n\n"));
        }
    }
}
