using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Hissal.AgentSkillsSync.Editor
{
    /// <summary>
    /// Once per editor session (not per domain reload), first offers to add each skills folder whose
    /// user-scope home appeared since the selection was made (declining is remembered per folder), then
    /// checks whether the project's skills are out of sync and, if so, offers to open the sync window.
    /// Declining keeps it quiet until something changes. With no folder selected it never notifies.
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
                var table = FolderLayout.Default;
                var environment = UserEnvironment.Current;
                var prefs = LocalPrefs.Load(projectRoot);
                OfferNewFolders(prefs, table, environment);

                var selected = FolderSelection.Effective(prefs, table, environment);
                var status = SyncStatus.Read(projectRoot, table, selected, UserScopeScanner.Scan(selected, environment, UserScopeScanner.DefaultSourcesFor(projectRoot)),
                    SkipChoices.From(prefs));
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

        /// <summary>One dialog per folder whose home appeared and was neither selected nor declined; saves each answer.</summary>
        static void OfferNewFolders(LocalPrefs prefs, FolderLayout table, UserEnvironment environment)
        {
            foreach (var folder in FolderSelection.Offers(prefs, table, environment))
            {
                var add = EditorUtility.DisplayDialog(
                    "Agent Skills Sync",
                    $"An agent that reads {folder.RelativePath} ({folder.Label}) now seems to be set up on this machine.\n\n" +
                    $"Add {folder.RelativePath} to the skills folders this project installs into? Nothing is installed until you sync. " +
                    "If you choose Not Now, you won't be asked about this folder again (you can still select it in the sync window).",
                    "Add Folder",
                    "Not Now");
                if (add) FolderSelection.Accept(prefs, table, folder, environment);
                else FolderSelection.Decline(prefs, folder);
                prefs.Save();
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
