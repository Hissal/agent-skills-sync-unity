# Code map

How a sync flows through `Packages/com.hissal.agent-skills-sync`. Terms are in [GLOSSARY.md](../GLOSSARY.md);
each type's doc comment holds the detail. CI checks that each C#-shaped name in backticks, other than a few
listed external names, still occurs in the package source (`.github/scripts/code-map/`); it doesn't check what the
map says about them.

## A sync, in order

`SkillSync.Run` runs the steps; `SkillSync.Plan` stops after step 3, offline. Parts marked _caller_ run before, in the
Editor, which hands their results to the `SkillSync` constructor.

1. **Read the lock.** _Caller:_ `Lockfile.FindRoot` finds it. `Lockfile` loads `skills-lock.json` into `LockedSkill`s;
   `LockfileException` when it is missing or unusable. `LockfileHash` hashes it for the startup check: the whole
   file, and (`LockfileHash.OfSelected`) only the entries this machine installs.
   `SkillName` owns the folder-name rule; `LockedSkill`, `UnsupportedSkill` and `PlanAction` enforce it on construction.
   Managed names read from prefs and `.gitignore` blocks are filtered by the same rule.
2. **Scan what is there.**
   - `ProjectScanner` reads each skills folder of the `FolderLayout` (default `FolderLayout.Default`, a list of
     `SkillsFolder`s) into a `ProjectState` of `FolderState`s.
   - _Caller,_ in `MachineChoices.Read`: `UserScopeScanner` asks each `IUserScopeSource` (`UserScopeLocationSource`
     for folders like `~/.codex/skills`, `ClaudePluginSource` for Claude Code plugins, `CodexPluginSource` for Codex
     plugins) for user-scope copies, giving
     a `UserScopeState` of `UserScopeCopy`s.
     `MachineChoices` bundles this machine's layout, `FolderSelection`, `UserScopeState`, `InstallAnywayChoices`
     and `SkillSelection` (which locked skills it installs: the contributor's opt-out or opt-in, else the default).
     The default comes from the project's `SkillGroups` (`SkillGroups.IsSelectedByDefault`), which the caller loads
     with `ProjectSyncSettings` and hands to `MachineChoices.Read`.
3. **Plan.** `InstallPlanner` turns lock + `ProjectState` + `MachineChoices` into an `InstallPlan` of `PlanAction`s
   (`PlanActionKind`), deciding whether a managed copy is current through an `IInstalledCopyCheck`. A skill
   `SkillSelection` leaves out gets no action but the withdrawal of its managed entries, so step 4 never fetches it.
4. **Fetch and verify.** For each action that needs a fetch, the `ISkillFetcher` downloads the skill and
   `SkillFolderHash` hashes it. `InstallModeStrategy` (picked by `InstallMode`) decides what to fetch, whether a
   changed source is refused, and which check plans step 5. Every `SkillFetchException` is collected and thrown
   as one `SyncAbortedException` before anything on disk changes. `LockVerification` compares copies with the lock.
5. **Apply.** `PlanExecutor` copies canonical skills, links them into link folders through an `ILinkCreator`, writes
   each folder's `ManagedStateFile` block and records managed skills in `LocalPrefs`. Returns a `SyncSummary`,
   which names removals caused by a changed group default apart (`SyncSummary.NoLongerSelectedByDefault`).

`SyncStatus` is the cheap, offline cousin of steps 1–3 (names and links only; it hashes no project copy, only
user-scope copies a skip checks against the lock) for the startup check. `SyncStatus.ToRemove` tells skills only
waiting for removal from missing ones.

## State

| State | Where | Read by | Written by |
| --- | --- | --- | --- |
| `Lockfile` | `skills-lock.json` (committed, `skills` CLI format) | `SkillSync`, `SyncStatus`, Editor | never by this package |
| `ProjectSyncSettings` (`InstallMode`, `SkillGroups`) | `ProjectSettings/AgentSkillsSync.json` (committed) | `SkillsSyncService`, `StartupNotifier` | `SkillsSyncWindow` (`InstallMode` only; groups are edited in the file and kept by `ProjectSyncSettings.Save`) |
| `LocalPrefs` | `UserSettings/AgentSkillsSync.json` (per machine) | the ↳ rows' readers, `ProjectScanner` | `PlanExecutor`, `SkillsSyncWindow`, `StartupNotifier` |
| ↳ folder selection | `LocalPrefs.SelectedFolders` / declined offers | `FolderSelection` | `FolderSelection.Save` |
| ↳ install anyway | `LocalPrefs.InstallAnywaySkills` | `InstallAnywayChoices` | `InstallAnywayChoices.Set` |
| ↳ skill selection | `LocalPrefs.OptedOutSkills` / `OptedInSkills` | `SkillSelection` | `SkillSelection.Set` |
| ↳ synced sources | `LocalPrefs.SyncedSources` (selected skills' sources only) | `SourceConsent` | `SourceConsent.RecordSynced` |
| ↳ last sync / decline | `LastSyncedLockHash`, `LastSyncedSelectedSkillsHash`, `DeclinedState` | `StartupCheck.ShouldNotify` | `StartupCheck.RecordSynced` / `RecordDeclined` |
| ↳ seen skills | `LocalPrefs.SeenSkills` | `SeenSkills.New` | `SeenSkills.Seed` / `Mark` / `RecordSynced` |
| ↳ new-skill notice | `LocalPrefs.NotifyNewSkills` | `StartupCheck.NewSkills` | `SkillsSyncWindow` |
| ↳ managed skills | `LocalPrefs.ManagedSkills` | `ProjectScanner` | `PlanExecutor` |
| `ManagedStateFile` | block in each skills folder's `.gitignore` (committed) | `ProjectScanner` | `PlanExecutor` |
| `MachineChoices` | in memory, built by `MachineChoices.Read` from `LocalPrefs` + `UserEnvironment` | `SkillSync`, `SyncStatus` | — |

The `Record*`/`Set`/`Save` helpers change a loaded `LocalPrefs`; the caller then calls `LocalPrefs.Save`.
`UserEnvironment` (home folder + environment variables) resolves each `UserScopeLocation`; tests pass a fake one.

## Seams tests fake

| Seam | Real implementation | Faked in tests by |
| --- | --- | --- |
| `ISkillFetcher` | `GitHubSkillFetcher` (downloads repo archives) | small fetchers in the test class, or `GitHubSkillFetcher` over `FakeGitHub` |
| `IArchiveDownloader` | `HttpArchiveDownloader` | `FakeGitHub` |
| `ILinkCreator` (`LinkMethod`) | `FallbackLinkCreator.Default`: `SymlinkCreator` → `JunctionCreator` → `CopyLinkCreator` | failing and recording linkers in `LinkFallbackTests` |
| `IInstalledCopyCheck` | `LockedHashCheck` (Pinned), `UpstreamHashCheck` (Latest), `FixedCheck` (Latest's preview and fetch, `SyncStatus`) | an always-stale check in `InstallPlannerTests` |
| `IUserScopeSource` | `UserScopeLocationSource`, `ClaudePluginSource` | a fake source in `UserScopeScannerTests`; temp homes via `UserEnvironment` |

## Editor side

- `SkillsSyncService` — shared setup for the window and optional Pipeline `skills_sync` command: resolves roots,
  loads one lock, mode and `SkillGroups`, reads machine choices, checks source consent, runs that snapshot, then records consent
  and notification state. Planning is offline; the fetcher is created only after the consent and mode checks.
  Tests pass a temp user environment and a fetcher backed by `FakeGitHub`.
- `SkillsSyncCommand` (`Pipeline/`) — parses invocation-only options and returns a structured plan or sync summary.
  Its Editor assembly is enabled by a package version define only when `com.unity.pipeline` is installed.
- `SkillsSyncWindow` — the sync window: folder selection, plan preview, source consent (`SourceConsent`), install
  mode, the skills in one section per `SkillGroupSection` (`SkillGroups.Sections`) with per-group and per-skill
  install checkboxes (`SkillSelection.Set`, then `SeenSkills.Mark`), a `NEW` tag on each of
  `SkillsSyncService.NewSkills`, the new-skill notice toggle, install-anyway toggles, then `SkillsSyncService.Run`. `SyncText` holds its texts. `UnityProjectRoot` holds `LocalPrefs` and
  `ProjectSyncSettings`; `SkillsRoot` holds `skills-lock.json` and the skills folders.
- `StartupNotifier` — once per editor session: offers newly found folders (`FolderSelection.Offers`), reads
  `SyncStatus`, seeds prefs that have no seen skills (`SeenSkills.Seed`), and asks `StartupCheck.ShouldNotify`
  whether to offer opening the window. One dialog says both parts (`SyncText.StartupMessage`).
- `StartupCheck` (Core) — the notify decision, in two parts: `StartupCheck.OutOfSync` (with the declined
  fingerprint) and `StartupCheck.NewSkills` (the `SyncStatus.OptionalSkills` not yet seen, unless the notice is off).
  `StartupNotifier` records declines, which also mark the named new skills seen; `SkillsSyncService` records syncs,
  which mark every locked skill seen for the window and the Pipeline command alike.
- `SeenSkills` (Core) — which locked skills the contributor has been shown. A skill is new when it is locked, left
  out by its default alone (`SeenSkills.Optional`; never one they opted out of) and not seen. Prefs without the
  list have no new skills until they are seeded with the whole lock.
  `StartupCheck.LockChanged` ignores a lock change that touches only skills this machine does not install. A changed
  group default moves no lock hash; it notifies through `SyncStatus.OutOfSyncSkills`.

## Tests

`Packages/com.hissal.agent-skills-sync/Tests/Editor/`, one `*Tests.cs` per Core type or behaviour. Helpers:

- `FakeGitHub` — serves fixture-built repo zips in place of GitHub, or fails as if offline.
- `TempDirectory` — deletes temp trees safely even when they hold symlinks or junctions.
- `Fixtures~/` — skill folders with known `skills` CLI hashes (read by `SkillFolderHashTests`,
  `FakeGitHub` and others).
