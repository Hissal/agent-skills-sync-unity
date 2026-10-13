using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class ProjectSyncSettingsTests
    {
        string _project;

        string SettingsPath => Path.Combine(_project, "ProjectSettings", "AgentSkillsSync.json");

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

        void WriteSettings(string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, json);
        }

        [Test]
        public void Load_NoFile_IsLatest()
        {
            Assert.That(ProjectSyncSettings.Load(_project).InstallMode, Is.EqualTo(InstallMode.Latest));
        }

        [Test]
        public void Load_NoInstallModeField_IsLatest()
        {
            WriteSettings("{ \"somethingElse\": true }");

            Assert.That(ProjectSyncSettings.Load(_project).InstallMode, Is.EqualTo(InstallMode.Latest));
        }

        [Test]
        public void Load_PinnedInFile_IsPinned()
        {
            WriteSettings("{ \"installMode\": \"pinned\" }");

            Assert.That(ProjectSyncSettings.Load(_project).InstallMode, Is.EqualTo(InstallMode.Pinned));
        }

        [Test]
        public void Save_ThenLoad_ReturnsTheSavedMode()
        {
            new ProjectSyncSettings(InstallMode.Pinned).Save(_project);

            Assert.That(File.Exists(SettingsPath), Is.True);
            Assert.That(ProjectSyncSettings.Load(_project).InstallMode, Is.EqualTo(InstallMode.Pinned));

            new ProjectSyncSettings(InstallMode.Latest).Save(_project);

            Assert.That(ProjectSyncSettings.Load(_project).InstallMode, Is.EqualTo(InstallMode.Latest));
        }

        [Test]
        public void Load_UnknownMode_ThrowsNamingTheFileAndValue()
        {
            WriteSettings("{ \"installMode\": \"newest\" }");

            var error = Assert.Throws<ProjectSyncSettingsException>(() => ProjectSyncSettings.Load(_project));

            Assert.That(error.Message, Does.Contain("AgentSkillsSync.json").And.Contain("newest"));
        }

        [Test]
        public void Load_MalformedJson_Throws()
        {
            WriteSettings("{ \"installMode\": ");

            Assert.Throws<ProjectSyncSettingsException>(() => ProjectSyncSettings.Load(_project));
        }

        const string Grouped = @"{
  ""installMode"": ""pinned"",
  ""ungroupedSelectedByDefault"": false,
  ""groups"": {
    ""recommended"": { ""selectedByDefault"": true, ""skills"": [""unity-cli"", ""tdd""] },
    ""art"": { ""selectedByDefault"": false, ""skills"": [""ui-uitk""] }
  }
}";

        [Test]
        public void Load_Groups_ParseInFileOrderWithTheirDefaultsAndSkills()
        {
            WriteSettings(Grouped);

            var groups = ProjectSyncSettings.Load(_project).Groups;

            Assert.That(groups.UngroupedSelectedByDefault, Is.False);
            Assert.That(groups.Groups.Select(g => g.Name), Is.EqualTo(new[] { "recommended", "art" }));
            Assert.That(groups.Groups[0].SelectedByDefault, Is.True);
            Assert.That(groups.Groups[0].Skills, Is.EqualTo(new[] { "unity-cli", "tdd" }));
            Assert.That(groups.Groups[1].SelectedByDefault, Is.False);
            Assert.That(groups.Groups[1].Skills, Is.EqualTo(new[] { "ui-uitk" }));
        }

        [Test]
        public void Load_NoFileOrNoGroupMembers_HasNoGroupsAndSelectsUngroupedSkills()
        {
            Assert.That(ProjectSyncSettings.Load(_project).Groups.Groups, Is.Empty);
            Assert.That(ProjectSyncSettings.Load(_project).Groups.UngroupedSelectedByDefault, Is.True);

            WriteSettings("{ \"installMode\": \"pinned\" }");

            Assert.That(ProjectSyncSettings.Load(_project).Groups.Groups, Is.Empty);
            Assert.That(ProjectSyncSettings.Load(_project).Groups.UngroupedSelectedByDefault, Is.True);
        }

        [Test]
        public void Load_GroupWithoutMembers_IsSelectedByDefaultAndListsNoSkills()
        {
            WriteSettings("{ \"groups\": { \"recommended\": {} } }");

            var group = ProjectSyncSettings.Load(_project).Groups.Groups.Single();

            Assert.That(group.SelectedByDefault, Is.True);
            Assert.That(group.Skills, Is.Empty);
        }

        [TestCase("{ \"ungroupedSelectedByDefault\": \"yes\" }", "ungroupedSelectedByDefault")]
        [TestCase("{ \"ungroupedSelectedByDefault\": null }", "ungroupedSelectedByDefault")]
        [TestCase("{ \"groups\": [] }", "groups")]
        [TestCase("{ \"groups\": { \"art\": true } }", "groups.art")]
        [TestCase("{ \"groups\": { \"art\": { \"selectedByDefault\": 1 } } }", "groups.art.selectedByDefault")]
        [TestCase("{ \"groups\": { \"art\": { \"skills\": \"ui-uitk\" } } }", "groups.art.skills")]
        [TestCase("{ \"groups\": { \"art\": { \"skills\": [\"ui-uitk\", 3] } } }", "groups.art.skills")]
        [TestCase("{ \"groups\": { \"art\": {}, \"art\": {} } }", "groups.art")]
        public void Load_WrongType_ThrowsNamingTheFileAndKey(string json, string key)
        {
            WriteSettings(json);

            var error = Assert.Throws<ProjectSyncSettingsException>(() => ProjectSyncSettings.Load(_project));

            Assert.That(error.Message, Does.Contain("AgentSkillsSync.json").And.Contain(key + " "));
        }

        [Test]
        public void Save_KeepsGroupsAndMembersThisVersionDoesNotKnow()
        {
            WriteSettings(Grouped.Replace("\"installMode\"", "\"futureSetting\": { \"a\": [1, true] }, \"installMode\""));

            new ProjectSyncSettings(InstallMode.Latest).Save(_project);

            Assert.That(File.ReadAllText(SettingsPath), Does.Contain(@"""futureSetting"": {").And.Contain(@"""a"": [1, true]"));
            var loaded = ProjectSyncSettings.Load(_project);
            Assert.That(loaded.InstallMode, Is.EqualTo(InstallMode.Latest));
            Assert.That(loaded.Groups.UngroupedSelectedByDefault, Is.False);
            Assert.That(loaded.Groups.Groups.Select(g => g.Name), Is.EqualTo(new[] { "recommended", "art" }));
            Assert.That(loaded.Groups.Groups[1].SelectedByDefault, Is.False);
            Assert.That(loaded.Groups.Groups[0].Skills, Is.EqualTo(new[] { "unity-cli", "tdd" }));
        }

        [Test]
        public void Save_OverAFileThatCannotBeRead_ThrowsAndLeavesItAlone()
        {
            WriteSettings("{ \"groups\": ");

            Assert.Throws<ProjectSyncSettingsException>(() => new ProjectSyncSettings(InstallMode.Pinned).Save(_project));

            Assert.That(File.ReadAllText(SettingsPath), Is.EqualTo("{ \"groups\": "));
        }
    }
}
