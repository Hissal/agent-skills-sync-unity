using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Hissal.AgentSkillsSync.Editor
{
    /// <summary>
    /// Once per editor session (not per domain reload), checks whether the project's skills are out of
    /// sync and, if so, offers to open the sync window. Declining keeps it quiet until something changes.
    /// </summary>
    [InitializeOnLoad]
    static class StartupNotifier
    {
        const string CheckedKey = "Hissal.AgentSkillsSync.StartupChecked";

        static StartupNotifier()
        {
            if (Application.isBatchMode || AssetDatabase.IsAssetImportWorkerProcess()) return;
            if (SessionState.GetBool(CheckedKey, false)) return;
            SessionState.SetBool(CheckedKey, true);
            EditorApplication.delayCall += Check;
        }

        static void Check()
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            try
            {
                var status = SyncStatus.Read(projectRoot);
                var prefs = LocalPrefs.Load(projectRoot);
                if (!StartupCheck.ShouldNotify(status, prefs)) return;

                if (EditorUtility.DisplayDialog(
                        "Agent Skills Sync",
                        Describe(status, prefs),
                        "Open Sync Window",
                        "Not Now"))
                {
                    SkillsSyncWindow.Open();
                    return;
                }

                StartupCheck.RecordDeclined(prefs, status);
                prefs.Save();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("Agent Skills Sync: startup check failed: " + e.Message);
            }
        }

        static string Describe(SyncStatus status, LocalPrefs prefs)
        {
            var reason = prefs.LastSyncedLockHash == null
                ? "This project's agent skills have not been synced on this machine yet."
                : status.LockHash != prefs.LastSyncedLockHash
                ? $"{Lockfile.FileName} changed since the last sync."
                : $"Some locked skills are missing: {string.Join(", ", status.MissingSkills)}.";
            return reason + "\n\nOpen the sync window to review and install them? If you choose Not Now, " +
                   "you won't be asked again until something changes.";
        }
    }
}
