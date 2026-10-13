using System;
using System.IO;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class LocalPrefsTests
    {
        [Test]
        public void ManagedSkills_UnsafeRecordedName_IsIgnored()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefsPath));
            File.WriteAllText(PrefsPath,
                @"{ ""managedSkills"": { "".agents/skills"": [""../../Assets"", ""tdd"", """", ""bad:name""] } }");

            Assert.That(LocalPrefs.Load(_project).ManagedSkills[".agents/skills"], Is.EqualTo(new[] { "tdd" }));
        }

        string _project;

        string PrefsPath => Path.Combine(_project, "UserSettings", "AgentSkillsSync.json");

        void WritePrefs(string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefsPath));
            File.WriteAllText(PrefsPath, json);
        }

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
        public void SeenSkills_Unset_IsNull()
        {
            Assert.That(LocalPrefs.Load(_project).SeenSkills, Is.Null);
        }

        [Test]
        public void SeenSkills_SaveThenLoad_RoundTripsSortedAndKeepsAnEmptyList()
        {
            var prefs = LocalPrefs.Load(_project);
            prefs.SeenSkills = new[] { "tdd", "code-review", "tdd" };
            prefs.Save();
            Assert.That(LocalPrefs.Load(_project).SeenSkills, Is.EqualTo(new[] { "code-review", "tdd" }));

            prefs.SeenSkills = new string[0];
            prefs.Save();
            Assert.That(LocalPrefs.Load(_project).SeenSkills, Is.Empty);
        }

        [Test]
        public void LastSyncedDefaultSkills_Unset_IsNull()
        {
            Assert.That(LocalPrefs.Load(_project).LastSyncedDefaultSkills, Is.Null);
        }

        [Test]
        public void LastSyncedDefaultSkills_SaveThenLoad_RoundTripsSortedAndKeepsAnEmptyList()
        {
            var prefs = LocalPrefs.Load(_project);
            prefs.LastSyncedDefaultSkills = new[] { "tdd", "code-review", "tdd" };
            prefs.Save();
            Assert.That(LocalPrefs.Load(_project).LastSyncedDefaultSkills, Is.EqualTo(new[] { "code-review", "tdd" }));

            prefs.LastSyncedDefaultSkills = new string[0];
            prefs.Save();
            Assert.That(LocalPrefs.Load(_project).LastSyncedDefaultSkills, Is.Empty);
        }

        [TestCase("\"tdd\"")]
        [TestCase("3")]
        [TestCase("true")]
        [TestCase("null")]
        [TestCase("{ \"tdd\": true }")]
        public void SkillNameLists_WrongType_ReadAsUnset(string value)
        {
            WritePrefs($@"{{ ""optedOutSkills"": {value}, ""optedInSkills"": {value}, ""seenSkills"": {value}, ""lastSyncedDefaultSkills"": {value} }}");

            var prefs = LocalPrefs.Load(_project);

            Assert.That(prefs.OptedOutSkills, Is.Empty);
            Assert.That(prefs.OptedInSkills, Is.Empty);
            Assert.That(prefs.SeenSkills, Is.Null);
            Assert.That(prefs.LastSyncedDefaultSkills, Is.Null);
        }

        [TestCase("optedOutSkills")]
        [TestCase("optedInSkills")]
        [TestCase("seenSkills")]
        [TestCase("lastSyncedDefaultSkills")]
        public void SkillNameLists_ItemsThatAreNotSafeSkillNames_AreIgnored(string member)
        {
            WritePrefs($@"{{ ""{member}"": [""tdd"", 3, null, true, [""art""], {{}}, """", ""../../Assets"", ""bad:name"", ""code-review""] }}");

            var prefs = LocalPrefs.Load(_project);
            var names = member == "optedOutSkills" ? prefs.OptedOutSkills
                : member == "optedInSkills" ? prefs.OptedInSkills
                : member == "seenSkills" ? prefs.SeenSkills
                : prefs.LastSyncedDefaultSkills;

            Assert.That(names, Is.EqualTo(new[] { "code-review", "tdd" }));
        }

        [TestCase("\"off\"")]
        [TestCase("0")]
        [TestCase("null")]
        [TestCase("[false]")]
        public void NotifyNewSkills_WrongType_IsOn(string value)
        {
            WritePrefs($@"{{ ""notifyNewSkills"": {value} }}");

            Assert.That(LocalPrefs.Load(_project).NotifyNewSkills, Is.True);
        }

        [Test]
        public void NotifyNewSkills_Unset_IsOn()
        {
            Assert.That(LocalPrefs.Load(_project).NotifyNewSkills, Is.True);
        }

        [Test]
        public void NotifyNewSkills_TurnedOffThenOn_RoundTrips()
        {
            var prefs = LocalPrefs.Load(_project);
            prefs.NotifyNewSkills = false;
            prefs.Save();
            Assert.That(LocalPrefs.Load(_project).NotifyNewSkills, Is.False);
            Assert.That(File.ReadAllText(PrefsPath), Does.Contain("\"notifyNewSkills\": false"));

            prefs = LocalPrefs.Load(_project);
            prefs.NotifyNewSkills = true;
            prefs.Save();
            Assert.That(LocalPrefs.Load(_project).NotifyNewSkills, Is.True);
            Assert.That(File.ReadAllText(PrefsPath), Does.Not.Contain("notifyNewSkills"));
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

        [Test]
        public void ManagedSkillsFor_PrefsWithoutARecordedRoot_DescribeTheProjectFolder()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefsPath));
            File.WriteAllText(PrefsPath, @"{ ""version"": 1, ""managedSkills"": { "".agents/skills"": [""tdd""] } }");
            var prefs = LocalPrefs.Load(_project);

            Assert.That(prefs.ManagedSkillsFor(_project)[".agents/skills"], Is.EqualTo(new[] { "tdd" }));
            Assert.That(prefs.ManagedSkillsFor(Path.GetDirectoryName(_project)), Is.Null);
        }
    }
}
