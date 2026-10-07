using System;
using System.Collections.Generic;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Decides whether a managed canonical copy of a locked skill is current, or the planner should Update it.
    /// The planner asks only this, so another install mode can swap in its own rule
    /// (for example comparing with the freshly fetched upstream hash instead of the lock).
    /// </summary>
    public interface IInstalledCopyCheck
    {
        /// <param name="skill">The locked skill.</param>
        /// <param name="installedHash">The <see cref="SkillFolderHash"/> of the installed copy; null when it could not be hashed.</param>
        bool IsCurrent(LockedSkill skill, string installedHash);
    }

    /// <summary>The default rule: the installed copy is current when its hash equals the locked <c>computedHash</c>.</summary>
    public sealed class LockedHashCheck : IInstalledCopyCheck
    {
        public static LockedHashCheck Instance { get; } = new LockedHashCheck();

        LockedHashCheck() { }

        public bool IsCurrent(LockedSkill skill, string installedHash)
        {
            // A skills.sh server hash is not comparable with ours: every installed copy would look stale and re-sync
            // forever. Such a copy counts as current; it is refreshed only when it goes missing.
            if (!LockVerification.CanVerify(skill)) return true;
            return installedHash != null && string.Equals(installedHash, skill.ComputedHash, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The <see cref="InstallMode.Latest"/> rule: the installed copy is current when its hash equals the freshly fetched
    /// upstream copy's, so a re-sync with unchanged upstream is a no-op even when upstream differs from the lock.
    /// </summary>
    internal sealed class UpstreamHashCheck : IInstalledCopyCheck
    {
        readonly IReadOnlyDictionary<string, string> _upstreamHashes;

        /// <param name="upstreamHashes">Skill name to the <see cref="SkillFolderHash"/> of its fetched upstream copy.</param>
        public UpstreamHashCheck(IReadOnlyDictionary<string, string> upstreamHashes) => _upstreamHashes = upstreamHashes;

        public bool IsCurrent(LockedSkill skill, string installedHash)
        {
            // Not fetched: nothing to update it with, so leave it.
            if (!_upstreamHashes.TryGetValue(skill.Name, out var upstream)) return true;
            return installedHash != null && string.Equals(installedHash, upstream, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>A fixed answer: every installed copy is current, or none is.</summary>
    internal sealed class FixedCheck : IInstalledCopyCheck
    {
        /// <summary>
        /// Plans no Update: Latest mode's offline preview, which cannot know upstream, and the startup status, whose quick
        /// scan has no hashes.
        /// </summary>
        public static FixedCheck AlwaysCurrent { get; } = new FixedCheck(true);

        /// <summary>Plans an Update for every managed copy; finds what Latest mode must fetch to compare.</summary>
        public static FixedCheck NeverCurrent { get; } = new FixedCheck(false);

        readonly bool _current;

        FixedCheck(bool current) => _current = current;

        public bool IsCurrent(LockedSkill skill, string installedHash) => _current;
    }
}
