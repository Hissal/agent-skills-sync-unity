using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hissal.AgentSkillsSync.Editor
{
    /// <summary>
    /// Lists the locked skills with their sources and installs them on Sync, once the contributor has
    /// ticked the consent box and confirmed every source repo new since the last sync. Consent and
    /// confirmations are held only by this window instance and reset on refresh or reload.
    /// A failed sync marks each skill that could not be fetched or verified with its error.
    /// The skills folders to install into are chosen at the top and stored in local prefs as soon as they change;
    /// until then they are pre-selected by autofill, and nothing is installed before Sync.
    /// Each skill lists its status per selected folder; where that folder's agents already have the skill at user scope,
    /// the row names where it was found and offers a skip toggle (stored in local prefs at once, applied on Sync).
    /// The project's install mode (Latest or Pinned) is shown and edited here, and saved to the committed project settings.
    /// </summary>
    public sealed class SkillsSyncWindow : EditorWindow
    {
        const string Title = "Agent Skills Sync";

        HelpBox _status;
        VisualElement _folders;
        VisualElement _newSources;
        EnumField _mode;
        HelpBox _modeHelp;
        Label _consentNote;
        VisualElement _skillList;
        Toggle _consent;
        Button _syncButton;
        HelpBox _summary;
        bool _canSync;
        InstallMode _installMode;
        IReadOnlyDictionary<string, SkillFetchException> _failures = new Dictionary<string, SkillFetchException>();
        Lockfile _lockfile;
        readonly HashSet<string> _confirmedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        static FolderLayout Layout => FolderLayout.Default;

        /// <summary>This machine's folder selection, user-scope copies and skips, as stored now.</summary>
        static MachineChoices Choices() =>
            MachineChoices.Read(ProjectRoot, LocalPrefs.Load(ProjectRoot), UserEnvironment.Current, Layout);

        [MenuItem("Window/Agent Skills Sync")]
        public static void Open() => GetWindow<SkillsSyncWindow>(Title).Show();

        void CreateGUI()
        {
            titleContent = new GUIContent(Title);
            var root = rootVisualElement;
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = root.style.paddingBottom = 8;

            // Everything above the Sync controls scrolls, so many new-source confirmations or skills never push them off-screen.
            var scroll = new ScrollView { style = { flexGrow = 1, marginBottom = 4 } };
            scroll.Add(new Label("Skills folders to install into on this machine") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            _folders = new VisualElement { style = { marginTop = 2, marginBottom = 8 } };
            scroll.Add(_folders);
            scroll.Add(new Label($"Skills locked in {Lockfile.FileName}") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            _status = new HelpBox("", HelpBoxMessageType.None) { style = { display = DisplayStyle.None } };
            scroll.Add(_status);

            _mode = new EnumField("Install mode", InstallMode.Latest)
            {
                tooltip = $"Saved to {ProjectSyncSettings.RelativePath} and committed with the project.",
                style = { marginTop = 4 },
            };
            _mode.RegisterValueChangedCallback(e => ChangeMode((InstallMode)e.newValue));
            scroll.Add(_mode);
            _modeHelp = new HelpBox("", HelpBoxMessageType.Info);
            scroll.Add(_modeHelp);

            _newSources = new VisualElement { style = { marginTop = 4 } };
            scroll.Add(_newSources);
            _skillList = new VisualElement { style = { marginTop = 4 } };
            scroll.Add(_skillList);
            root.Add(scroll);

            _consent = new Toggle("I trust these sources. Skills run with my agent's permissions.");
            _consent.RegisterValueChangedCallback(_ => UpdateSyncButton());
            root.Add(_consent);
            _consentNote = new Label { style = { whiteSpace = WhiteSpace.Normal, marginLeft = 4 } };
            root.Add(_consentNote);

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
            _syncButton = new Button(Sync) { text = "Sync" };
            buttons.Add(_syncButton);
            buttons.Add(new Button(Refresh) { text = "Refresh" });
            root.Add(buttons);

            _summary = new HelpBox("", HelpBoxMessageType.Info) { style = { display = DisplayStyle.None, marginTop = 8 } };
            root.Add(_summary);

            Refresh();
        }

        void Refresh()
        {
            _skillList.Clear();
            _folders.Clear();
            _newSources.Clear();
            _confirmedSources.Clear();
            _consent.SetValueWithoutNotify(false);
            _canSync = false;
            _lockfile = null;
            ShowStatus(null, HelpBoxMessageType.None);

            var choices = Choices();
            var selected = choices.Selected;
            ShowFolders(selected);

            try
            {
                _installMode = ProjectSyncSettings.Load(ProjectRoot).InstallMode;
                ShowMode();

                var lockfile = Lockfile.Load(ProjectRoot);
                var sync = new SkillSync(ProjectRoot, fetcher: null, choices: choices, mode: _installMode);
                var plan = sync.Plan(lockfile);
                var differs = _installMode == InstallMode.Latest
                    ? new HashSet<string>(sync.InstalledDiffersFromLock(lockfile))
                    : new HashSet<string>();
                var newSources = SourceConsent.NewSources(lockfile, LocalPrefs.Load(ProjectRoot));
                var isNew = new HashSet<string>(newSources, StringComparer.OrdinalIgnoreCase);
                foreach (var skill in lockfile.Skills)
                {
                    _skillList.Add(SkillRow(skill, selected.Count == 0 ? "not installed (no folder selected)" : PendingLabel(plan, skill.Name),
                        _installMode, isNew.Contains(skill.Source), differs.Contains(skill.Name),
                        _failures.TryGetValue(skill.Name, out var failure) ? failure : null));
                    foreach (var folder in selected)
                        _skillList.Add(FolderRow(skill, folder, plan, choices));
                }
                ShowNewSources(newSources);

                _lockfile = lockfile;

                var removals = plan.Actions.Where(a => a.Kind == PlanActionKind.Remove).Select(a => a.SkillName).Distinct().ToList();
                if (removals.Count > 0)
                    ShowStatus($"Removed on Sync: {string.Join(", ", removals)}", HelpBoxMessageType.Info);
                else if (selected.Count == 0)
                    ShowStatus("No skills folder is selected, so Sync installs nothing. Select a folder above to opt in.", HelpBoxMessageType.Info);
                else if (lockfile.Skills.Count == 0)
                    ShowStatus("The lockfile lists no skills.", HelpBoxMessageType.Info);
                _canSync = (selected.Count > 0 && lockfile.Skills.Count > 0) || plan.HasChanges;
            }
            catch (Exception e) when (e is LockfileException || e is ProjectSyncSettingsException)
            {
                ShowStatus(e.Message, HelpBoxMessageType.Error);
            }

            UpdateSyncButton();
        }

        void ShowMode()
        {
            _mode.SetValueWithoutNotify(_installMode);
            if (_installMode == InstallMode.Latest)
            {
                _modeHelp.text = "Latest: installs each skill's current upstream copy. A skill whose upstream changed since it " +
                                 $"was locked is installed and marked \"differs from lock\". {Lockfile.FileName} is never rewritten; " +
                                 "update it with `npx skills update`.";
                _consentNote.text = "Latest mode: skills that differ from the lock will be installed.";
            }
            else
            {
                _modeHelp.text = "Pinned: installs a skill only if its upstream still matches the locked hash, and refuses it " +
                                 $"otherwise (run `npx skills update` and commit {Lockfile.FileName}).";
                _consentNote.text = "";
            }
            _consentNote.style.display = _consentNote.text.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void ChangeMode(InstallMode mode)
        {
            if (mode == _installMode) return;
            try
            {
                new ProjectSyncSettings(mode).Save(ProjectRoot);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                ShowStatus($"Could not save {ProjectSyncSettings.RelativePath}: {e.Message}", HelpBoxMessageType.Error);
                _mode.SetValueWithoutNotify(_installMode);
                return;
            }
            _failures = new Dictionary<string, SkillFetchException>();
            Refresh();
        }

        /// <summary>What Sync would do to the skill, most significant first.</summary>
        static string PendingLabel(InstallPlan plan, string skillName)
        {
            var kinds = plan.Actions.Where(a => a.SkillName == skillName).Select(a => a.Kind).ToList();
            if (kinds.Contains(PlanActionKind.Install)) return "to install";
            if (kinds.Contains(PlanActionKind.Update)) return "to update";
            if (kinds.Contains(PlanActionKind.Link)) return "to link";
            if (kinds.Contains(PlanActionKind.LeaveForeign)) return "left alone (not managed)";
            if (kinds.Contains(PlanActionKind.SkipUserScope))
                return kinds.Contains(PlanActionKind.WarnUserScopeDiffers)
                    ? "skipped where you have your own copy (differs from the lock)"
                    : "skipped where you have your own copy";
            return "installed";
        }

        /// <summary>What Sync does to the skill in one folder, from the plan.</summary>
        static string FolderStatus(InstallPlan plan, LockedSkill skill, SkillsFolder folder, bool skipStored, bool foundAtUserScope)
        {
            var kinds = plan.Actions.Where(a => a.SkillName == skill.Name && a.Folder.RelativePath == folder.RelativePath)
                .Select(a => a.Kind).ToList();
            if (kinds.Contains(PlanActionKind.SkipUserScope))
                return kinds.Contains(PlanActionKind.Unlink) || kinds.Contains(PlanActionKind.Remove)
                    ? "skipped (project copy removed on Sync)"
                    : "skipped (using your copy)";
            if (kinds.Contains(PlanActionKind.Install)) return "to install";
            if (kinds.Contains(PlanActionKind.Update)) return "to update";
            if (kinds.Contains(PlanActionKind.Link)) return "to link";
            if (kinds.Contains(PlanActionKind.LeaveForeign)) return "left alone (not managed)";
            if (skipStored && foundAtUserScope && folder.Role == SkillsFolderRole.Canonical)
                return "installed (kept: another selected folder links to it)";
            return "installed";
        }

        /// <summary>
        /// The skill's status in one selected folder, where its agents already have it at user scope, and a toggle to
        /// skip the project copy there. The toggle is shown while a user-scope copy is found or a skip is stored.
        /// </summary>
        VisualElement FolderRow(LockedSkill skill, SkillsFolder folder, InstallPlan plan, MachineChoices choices)
        {
            var copies = choices.UserScope.CopiesOf(folder, skill.Name);
            var skipStored = choices.Skips.IsSkipped(folder, skill.Name);
            var container = new VisualElement { style = { marginLeft = 16, marginBottom = 2 } };
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            row.Add(new Label(folder.RelativePath) { style = { width = 184 } });
            row.Add(new Label(FolderStatus(plan, skill, folder, skipStored, copies.Count > 0)) { style = { flexGrow = 1 } });
            container.Add(row);

            if (copies.Count > 0)
                container.Add(new Label("The project also provides this skill, and you already have it " +
                                        string.Join(", ", copies.Select(Where)) + ".")
                {
                    tooltip = string.Join("\n", copies.Select(c => c.Agents == null ? c.Path : $"{c.Path} - read by {c.Agents}")),
                    style = { whiteSpace = WhiteSpace.Normal, color = new Color(0.9f, 0.6f, 0.1f) },
                });
            else if (skipStored)
                container.Add(new Label("Skipped, but no user-scope copy was found any more, so the project copy is installed.")
                    { style = { whiteSpace = WhiteSpace.Normal } });

            var differs = plan.Actions.FirstOrDefault(a => a.Kind == PlanActionKind.WarnUserScopeDiffers &&
                                                          a.SkillName == skill.Name && a.Folder.RelativePath == folder.RelativePath);
            if (differs != null)
                container.Add(new HelpBox(DiffersMessage(differs), HelpBoxMessageType.Warning)
                    { tooltip = string.Join("\n", differs.UserScopeCopies.Select(c => c.Path)) });

            if (copies.Count > 0 || skipStored)
            {
                var toggle = new Toggle($"Skip the project copy in {folder.RelativePath} (use mine)")
                {
                    tooltip = "Stored on this machine only. Applies on the next Sync: skipping removes the tool's link or copy " +
                              "from this folder only; un-skipping installs it again.",
                };
                toggle.SetValueWithoutNotify(skipStored);
                toggle.RegisterValueChangedCallback(e => SaveSkip(folder, skill.Name, e.newValue));
                container.Add(toggle);
            }
            return container;
        }

        /// <summary>Where a user-scope copy comes from, as the end of "you already have it ...".</summary>
        static string Where(UserScopeCopy copy) =>
            copy.Plugin != null ? $"provided by plugin {copy.Plugin}" : $"at {copy.FoundIn}";

        static string DiffersMessage(PlanAction warning) =>
            $"Your {warning.SkillName} {string.Join(", ", warning.UserScopeCopies.Select(Where))} differs from the version " +
            $"locked in {Lockfile.FileName}, so agents reading {warning.Folder.RelativePath} don't run what your teammates run.";

        void SaveSkip(SkillsFolder folder, string skillName, bool skip)
        {
            try
            {
                var prefs = LocalPrefs.Load(ProjectRoot);
                SkipChoices.Set(prefs, folder, skillName, skip);
                prefs.Save();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                ShowSummary("Saving the skip choice failed: " + e.Message, HelpBoxMessageType.Error);
                Debug.LogException(e);
            }
            Refresh();
        }

        static VisualElement SkillRow(LockedSkill skill, string pending, InstallMode mode, bool newSource, bool differsFromLock,
            SkillFetchException failure)
        {
            var container = new VisualElement { style = { marginBottom = 2 } };
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            row.Add(new Label(skill.Name) { style = { width = 200, unityFontStyleAndWeight = FontStyle.Bold } });
            row.Add(new Label(skill.Source) { style = { flexGrow = 1 } });
            if (newSource) row.Add(new Label("NEW SOURCE") { style = { unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.9f, 0.6f, 0.1f), marginRight = 8 } });
            if (failure == null && !LockVerification.CanVerify(skill))
                row.Add(mode == InstallMode.Pinned
                    ? new Label("can't verify lock hash")
                    {
                        tooltip = "Locked with a skills.sh server hash, which this tool cannot check. Pinned mode refuses to install or update it.",
                        style = { marginRight = 8 },
                    }
                    : new Label("hash not verifiable")
                    {
                        tooltip = "Locked with a skills.sh server hash; Latest mode installs it without a hash check.",
                        style = { marginRight = 8 },
                    });
            if (differsFromLock)
                row.Add(new Label("differs from lock")
                {
                    tooltip = "The installed copy is upstream's, which changed since it was locked. " +
                              $"Run `npx skills update` and commit {Lockfile.FileName} to lock it.",
                    style = { marginRight = 8, color = new StyleColor(new Color(0.9f, 0.7f, 0.2f)) },
                });
            row.Add(new Label(failure != null ? FailureLabel(failure.Failure) : pending)
            {
                style = { color = failure != null ? new StyleColor(new Color(0.9f, 0.3f, 0.3f)) : new StyleColor(StyleKeyword.Null) },
            });
            container.Add(row);

            if (failure != null)
                container.Add(new HelpBox(failure.Message, HelpBoxMessageType.Error));
            return container;
        }

        static string FailureLabel(SkillFetchFailure failure)
        {
            switch (failure)
            {
                case SkillFetchFailure.Download: return "download failed";
                case SkillFetchFailure.HashMismatch: return "changed since locked";
                case SkillFetchFailure.Unverifiable: return "can't verify lock hash";
                case SkillFetchFailure.SourceUnusable: return "not found in source";
                default: return "fetch failed";
            }
        }

        /// <summary>One toggle per folder-layout entry; a change is stored at once and re-plans.</summary>
        void ShowFolders(IReadOnlyList<SkillsFolder> selected)
        {
            var environment = UserEnvironment.Current;
            var chosen = new HashSet<string>(selected.Select(f => f.RelativePath), StringComparer.Ordinal);
            var toggles = new List<KeyValuePair<SkillsFolder, Toggle>>();
            foreach (var folder in Layout.Folders)
            {
                var toggle = new Toggle($"{folder.RelativePath}  ({folder.Label})")
                {
                    tooltip = "User-scope folders these agents read:\n" + string.Join("\n",
                        folder.UserScopeLocations.Select(l => $"{l} - {l.Agents}")),
                };
                toggle.SetValueWithoutNotify(chosen.Contains(folder.RelativePath));
                toggles.Add(new KeyValuePair<SkillsFolder, Toggle>(folder, toggle));
                _folders.Add(toggle);
            }
            foreach (var pair in toggles)
                pair.Value.RegisterValueChangedCallback(_ =>
                    SaveFolders(toggles.Where(t => t.Value.value).Select(t => t.Key), environment));

            if (!FolderSelection.IsChosen(LocalPrefs.Load(ProjectRoot)))
                _folders.Add(new HelpBox(selected.Count > 0
                    ? "Pre-selected because these agents' user folders exist on this machine. Nothing is installed until you Sync."
                    : "No agent user folders found on this machine, so nothing is pre-selected.", HelpBoxMessageType.Info));
        }

        void SaveFolders(IEnumerable<SkillsFolder> selected, UserEnvironment environment)
        {
            try
            {
                var prefs = LocalPrefs.Load(ProjectRoot);
                FolderSelection.Save(prefs, Layout, selected, environment);
                prefs.Save();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                ShowSummary("Saving the folder selection failed: " + e.Message, HelpBoxMessageType.Error);
                Debug.LogException(e);
            }
            Refresh();
        }

        /// <summary>A warning plus one confirmation toggle per source repo not synced before.</summary>
        void ShowNewSources(IReadOnlyList<string> newSources)
        {
            if (newSources.Count == 0) return;
            _newSources.Add(new HelpBox(
                "These source repos are new since the last sync. Their skills run with your agent's full permissions. " +
                "Confirm each one you trust before syncing.", HelpBoxMessageType.Warning));
            foreach (var source in newSources)
            {
                var toggle = new Toggle($"I trust the new source {source}");
                toggle.RegisterValueChangedCallback(e =>
                {
                    if (e.newValue) _confirmedSources.Add(source);
                    else _confirmedSources.Remove(source);
                    UpdateSyncButton();
                });
                _newSources.Add(toggle);
            }
        }

        bool HasUnconfirmedSources(Lockfile lockfile) =>
            SourceConsent.Unconfirmed(lockfile, LocalPrefs.Load(ProjectRoot), _confirmedSources).Count > 0;

        void UpdateSyncButton() =>
            _syncButton.SetEnabled(_canSync && _consent.value && _lockfile != null && !HasUnconfirmedSources(_lockfile));

        void Sync()
        {
            if (!_consent.value) return;
            _failures = new Dictionary<string, SkillFetchException>();

            SyncSummary summary = null;
            Lockfile lockfile = null;
            MachineChoices choices = null;
            try
            {
                // Re-check against the lockfile as it is now: it may have gained a source since the window listed it.
                lockfile = Lockfile.Load(ProjectRoot);
                if (HasUnconfirmedSources(lockfile))
                {
                    ShowSummary("The lockfile has a new source you have not confirmed. Review it and sync again.", HelpBoxMessageType.Warning);
                    Refresh();
                    return;
                }

                // The consent note described the mode the window listed; a mode changed since (a pull) needs a fresh look.
                var mode = ProjectSyncSettings.Load(ProjectRoot).InstallMode;
                if (mode != _installMode)
                {
                    ShowSummary($"The install mode changed to {mode} since the window listed the skills. Review it and sync again.", HelpBoxMessageType.Warning);
                    Refresh();
                    return;
                }

                EditorUtility.DisplayProgressBar(Title, "Downloading and installing skills...", 0.5f);
                choices = Choices();
                // Run the very instance that passed the check, never a fresh read of the file.
                summary = new SkillSync(ProjectRoot, new GitHubSkillFetcher(mode: mode), choices, mode: mode).Run(lockfile);
                ShowSummary(Describe(summary), summary.UserScopeDiffers.Count > 0 ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info);
            }
            catch (SyncAbortedException e)
            {
                _failures = e.Failures;
                ShowSummary($"Sync aborted, nothing was changed: {e.Failures.Count} skill(s) could not be fetched. See the errors in the list.", HelpBoxMessageType.Error);
                Debug.LogError(e.Message);
            }
            catch (Exception e) when (e is LockfileException || e is ProjectSyncSettingsException || e is IOException || e is UnauthorizedAccessException)
            {
                ShowSummary("Sync failed: " + e.Message, HelpBoxMessageType.Error);
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // Kept out of the sync's try: the sync already changed the project, so failing here must not read as "Sync failed".
            if (summary != null)
            {
                try
                {
                    RecordSynced(lockfile, choices);
                }
                catch (Exception e) when (e is LockfileException || e is IOException || e is UnauthorizedAccessException)
                {
                    ShowSummary(Describe(summary) + "\nWarning: the sync finished, but saving the out-of-sync notification state failed: "
                        + e.Message, HelpBoxMessageType.Warning);
                    Debug.LogException(e);
                }
            }

            Refresh();
        }

        /// <summary>
        /// Remembers the synced lockfile so the startup check stays quiet until something changes,
        /// its sources so they are not flagged as new again, and the folder selection synced into (which
        /// confirms an autofilled one). Only after a successful sync.
        /// </summary>
        static void RecordSynced(Lockfile lockfile, MachineChoices choices)
        {
            var prefs = LocalPrefs.Load(ProjectRoot);
            FolderSelection.Save(prefs, Layout, choices.Selected, UserEnvironment.Current);
            StartupCheck.RecordSynced(prefs, SyncStatus.Read(ProjectRoot, choices));
            SourceConsent.RecordSynced(prefs, lockfile);
            prefs.Save();
        }

        static string Describe(SyncSummary summary)
        {
            var text = new StringBuilder();
            if (summary.NothingChanged) text.AppendLine("Everything is already in sync.");
            else DescribeChanges(summary, text);
            if (summary.DiffersFromLock.Count > 0)
                text.AppendLine($"Differs from lock ({summary.DiffersFromLock.Count}): {string.Join(", ", summary.DiffersFromLock)}. " +
                                $"Installed from upstream; run `npx skills update` and commit {Lockfile.FileName} to lock them.");
            foreach (var warning in summary.UserScopeDiffers)
                text.AppendLine("Warning: " + DiffersMessage(warning));
            return text.ToString().TrimEnd();
        }

        static void DescribeChanges(SyncSummary summary, StringBuilder text)
        {
            Line(text, "Installed", summary.Installed);
            Line(text, "Updated", summary.Updated);
            Line(text, "Removed", summary.Removed);
            Line(text, "Skipped (not managed by the tool)", summary.Skipped);
            if (summary.SkippedForUserScope.Count > 0)
                text.AppendLine($"Skipped in favour of your user-scope copy ({summary.SkippedForUserScope.Count}): {string.Join(", ", summary.SkippedForUserScope)}");
            if (summary.Linked.Count > 0) text.AppendLine($"Linked ({summary.Linked.Count}): {string.Join(", ", summary.Linked)}");
            if (summary.Unlinked.Count > 0) text.AppendLine($"Unlinked ({summary.Unlinked.Count}): {string.Join(", ", summary.Unlinked)}");
            var junctions = summary.LinkedBy(LinkMethod.Junction);
            if (junctions.Count > 0) text.AppendLine($"Linked as junctions (symlinks unavailable): {string.Join(", ", junctions)}");
            var copies = summary.LinkedBy(LinkMethod.Copy);
            if (copies.Count > 0) text.AppendLine($"Linked as plain copies (symlinks and junctions unavailable; re-sync after edits): {string.Join(", ", copies)}");
        }

        static void Line(StringBuilder text, string label, IReadOnlyList<string> names)
        {
            text.Append($"{label}: {names.Count}");
            if (names.Count > 0) text.Append(" (").Append(string.Join(", ", names)).Append(')');
            text.AppendLine();
        }

        void ShowStatus(string message, HelpBoxMessageType type)
        {
            _status.text = message ?? "";
            _status.messageType = type;
            _status.style.display = message == null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void ShowSummary(string message, HelpBoxMessageType type)
        {
            _summary.text = message;
            _summary.messageType = type;
            _summary.style.display = DisplayStyle.Flex;
        }
    }
}
