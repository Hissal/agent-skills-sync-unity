using System.Collections.Generic;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// The decision behind the once-per-session editor notification, which has two parts. Out of sync: the lockfile
    /// changed since the last sync or a locked skill is missing, unless the contributor already declined this exact
    /// status. New optional skills: a skill this machine leaves out by default was locked and the contributor has not
    /// been shown it (<see cref="SeenSkills"/>), unless they turned that notice off. Never notifies while no skills
    /// folder is selected. Only the skills this machine installs count as out of sync (<see cref="SkillSelection"/>):
    /// locking, changing or removing another is not a change, so a newly locked optional skill reaches the contributor
    /// through the second part alone.
    /// </summary>
    public static class StartupCheck
    {
        /// <param name="status">From <see cref="SyncStatus.Read"/>; null (no lockfile) never notifies.</param>
        public static bool ShouldNotify(SyncStatus status, LocalPrefs prefs) =>
            OutOfSync(status, prefs) || NewSkills(status, prefs).Count > 0;

        /// <summary>Whether the project is out of sync in a way the contributor has not declined yet.</summary>
        public static bool OutOfSync(SyncStatus status, LocalPrefs prefs)
        {
            if (status == null || status.NoFolderSelected) return false;
            var outOfSync = LockChanged(status, prefs) || status.OutOfSyncSkills.Count > 0;
            return outOfSync && !status.HasFingerprint(prefs.DeclinedState);
        }

        /// <summary>
        /// The sorted new optional skills to tell the contributor about: <see cref="SyncStatus.OptionalSkills"/> they have
        /// not seen. Empty while the notice is turned off (<see cref="LocalPrefs.NotifyNewSkills"/>).
        /// </summary>
        public static IReadOnlyList<string> NewSkills(SyncStatus status, LocalPrefs prefs)
        {
            if (status == null || status.NoFolderSelected || !prefs.NotifyNewSkills) return new string[0];
            return SeenSkills.New(prefs, status.OptionalSkills);
        }

        /// <summary>
        /// Whether the lock changed since the last sync for a skill this machine installs. An unchanged file never did.
        /// A changed one did unless the selected skills hash to what the last sync recorded. Prefs written before that
        /// hash was recorded get it from <see cref="Seed"/> at the next editor start; until then, and for an unusable
        /// lockfile, any file change counts.
        /// </summary>
        public static bool LockChanged(SyncStatus status, LocalPrefs prefs) =>
            status.LockHash != prefs.LastSyncedLockHash &&
            (status.SelectedSkillsHash == null || status.SelectedSkillsHash != prefs.LastSyncedSelectedSkillsHash);

        /// <summary>
        /// Fills in, at editor start, what prefs written by an earlier version lack, so upgrading announces nothing:
        /// the seen skills (<see cref="SeenSkills.Seed"/>), and the selected-skills hash of the last sync while the
        /// lockfile is still the one that sync recorded. Without that hash the first lock change after an upgrade
        /// would count even when it touches only skills this machine does not install. A null status (no lockfile)
        /// changes nothing. Call <see cref="LocalPrefs.Save"/> after when it returns true.
        /// </summary>
        /// <returns>Whether the prefs changed.</returns>
        public static bool Seed(LocalPrefs prefs, SyncStatus status)
        {
            var changed = SeenSkills.Seed(prefs, status?.LockedSkills);
            if (status?.SelectedSkillsHash == null || prefs.LastSyncedSelectedSkillsHash != null ||
                status.LockHash != prefs.LastSyncedLockHash) return changed;
            prefs.LastSyncedSelectedSkillsHash = status.SelectedSkillsHash;
            return true;
        }

        /// <summary>
        /// Records a successful sync of the lockfile in <paramref name="status"/>: clears any decline and marks every
        /// locked skill seen. Call <see cref="LocalPrefs.Save"/> after.
        /// </summary>
        public static void RecordSynced(LocalPrefs prefs, SyncStatus status)
        {
            prefs.LastSyncedLockHash = status?.LockHash;
            prefs.LastSyncedSelectedSkillsHash = status?.SelectedSkillsHash;
            prefs.DeclinedState = null;
            SeenSkills.RecordSynced(prefs, status?.LockedSkills);
        }

        /// <summary>
        /// Records that the contributor dismissed the notification for <paramref name="status"/>: declines its out-of-sync
        /// state and marks the new skills it named seen. Call <see cref="LocalPrefs.Save"/> after.
        /// </summary>
        public static void RecordDeclined(LocalPrefs prefs, SyncStatus status)
        {
            SeenSkills.Mark(prefs, NewSkills(status, prefs));
            prefs.DeclinedState = status?.Fingerprint;
        }
    }
}
