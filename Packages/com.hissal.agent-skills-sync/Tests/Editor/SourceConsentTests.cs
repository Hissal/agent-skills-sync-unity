using System;
using System.IO;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class SourceConsentTests
    {
        string _project;

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

        static Lockfile Lock(params (string name, string source)[] skills)
        {
            var locked = new LockedSkill[skills.Length];
            for (var i = 0; i < skills.Length; i++)
                locked[i] = new LockedSkill(skills[i].name, skills[i].source, "github", null, "hash");
            return new Lockfile(locked);
        }

        LocalPrefs Prefs => LocalPrefs.Load(_project);

        [Test]
        public void NewSources_FirstSync_EverySourceIsNew()
        {
            var lockfile = Lock(("tdd", "owner/skills"), ("review", "owner/skills"), ("unity", "other/unity-skills"));

            Assert.That(SourceConsent.NewSources(lockfile, Prefs), Is.EqualTo(new[] { "other/unity-skills", "owner/skills" }));
        }

        void RecordSynced(Lockfile lockfile)
        {
            var prefs = Prefs;
            SourceConsent.RecordSynced(prefs, lockfile);
            prefs.Save();
        }

        [Test]
        public void NewSources_LaterSync_FlagsOnlySourcesNotSeenBefore()
        {
            RecordSynced(Lock(("tdd", "owner/skills")));

            var lockfile = Lock(("tdd", "owner/skills"), ("review", "owner/skills"), ("unity", "other/unity-skills"));

            Assert.That(SourceConsent.NewSources(lockfile, Prefs), Is.EqualTo(new[] { "other/unity-skills" }));
        }

        [Test]
        public void NewSources_SourceDroppedFromLockThenReadded_IsNotNewAgain()
        {
            RecordSynced(Lock(("tdd", "owner/skills"), ("unity", "other/unity-skills")));
            RecordSynced(Lock(("tdd", "owner/skills")));

            var lockfile = Lock(("tdd", "owner/skills"), ("unity", "other/unity-skills"));

            Assert.That(SourceConsent.NewSources(lockfile, Prefs), Is.Empty);
        }

        [Test]
        public void NewSources_SameRepoDifferentCase_IsNotNew()
        {
            RecordSynced(Lock(("tdd", "Owner/Skills")));

            Assert.That(SourceConsent.NewSources(Lock(("tdd", "owner/skills")), Prefs), Is.Empty);
        }

        [Test]
        public void NewSources_NothingRecordedUntilRecordSynced()
        {
            var lockfile = Lock(("tdd", "owner/skills"));
            var prefs = Prefs;
            prefs.LastSyncedLockHash = "abc";
            prefs.Save();

            Assert.That(SourceConsent.NewSources(lockfile, Prefs), Is.EqualTo(new[] { "owner/skills" }));
        }

        [Test]
        public void Unconfirmed_NewSourceNotConfirmed_BlocksSync()
        {
            RecordSynced(Lock(("tdd", "owner/skills")));
            var lockfile = Lock(("tdd", "owner/skills"), ("unity", "other/unity-skills"), ("x", "third/x"));

            Assert.That(SourceConsent.Unconfirmed(lockfile, Prefs, new[] { "third/x" }), Is.EqualTo(new[] { "other/unity-skills" }));
        }

        [Test]
        public void Unconfirmed_EveryNewSourceConfirmed_IsEmpty()
        {
            var lockfile = Lock(("tdd", "owner/skills"), ("unity", "other/unity-skills"));

            Assert.That(SourceConsent.Unconfirmed(lockfile, Prefs, new[] { "owner/skills", "other/unity-skills" }), Is.Empty);
        }

        [Test]
        public void Unconfirmed_NoNewSources_IsEmptyWithoutConfirmation()
        {
            var lockfile = Lock(("tdd", "owner/skills"));
            RecordSynced(lockfile);

            Assert.That(SourceConsent.Unconfirmed(lockfile, Prefs, Array.Empty<string>()), Is.Empty);
        }
    }
}
