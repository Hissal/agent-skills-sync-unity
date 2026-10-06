using System;
using System.IO;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>Per-folder skip choices kept in local prefs.</summary>
    public class SkipChoicesTests
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

        SkipChoices Reloaded() => SkipChoices.From(LocalPrefs.Load(_project));

        void Set(SkillsFolder folder, string skill, bool skip)
        {
            var prefs = LocalPrefs.Load(_project);
            SkipChoices.Set(prefs, folder, skill, skip);
            prefs.Save();
        }

        [Test]
        public void From_NoPrefs_SkipsNothing()
        {
            Assert.That(Reloaded().IsSkipped(Claude, "tdd"), Is.False);
            Assert.That(LocalPrefs.Load(_project).SkippedSkills, Is.Empty);
        }

        [Test]
        public void Set_SkipForClaude_IsKeptForClaudeOnly()
        {
            Set(Claude, "tdd", true);

            var choices = Reloaded();
            Assert.That(choices.IsSkipped(Claude, "tdd"), Is.True);
            Assert.That(choices.IsSkipped(Agents, "tdd"), Is.False);
            Assert.That(choices.IsSkipped(Claude, "other"), Is.False);
        }

        [Test]
        public void Set_SkipBothFoldersThenUnskipOne_KeepsTheOther()
        {
            Set(Claude, "tdd", true);
            Set(Agents, "tdd", true);

            Set(Claude, "tdd", false);

            var choices = Reloaded();
            Assert.That(choices.IsSkipped(Claude, "tdd"), Is.False);
            Assert.That(choices.IsSkipped(Agents, "tdd"), Is.True);
        }

        [Test]
        public void Set_UnskipTheLastOne_RemovesTheSettingFromTheFile()
        {
            Set(Claude, "tdd", true);

            Set(Claude, "tdd", false);

            Assert.That(File.ReadAllText(LocalPrefs.PathFor(_project)), Does.Not.Contain("skippedSkills"));
        }

        [Test]
        public void Save_WritesOneSortedArrayPerFolder()
        {
            Set(Claude, "zeta", true);
            Set(Claude, "alpha", true);
            Set(Agents, "tdd", true);

            Assert.That(File.ReadAllText(LocalPrefs.PathFor(_project)).Replace("\r\n", "\n"), Does.Contain(
                "\"skippedSkills\": {\n    \".agents/skills\": [\"tdd\"],\n    \".claude/skills\": [\"alpha\", \"zeta\"]\n  }"));
        }
    }
}
