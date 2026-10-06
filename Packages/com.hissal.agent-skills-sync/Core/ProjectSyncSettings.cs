using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Hissal.AgentSkillsSync
{
    /// <summary>How a sync treats a locked skill whose upstream content no longer matches its <c>computedHash</c>.</summary>
    /// <remarks>
    /// The lockfile stores a content hash, not a commit, so the locked version itself can never be fetched, only verified.
    /// </remarks>
    public enum InstallMode
    {
        /// <summary>Install the current upstream copy and report each skill that differs from the lock. The default.</summary>
        Latest,

        /// <summary>Refuse a skill whose upstream no longer matches the lock.</summary>
        Pinned,
    }

    /// <summary>
    /// The project-wide sync settings, committed with the project in <c>ProjectSettings/AgentSkillsSync.json</c>
    /// (not in <c>skills-lock.json</c>, which stays compatible with the <c>skills</c> CLI).
    /// </summary>
    public sealed class ProjectSyncSettings
    {
        /// <summary>The settings file, relative to the project root.</summary>
        public const string RelativePath = "ProjectSettings/AgentSkillsSync.json";

        const string InstallModeKey = "installMode";

        public ProjectSyncSettings(InstallMode installMode) => InstallMode = installMode;

        public InstallMode InstallMode { get; }

        /// <summary>Reads the settings; a missing file or field means <see cref="InstallMode.Latest"/>.</summary>
        /// <exception cref="ProjectSyncSettingsException">The file is not valid JSON or names an unknown install mode.</exception>
        public static ProjectSyncSettings Load(string projectRoot)
        {
            var path = Paths.InProject(projectRoot, RelativePath);
            if (!File.Exists(path)) return new ProjectSyncSettings(InstallMode.Latest);

            object root;
            try
            {
                root = JsonReader.Read(File.ReadAllText(path));
            }
            catch (FormatException e)
            {
                throw new ProjectSyncSettingsException($"{RelativePath} is not valid JSON: {e.Message}");
            }

            if (!(root is List<KeyValuePair<string, object>> settings))
                throw new ProjectSyncSettingsException($"{RelativePath} must contain a JSON object.");

            object mode = null;
            foreach (var member in settings)
                if (member.Key == InstallModeKey) mode = member.Value;

            if (mode == null) return new ProjectSyncSettings(InstallMode.Latest);
            foreach (InstallMode known in Enum.GetValues(typeof(InstallMode)))
                if (mode is string text && string.Equals(text, known.ToString(), StringComparison.OrdinalIgnoreCase))
                    return new ProjectSyncSettings(known);

            throw new ProjectSyncSettingsException(
                $"{RelativePath} has {InstallModeKey} \"{mode}\"; expected \"latest\" or \"pinned\".");
        }

        /// <summary>Writes the settings, creating the file and folder if needed.</summary>
        public void Save(string projectRoot)
        {
            var path = Paths.InProject(projectRoot, RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var json = "{\n  \"" + InstallModeKey + "\": \"" + InstallMode.ToString().ToLowerInvariant() + "\"\n}\n";
            File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    /// <summary>The project sync settings file cannot be used; the message says why.</summary>
    public sealed class ProjectSyncSettingsException : Exception
    {
        public ProjectSyncSettingsException(string message) : base(message) { }
    }
}
