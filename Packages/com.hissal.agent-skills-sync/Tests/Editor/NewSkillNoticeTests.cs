using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>The startup check's notice of a newly locked skill this machine does not install.</summary>
    public class NewSkillNoticeTests
    {
        static readonly string FixturesRoot =
            Path.GetFullPath("Packages/com.hissal.agent-skills-sync/Tests/Editor/Fixtures~/SkillFolderHash");

        static string Entry(string name, string hash = FakeGitHub.MinimalHash) =>
            $@"    ""{name}"": {{ ""source"": ""owner/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/{name}/SKILL.md"", ""computedHash"": ""{hash}"" }}";

        string _project;

        [SetUp]
        public void SetUp()
        {
            _project = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_project);
            Lock("tdd");
        }

        [TearDown]
        public void TearDown() => TempDirectory.Delete(_project);

        void WriteLock(string json) => File.WriteAllText(Path.Combine(_project, Lockfile.FileName), json);

        /// <summary>Locks exactly these skills, each at the `minimal` fixture's hash.</summary>
        void Lock(params string[] names) =>
            WriteLock("{\n  \"version\": 1,\n  \"skills\": {\n" + string.Join(",\n", names.Select(n => Entry(n))) + "\n  }\n}");

        /// <summary>The project selects only tdd by default: every other skill is optional. The contributor's choices are on top.</summary>
        static MachineChoices Choices(string[] optedOut = null, string[] optedIn = null) =>
            new MachineChoices(skills: new SkillSelection(optedOut, optedIn, OnlyTdd));

        static readonly SkillGroups OnlyTdd =
            new SkillGroups(new[] { new SkillGroup("recommended", true, new[] { "tdd" }) }, ungroupedSelectedByDefault: false);

        LocalPrefs Prefs => LocalPrefs.Load(_project);

        SyncStatus Status(MachineChoices choices = null) => SyncStatus.Read(_project, choices ?? Choices());

        bool ShouldNotify(MachineChoices choices = null) => StartupCheck.ShouldNotify(Status(choices), Prefs);

        IReadOnlyList<string> NewSkills(MachineChoices choices = null) => StartupCheck.NewSkills(Status(choices), Prefs);

        /// <summary>Installs what the choices select and records the sync, as the sync service does.</summary>
        void Sync(MachineChoices choices = null)
        {
            choices = choices ?? Choices();
            var lockfile = Lockfile.Load(_project);
            var plan = InstallPlanner.Plan(lockfile, ProjectScanner.Scan(_project, FolderLayout.Default), choices);
            new PlanExecutor().Execute(_project, plan,
                choices.Skills.SelectedIn(lockfile).ToDictionary(s => s.Name, _ => Path.Combine(FixturesRoot, "minimal")));
            var prefs = Prefs;
            StartupCheck.RecordSynced(prefs, Status(choices));
            prefs.Save();
        }

        void Decline(MachineChoices choices = null)
        {
            var prefs = Prefs;
            StartupCheck.RecordDeclined(prefs, Status(choices));
            prefs.Save();
        }

        [Test]
        public void ShouldNotify_UnselectedSkillLockedSinceTheLastSync_Notifies()
        {
            Sync();

            Lock("tdd", "ui-uitk");

            Assert.That(NewSkills(), Is.EqualTo(new[] { "ui-uitk" }));
            Assert.That(ShouldNotify(), Is.True);
            Assert.That(StartupCheck.OutOfSync(Status(), Prefs), Is.False, "an unselected skill never makes the project out of sync");
        }

        [Test]
        public void ShouldNotify_UnselectedSkillAlreadySeen_IsQuiet()
        {
            Lock("tdd", "ui-uitk");
            Sync();

            Assert.That(Prefs.SeenSkills, Is.EqualTo(new[] { "tdd", "ui-uitk" }));
            Assert.That(NewSkills(), Is.Empty);
            Assert.That(ShouldNotify(), Is.False);
        }

        [Test]
        public void NewSkills_NewlyLockedSelectedSkill_IsLeftToTheOutOfSyncNotice()
        {
            Sync();

            Lock("tdd", "ui-uitk");
            var optedIn = Choices(optedIn: new[] { "ui-uitk" });

            Assert.That(NewSkills(optedIn), Is.Empty);
            Assert.That(StartupCheck.OutOfSync(Status(optedIn), Prefs), Is.True);
        }

        [Test]
        public void ShouldNotify_NoticeTurnedOff_IsQuiet()
        {
            Sync();
            var prefs = Prefs;
            prefs.NotifyNewSkills = false;
            prefs.Save();

            Lock("tdd", "ui-uitk");

            Assert.That(NewSkills(), Is.Empty);
            Assert.That(ShouldNotify(), Is.False);
        }

        [Test]
        public void ShouldNotify_Dismissed_IsQuietUntilAnotherNewSkillIsLocked()
        {
            Sync();
            Lock("tdd", "ui-uitk");
            Decline();

            Assert.That(Prefs.SeenSkills, Does.Contain("ui-uitk"));
            Assert.That(ShouldNotify(), Is.False);

            Lock("tdd", "ui-uitk", "audio-mixing");

            Assert.That(NewSkills(), Is.EqualTo(new[] { "audio-mixing" }));
            Assert.That(ShouldNotify(), Is.True);
        }

        [Test]
        public void Seed_PrefsWithoutSeenSkills_SeedsEveryLockedSkillWithoutNotifying()
        {
            Lock("tdd", "ui-uitk");
            Sync();
            var prefs = Prefs;
            prefs.SeenSkills = null;
            prefs.Save();
            Assert.That(File.ReadAllText(LocalPrefs.PathFor(_project)), Does.Not.Contain("seenSkills"));

            Assert.That(ShouldNotify(), Is.False, "prefs written before seen skills were recorded have no new skills");

            prefs = Prefs;
            Assert.That(SeenSkills.Seed(prefs, Status().LockedSkills), Is.True);
            prefs.Save();

            Assert.That(Prefs.SeenSkills, Is.EqualTo(new[] { "tdd", "ui-uitk" }));
            Assert.That(ShouldNotify(), Is.False);
            Assert.That(SeenSkills.Seed(Prefs, new[] { "another" }), Is.False, "seeded prefs are left alone");

            Lock("tdd", "ui-uitk", "audio-mixing");
            Assert.That(NewSkills(), Is.EqualTo(new[] { "audio-mixing" }));
        }

        [Test]
        public void Seed_LockWithNoSkills_StillCountsAsSeeded()
        {
            var prefs = Prefs;
            Assert.That(SeenSkills.Seed(prefs, new string[0]), Is.True);
            prefs.Save();

            Assert.That(Prefs.SeenSkills, Is.Empty);
            Assert.That(SeenSkills.New(Prefs, new[] { "ui-uitk" }), Is.EqualTo(new[] { "ui-uitk" }));
        }

        [Test]
        public void Seed_UnusableLockfile_DoesNotSeed()
        {
            WriteLock("{ broken");
            var prefs = Prefs;

            Assert.That(Status().LockedSkills, Is.Null);
            Assert.That(SeenSkills.Seed(prefs, Status().LockedSkills), Is.False);
            Assert.That(prefs.SeenSkills, Is.Null);
        }

        [Test]
        public void RecordSynced_FirstEverSync_SeedsEveryLockedSkillAsSeen()
        {
            Lock("tdd", "ui-uitk");
            Assert.That(Prefs.SeenSkills, Is.Null);

            Sync();

            Assert.That(Prefs.SeenSkills, Is.EqualTo(new[] { "tdd", "ui-uitk" }));
            Assert.That(ShouldNotify(), Is.False);
        }

        // An opt-out is the contributor's answer to the skill, so there is nothing left to tell them about it.
        [Test]
        public void NewSkills_SkillTheContributorOptedOutOf_IsNeverNew()
        {
            Sync();

            Lock("tdd", "ui-uitk");
            var optedOut = Choices(optedOut: new[] { "ui-uitk" });

            Assert.That(Prefs.SeenSkills, Does.Not.Contain("ui-uitk"));
            Assert.That(NewSkills(optedOut), Is.Empty);
            Assert.That(ShouldNotify(optedOut), Is.False);
        }

        [Test]
        public void Mark_OptedInSkill_IsSeenOnceItFollowsItsDefaultAgain()
        {
            Sync();
            Lock("tdd", "ui-uitk");

            var prefs = Prefs;
            SeenSkills.Mark(prefs, new[] { "ui-uitk" });
            prefs.Save();

            Assert.That(NewSkills(), Is.Empty);
        }

        [Test]
        public void Mark_PrefsNotSeededYet_LeavesThemUnseeded()
        {
            var prefs = Prefs;

            SeenSkills.Mark(prefs, new[] { "ui-uitk" });

            Assert.That(prefs.SeenSkills, Is.Null, "seeding with only this name would make every other locked skill new");
        }

        [Test]
        public void ShouldNotify_SelectedSkillOutOfSyncAndNewSkillLocked_DeclineQuietsEachPartUntilItsOwnStateChanges()
        {
            Sync();
            DirectoryLink.Remove(Path.Combine(_project, ".claude/skills/tdd"));
            Lock("tdd", "ui-uitk");

            Assert.That(StartupCheck.OutOfSync(Status(), Prefs), Is.True);
            Assert.That(NewSkills(), Is.EqualTo(new[] { "ui-uitk" }));

            Decline();
            Assert.That(ShouldNotify(), Is.False);

            Lock("tdd", "ui-uitk", "audio-mixing");
            Assert.That(StartupCheck.OutOfSync(Status(), Prefs), Is.False, "the declined out-of-sync state did not change");
            Assert.That(NewSkills(), Is.EqualTo(new[] { "audio-mixing" }));
            Assert.That(ShouldNotify(), Is.True);

            Decline();
            WriteLock("{\n  \"version\": 1,\n  \"skills\": {\n" + Entry("tdd", "changed-2") + ",\n" + Entry("ui-uitk") + ",\n" + Entry("audio-mixing") + "\n  }\n}");
            Assert.That(StartupCheck.OutOfSync(Status(), Prefs), Is.True);
            Assert.That(NewSkills(), Is.Empty);
        }

        [Test]
        public void ShouldNotify_NoticeTurnedOffAndDeclined_LeavesTheNewSkillUnseen()
        {
            Sync();
            var prefs = Prefs;
            prefs.NotifyNewSkills = false;
            prefs.Save();
            DirectoryLink.Remove(Path.Combine(_project, ".claude/skills/tdd"));
            Lock("tdd", "ui-uitk");

            Decline();

            Assert.That(Prefs.SeenSkills, Does.Not.Contain("ui-uitk"), "the dialog did not name it");
        }

        [Test]
        public void ShouldNotify_NoFolderSelected_NeverNotifiesAboutNewSkills()
        {
            Sync();
            Lock("tdd", "ui-uitk");

            var status = SyncStatus.Read(_project, new MachineChoices(selected: new SkillsFolder[0], skills: Choices().Skills));

            Assert.That(StartupCheck.NewSkills(status, Prefs), Is.Empty);
            Assert.That(StartupCheck.ShouldNotify(status, Prefs), Is.False);
        }
    }
}
