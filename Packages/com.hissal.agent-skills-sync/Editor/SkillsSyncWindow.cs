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
    /// </summary>
    public sealed class SkillsSyncWindow : EditorWindow
    {
        const string Title = "Agent Skills Sync";

        HelpBox _status;
        VisualElement _newSources;
        VisualElement _skillList;
        Toggle _consent;
        Button _syncButton;
        HelpBox _summary;
        bool _canSync;
        IReadOnlyDictionary<string, SkillFetchException> _failures = new Dictionary<string, SkillFetchException>();
        Lockfile _lockfile;
        readonly HashSet<string> _confirmedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        [MenuItem("Window/Agent Skills Sync")]
        public static void Open() => GetWindow<SkillsSyncWindow>(Title).Show();

        void CreateGUI()
        {
            titleContent = new GUIContent(Title);
            var root = rootVisualElement;
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = root.style.paddingBottom = 8;

            root.Add(new Label($"Skills locked in {Lockfile.FileName}") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            _status = new HelpBox("", HelpBoxMessageType.None) { style = { display = DisplayStyle.None } };
            root.Add(_status);
            _newSources = new VisualElement { style = { marginTop = 4 } };
            root.Add(_newSources);

            var scroll = new ScrollView { style = { flexGrow = 1, marginTop = 4, marginBottom = 4 } };
            _skillList = new VisualElement();
            scroll.Add(_skillList);
            root.Add(scroll);

            _consent = new Toggle("I trust these sources. Skills run with my agent's permissions.");
            _consent.RegisterValueChangedCallback(_ => UpdateSyncButton());
            root.Add(_consent);

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
            _newSources.Clear();
            _confirmedSources.Clear();
            _consent.SetValueWithoutNotify(false);
            _canSync = false;
            _lockfile = null;
            ShowStatus(null, HelpBoxMessageType.None);

            try
            {
                var lockfile = Lockfile.Load(ProjectRoot);
                var plan = new SkillSync(ProjectRoot, fetcher: null).Plan();
                var newSources = SourceConsent.NewSources(lockfile, LocalPrefs.Load(ProjectRoot));
                var isNew = new HashSet<string>(newSources, StringComparer.OrdinalIgnoreCase);
                foreach (var skill in lockfile.Skills)
                    _skillList.Add(SkillRow(skill, PendingLabel(plan, skill.Name), isNew.Contains(skill.Source),
                        _failures.TryGetValue(skill.Name, out var failure) ? failure : null));
                ShowNewSources(newSources);

                _lockfile = lockfile;

                var removals = plan.Actions.Where(a => a.Kind == PlanActionKind.Remove).Select(a => a.SkillName).Distinct().ToList();
                if (removals.Count > 0)
                    ShowStatus($"No longer locked, removed on Sync: {string.Join(", ", removals)}", HelpBoxMessageType.Info);
                else if (lockfile.Skills.Count == 0)
                    ShowStatus("The lockfile lists no skills.", HelpBoxMessageType.Info);
                _canSync = lockfile.Skills.Count > 0 || plan.HasChanges;
            }
            catch (LockfileException e)
            {
                ShowStatus(e.Message, HelpBoxMessageType.Error);
            }

            UpdateSyncButton();
        }

        /// <summary>What Sync would do to the skill, most significant first.</summary>
        static string PendingLabel(InstallPlan plan, string skillName)
        {
            var kinds = plan.Actions.Where(a => a.SkillName == skillName).Select(a => a.Kind).ToList();
            if (kinds.Contains(PlanActionKind.Install)) return "to install";
            if (kinds.Contains(PlanActionKind.Update)) return "to update";
            if (kinds.Contains(PlanActionKind.Link)) return "to link";
            if (kinds.Contains(PlanActionKind.LeaveForeign)) return "left alone (not managed)";
            return "installed";
        }

        static VisualElement SkillRow(LockedSkill skill, string pending, bool newSource, SkillFetchException failure)
        {
            var container = new VisualElement { style = { marginBottom = 2 } };
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            row.Add(new Label(skill.Name) { style = { width = 200, unityFontStyleAndWeight = FontStyle.Bold } });
            row.Add(new Label(skill.Source) { style = { flexGrow = 1 } });
            if (newSource) row.Add(new Label("NEW SOURCE") { style = { unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.9f, 0.6f, 0.1f), marginRight = 8 } });
            if (failure == null && !GitHubSkillFetcher.CanVerify(skill))
                row.Add(new Label("can't verify lock hash")
                {
                    tooltip = "Locked with a skills.sh server hash, which this tool cannot check. Sync refuses to install or update it.",
                    style = { marginRight = 8 },
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

                EditorUtility.DisplayProgressBar(Title, "Downloading and installing skills...", 0.5f);
                summary = new SkillSync(ProjectRoot, new GitHubSkillFetcher()).Run();
                ShowSummary(Describe(summary), HelpBoxMessageType.Info);
            }
            catch (SyncAbortedException e)
            {
                _failures = e.Failures;
                ShowSummary($"Sync aborted, nothing was changed: {e.Failures.Count} skill(s) could not be fetched. See the errors in the list.", HelpBoxMessageType.Error);
                Debug.LogError(e.Message);
            }
            catch (Exception e) when (e is LockfileException || e is IOException || e is UnauthorizedAccessException)
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
                    RecordSynced(lockfile);
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
        /// and its sources so they are not flagged as new again. Only after a successful sync.
        /// </summary>
        static void RecordSynced(Lockfile lockfile)
        {
            var prefs = LocalPrefs.Load(ProjectRoot);
            StartupCheck.RecordSynced(prefs, SyncStatus.Read(ProjectRoot));
            SourceConsent.RecordSynced(prefs, lockfile);
            prefs.Save();
        }

        static string Describe(SyncSummary summary)
        {
            if (summary.NothingChanged) return "Everything is already in sync.";
            var text = new StringBuilder();
            Line(text, "Installed", summary.Installed);
            Line(text, "Updated", summary.Updated);
            Line(text, "Removed", summary.Removed);
            Line(text, "Skipped (not managed by the tool)", summary.Skipped);
            if (summary.Linked.Count > 0) text.AppendLine($"Linked ({summary.Linked.Count}): {string.Join(", ", summary.Linked)}");
            if (summary.Unlinked.Count > 0) text.AppendLine($"Unlinked ({summary.Unlinked.Count}): {string.Join(", ", summary.Unlinked)}");
            var junctions = summary.LinkedBy(LinkMethod.Junction);
            if (junctions.Count > 0) text.AppendLine($"Linked as junctions (symlinks unavailable): {string.Join(", ", junctions)}");
            var copies = summary.LinkedBy(LinkMethod.Copy);
            if (copies.Count > 0) text.AppendLine($"Linked as plain copies (symlinks and junctions unavailable; re-sync after edits): {string.Join(", ", copies)}");
            return text.ToString().TrimEnd();
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
