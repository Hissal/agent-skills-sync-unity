namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// The decision behind the once-per-session editor notification: notify when the lockfile changed
    /// since the last sync or a locked skill is missing, unless the contributor already declined this
    /// exact status.
    /// </summary>
    public static class StartupCheck
    {
        /// <param name="status">From <see cref="SyncStatus.Read"/>; null (no lockfile) never notifies.</param>
        public static bool ShouldNotify(SyncStatus status, LocalPrefs prefs)
        {
            if (status == null) return false;
            var outOfSync = status.LockHash != prefs.LastSyncedLockHash || status.MissingSkills.Count > 0;
            return outOfSync && status.Fingerprint != prefs.DeclinedState;
        }

        /// <summary>Records a successful sync of the lockfile in <paramref name="status"/>; clears any decline. Call <see cref="LocalPrefs.Save"/> after.</summary>
        public static void RecordSynced(LocalPrefs prefs, SyncStatus status)
        {
            prefs.LastSyncedLockHash = status?.LockHash;
            prefs.DeclinedState = null;
        }

        /// <summary>Records that the contributor declined to sync <paramref name="status"/>. Call <see cref="LocalPrefs.Save"/> after.</summary>
        public static void RecordDeclined(LocalPrefs prefs, SyncStatus status) => prefs.DeclinedState = status?.Fingerprint;
    }
}
