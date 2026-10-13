using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// (not in <c>skills-lock.json</c>, which stays compatible with the <c>skills</c> CLI): the install mode and the
    /// skill groups. The groups are edited in the file only; <see cref="Save"/> keeps them.
    /// </summary>
    public sealed class ProjectSyncSettings
    {
        /// <summary>The settings file, relative to the project root.</summary>
        public const string RelativePath = "ProjectSettings/AgentSkillsSync.json";

        const string InstallModeKey = "installMode";
        const string UngroupedSelectedByDefaultKey = "ungroupedSelectedByDefault";
        const string GroupsKey = "groups";
        const string SelectedByDefaultKey = "selectedByDefault";
        const string SkillsKey = "skills";

        /// <param name="groups">The project's skill groups; null = <see cref="SkillGroups.None"/>.</param>
        public ProjectSyncSettings(InstallMode installMode, SkillGroups groups = null)
        {
            InstallMode = installMode;
            Groups = groups ?? SkillGroups.None;
        }

        public InstallMode InstallMode { get; }

        /// <summary>The project's skill groups, which set each locked skill's default; never null.</summary>
        public SkillGroups Groups { get; }

        /// <summary>
        /// Reads the settings. A missing file or field means <see cref="InstallMode.Latest"/>, no groups, and every
        /// group and ungrouped skill selected by default.
        /// </summary>
        /// <exception cref="ProjectSyncSettingsException">
        /// The file is not valid JSON, names an unknown install mode, or a member has the wrong type; the message names it.
        /// </exception>
        public static ProjectSyncSettings Load(string projectRoot)
        {
            var settings = ReadMembers(Paths.InProject(projectRoot, RelativePath));
            if (settings == null) return new ProjectSyncSettings(InstallMode.Latest);

            return new ProjectSyncSettings(ModeIn(settings), GroupsIn(settings));
        }

        /// <summary>The file's top-level members; null when there is no file.</summary>
        static List<KeyValuePair<string, object>> ReadMembers(string path)
        {
            if (!File.Exists(path)) return null;

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
            return settings;
        }

        static InstallMode ModeIn(List<KeyValuePair<string, object>> settings)
        {
            if (!TryGet(settings, InstallModeKey, out var mode)) return InstallMode.Latest;
            foreach (InstallMode known in Enum.GetValues(typeof(InstallMode)))
                if (mode is string text && string.Equals(text, known.ToString(), StringComparison.OrdinalIgnoreCase))
                    return known;

            throw new ProjectSyncSettingsException(
                $"{RelativePath} has {InstallModeKey} \"{mode}\"; expected \"latest\" or \"pinned\".");
        }

        static SkillGroups GroupsIn(List<KeyValuePair<string, object>> settings)
        {
            var groups = new List<SkillGroup>();
            if (TryGet(settings, GroupsKey, out var value))
            {
                if (!(value is List<KeyValuePair<string, object>> members))
                    throw WrongType(GroupsKey, "an object of groups keyed by name");
                foreach (var member in members)
                {
                    var key = GroupsKey + "." + member.Key;
                    if (groups.Any(g => g.Name == member.Key))
                        throw new ProjectSyncSettingsException($"{RelativePath}: {key} is defined more than once.");
                    if (!(member.Value is List<KeyValuePair<string, object>> group))
                        throw WrongType(key, "an object");
                    groups.Add(new SkillGroup(member.Key, Flag(group, SelectedByDefaultKey, key + "."), Names(group, key + "." + SkillsKey)));
                }
            }
            return new SkillGroups(groups, Flag(settings, UngroupedSelectedByDefaultKey, ""));
        }

        /// <summary>A true/false member; true when it is missing.</summary>
        static bool Flag(List<KeyValuePair<string, object>> members, string key, string prefix)
        {
            if (!TryGet(members, key, out var value)) return true;
            return value is bool flag ? flag : throw WrongType(prefix + key, "true or false");
        }

        /// <summary>A group's <c>skills</c> list; empty when it is missing.</summary>
        static IEnumerable<string> Names(List<KeyValuePair<string, object>> group, string key)
        {
            if (!TryGet(group, SkillsKey, out var value)) return Enumerable.Empty<string>();
            if (!(value is List<object> items) || items.Any(i => !(i is string)))
                throw WrongType(key, "a list of skill names");
            return items.Cast<string>();
        }

        static bool TryGet(List<KeyValuePair<string, object>> members, string key, out object value)
        {
            var index = members.FindLastIndex(m => m.Key == key);
            value = index < 0 ? null : members[index].Value;
            return index >= 0;
        }

        static ProjectSyncSettingsException WrongType(string key, string expected) =>
            new ProjectSyncSettingsException($"{RelativePath}: {key} must be {expected}.");

        /// <summary>
        /// Writes the install mode, creating the file and folder if needed. Every other member of an existing file is
        /// kept as it is: the groups, and members this version does not know.
        /// </summary>
        /// <exception cref="ProjectSyncSettingsException">The existing file is not a JSON object; it is left alone.</exception>
        public void Save(string projectRoot)
        {
            var path = Paths.InProject(projectRoot, RelativePath);
            var settings = ReadMembers(path) ?? new List<KeyValuePair<string, object>>();
            var mode = new KeyValuePair<string, object>(InstallModeKey, InstallMode.ToString().ToLowerInvariant());
            var index = settings.FindLastIndex(m => m.Key == InstallModeKey);
            if (index < 0) settings.Insert(0, mode);
            else settings[index] = mode;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonWriter.Write(settings), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    /// <summary>The project sync settings file cannot be used; the message says why.</summary>
    public sealed class ProjectSyncSettingsException : Exception
    {
        public ProjectSyncSettingsException(string message) : base(message) { }
    }
}
