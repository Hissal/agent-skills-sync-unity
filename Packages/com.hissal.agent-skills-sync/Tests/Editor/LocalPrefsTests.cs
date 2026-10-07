using System;
using System.IO;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class LocalPrefsTests
    {
        string _project;

        string PrefsPath => Path.Combine(_project, "UserSettings", "AgentSkillsSync.json");

        [SetUp]
        public void SetUp()
        {
            _project = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_project);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_project)) Directory.Delete(_project, recursive: true);
        }

        [Test]
        public void Load_NoPrefsFile_ReturnsEmptyPrefs()
        {
            var prefs = LocalPrefs.Load(_project);

            Assert.That(prefs.LastSyncedLockHash, Is.Null);
            Assert.That(prefs.DeclinedState, Is.Null);
        }

        [Test]
        public void Save_ThenLoad_RoundTripsTheValues()
        {
            var prefs = LocalPrefs.Load(_project);
            prefs.LastSyncedLockHash = "abc123";
            prefs.DeclinedState = "def \"quoted\" \\ 456";
            prefs.Save();

            var loaded = LocalPrefs.Load(_project);

            Assert.That(File.Exists(PrefsPath), Is.True);
            Assert.That(loaded.LastSyncedLockHash, Is.EqualTo("abc123"));
            Assert.That(loaded.DeclinedState, Is.EqualTo("def \"quoted\" \\ 456"));
        }

        [Test]
        public void SyncedSources_Unset_IsEmpty()
        {
            Assert.That(LocalPrefs.Load(_project).SyncedSources, Is.Empty);
        }

        [Test]
        public void SyncedSources_SaveThenLoad_RoundTrips()
        {
            var prefs = LocalPrefs.Load(_project);
            prefs.SyncedSources = new[] { "owner/skills", "other/unity-skills" };
            prefs.Save();

            Assert.That(LocalPrefs.Load(_project).SyncedSources, Is.EqualTo(new[] { "owner/skills", "other/unity-skills" }));
        }

        [Test]
        public void Save_ValueSetToNull_RemovesIt()
        {
            var prefs = LocalPrefs.Load(_project);
            prefs.LastSyncedLockHash = "abc";
            prefs.Save();

            prefs.LastSyncedLockHash = null;
            prefs.Save();

            Assert.That(LocalPrefs.Load(_project).LastSyncedLockHash, Is.Null);
            Assert.That(File.ReadAllText(PrefsPath), Does.Not.Contain("lastSyncedLockHash"));
        }

        [Test]
        public void Save_KeepsMembersThisVersionDoesNotKnow()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefsPath));
            File.WriteAllText(PrefsPath, @"{ ""version"": 1, ""futureSetting"": { ""list"": [""a"", 2, true, null] } }");

            var prefs = LocalPrefs.Load(_project);
            prefs.LastSyncedLockHash = "abc";
            prefs.Save();

            var text = File.ReadAllText(PrefsPath);
            Assert.That(text, Does.Contain("\"futureSetting\""));
            Assert.That(text, Does.Contain("[\"a\", 2, true, null]").Or.Contain("[\"a\",2,true,null]"));
            Assert.That(LocalPrefs.Load(_project).LastSyncedLockHash, Is.EqualTo("abc"), "the written file reads back as valid JSON");
        }

        [Test]
        public void Load_CorruptFile_ReturnsEmptyPrefs()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefsPath));
            File.WriteAllText(PrefsPath, "{ not json");

            var prefs = LocalPrefs.Load(_project);

            Assert.That(prefs.LastSyncedLockHash, Is.Null);
        }
    }
}
