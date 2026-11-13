using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// The parsed <c>skills-lock.json</c>, the project's source of truth for which skills to install.
    /// Reads the <c>skills</c> CLI's format, <c>version</c> 1 or 2; the version picks how its <c>computedHash</c>es are
    /// made (<see cref="HashAlgorithm"/>). Only <c>github</c> sources are installed; entries of any other source type
    /// are listed in <see cref="Unsupported"/> and left to whatever installs them.
    /// </summary>
    public sealed class Lockfile
    {
        public const string FileName = "skills-lock.json";
        public const int MinSupportedVersion = 1;
        public const int MaxSupportedVersion = 2;

        [Obsolete("The lockfile reads versions 1 and 2; use MinSupportedVersion and MaxSupportedVersion.")]
        public const int SupportedVersion = MinSupportedVersion;
        public const string GitHubSourceType = "github";

        /// <exception cref="ArgumentException">A skill's hash algorithm is not this lock version's.</exception>
        public Lockfile(IReadOnlyList<LockedSkill> skills, IReadOnlyList<UnsupportedSkill> unsupported = null, int version = MinSupportedVersion)
        {
            if (version < MinSupportedVersion || version > MaxSupportedVersion)
                throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported lockfile version.");
            Version = version;
            HashAlgorithm = (SkillHashAlgorithm)version;
            foreach (var skill in skills)
                if (skill.HashAlgorithm != HashAlgorithm)
                    throw new ArgumentException(
                        $"Skill \"{skill.Name}\" has {skill.HashAlgorithm} hashes, but a version {version} lock uses {HashAlgorithm}.", nameof(skills));
            Skills = skills;
            Unsupported = unsupported ?? Array.Empty<UnsupportedSkill>();
        }

        /// <summary>The lock's format <c>version</c>: 1 or 2.</summary>
        public int Version { get; }

        /// <summary>How every <c>computedHash</c> in this lock was made; compare copies with it using only this algorithm.</summary>
        public SkillHashAlgorithm HashAlgorithm { get; }

        /// <summary>The locked <c>github</c> skills this tool installs, in lockfile order.</summary>
        public IReadOnlyList<LockedSkill> Skills { get; }

        /// <summary>The entries with another source type, in lockfile order. The tool never installs or touches them.</summary>
        public IReadOnlyList<UnsupportedSkill> Unsupported { get; }

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

        /// <exception cref="LockfileException">The JSON is malformed, the version is unknown, or a skill entry is unusable.</exception>
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
            if (!(version is double number) || (number != 1 && number != 2))
                throw new LockfileException(
                    $"{FileName} has format version {Describe(version)}, but this tool only understands versions " +
                    $"{MinSupportedVersion} and {MaxSupportedVersion}. Update the Agent Skills Sync package.");
            var algorithm = (SkillHashAlgorithm)(int)number;

            if (!(Get(rootObject, "skills") is List<KeyValuePair<string, object>> skillsObject))
                throw new LockfileException($"{FileName} has no \"skills\" object.");

            var skills = new List<LockedSkill>();
            var unsupported = new List<UnsupportedSkill>();
            foreach (var entry in skillsObject)
            {
                SkillName.Validate(entry.Key);
                if (!(entry.Value is List<KeyValuePair<string, object>> skill))
                    throw new LockfileException($"Skill \"{entry.Key}\" in {FileName} must be a JSON object.");
                if (!(Get(skill, "sourceType") is string sourceType) || sourceType.Length == 0)
                    throw new LockfileException($"Skill \"{entry.Key}\" in {FileName} has no source type.");
                if (sourceType == GitHubSourceType) skills.Add(ParseSkill(entry.Key, skill, algorithm));
                else unsupported.Add(new UnsupportedSkill(entry.Key, sourceType));
            }
            return new Lockfile(skills, unsupported, (int)number);
        }

        static LockedSkill ParseSkill(string name, List<KeyValuePair<string, object>> skill, SkillHashAlgorithm algorithm)
        {
            var source = Get(skill, "source") as string;
            if (string.IsNullOrEmpty(source))
                throw new LockfileException($"Skill \"{name}\" in {FileName} has no source.");

            return new LockedSkill(
                name,
                source,
                GitHubSourceType,
                Get(skill, "skillPath") as string,
                Get(skill, "computedHash") as string,
                ParseRef(name, Get(skill, "ref")),
                algorithm);
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
