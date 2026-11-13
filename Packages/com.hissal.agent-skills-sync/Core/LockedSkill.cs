namespace Hissal.AgentSkillsSync
{
    /// <summary>One entry of <c>skills-lock.json</c>: a skill the project pins to a source and content hash.</summary>
    public sealed class LockedSkill
    {
        public LockedSkill(string name, string source, string sourceType, string skillPath, string computedHash, string reference = null,
            SkillHashAlgorithm hashAlgorithm = SkillHashAlgorithm.Version1)
        {
            SkillName.Validate(name);
            Name = name;
            Source = source;
            SourceType = sourceType;
            SkillPath = skillPath;
            ComputedHash = computedHash;
            Ref = reference;
            HashAlgorithm = hashAlgorithm;
        }

        /// <summary>The skill's folder name in every skills folder.</summary>
        public string Name { get; }

        /// <summary>The source repo, e.g. <c>owner/repo</c>.</summary>
        public string Source { get; }

        /// <summary>Always <c>github</c>; the lockfile lists other source types in <see cref="Lockfile.Unsupported"/>.</summary>
        public string SourceType { get; }

        /// <summary>Path of the skill's <c>SKILL.md</c> inside the source repo, or null when the lock omits it.</summary>
        public string SkillPath { get; }

        /// <summary>
        /// Content hash as computed by the <c>skills</c> CLI, with <see cref="HashAlgorithm"/>; the fetcher checks
        /// downloads against it. Empty when the CLI's version 1 → 2 migration found no matching installed copy.
        /// </summary>
        public string ComputedHash { get; }

        /// <summary>How <see cref="ComputedHash"/> was made: the algorithm of the lock version it was read from.</summary>
        public SkillHashAlgorithm HashAlgorithm { get; }

        /// <summary>The branch, tag or commit the skill is locked to (the lock's <c>ref</c>), or null for the repo's default branch.</summary>
        public string Ref { get; }
    }
}
