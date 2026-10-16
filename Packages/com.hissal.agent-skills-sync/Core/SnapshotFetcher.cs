namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// The restore's fetcher: hands out the snapshot recorded for each lock entry and never touches the network, so a
    /// restore installs what the last explicit sync installed, offline, rather than whatever upstream holds now.
    /// <see cref="InstallMode.Pinned"/> keeps its lock verification: it prefers the snapshot whose hash is the locked
    /// <c>computedHash</c>, and refuses an unverifiable lock hash or a recorded snapshot that differs from the lock.
    /// </summary>
    internal sealed class SnapshotFetcher : ISkillFetcher
    {
        readonly SnapshotStore _store;
        readonly SnapshotResolutions _resolutions;
        readonly InstallMode _mode;

        public SnapshotFetcher(SnapshotStore store, SnapshotResolutions resolutions, InstallMode mode)
        {
            _store = store;
            _resolutions = resolutions;
            _mode = mode;
        }

        public string Fetch(LockedSkill skill) =>
            Locate(skill, out var reason) ?? throw new SkillFetchException(skill.Name, SkillFetchFailure.NotRecorded,
                $"Skill \"{skill.Name}\": {reason}");

        /// <summary>The snapshot folder to install for <paramref name="skill"/>; null, with the reason, when there is none.</summary>
        public string Locate(LockedSkill skill, out string reason)
        {
            reason = null;
            if (_mode == InstallMode.Pinned)
            {
                if (!LockVerification.CanVerify(skill))
                {
                    reason = $"the lock holds a skills.sh server hash for {skill.Source}, which Pinned mode cannot verify.";
                    return null;
                }
                var verified = _store.Find(skill.ComputedHash?.Trim());
                if (verified != null) return verified;
            }

            var resolution = _resolutions.Find(skill);
            if (resolution == null)
            {
                reason = "no snapshot is recorded for this lock entry on this machine. " +
                         "Run an explicit sync (or import an existing installation) first; a restore never downloads.";
                return null;
            }
            var folder = _store.Find(resolution.Snapshot);
            if (folder == null)
            {
                reason = $"the recorded snapshot {Short(resolution.Snapshot)} is no longer in the snapshot store. Run an explicit sync.";
                return null;
            }
            if (_mode == InstallMode.Pinned && !LockVerification.MatchesLock(skill, resolution.Snapshot))
            {
                reason = $"the recorded snapshot {Short(resolution.Snapshot)} differs from the lock, which Pinned mode refuses. " +
                         "Run an explicit sync in Pinned mode, or switch the install mode to Latest.";
                return null;
            }
            return folder;
        }

        static string Short(string hash) => hash.Substring(0, 12);
    }
}
