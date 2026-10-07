namespace Hissal.AgentSkillsSync
{
    /// <summary>One entry of <c>skills-lock.json</c>: a skill the project pins to a source and content hash.</summary>
    public sealed class LockedSkill
    {
        public LockedSkill(string name, string source, string sourceType, string skillPath, string computedHash, string reference = null)
        {
            Name = name;
            Source = source;
            SourceType = sourceType;
            SkillPath = skillPath;
            ComputedHash = computedHash;
            Ref = reference;
        }

        /// <summary>The skill's folder name in every skills folder.</summary>
        public string Name { get; }

        /// <summary>The source repo, e.g. <c>owner/repo</c>.</summary>
        public string Source { get; }

        /// <summary>Always <c>github</c>; the lockfile lists other source types in <see cref="Lockfile.Unsupported"/>.</summary>
        public string SourceType { get; }

        /// <summary>Path of the skill's <c>SKILL.md</c> inside the source repo, or null when the lock omits it.</summary>
        public string SkillPath { get; }

        /// <summary>Content hash as computed by the <c>skills</c> CLI; the fetcher checks downloads against it.</summary>
        public string ComputedHash { get; }

        /// <summary>The branch, tag or commit the skill is locked to (the lock's <c>ref</c>), or null for the repo's default branch.</summary>
        public string Ref { get; }
    }
}
