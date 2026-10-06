using System;

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
            if (!GitHubSkillFetcher.CanVerify(skill)) return true;
            return installedHash != null && string.Equals(installedHash, skill.ComputedHash, StringComparison.OrdinalIgnoreCase);
        }
    }
}
