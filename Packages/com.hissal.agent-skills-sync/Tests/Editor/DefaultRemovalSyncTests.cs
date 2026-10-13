using System;
using System.IO;
using Hissal.AgentSkillsSync.Editor;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>Syncing in a temp project while a skill's default changes under a machine that installed it.</summary>
    public class DefaultRemovalSyncTests
    {
        sealed class FixtureFetcher : ISkillFetcher
        {
            static readonly string Minimal =
                Path.GetFullPath("Packages/com.hissal.agent-skills-sync/Tests/Editor/Fixtures~/SkillFolderHash/minimal");

            readonly string _root;

            public FixtureFetcher(string root) => _root = root;

            public string Fetch(LockedSkill skill)
            {
                var folder = Path.Combine(_root, "fetched", skill.Name, Guid.NewGuid().ToString("N"));
                Paths.CopyDirectory(Minimal, folder);
                return folder;
            }
        }

        const string Lock = @"{
  ""version"": 1,
  ""skills"": {
    ""tdd"": { ""source"": ""owner/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/tdd/SKILL.md"", ""computedHash"": """ + FakeGitHub.MinimalHash + @""" },
    ""ui-uitk"": { ""source"": ""owner/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/ui-uitk/SKILL.md"", ""computedHash"": """ + FakeGitHub.MinimalHash + @""" }
  }
}";

        static readonly SkillGroups Recommended = new SkillGroups(new[] { new SkillGroup("recommended", true, new[] { "tdd", "ui-uitk" }) });

        /// <summary>The project moved ui-uitk out of every group that selects by default.</summary>
        static readonly SkillGroups UiUitkOptional = new SkillGroups(new[]
        {
            new SkillGroup("recommended", true, new[] { "tdd" }),
            new SkillGroup("art", false, new[] { "ui-uitk" }),
        });

        string _root;
        string _project;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            _project = Path.Combine(_root, "project");
            Directory.CreateDirectory(_project);
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), Lock);
        }

        [TearDown]
        public void TearDown() => TempDirectory.Delete(_root);

        /// <summary>Syncs with the choices stored in this machine's prefs over the given group defaults.</summary>
        SyncSummary Sync(SkillGroups groups)
        {
            var layout = FolderLayout.Default;
            var skills = SkillSelection.From(LocalPrefs.Load(_project), groups);
            return new SkillSync(_project, new FixtureFetcher(_root), new MachineChoices(layout, layout.Folders, skills: skills)).Run();
        }

        void Choose(string skill, bool selected, SkillGroups groups)
        {
            var prefs = LocalPrefs.Load(_project);
            SkillSelection.Set(prefs, new[] { skill }, selected, groups);
            prefs.Save();
        }

        bool Installed(string skill) => File.Exists(Path.Combine(_project, ".agents", "skills", skill, "SKILL.md"));

        [Test]
        public void Run_SkillNoLongerSelectedByDefault_IsRemovedAndNamedInTheSummary()
        {
            Sync(Recommended);

            var summary = Sync(UiUitkOptional);

            Assert.That(Installed("ui-uitk"), Is.False);
            Assert.That(Installed("tdd"), Is.True);
            Assert.That(summary.Removed, Is.EqualTo(new[] { "ui-uitk" }));
            Assert.That(summary.NoLongerSelectedByDefault, Is.EqualTo(new[] { "ui-uitk" }));
            Assert.That(SyncText.Describe(summary), Does.Contain("no longer selected by default (1): ui-uitk"));
        }

        [Test]
        public void Run_UngroupedDefaultTurnedOff_RemovesAndNamesEveryUngroupedSkill()
        {
            Sync(SkillGroups.None);

            var summary = Sync(new SkillGroups(ungroupedSelectedByDefault: false));

            Assert.That(summary.NoLongerSelectedByDefault, Is.EquivalentTo(new[] { "tdd", "ui-uitk" }));
            Assert.That(Installed("tdd"), Is.False);
        }

        [Test]
        public void Run_DefaultChangesForASkillTheContributorOptedInTo_KeepsIt()
        {
            Choose("ui-uitk", true, UiUitkOptional);
            Sync(Recommended);

            var summary = Sync(UiUitkOptional);

            Assert.That(Installed("ui-uitk"), Is.True);
            Assert.That(summary.NothingChanged, Is.True);
            Assert.That(summary.NoLongerSelectedByDefault, Is.Empty);
        }

        [Test]
        public void Run_ContributorOptsOut_IsRemovedButNotNamedAsADefaultChange()
        {
            Sync(Recommended);
            Choose("ui-uitk", false, Recommended);

            var summary = Sync(Recommended);

            Assert.That(summary.Removed, Is.EqualTo(new[] { "ui-uitk" }));
            Assert.That(summary.NoLongerSelectedByDefault, Is.Empty);
            Assert.That(SyncText.Describe(summary), Does.Not.Contain("selected by default"));
        }

        [Test]
        public void Run_ContributorDropsTheirOptIn_IsRemovedButNotNamedAsADefaultChange()
        {
            Choose("ui-uitk", true, UiUitkOptional);
            Sync(UiUitkOptional);
            Assert.That(Installed("ui-uitk"), Is.True);
            Choose("ui-uitk", false, UiUitkOptional);
            Assert.That(LocalPrefs.Load(_project).OptedOutSkills, Is.Empty, "a choice equal to the default is not stored");

            var summary = Sync(UiUitkOptional);

            Assert.That(summary.Removed, Is.EqualTo(new[] { "ui-uitk" }));
            Assert.That(summary.NoLongerSelectedByDefault, Is.Empty);
            Assert.That(SyncText.Describe(summary), Does.Not.Contain("selected by default"));
        }

        [Test]
        public void Run_NoFolderSelectedForASkillStillSelectedByDefault_IsNotNamedAsADefaultChange()
        {
            Sync(Recommended);

            var layout = FolderLayout.Default;
            var skills = SkillSelection.From(LocalPrefs.Load(_project), Recommended);
            var summary = new SkillSync(_project, new FixtureFetcher(_root), new MachineChoices(layout, new SkillsFolder[0], skills: skills)).Run();

            Assert.That(summary.Removed, Is.EquivalentTo(new[] { "tdd", "ui-uitk" }));
            Assert.That(summary.NoLongerSelectedByDefault, Is.Empty);
        }

        [Test]
        public void Run_DefaultChangedAfterASyncByAVersionThatRecordedNoDefaults_StillNamesTheRemoval()
        {
            Sync(Recommended);
            var prefs = LocalPrefs.Load(_project);
            prefs.LastSyncedDefaultSkills = null;
            prefs.Save();

            var summary = Sync(UiUitkOptional);

            Assert.That(summary.NoLongerSelectedByDefault, Is.EqualTo(new[] { "ui-uitk" }));
        }

        [Test]
        public void Run_SkillNeverInstalledAndNotSelectedByDefault_NamesNothing()
        {
            var summary = Sync(UiUitkOptional);

            Assert.That(summary.Installed, Is.EqualTo(new[] { "tdd" }));
            Assert.That(summary.NoLongerSelectedByDefault, Is.Empty);
        }

        [Test]
        public void UnselectedLabel_TellsADefaultFromAnOptOutAndWhetherSyncRemovesACopy()
        {
            Sync(Recommended);
            InstallPlan Plan(SkillSelection skills) =>
                new SkillSync(_project, null, new MachineChoices(FolderLayout.Default, FolderLayout.Default.Folders, skills: skills)).Plan();
            var byDefault = new SkillSelection(groups: UiUitkOptional);
            var optedOut = new SkillSelection(optedOut: new[] { "ui-uitk" });

            Assert.That(SyncText.UnselectedLabel(Plan(byDefault), "ui-uitk", byDefault), Is.EqualTo("not selected by default (removed on Sync)"));
            Assert.That(SyncText.UnselectedLabel(Plan(optedOut), "ui-uitk", optedOut), Is.EqualTo("opted out (removed on Sync)"));

            Sync(UiUitkOptional);

            Assert.That(SyncText.UnselectedLabel(Plan(byDefault), "ui-uitk", byDefault), Is.EqualTo("not installed (not selected by default)"));
            Assert.That(SyncText.UnselectedLabel(Plan(optedOut), "ui-uitk", optedOut), Is.EqualTo("not installed (opted out)"));
        }
    }
}
