using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// The parsed <c>skills-lock.json</c>, the project's source of truth for which skills to install.
    /// Reads the <c>skills</c> CLI's format; only <c>version</c> 1 and <c>github</c> sources are accepted.
    /// </summary>
    public sealed class Lockfile
    {
        public const string FileName = "skills-lock.json";
        public const int SupportedVersion = 1;
        public const string GitHubSourceType = "github";

        public Lockfile(IReadOnlyList<LockedSkill> skills) => Skills = skills;

        /// <summary>The locked skills, in lockfile order.</summary>
        public IReadOnlyList<LockedSkill> Skills { get; }

        /// <summary>Reads <c>skills-lock.json</c> at the project root.</summary>
        /// <exception cref="LockfileException">The file is missing or unusable.</exception>
        public static Lockfile Load(string projectRoot)
        {
            var path = Path.Combine(projectRoot, FileName);
            if (!File.Exists(path)) throw new LockfileException($"No {FileName} found at the project root ({projectRoot}).");
            return Parse(File.ReadAllText(path));
        }

        /// <exception cref="LockfileException">The JSON is malformed, the version is unknown, or a skill has an unsupported source.</exception>
        public static Lockfile Parse(string json)
        {
            object root;
            try
            {
                root = JsonReader.Read(json);
            }
            catch (FormatException e)
            {
                throw new LockfileException($"{FileName} is not valid JSON: {e.Message}");
            }

            if (!(root is List<KeyValuePair<string, object>> rootObject))
                throw new LockfileException($"{FileName} must contain a JSON object.");

            var version = Get(rootObject, "version");
            if (!(version is double number) || number != SupportedVersion)
                throw new LockfileException(
                    $"{FileName} has format version {Describe(version)}, but this tool only understands version {SupportedVersion}. " +
                    "Update the Agent Skills Sync package.");

            if (!(Get(rootObject, "skills") is List<KeyValuePair<string, object>> skillsObject))
                throw new LockfileException($"{FileName} has no \"skills\" object.");

            var skills = new List<LockedSkill>();
            foreach (var entry in skillsObject)
                skills.Add(ParseSkill(entry.Key, entry.Value));
            return new Lockfile(skills);
        }

        static LockedSkill ParseSkill(string name, object value)
        {
            if (!(value is List<KeyValuePair<string, object>> skill))
                throw new LockfileException($"Skill \"{name}\" in {FileName} must be a JSON object.");

            var sourceType = Get(skill, "sourceType") as string;
            if (sourceType != GitHubSourceType)
                throw new LockfileException(
                    $"Skill \"{name}\" has source type {Describe(Get(skill, "sourceType"))}; only \"{GitHubSourceType}\" sources are supported.");

            var source = Get(skill, "source") as string;
            if (string.IsNullOrEmpty(source))
                throw new LockfileException($"Skill \"{name}\" in {FileName} has no source.");

            return new LockedSkill(
                name,
                source,
                sourceType,
                Get(skill, "skillPath") as string,
                Get(skill, "computedHash") as string);
        }

        static object Get(List<KeyValuePair<string, object>> obj, string key)
        {
            foreach (var member in obj)
                if (member.Key == key) return member.Value;
            return null;
        }

        static string Describe(object value) =>
            value == null ? "(missing)"
            : value is string s ? $"\"{s}\""
            : Convert.ToString(value, CultureInfo.InvariantCulture);
    }
}
