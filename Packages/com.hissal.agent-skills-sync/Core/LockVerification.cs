using System;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Checks a copy of a locked skill against its locked <c>computedHash</c>: whether the lock hash can be checked at
    /// all, whether a <see cref="SkillFolderHash"/> matches it, and whether a folder verifiably differs from it.
    /// </summary>
    public static class LockVerification
    {
        /// <summary>
        /// Owners (and owner/repo sources) the <c>skills</c> CLI installs from skills.sh snapshots, locking a server hash
        /// of another algorithm. See docs/skills-cli-findings.md §1 "Blob-installed sources".
        /// </summary>
        static readonly string[] SkillsShHashedSources = { "vercel", "vercel-labs", "heygen-com", "remotion-dev", "zapier/connectors" };

        /// <summary>
        /// Whether the skill's <c>computedHash</c> can be checked. False for sources the CLI locks with a skills.sh
        /// server hash, which <see cref="SkillFolderHash"/> does not reproduce; Pinned mode refuses those.
        /// </summary>
        public static bool CanVerify(LockedSkill skill)
        {
            string owner, name;
            try
            {
                (owner, name) = GitHubSkillFetcher.ParseRepo(skill);
            }
            catch (SkillFetchException)
            {
                return true; // Fetch refuses it anyway.
            }

            foreach (var source in SkillsShHashedSources)
                if (string.Equals(source, owner, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(source, owner + "/" + name, StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }

        /// <summary>Whether <paramref name="hash"/> (a <see cref="SkillFolderHash"/>) is the skill's locked <c>computedHash</c>.</summary>
        internal static bool MatchesLock(LockedSkill skill, string hash) =>
            hash != null && string.Equals(hash, skill.ComputedHash?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether <paramref name="folder"/> (a copy of the skill) verifiably differs from the lock: false when it hashes
        /// to the lock as it is or as a CRLF checkout, and when a mismatch can't tell (no locked hash, skills.sh hash,
        /// non-ASCII paths).
        /// </summary>
        internal static bool DiffersFromLock(LockedSkill skill, string folder) =>
            !string.IsNullOrWhiteSpace(skill.ComputedHash)
            && CanVerify(skill)
            && !MatchesLock(skill, SkillFolderHash.Compute(folder))
            && !MatchesLock(skill, SkillFolderHash.ComputeAsCrlfCheckout(folder))
            && !SkillFolderHash.HasNonAsciiPath(folder);

        /// <summary>The Pinned-mode refusal of a skill whose upstream content hashes to <paramref name="actual"/>, not the lock.</summary>
        internal static SkillFetchException SourceChangedSinceLocked(LockedSkill skill, string actual)
        {
            var locked = skill.ComputedHash?.Trim() ?? "";
            var lockedText = locked.Length == 0 ? "no hash" : Short(locked);
            return new SkillFetchException(skill.Name, SkillFetchFailure.HashMismatch,
                $"Skill \"{skill.Name}\": source {skill.Source} changed since it was locked (locked {lockedText}, now {Short(actual)}). " +
                $"Run `npx skills update` and commit {Lockfile.FileName}, or switch the install mode to Latest.");
        }

        static string Short(string hash) => hash.Length > 12 ? hash.Substring(0, 12) : hash;
    }
}
