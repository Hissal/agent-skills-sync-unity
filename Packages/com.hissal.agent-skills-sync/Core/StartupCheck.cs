namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// The decision behind the once-per-session editor notification: notify when the lockfile changed
    /// since the last sync or a locked skill is missing, unless the contributor already declined this
    /// exact status. Never notifies while no skills folder is selected. Only the skills this machine
    /// installs count (<see cref="SkillSelection"/>): locking, changing or removing another is not a change.
    /// </summary>
    public static class StartupCheck
    {
        /// <param name="status">From <see cref="SyncStatus.Read"/>; null (no lockfile) never notifies.</param>
        public static bool ShouldNotify(SyncStatus status, LocalPrefs prefs)
        {
            if (status == null || status.NoFolderSelected) return false;
            var outOfSync = LockChanged(status, prefs) || status.OutOfSyncSkills.Count > 0;
            return outOfSync && !status.HasFingerprint(prefs.DeclinedState);
        }

        /// <summary>
        /// Whether the lock changed since the last sync for a skill this machine installs. An unchanged file never did.
        /// A changed one did unless the selected skills hash to what the last sync recorded; prefs written before that
        /// hash was recorded, and an unusable lockfile, count any file change.
        /// </summary>
        public static bool LockChanged(SyncStatus status, LocalPrefs prefs) =>
            status.LockHash != prefs.LastSyncedLockHash &&
            (status.SelectedSkillsHash == null || status.SelectedSkillsHash != prefs.LastSyncedSelectedSkillsHash);

        /// <summary>Records a successful sync of the lockfile in <paramref name="status"/>; clears any decline. Call <see cref="LocalPrefs.Save"/> after.</summary>
        public static void RecordSynced(LocalPrefs prefs, SyncStatus status)
        {
            prefs.LastSyncedLockHash = status?.LockHash;
            prefs.LastSyncedSelectedSkillsHash = status?.SelectedSkillsHash;
            prefs.DeclinedState = null;
        }

        /// <summary>Records that the contributor declined to sync <paramref name="status"/>. Call <see cref="LocalPrefs.Save"/> after.</summary>
        public static void RecordDeclined(LocalPrefs prefs, SyncStatus status) => prefs.DeclinedState = status?.Fingerprint;
    }
}
