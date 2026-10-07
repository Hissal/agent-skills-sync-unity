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

        /// <summary>
        /// The folder holding the <c>skills-lock.json</c> a Unity project syncs: the Unity project folder itself, else
        /// the folder above it (a repo that keeps its Unity project in a subfolder). Null when neither holds one.
        /// </summary>
        public static string FindRoot(string unityProjectRoot)
        {
            if (File.Exists(Path.Combine(unityProjectRoot, FileName))) return unityProjectRoot;
            var parent = Path.GetDirectoryName(Path.GetFullPath(unityProjectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return parent != null && File.Exists(Path.Combine(parent, FileName)) ? parent : null;
        }

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
            if (!IsSafeFolderName(name))
                throw new LockfileException(
                    $"Skill name {Describe(name)} in {FileName} is not a safe skill folder name. " +
                    "A skill name must be a single folder name: no path separators, no \".\" or \"..\", no drive or root, " +
                    "no characters that are invalid in file names, and no trailing dot or space.");

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
                Get(skill, "computedHash") as string,
                ParseRef(name, Get(skill, "ref")));
        }

        // The ref goes into the archive URL and the fetch cache path, so only plain git ref names are accepted
        // (git check-ref-format's rules, plus no characters that would change the URL's meaning).
        static readonly char[] UnsafeRefChars = { ' ', '~', '^', ':', '?', '*', '[', '\\', '#', '%', '"', '<', '>', '|' };

        static string ParseRef(string name, object value)
        {
            if (value == null) return null;
            if (!(value is string reference))
                throw new LockfileException($"Skill \"{name}\" in {FileName} has a ref that is not a string.");
            if (reference.Length == 0) return null;
            if (!IsSafeRef(reference))
                throw new LockfileException(
                    $"Skill \"{name}\" in {FileName} has ref {Describe(reference)}, which is not a valid git branch, tag or commit name.");
            return reference;
        }

        static bool IsSafeRef(string reference)
        {
            if (reference.IndexOfAny(UnsafeRefChars) >= 0 || reference.Contains("..") || reference.Contains("@{")) return false;
            foreach (var c in reference)
                if (c < 0x20 || c == 0x7f) return false;
            foreach (var part in reference.Split('/'))
                if (part.Length == 0 || part[0] == '.' || part.EndsWith(".lock", StringComparison.Ordinal)) return false;
            return !reference.EndsWith(".", StringComparison.Ordinal);
        }

        // The name becomes the last component of paths under the skills folders and the fetch cache, so it must
        // not be able to point anywhere else. Checked against Windows' rules on every OS so a lock that works
        // on one machine works on all of them.
        static readonly char[] UnsafeNameChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

        static bool IsSafeFolderName(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "." || name == "..") return false;
            if (name.IndexOfAny(UnsafeNameChars) >= 0) return false;
            foreach (var c in name)
                if (c < 0x20) return false;
            var last = name[name.Length - 1];
            return last != '.' && last != ' ';
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
