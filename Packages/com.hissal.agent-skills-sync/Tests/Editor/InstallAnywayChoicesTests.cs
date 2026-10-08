using System;
using System.IO;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>Per-folder install-anyway choices kept in local prefs.</summary>
    public class InstallAnywayChoicesTests
    {
        string _project;

        static SkillsFolder Agents => FolderLayout.Default.Find(".agents/skills");

        static SkillsFolder Claude => FolderLayout.Default.Find(".claude/skills");

        [SetUp]
        public void SetUp()
        {
            _project = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_project);
        }

        [TearDown]
        public void TearDown() => TempDirectory.Delete(_project);

        InstallAnywayChoices Reloaded() => InstallAnywayChoices.From(LocalPrefs.Load(_project));

        void Set(SkillsFolder folder, string skill, bool installAnyway)
        {
            var prefs = LocalPrefs.Load(_project);
            InstallAnywayChoices.Set(prefs, folder, skill, installAnyway);
            prefs.Save();
        }

        [Test]
        public void From_NoPrefs_HasNoOverrides()
        {
            Assert.That(Reloaded().IsInstalledAnyway(Claude, "tdd"), Is.False);
            Assert.That(LocalPrefs.Load(_project).InstallAnywaySkills, Is.Empty);
        }

        [Test]
        public void Set_InstallAnywayForClaude_IsKeptForClaudeOnly()
        {
            Set(Claude, "tdd", true);

            var choices = Reloaded();
            Assert.That(choices.IsInstalledAnyway(Claude, "tdd"), Is.True);
            Assert.That(choices.IsInstalledAnyway(Agents, "tdd"), Is.False);
            Assert.That(choices.IsInstalledAnyway(Claude, "other"), Is.False);
        }

        [Test]
        public void Set_OverrideBothFoldersThenUseMineInOne_KeepsTheOther()
        {
            Set(Claude, "tdd", true);
            Set(Agents, "tdd", true);

            Set(Claude, "tdd", false);

            var choices = Reloaded();
            Assert.That(choices.IsInstalledAnyway(Claude, "tdd"), Is.False);
            Assert.That(choices.IsInstalledAnyway(Agents, "tdd"), Is.True);
        }

        [Test]
        public void Set_RemoveTheLastOverride_RemovesTheSettingFromTheFile()
        {
            Set(Claude, "tdd", true);

            Set(Claude, "tdd", false);

            Assert.That(File.ReadAllText(LocalPrefs.PathFor(_project)), Does.Not.Contain("installAnywaySkills"));
        }

        [Test]
        public void Load_LegacySkipsAreIgnoredAndDroppedOnSave_UnknownMembersSurvive()
        {
            var path = LocalPrefs.PathFor(_project);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{\"skippedSkills\":{\".claude/skills\":[\"tdd\"]},\"future\":{\"keep\":true}}");

            var prefs = LocalPrefs.Load(_project);
            Assert.That(prefs.InstallAnywaySkills, Is.Empty);
            prefs.Save();

            var saved = File.ReadAllText(path);
            Assert.That(saved, Does.Not.Contain("skippedSkills"));
            Assert.That(saved, Does.Contain("\"future\""));
            Assert.That(saved, Does.Contain("\"keep\": true"));
        }

        [Test]
        public void Save_WritesOneSortedArrayPerFolder()
        {
            Set(Claude, "zeta", true);
            Set(Claude, "alpha", true);
            Set(Agents, "tdd", true);

            Assert.That(File.ReadAllText(LocalPrefs.PathFor(_project)).Replace("\r\n", "\n"), Does.Contain(
                "\"installAnywaySkills\": {\n    \".agents/skills\": [\"tdd\"],\n    \".claude/skills\": [\"alpha\", \"zeta\"]\n  }"));
        }
    }
}
