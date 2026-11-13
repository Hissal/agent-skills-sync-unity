using System;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Checks a copy of a locked skill against its locked <c>computedHash</c>: whether the lock hash can be checked at
    /// all, whether a <see cref="SkillFolderHash"/> matches it, and whether a folder verifiably differs from it. Every
    /// hash compared with the lock is made with the skill's <see cref="LockedSkill.HashAlgorithm"/> (<see cref="Hash"/>).
    /// </summary>
    public static class LockVerification
    {
        /// <summary>
        /// Owners (and owner/repo sources) the <c>skills</c> CLI installs from skills.sh snapshots. In a version 1 lock
        /// it locks a server hash of another algorithm; a version 2 lock holds the version 2 hash of the same files.
        /// See docs/skills-cli-findings.md §1 "Blob-installed sources".
        /// </summary>
        static readonly string[] SkillsShHashedSources = { "vercel", "vercel-labs", "heygen-com", "remotion-dev", "zapier/connectors" };

        /// <summary>
        /// Whether the skill's <c>computedHash</c> can be checked. False for sources a version 1 lock holds a skills.sh
        /// server hash for, which <see cref="SkillFolderHash"/> does not reproduce; Pinned mode refuses those.
        /// </summary>
        public static bool CanVerify(LockedSkill skill)
        {
            if (skill.HashAlgorithm == SkillHashAlgorithm.Version2) return true;
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

        /// <summary>The <see cref="SkillFolderHash"/> of <paramref name="folder"/> with the skill's lock algorithm, to compare with its lock.</summary>
        internal static string Hash(LockedSkill skill, string folder) => SkillFolderHash.Compute(folder, skill.HashAlgorithm);

        /// <summary>
        /// Whether the lock entry has no <c>computedHash</c> to check, in a version 2 lock. The <c>skills</c> CLI writes an
        /// empty one when its version 1 → 2 migration finds no installed copy matching the old hash.
        /// </summary>
        internal static bool LacksMigratedHash(LockedSkill skill) =>
            skill.HashAlgorithm == SkillHashAlgorithm.Version2 && string.IsNullOrWhiteSpace(skill.ComputedHash);

        /// <summary>The Pinned-mode refusal of a <see cref="LacksMigratedHash"/> entry, which nothing can be verified against.</summary>
        internal static SkillFetchException NoLockedHash(LockedSkill skill) =>
            new SkillFetchException(skill.Name, SkillFetchFailure.Unverifiable, $"Skill \"{skill.Name}\": {NoLockedHashReason(skill)}");

        /// <summary><see cref="NoLockedHash"/>'s message without the skill name in front.</summary>
        internal static string NoLockedHashReason(LockedSkill skill) =>
            $"{Lockfile.FileName} has no content hash for it, so Pinned mode can't verify it. " +
            "This usually means the skills CLI's lock version 1 → 2 migration found no installed copy matching the old hash. " +
            $"Reinstall the skill with the skills CLI (`npx skills add {skill.Source} --skill {skill.Name}`) and commit {Lockfile.FileName}, " +
            "or switch the install mode to Latest.";

        /// <summary>Whether <paramref name="hash"/> (the skill's <see cref="Hash"/>) is the skill's locked <c>computedHash</c>.</summary>
        internal static bool MatchesLock(LockedSkill skill, string hash) =>
            hash != null && string.Equals(hash, skill.ComputedHash?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether <paramref name="folder"/> (a copy of the skill) verifiably differs from the lock: false when it hashes
        /// to the lock, and when a mismatch can't tell (no locked hash, skills.sh hash). A version 1 lock also matches a
        /// CRLF checkout and can't tell for non-ASCII paths; version 2 hashes need neither exception.
        /// </summary>
        internal static bool DiffersFromLock(LockedSkill skill, string folder) =>
            !string.IsNullOrWhiteSpace(skill.ComputedHash)
            && CanVerify(skill)
            && !MatchesLock(skill, Hash(skill, folder))
            && (skill.HashAlgorithm == SkillHashAlgorithm.Version2
                || (!MatchesLock(skill, SkillFolderHash.ComputeAsCrlfCheckout(folder))
                    && !SkillFolderHash.HasNonAsciiPath(folder)));

        /// <summary>The Pinned-mode refusal of a skill whose upstream content hashes to <paramref name="actual"/>, not the lock.</summary>
        internal static SkillFetchException SourceChangedSinceLocked(LockedSkill skill, string actual)
        {
            if (LacksMigratedHash(skill)) return NoLockedHash(skill);
            var locked = skill.ComputedHash?.Trim() ?? "";
            var lockedText = locked.Length == 0 ? "no hash" : Short(locked);
            return new SkillFetchException(skill.Name, SkillFetchFailure.HashMismatch,
                $"Skill \"{skill.Name}\": source {skill.Source} changed since it was locked (locked {lockedText}, now {Short(actual)}). " +
                $"Run `npx skills update` and commit {Lockfile.FileName}, or switch the install mode to Latest.");
        }

        static string Short(string hash) => hash.Length > 12 ? hash.Substring(0, 12) : hash;
    }
}
