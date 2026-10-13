using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Hissal.AgentSkillsSync.Editor
{
    /// <summary>
    /// Once per editor session (not per domain reload), first offers to add each skills folder whose
    /// user-scope home appeared since the selection was made (declining is remembered per folder), then
    /// checks whether the project's skills are out of sync or a new optional skill was locked and, if so, offers to
    /// open the sync window in one dialog. Declining keeps each part quiet until it changes. With no folder selected
    /// it never notifies. Prefs an earlier version wrote are seeded here (<see cref="StartupCheck.Seed"/>), so upgrading
    /// announces neither the skills already locked nor a later lock change this machine does not install.
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
            var unityRoot = SkillsSyncWindow.UnityProjectRoot;
            var skillsRoot = SkillsSyncWindow.SkillsRoot;
            try
            {
                var layout = FolderLayout.Default;
                var environment = UserEnvironment.Current;
                var prefs = LocalPrefs.Load(unityRoot);
                OfferNewFolders(prefs, layout, environment);

                var groups = ProjectSyncSettings.Load(unityRoot).Groups;
                var status = SyncStatus.Read(skillsRoot, MachineChoices.Read(skillsRoot, prefs, environment, layout, groups), unityRoot);
                if (StartupCheck.Seed(prefs, status)) prefs.Save();
                if (!StartupCheck.ShouldNotify(status, prefs)) return;

                if (EditorUtility.DisplayDialog(
                        "Agent Skills Sync",
                        SyncText.StartupMessage(status, prefs),
                        "Open Sync Window",
                        "Not Now"))
                {
                    SkillsSyncWindow.Open();
                    return;
                }

                StartupCheck.RecordDeclined(prefs, status);
                prefs.Save();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ProjectSyncSettingsException)
            {
                Debug.LogWarning("Agent Skills Sync: startup check failed: " + e.Message);
            }
        }

        /// <summary>One dialog per folder whose home appeared and was neither selected nor declined; saves each answer.</summary>
        static void OfferNewFolders(LocalPrefs prefs, FolderLayout layout, UserEnvironment environment)
        {
            foreach (var folder in FolderSelection.Offers(prefs, layout, environment))
            {
                var add = EditorUtility.DisplayDialog(
                    "Agent Skills Sync",
                    $"An agent that reads {folder.RelativePath} ({folder.Label}) now seems to be set up on this machine.\n\n" +
                    $"Add {folder.RelativePath} to the skills folders this project installs into? Nothing is installed until you sync. " +
                    "If you choose Not Now, you won't be asked about this folder again (you can still select it in the sync window).",
                    "Add Folder",
                    "Not Now");
                if (add) FolderSelection.Accept(prefs, layout, folder, environment);
                else FolderSelection.Decline(prefs, folder);
                prefs.Save();
            }
        }
    }
}
