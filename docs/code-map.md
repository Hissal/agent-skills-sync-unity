# Code map

How a sync flows through `Packages/com.hissal.agent-skills-sync`. Terms are in [GLOSSARY.md](../GLOSSARY.md);
each type's doc comment holds the detail. CI checks that each name in backticks still occurs in the package source
(`.github/scripts/code-map/`); it doesn't check what the map says about them.

## A sync, in order

`SkillSync.Run` runs the steps; `SkillSync.Plan` stops after step 3, offline. Parts marked _caller_ run before, in the
Editor, which hands their results to the `SkillSync` constructor.

1. **Read the lock.** _Caller:_ `Lockfile.FindRoot` finds it. `Lockfile` loads `skills-lock.json` into `LockedSkill`s;
   `LockfileException` when it is missing or unusable. `LockfileHash` hashes it for the startup check.
2. **Scan what is there.**
   - `ProjectScanner` reads each skills folder of the `FolderLayout` (default `FolderLayout.Default`, a list of
     `SkillsFolder`s) into a `ProjectState` of `FolderState`s.
   - _Caller,_ in `MachineChoices.Read`: `UserScopeScanner` asks each `IUserScopeSource` (`UserScopeLocationSource`
     for folders like `~/.codex/skills`, `ClaudePluginSource` for Claude Code plugins) for user-scope copies, giving
     a `UserScopeState` of `UserScopeCopy`s.
     `MachineChoices` bundles this machine's layout, `FolderSelection`, `UserScopeState` and `InstallAnywayChoices`.
3. **Plan.** `InstallPlanner` turns lock + `ProjectState` + `MachineChoices` into an `InstallPlan` of `PlanAction`s
   (`PlanActionKind`), deciding whether a managed copy is current through an `IInstalledCopyCheck`.
4. **Fetch and verify.** For each action that needs a fetch, the `ISkillFetcher` downloads the skill and
   `SkillFolderHash` hashes it. `InstallModeStrategy` (picked by `InstallMode`) decides what to fetch, whether a
   changed source is refused, and which check plans step 5. Every `SkillFetchException` is collected and thrown
   as one `SyncAbortedException` before anything on disk changes. `LockVerification` compares copies with the lock.
5. **Apply.** `PlanExecutor` copies canonical skills, links them into link folders through an `ILinkCreator`, writes
   each folder's `ManagedStateFile` block and records managed skills in `LocalPrefs`. Returns a `SyncSummary`.

`SyncStatus` is the cheap, offline cousin of steps 1–3 (names and links only; it hashes no project copy, only
user-scope copies a skip checks against the lock) for the startup check.

## State

| State | Where | Read by | Written by |
| --- | --- | --- | --- |
| `Lockfile` | `skills-lock.json` (committed, `skills` CLI format) | `SkillSync`, `SyncStatus`, Editor | never by this package |
| `ProjectSyncSettings` (`InstallMode`) | `ProjectSettings/AgentSkillsSync.json` (committed) | `SkillsSyncWindow` | `SkillsSyncWindow` |
| `LocalPrefs` | `UserSettings/AgentSkillsSync.json` (per machine) | the ↳ rows' readers, `ProjectScanner` | `PlanExecutor`, `SkillsSyncWindow`, `StartupNotifier` |
| ↳ folder selection | `LocalPrefs.SelectedFolders` / declined offers | `FolderSelection` | `FolderSelection.Save` |
| ↳ install anyway | `LocalPrefs.InstallAnywaySkills` | `InstallAnywayChoices` | `InstallAnywayChoices.Set` |
| ↳ synced sources | `LocalPrefs.SyncedSources` | `SourceConsent` | `SourceConsent.RecordSynced` |
| ↳ last sync / decline | `LastSyncedLockHash`, `DeclinedState` | `StartupCheck.ShouldNotify` | `StartupCheck.RecordSynced` / `RecordDeclined` |
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
  loads one lock and mode, reads machine choices, checks source consent, runs that snapshot, then records consent
  and notification state. Planning is offline; the fetcher is created only after the consent and mode checks.
  Tests pass a temp user environment and a fetcher backed by `FakeGitHub`.
- `SkillsSyncCommand` (`Pipeline/`) — parses invocation-only options and returns a structured plan or sync summary.
  Its Editor assembly is enabled by a package version define only when `com.unity.pipeline` is installed.
- `SkillsSyncWindow` — the sync window: folder selection, plan preview, source consent (`SourceConsent`), install
  mode, install-anyway toggles, then `SkillsSyncService.Run`. `UnityProjectRoot` holds `LocalPrefs` and
  `ProjectSyncSettings`; `SkillsRoot` holds `skills-lock.json` and the skills folders.
- `StartupNotifier` — once per editor session: offers newly found folders (`FolderSelection.Offers`), reads
  `SyncStatus`, and asks `StartupCheck.ShouldNotify` whether to offer opening the window.
- `StartupCheck` (Core) — the notify decision; `StartupNotifier` records declines, `SkillsSyncWindow` records syncs.

## Tests

`Packages/com.hissal.agent-skills-sync/Tests/Editor/`, one `*Tests.cs` per Core type or behaviour. Helpers:

- `FakeGitHub` — serves fixture-built repo zips in place of GitHub, or fails as if offline.
- `TempDirectory` — deletes temp trees safely even when they hold symlinks or junctions.
- `Fixtures~/` — skill folders with known `skills` CLI hashes (read by `SkillFolderHashTests`,
  `FakeGitHub` and others).
