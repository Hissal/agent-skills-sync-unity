using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// Each skill has an install checkbox (stored in local prefs at once, applied on Sync); unchecking it opts this machine
    /// out of the skill, which then shows no folder rows. When the project settings define skill groups, the skills are
    /// listed in one collapsible section per group, in file order, with ungrouped skills in a trailing "Other" section.
    /// A section's own checkbox selects or deselects all its skills; a skill in several groups is listed in each, and
    /// every row of it shows the one stored choice.
    /// A skill locked since the contributor last looked that this machine leaves out by default is tagged NEW until they
    /// sync or choose for it; a toggle (stored in local prefs) turns the startup notice about such skills off.
    /// Each selected skill lists its status per selected folder; where that folder's agents already have the skill at user scope,
    /// the row names where it was found and offers an install-anyway toggle (stored in local prefs at once, applied on Sync).
    /// The project's install mode (Latest or Pinned) is shown and edited here, and saved to the committed project settings.
    /// In a checkout following the repository's shared choices (as a restore leaves it), each choice stored in local prefs
    /// also becomes the shared one (<see cref="SharedChoices"/>).
    /// </summary>
    public sealed class SkillsSyncWindow : EditorWindow
    {
        const string Title = "Agent Skills Sync";

        HelpBox _status;
        Label _lockPath;
        VisualElement _folders;
        VisualElement _worktrees;
        VisualElement _newSources;
        EnumField _mode;
        HelpBox _modeHelp;
        Toggle _notifyNewSkills;
        Label _consentNote;
        VisualElement _skillList;
        Toggle _consent;
        Button _syncButton;
        HelpBox _summary;
        bool _canSync;
        InstallMode _installMode;
        IReadOnlyDictionary<string, SkillFetchException> _failures = new Dictionary<string, SkillFetchException>();
        Lockfile _lockfile;
        SkillSelection _skills = SkillSelection.All;
        SkillGroups _groups = SkillGroups.None;
        // Names of the group sections the contributor collapsed, kept across refreshes.
        readonly HashSet<string> _collapsed = new HashSet<string>(StringComparer.Ordinal);
        // The same for the ungrouped section, which has no name.
        bool _ungroupedCollapsed;
        bool? _worktreesExpanded;
        readonly HashSet<string> _confirmedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The Unity project folder: holds this machine's local prefs and the committed project settings.</summary>
        internal static string UnityProjectRoot => Path.GetDirectoryName(Application.dataPath);

        /// <summary>
        /// The folder holding <c>skills-lock.json</c> (see <see cref="Lockfile.FindRoot"/>), where the skills folders are
        /// installed; the Unity project folder when no lock is found.
        /// </summary>
        internal static string SkillsRoot => Lockfile.FindRoot(UnityProjectRoot) ?? UnityProjectRoot;

        static FolderLayout Layout => FolderLayout.Default;

        /// <summary>This checkout's prefs, with the shared choices while it follows them (<see cref="SharedChoices"/>).</summary>
        static LocalPrefs LoadPrefs() => SharedChoices.Load(UnityProjectRoot, UserEnvironment.Current);

        /// <summary>Saves the prefs; a choice made in a checkout following the shared choices becomes the shared one.</summary>
        static void SavePrefs(LocalPrefs prefs) => SharedChoices.Save(prefs, UnityProjectRoot, UserEnvironment.Current);

        /// <summary>
        /// The skills folders this machine installs into, as stored now: shown even when the lock or the project
        /// settings cannot be read. Every other machine choice comes from the <see cref="SkillsSyncService"/>.
        /// </summary>
        static IReadOnlyList<SkillsFolder> SelectedFolders() =>
            FolderSelection.Effective(LoadPrefs(), Layout, UserEnvironment.Current);

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
            _worktrees = new VisualElement { style = { marginBottom = 8 } };
            scroll.Add(_worktrees);
            scroll.Add(new Label($"Skills locked in {Lockfile.FileName}") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            _lockPath = new Label { selection = { isSelectable = true }, style = { whiteSpace = WhiteSpace.Normal, marginLeft = 4 } };
            scroll.Add(_lockPath);
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

            _notifyNewSkills = new Toggle("Tell me at editor start when a new optional skill is locked")
            {
                tooltip = "An optional skill is one this machine does not install unless you select it. " +
                          "Stored on this machine only.",
                style = { marginTop = 4 },
            };
            _notifyNewSkills.RegisterValueChangedCallback(e => SaveNotifyNewSkills(e.newValue));
            scroll.Add(_notifyNewSkills);

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
            ShowLockPath();

            ShowWorktrees();
            var selected = SelectedFolders();
            ShowFolders(selected);
            _notifyNewSkills.SetValueWithoutNotify(LoadPrefs().NotifyNewSkills);

            try
            {
                var service = new SkillsSyncService(UnityProjectRoot);
                _installMode = service.Mode;
                ShowMode();

                var lockfile = service.Lock;
                var plan = service.Plan();
                var differs = _installMode == InstallMode.Latest
                    ? new HashSet<string>(service.InstalledDiffersFromLock())
                    : new HashSet<string>();
                var newSources = service.NewSources;
                var isNew = new HashSet<string>(newSources, StringComparer.OrdinalIgnoreCase);
                var choices = service.Choices;
                var skills = choices.Skills;
                var newSkills = new HashSet<string>(service.NewSkills, StringComparer.Ordinal);
                _groups = service.Groups;
                var sections = _groups.Sections(lockfile);
                var unknown = SyncText.UnknownGroupSkillsMessage(sections);
                if (unknown.Length > 0) _skillList.Add(new HelpBox(unknown, HelpBoxMessageType.Warning));
                foreach (var section in sections)
                {
                    // Without groups the list stays flat, as it was before groups existed.
                    var rows = _groups.Groups.Count == 0 ? _skillList : GroupSection(section, skills);
                    foreach (var skill in section.Skills)
                    {
                        var name = skill.Name;
                        var install = skills.IsSelected(name);
                        var pending = !install ? SyncText.UnselectedLabel(plan, name, skills)
                            : selected.Count == 0 ? "not installed (no folder selected)"
                            : SyncText.PendingLabel(plan, name);
                        rows.Add(SkillRow(skill, pending, _installMode, install && isNew.Contains(skill.Source), differs.Contains(name),
                            install && _failures.TryGetValue(name, out var failure) ? failure : null,
                            install, value => SaveSkillsSelected(new[] { name }, value), newSkills.Contains(name)));
                        // A skill this machine does not install is in no folder, so it has no per-folder status.
                        if (!install) continue;
                        foreach (var folder in selected)
                            rows.Add(FolderRow(skill, folder, plan, choices));
                    }
                    foreach (var unsupported in section.Unsupported)
                        rows.Add(UnsupportedRow(unsupported));
                }
                ShowNewSources(newSources);
                if (lockfile.Unsupported.Count > 0)
                    _skillList.Add(new HelpBox(SyncText.UnsupportedMessage(lockfile.Unsupported), HelpBoxMessageType.Info));

                _lockfile = lockfile;
                _skills = skills;

                var removals = plan.Actions.Where(a => a.Kind == PlanActionKind.Remove).Select(a => a.SkillName).Distinct().ToList();
                if (removals.Count > 0)
                    ShowStatus($"Removed on Sync: {string.Join(", ", removals)}", HelpBoxMessageType.Info);
                else if (selected.Count == 0)
                    ShowStatus("No skills folder is selected, so Sync installs nothing. Select a folder above to opt in.", HelpBoxMessageType.Info);
                else if (lockfile.Skills.Count == 0)
                    ShowStatus(lockfile.Unsupported.Count == 0 ? "The lockfile lists no skills." : "The lockfile lists no skills this tool installs.",
                        HelpBoxMessageType.Info);
                _canSync = (selected.Count > 0 && lockfile.Skills.Count > 0) || plan.HasChanges;
            }
            catch (Exception e) when (e is LockfileException || e is ProjectSyncSettingsException)
            {
                ShowStatus(e.Message, HelpBoxMessageType.Error);
            }

            UpdateSyncButton();
        }

        /// <summary>Names the lockfile in use, since it may be the one above the Unity project folder.</summary>
        void ShowLockPath()
        {
            var root = Lockfile.FindRoot(UnityProjectRoot);
            _lockPath.text = root == null ? "" : "Using " + Path.GetFullPath(Path.Combine(root, Lockfile.FileName));
            _lockPath.style.display = root == null ? DisplayStyle.None : DisplayStyle.Flex;
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

        void ShowWorktrees()
        {
            _worktrees.Clear();
            var identity = RepositoryIdentity.Find(UnityProjectRoot);
            if (identity == null)
            {
                _worktrees.Add(new Label(SyncText.WorktreesOutsideGit) { style = { whiteSpace = WhiteSpace.Normal } });
                return;
            }
            var section = new Foldout { text = SyncText.WorktreesTitle, value = _worktreesExpanded ?? false };
            section.RegisterValueChangedCallback(e => { if (e.target == section) _worktreesExpanded = e.newValue; });
            _worktrees.Add(section);
            section.Add(new Label(SyncText.SharedChoicesExplanation) { style = { whiteSpace = WhiteSpace.Normal } });
            section.Add(new Label(SyncText.WorktreeIdentity(identity)) { style = { whiteSpace = WhiteSpace.Normal } });
            try
            {
                section.SetValueWithoutNotify(_worktreesExpanded ?? identity.Checkouts().Count > 1);
                var setup = WorktreeSetupStatus.Read(UnityProjectRoot);
                section.Add(new Label(SyncText.WorktreeScope(setup)) { style = { whiteSpace = WhiteSpace.Normal } });
                var scope = new Toggle(SyncText.FollowSharedChoices) { value = setup.FollowsShared };
                scope.RegisterValueChangedCallback(e => ChangeWorktreeSetup(() =>
                {
                    WorktreeSetup.SetScope(UnityProjectRoot, e.newValue ? ChoiceScope.Shared : ChoiceScope.Worktree);
                    return e.newValue ? SyncText.UsingSharedChoices : SyncText.KeepingOwnChoices;
                }));
                section.Add(scope);
                if (setup.HasSharedChoices)
                {
                    section.Add(new Label(SyncText.WorktreeDifferences(setup.Differences)) { style = { whiteSpace = WhiteSpace.Normal } });
                    section.Add(new Button(() => ChangeWorktreeSetup(() =>
                    {
                        WorktreeSetup.UseShared(UnityProjectRoot);
                        return SyncText.UsingSharedChoices;
                    })) { text = SyncText.UseSharedChoices });
                }
                section.Add(new Button(() => ChangeWorktreeSetup(() =>
                {
                    WorktreeSetup.Share(UnityProjectRoot);
                    return SyncText.SharedTheseChoices;
                })) { text = SyncText.ShareTheseChoices });
                if (setup.Candidates.Count > 0)
                    section.Add(new HelpBox(SyncText.BaselineExplanation, HelpBoxMessageType.Warning));
                foreach (var candidate in setup.Candidates)
                {
                    var row = new Foldout { text = candidate.Project, value = true };
                    row.Add(new Label(SyncText.BaselineDifferences(candidate.Differences)) { style = { whiteSpace = WhiteSpace.Normal } });
                    row.Add(new Button(() => ChangeWorktreeSetup(() =>
                    {
                        WorktreeSetup.AdoptBaseline(UnityProjectRoot, candidate.Project);
                        return SyncText.AdoptedBaseline(candidate.Project);
                    })) { text = SyncText.MakeBaseline });
                    section.Add(row);
                }
                var snapshots = new Foldout { text = SyncText.SelectedSkillSnapshots, value = setup.Snapshots.Any(s => !s.Available) };
                foreach (var snapshot in setup.Snapshots)
                    snapshots.Add(new Label(SyncText.WorktreeSnapshot(snapshot)) { style = { whiteSpace = WhiteSpace.Normal } });
                if (setup.Snapshots.Count == 0) snapshots.Add(new Label(SyncText.NoSelectedSkills));
                section.Add(snapshots);
                if (setup.Snapshots.Any(s => !s.Available))
                    section.Add(new Button(() => ChangeWorktreeSetup(() =>
                        SyncText.RecordedInstalledSkills(SnapshotRecorder.Import(UnityProjectRoot)))) { text = SyncText.RecordInstalledSkills });
                section.Add(new HelpBox(SyncText.NewWorktreeRestore(setup.NewWorktree),
                    setup.NewWorktree.Succeeded ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning));
            }
            catch (Exception e) when (e is LockfileException || e is ProjectSyncSettingsException || e is RepositoryException ||
                                      e is IOException || e is UnauthorizedAccessException)
            {
                section.Add(new HelpBox(e.Message, HelpBoxMessageType.Warning));
            }
            ShowRestoreHook(section);
        }

        void ChangeWorktreeSetup(Func<string> action)
        {
            try
            {
                var message = action();
                Refresh();
                ShowSummary(message, HelpBoxMessageType.Info);
            }
            catch (Exception e)
            {
                Refresh();
                ShowSummary(SyncText.WorktreeActionFailed(e.Message), HelpBoxMessageType.Error);
            }
        }

        void ShowRestoreHook(VisualElement parent)
        {
            parent.Add(new Label(SyncText.AutomaticRestore) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 4 } });
            parent.Add(new Label("Restore skills in new worktrees, for every launcher. Install once per clone on this machine.")
                { style = { whiteSpace = WhiteSpace.Normal } });
            try
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SkillsSyncWindow).Assembly);
                var command = new RestoreCommand(package.resolvedPath, package.version, UserEnvironment.Current);
                var hook = new RestoreHook(UnityProjectRoot, command.PathForCommand, package.version);
                var report = hook.Status();
                var sdk = command.HasSdk();
                parent.Add(new HelpBox(report.Message, report.Status == RestoreHookStatus.Blocked || report.Status == RestoreHookStatus.Outdated
                    ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info));
                if (!sdk && !File.Exists(command.PathForCommand))
                {
                    parent.Add(new HelpBox("No .NET SDK found. Install the .NET 8 SDK or newer to prepare the restore command.", HelpBoxMessageType.Warning));
                    parent.Add(new Button(() => Application.OpenURL("https://dotnet.microsoft.com/download")) { text = "Get the .NET SDK" });
                }
                var install = new Button(() => ChangeHook(hook, command, true))
                { text = report.Status == RestoreHookStatus.Outdated ? "Update restore hook" : "Restore skills in new worktrees" };
                install.SetEnabled(report.Status != RestoreHookStatus.Blocked && report.Status != RestoreHookStatus.Installed &&
                    (sdk || File.Exists(command.PathForCommand)));
                parent.Add(install);
                var uninstall = new Button(() => ChangeHook(hook, command, false)) { text = "Uninstall for this project" };
                uninstall.SetEnabled(report.Status == RestoreHookStatus.Installed || report.Status == RestoreHookStatus.Outdated);
                parent.Add(uninstall);
                parent.Add(new Button(() => Application.OpenURL("https://github.com/Hissal/agent-skills-sync-unity#new-worktrees")) { text = "New worktree setup and limitations" });
            }
            catch (Exception e) when (e is RepositoryException || e is IOException || e is UnauthorizedAccessException || e is System.ComponentModel.Win32Exception)
            {
                parent.Add(new HelpBox(e.Message, HelpBoxMessageType.Warning));
            }
        }

        void ChangeHook(RestoreHook hook, RestoreCommand command, bool install)
        {
            try
            {
                if (install)
                {
                    EditorUtility.DisplayProgressBar(Title, "Preparing the restore command...", 0.5f);
                    command.Build();
                }
                var report = install ? hook.Install() : hook.Uninstall();
                ShowSummary(report.Message, report.Status == RestoreHookStatus.Blocked ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info);
            }
            catch (Exception e)
            {
                ShowSummary("Could not change the restore hook: " + e.Message, HelpBoxMessageType.Error);
            }
            finally { EditorUtility.ClearProgressBar(); }
            ShowWorktrees();
        }

        void ChangeMode(InstallMode mode)
        {
            if (mode == _installMode) return;
            try
            {
                new ProjectSyncSettings(mode).Save(UnityProjectRoot);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ProjectSyncSettingsException)
            {
                ShowStatus($"Could not save {ProjectSyncSettings.RelativePath}: {e.Message}", HelpBoxMessageType.Error);
                _mode.SetValueWithoutNotify(_installMode);
                return;
            }
            _failures = new Dictionary<string, SkillFetchException>();
            Refresh();
        }

        /// <summary>
        /// The skill's status in one selected folder, where its agents already have it at user scope, and a toggle to
        /// install the project copy anyway. The toggle is shown while a user-scope copy is found.
        /// </summary>
        VisualElement FolderRow(LockedSkill skill, SkillsFolder folder, InstallPlan plan, MachineChoices choices)
        {
            var copies = choices.UserScope.CopiesOf(folder, skill.Name);
            var installAnywayStored = choices.InstallAnyway.IsInstalledAnyway(folder, skill.Name);
            var container = new VisualElement { style = { marginLeft = 16, marginBottom = 2 } };
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            row.Add(new Label(folder.RelativePath) { style = { width = 184 } });
            row.Add(new Label(SyncText.FolderStatus(plan, skill, folder, installAnywayStored, copies)) { style = { flexGrow = 1 } });
            container.Add(row);

            if (copies.Count > 0)
                container.Add(new Label("The project also provides this skill, and you already have it " +
                                        string.Join(", ", copies.Select(SyncText.Where)) + ".")
                {
                    tooltip = string.Join("\n", copies.Select(c => c.Agents == null ? c.Path : $"{c.Path} - read by {c.Agents}")),
                    style = { whiteSpace = WhiteSpace.Normal, color = new Color(0.9f, 0.6f, 0.1f) },
                });
            var differs = plan.Actions.FirstOrDefault(a => a.Kind == PlanActionKind.WarnUserScopeDiffers &&
                                                          a.SkillName == skill.Name && a.Folder.RelativePath == folder.RelativePath);
            if (differs != null)
                container.Add(new HelpBox(SyncText.DiffersMessage(differs), HelpBoxMessageType.Warning)
                    { tooltip = string.Join("\n", differs.UserScopeCopies.Select(c => c.Path)) });

            if (copies.Count > 0)
            {
                var toggle = new Toggle($"Install the project copy in {folder.RelativePath} anyway")
                {
                    tooltip = "Stored on this machine only. Applies on the next Sync in this folder. " +
                              "Leave unchecked to use your user-scope copy.",
                };
                toggle.SetValueWithoutNotify(installAnywayStored);
                toggle.RegisterValueChangedCallback(e => SaveInstallAnyway(folder, skill.Name, e.newValue));
                container.Add(toggle);
                var coverage = SyncText.SkipCoverageMessage(folder, copies);
                if (coverage.Length > 0)
                    container.Add(new HelpBox(coverage, HelpBoxMessageType.Warning));
            }
            return container;
        }

        void SaveInstallAnyway(SkillsFolder folder, string skillName, bool installAnyway)
        {
            try
            {
                var prefs = LoadPrefs();
                InstallAnywayChoices.Set(prefs, folder, skillName, installAnyway);
                SavePrefs(prefs);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                ShowSummary("Saving the install-anyway choice failed: " + e.Message, HelpBoxMessageType.Error);
                Debug.LogException(e);
            }
            Refresh();
        }

        /// <summary>
        /// A collapsible section for one skill group (or the ungrouped skills), added to the skill list. Its checkbox is
        /// checked when this machine installs every skill of the section, mixed when only some, and sets them all at once.
        /// </summary>
        /// <returns>The element the section's skill rows go into.</returns>
        VisualElement GroupSection(SkillGroupSection section, SkillSelection skills)
        {
            var names = section.Skills.Select(s => s.Name).ToList();
            var selectedCount = names.Count(skills.IsSelected);
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.FlexStart, marginTop = 2 } };
            var all = new Toggle
            {
                tooltip = "Install every skill of this group on this machine, or none of them. Stored on this machine only; applies on the next Sync.",
                showMixedValue = selectedCount > 0 && selectedCount < names.Count,
                style = { marginRight = 4 },
            };
            all.SetValueWithoutNotify(names.Count > 0 && selectedCount == names.Count);
            all.SetEnabled(names.Count > 0);
            all.RegisterValueChangedCallback(e => SaveSkillsSelected(names, e.newValue));
            row.Add(all);

            var collapsed = section.Name == null ? _ungroupedCollapsed : _collapsed.Contains(section.Name);
            var foldout = new Foldout { text = SyncText.GroupHeader(section, skills), value = !collapsed, style = { flexGrow = 1 } };
            foldout.RegisterValueChangedCallback(e =>
            {
                // Rows inside the foldout raise their own change events, which bubble up to it.
                if (e.target != foldout) return;
                if (section.Name == null) _ungroupedCollapsed = !e.newValue;
                else if (e.newValue) _collapsed.Remove(section.Name);
                else _collapsed.Add(section.Name);
            });
            row.Add(foldout);
            _skillList.Add(row);
            return foldout;
        }

        /// <summary>A lock entry a group lists but this tool does not install: named, with no checkbox.</summary>
        static VisualElement UnsupportedRow(UnsupportedSkill skill)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2, marginLeft = 20 } };
            row.Add(new Label(skill.Name) { style = { width = 200, unityFontStyleAndWeight = FontStyle.Bold } });
            row.Add(new Label(SyncText.UnsupportedInGroupLabel(skill)) { style = { flexGrow = 1 } });
            return row;
        }

        void SaveNotifyNewSkills(bool notify)
        {
            try
            {
                var prefs = LoadPrefs();
                prefs.NotifyNewSkills = notify;
                SavePrefs(prefs);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                ShowSummary("Saving the new-skill notice setting failed: " + e.Message, HelpBoxMessageType.Error);
                Debug.LogException(e);
            }
            Refresh();
        }

        /// <summary>
        /// Stores the install choice for each skill against its own default, so only deviations are kept. A skill the
        /// contributor chose for is no longer new, whatever they chose.
        /// </summary>
        void SaveSkillsSelected(IReadOnlyList<string> skillNames, bool selected)
        {
            try
            {
                var prefs = LoadPrefs();
                SkillSelection.Set(prefs, skillNames, selected, _groups);
                SeenSkills.Mark(prefs, skillNames);
                SavePrefs(prefs);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                ShowSummary("Saving the skill choice failed: " + e.Message, HelpBoxMessageType.Error);
                Debug.LogException(e);
            }
            Refresh();
        }

        /// <param name="install">Whether this machine installs the skill: the row's checkbox. <paramref name="setInstall"/> stores a change.</param>
        /// <param name="newSkill">Whether the skill is new to the contributor (see <see cref="SeenSkills"/>): tagged NEW.</param>
        static VisualElement SkillRow(LockedSkill skill, string pending, InstallMode mode, bool newSource, bool differsFromLock,
            SkillFetchException failure, bool install, Action<bool> setInstall, bool newSkill)
        {
            var container = new VisualElement { style = { marginBottom = 2 } };
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var toggle = new Toggle
            {
                tooltip = "Install this skill on this machine. Stored on this machine only; applies on the next Sync. " +
                          "Uncheck to opt out: Sync then removes the tool's copy and links of it. " +
                          "Unchecked without a choice of yours when the project's groups do not select it by default.",
                style = { marginRight = 4 },
            };
            toggle.SetValueWithoutNotify(install);
            toggle.RegisterValueChangedCallback(e => setInstall(e.newValue));
            row.Add(toggle);
            row.Add(new Label(skill.Name) { style = { width = 200, unityFontStyleAndWeight = FontStyle.Bold } });
            row.Add(new Label(skill.Source) { style = { flexGrow = 1 } });
            if (newSkill)
                row.Add(new Label("NEW")
                {
                    tooltip = "A newly locked optional skill you have not reviewed yet. This machine does not install it unless you check it.",
                    style = { unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.3f, 0.7f, 0.3f), marginRight = 8 },
                });
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
            row.Add(new Label(failure != null ? SyncText.FailureLabel(failure.Failure) : pending)
            {
                style = { color = failure != null ? new StyleColor(new Color(0.9f, 0.3f, 0.3f)) : new StyleColor(StyleKeyword.Null) },
            });
            container.Add(row);

            if (failure != null)
                container.Add(new HelpBox(failure.Message, HelpBoxMessageType.Error));
            return container;
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

            if (!FolderSelection.IsChosen(LoadPrefs()))
                _folders.Add(new HelpBox(selected.Count > 0
                    ? "Pre-selected because these agents' user folders exist on this machine. Nothing is installed until you Sync."
                    : "No agent user folders found on this machine, so nothing is pre-selected.", HelpBoxMessageType.Info));
        }

        void SaveFolders(IEnumerable<SkillsFolder> selected, UserEnvironment environment)
        {
            try
            {
                var prefs = LoadPrefs();
                FolderSelection.Save(prefs, Layout, selected, environment);
                SavePrefs(prefs);
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
            SourceConsent.Unconfirmed(lockfile, LoadPrefs(), _confirmedSources, _skills).Count > 0;

        void UpdateSyncButton() =>
            _syncButton.SetEnabled(_canSync && _consent.value && _lockfile != null && !HasUnconfirmedSources(_lockfile));

        void Sync()
        {
            if (!_consent.value) return;
            _failures = new Dictionary<string, SkillFetchException>();

            try
            {
                // Re-check against the lockfile as it is now: it may have gained a source since the window listed it.
                var service = new SkillsSyncService(UnityProjectRoot);
                EditorUtility.DisplayProgressBar(Title, "Downloading and installing skills...", 0.5f);
                var summary = service.Run(_confirmedSources, expectedMode: _installMode, expectedGroups: _groups);
                ShowSummary(SyncText.Describe(summary), summary.UserScopeDiffers.Count > 0 ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info);
            }
            catch (Exception e) when (e is SourceConsentException || e is InstallModeChangedException || e is SkillGroupsChangedException)
            {
                ShowSummary(e.Message, HelpBoxMessageType.Warning);
            }
            catch (SyncStateSaveException e)
            {
                ShowSummary(SyncText.Describe(e.Summary) + "\nWarning: " + e.Message, HelpBoxMessageType.Warning);
                Debug.LogException(e.InnerException);
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

            Refresh();
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
