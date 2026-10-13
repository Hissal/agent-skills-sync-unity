using System;
using System.IO;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>Per-skill opt-out and opt-in choices kept in local prefs.</summary>
    public class SkillSelectionTests
    {
        string _project;

        string PrefsPath => LocalPrefs.PathFor(_project);

        [SetUp]
        public void SetUp()
        {
            _project = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_project);
        }

        [TearDown]
        public void TearDown() => TempDirectory.Delete(_project);

        SkillSelection Reloaded(Func<string, bool> selectedByDefault = null) =>
            SkillSelection.From(LocalPrefs.Load(_project), selectedByDefault);

        void Set(string skill, bool selected, bool selectedByDefault = true)
        {
            var prefs = LocalPrefs.Load(_project);
            SkillSelection.Set(prefs, skill, selected, selectedByDefault);
            prefs.Save();
        }

        [Test]
        public void From_NoPrefs_SelectsEverySkill()
        {
            Assert.That(Reloaded().IsSelected("tdd"), Is.True);
            Assert.That(SkillSelection.All.IsSelected("tdd"), Is.True);
            Assert.That(LocalPrefs.Load(_project).OptedOutSkills, Is.Empty);
            Assert.That(LocalPrefs.Load(_project).OptedInSkills, Is.Empty);
        }

        [Test]
        public void Prefs_OptedOutAndOptedInNames_RoundTripSorted()
        {
            var prefs = LocalPrefs.Load(_project);
            prefs.OptedOutSkills = new[] { "tdd", "code-review" };
            prefs.OptedInSkills = new[] { "ui-uitk", "art" };
            prefs.Save();

            var loaded = LocalPrefs.Load(_project);
            Assert.That(loaded.OptedOutSkills, Is.EqualTo(new[] { "code-review", "tdd" }));
            Assert.That(loaded.OptedInSkills, Is.EqualTo(new[] { "art", "ui-uitk" }));
        }

        [Test]
        public void Set_OptOut_IsKeptForThatSkillOnly()
        {
            Set("tdd", false);

            Assert.That(Reloaded().IsSelected("tdd"), Is.False);
            Assert.That(Reloaded().IsSelected("code-review"), Is.True);
            Assert.That(LocalPrefs.Load(_project).OptedOutSkills, Is.EqualTo(new[] { "tdd" }));
        }

        [Test]
        public void Set_OptBackIn_StoresNoChoiceWhileTheDefaultIsSelected()
        {
            Set("tdd", false);

            Set("tdd", true);

            Assert.That(Reloaded().IsSelected("tdd"), Is.True);
            Assert.That(File.ReadAllText(PrefsPath), Does.Not.Contain("optedOutSkills").And.Not.Contain("optedInSkills"));
        }

        [Test]
        public void Set_OptInToASkillUnselectedByDefault_IsStoredAsAnOptIn()
        {
            Set("art", true, selectedByDefault: false);

            Assert.That(LocalPrefs.Load(_project).OptedInSkills, Is.EqualTo(new[] { "art" }));
            Assert.That(Reloaded(_ => false).IsSelected("art"), Is.True);
            Assert.That(Reloaded(_ => false).IsSelected("audio"), Is.False);
        }

        [Test]
        public void IsSelected_ExplicitChoiceWinsOverTheDefault()
        {
            var selection = new SkillSelection(optedOut: new[] { "tdd" }, optedIn: new[] { "art" },
                selectedByDefault: name => name != "art" && name != "audio");

            Assert.That(selection.IsSelected("tdd"), Is.False);
            Assert.That(selection.IsSelected("art"), Is.True);
            Assert.That(selection.IsSelected("audio"), Is.False);
            Assert.That(selection.IsSelected("code-review"), Is.True);
        }

        [Test]
        public void Save_AfterAnOptOut_KeepsMembersThisVersionDoesNotKnow()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefsPath));
            File.WriteAllText(PrefsPath, @"{ ""version"": 1, ""futureSetting"": { ""a"": [1, true] }, ""optedInSkills"": [""art""] }");

            Set("tdd", false);

            var json = File.ReadAllText(PrefsPath);
            Assert.That(json, Does.Contain(@"""futureSetting"": {").And.Contain(@"""a"": ["));
            var loaded = LocalPrefs.Load(_project);
            Assert.That(loaded.OptedInSkills, Is.EqualTo(new[] { "art" }));
            Assert.That(loaded.OptedOutSkills, Is.EqualTo(new[] { "tdd" }));
        }
    }
}
