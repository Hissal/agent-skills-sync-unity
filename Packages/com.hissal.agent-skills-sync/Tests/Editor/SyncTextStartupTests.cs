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
