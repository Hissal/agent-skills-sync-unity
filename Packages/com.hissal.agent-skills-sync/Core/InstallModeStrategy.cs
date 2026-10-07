using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// What an <see cref="InstallMode"/> changes about a <see cref="SkillSync"/>: which managed copies count as current,
    /// which get fetched, whether a fetched copy must match the lock, and what is reported as differing from it.
    /// </summary>
    internal abstract class InstallModeStrategy
    {
        public static InstallModeStrategy For(InstallMode mode) => mode == InstallMode.Latest ? Latest : Pinned;

        static readonly InstallModeStrategy Pinned = new PinnedStrategy();
        static readonly InstallModeStrategy Latest = new LatestStrategy();

        /// <summary>Whether a managed copy is current when planning without fetching (<see cref="SkillSync.Plan()"/>).</summary>
        public abstract IInstalledCopyCheck PreviewCheck { get; }

        /// <summary>Whether a managed copy is current when deciding what a sync fetches: the copies it does not count as current.</summary>
        public abstract IInstalledCopyCheck FetchCheck { get; }

        /// <summary>Refuses a fetched copy hashing to <paramref name="hash"/> by throwing; returns when the mode accepts it.</summary>
        /// <exception cref="SkillFetchException">The mode refuses the copy.</exception>
        public abstract void Accept(LockedSkill skill, string hash);

        /// <summary>
        /// The check the applied plan uses, given each fetched copy's <see cref="SkillFolderHash"/>; null applies the
        /// plan made with <see cref="FetchCheck"/> as it is.
        /// </summary>
        public abstract IInstalledCopyCheck ApplyCheck(IReadOnlyDictionary<string, string> upstreamHashes);

        /// <summary>
        /// Locked skills whose fetched copy (skill name to folder) verifiably differs from the lock, in lock order, for
        /// <see cref="SyncSummary.DiffersFromLock"/>; null when the mode reports none.
        /// </summary>
        public abstract IReadOnlyList<string> DiffersFromLock(Lockfile lockfile, IReadOnlyDictionary<string, string> fetched);

        /// <summary>Installs only what matches the lock: a stale copy is fetched, and a changed source is refused.</summary>
        sealed class PinnedStrategy : InstallModeStrategy
        {
            public override IInstalledCopyCheck PreviewCheck => LockedHashCheck.Instance;

            public override IInstalledCopyCheck FetchCheck => LockedHashCheck.Instance;

            // Pinned refuses a changed source whichever fetcher is in use.
            public override void Accept(LockedSkill skill, string hash)
            {
                if (GitHubSkillFetcher.CanVerify(skill) && !GitHubSkillFetcher.MatchesLock(skill, hash))
                    throw GitHubSkillFetcher.SourceChangedSinceLocked(skill, hash);
            }

            public override IInstalledCopyCheck ApplyCheck(IReadOnlyDictionary<string, string> upstreamHashes) => null;

            public override IReadOnlyList<string> DiffersFromLock(Lockfile lockfile, IReadOnlyDictionary<string, string> fetched) => null;
        }

        /// <summary>
        /// Installs current upstream: every managed copy is fetched and compared with upstream, not the lock, so re-running
        /// with unchanged upstream is a no-op. Offline, an installed copy counts as current, since only a fetch can tell.
        /// </summary>
        sealed class LatestStrategy : InstallModeStrategy
        {
            public override IInstalledCopyCheck PreviewCheck => FixedCheck.AlwaysCurrent;

            public override IInstalledCopyCheck FetchCheck => FixedCheck.NeverCurrent;

            public override void Accept(LockedSkill skill, string hash) { }

            public override IInstalledCopyCheck ApplyCheck(IReadOnlyDictionary<string, string> upstreamHashes) =>
                new UpstreamHashCheck(upstreamHashes);

            // Judged on the fetched copies, as fetched.
            public override IReadOnlyList<string> DiffersFromLock(Lockfile lockfile, IReadOnlyDictionary<string, string> fetched) =>
                lockfile.Skills.Where(s => fetched.TryGetValue(s.Name, out var folder) && GitHubSkillFetcher.DiffersFromLock(s, folder))
                    .Select(s => s.Name).ToList();
        }
    }
}
