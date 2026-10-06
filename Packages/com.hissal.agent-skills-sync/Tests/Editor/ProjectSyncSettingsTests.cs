using System;
using System.IO;
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
    }
}
