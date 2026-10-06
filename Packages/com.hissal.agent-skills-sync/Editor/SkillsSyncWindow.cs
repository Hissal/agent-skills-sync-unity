using System;
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
    /// ticked the consent box. Consent is held only by this window instance and resets on reload.
    /// </summary>
    public sealed class SkillsSyncWindow : EditorWindow
    {
        const string Title = "Agent Skills Sync";

        HelpBox _status;
        VisualElement _skillList;
        Toggle _consent;
        Button _syncButton;
        HelpBox _summary;
        bool _canSync;

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
            _consent.SetValueWithoutNotify(false);
            _canSync = false;
            ShowStatus(null, HelpBoxMessageType.None);

            try
            {
                var lockfile = Lockfile.Load(ProjectRoot);
                var plan = new SkillSync(ProjectRoot, fetcher: null).Plan();
                var pending = plan.Actions.Select(a => a.SkillName).ToHashSet();

                foreach (var skill in lockfile.Skills)
                    _skillList.Add(SkillRow(skill, pending.Contains(skill.Name)));

                if (lockfile.Skills.Count == 0) ShowStatus("The lockfile lists no skills.", HelpBoxMessageType.Info);
                _canSync = lockfile.Skills.Count > 0;
            }
            catch (LockfileException e)
            {
                ShowStatus(e.Message, HelpBoxMessageType.Error);
            }

            UpdateSyncButton();
        }

        static VisualElement SkillRow(LockedSkill skill, bool pending)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2 } };
            row.Add(new Label(skill.Name) { style = { width = 200, unityFontStyleAndWeight = FontStyle.Bold } });
            row.Add(new Label(skill.Source) { style = { flexGrow = 1 } });
            row.Add(new Label(pending ? "to install" : "installed"));
            return row;
        }

        void UpdateSyncButton() => _syncButton.SetEnabled(_canSync && _consent.value);

        void Sync()
        {
            if (!_consent.value) return;

            try
            {
                EditorUtility.DisplayProgressBar(Title, "Downloading and installing skills...", 0.5f);
                var summary = new SkillSync(ProjectRoot, new GitHubSkillFetcher()).Run();
                ShowSummary(Describe(summary), HelpBoxMessageType.Info);
            }
            catch (Exception e) when (e is LockfileException || e is SkillFetchException || e is IOException || e is UnauthorizedAccessException)
            {
                ShowSummary("Sync failed: " + e.Message, HelpBoxMessageType.Error);
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Refresh();
        }

        static string Describe(SyncSummary summary)
        {
            if (summary.NothingChanged) return "Everything is already in sync.";
            var text = new StringBuilder();
            if (summary.Installed.Count > 0) text.AppendLine($"Installed ({summary.Installed.Count}): {string.Join(", ", summary.Installed)}");
            if (summary.Linked.Count > 0) text.AppendLine($"Linked ({summary.Linked.Count}): {string.Join(", ", summary.Linked)}");
            return text.ToString().TrimEnd();
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
