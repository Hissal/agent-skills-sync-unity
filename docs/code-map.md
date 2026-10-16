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
   Skill names read from `LocalPrefs` (managed, opted out, opted in, seen, synced by default) and from `.gitignore`
   blocks are filtered by the same rule. A skill group's names are not: they are only compared with the lock's, so
   an unsafe one shows as not in the lock (`SkillGroupSection.Unknown`).
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
     with `ProjectSyncSettings` and hands to `MachineChoices.Read`. A locked skill left out by its default alone is
     optional (`SkillSelection.IsOptional`).
3. **Plan.** `InstallPlanner` turns lock + `ProjectState` + `MachineChoices` into an `InstallPlan` of `PlanAction`s
   (`PlanActionKind`), deciding whether a managed copy is current through an `IInstalledCopyCheck`. A skill
   `SkillSelection` leaves out gets no action but the withdrawal of its managed entries, so step 4 never fetches it.
4. **Fetch and verify.** For each action that needs a fetch, the `ISkillFetcher` downloads the skill and
   `SkillFolderHash` hashes it. `InstallModeStrategy` (picked by `InstallMode`) decides what to fetch, whether a
   changed source is refused, and which check plans step 5. Every `SkillFetchException` is collected and thrown
   as one `SyncAbortedException` before anything on disk changes. `LockVerification` compares copies with the lock.
5. **Apply.** `PlanExecutor` copies canonical skills, links them into link folders through an `ILinkCreator`, writes
   each folder's `ManagedStateFile` block and records managed skills in `LocalPrefs`. `SkillSync.Run` then records
   the skills it selected by their default alone (`SkillSelection.RecordSynced`) and returns a `SyncSummary`,
   which names removals caused by a changed group default apart (`SyncSummary.NoLongerSelectedByDefault`): the
   withdrawn skills the previous sync had so recorded and that are optional now
   (`SkillSelection.IsNoLongerSelectedByDefault`), never a choice the contributor made or dropped.

## A restore

`WorktreeRestore` prepares a checkout without Unity or the network, for a worktree launcher (the standalone command in
`Tools~/AgentSkillsSync.Cli`, whose `Program` maps it to JSON and exit codes). `WorktreeRestore.Plan` writes nothing;
`WorktreeRestore.Run` checks everything before its first write and returns a `RestoreReport` whose `RestoreStatus`
says what stopped it.

1. **Find the repository.** `RepositoryIdentity.Find` reads the checkout's `.git` file or folder and `commondir`;
   its `RepositoryIdentity.Key` (common directory + project path) names the `SharedState` folder.
2. **Decide the choices.** The checkout's own (`ChoiceScope.Worktree`) or the `SharedChoices`; while none are
   shared, the other checkouts' (`SharedState.ProjectFolders`: Git's checkouts plus the projects that synced) when
   they agree, else the named baseline or `RestoreStatus.BaselineRequired`. Synced sources of every checkout and the
   shared ones count as approved.
3. **Check.** Unapproved sources (`RestoreStatus.SourcesNeedConfirmation`), then each skill the run would fetch
   (`SkillSync.ToFetch`) without a recorded snapshot (`SnapshotFetcher.Locate`, `RestoreStatus.SnapshotsMissing`).
4. **Run** steps 1–5 of a sync through `SkillsSyncService` with the `SnapshotFetcher`, which hands out the
   `SnapshotStore` copy its `SnapshotResolutions` names for the lock entry (Pinned: only lock-verified ones).

A sync through `SkillsSyncService` (window, Pipeline, the command's `sync`) records what it installed with
`SnapshotRecorder.RecordSync`; `SnapshotRecorder.Import` records an older installation as found
(`SnapshotProvenance.Import`). The optional Pipeline `SkillsRestoreCommand` runs the same `WorktreeRestore` in an open
editor. A restore that adopted a baseline records it (`SharedChoices.Adopt`, `LocalPrefs.BaselineProject`), so that
checkout keeps sharing its choices without a scope of its own (`SharedChoices.Follows`).

`SyncStatus` is the cheap, offline cousin of steps 1–3 (names and links only; it hashes no project copy, only
user-scope copies a skip checks against the lock) for the startup check. `SyncStatus.ToRemove` tells skills only
waiting for removal from missing ones.

## New worktrees

`WorktreeSetupStatus.Read` is the window's offline, read-only view: identity, explicit or never-set choice scope,
effective choices versus shared choices (`WorktreeChoiceDifference`), disagreeing baseline candidates, selected
skills' snapshot availability and provenance, and `WorktreeRestore.PlanNewWorktree`. That plan uses empty local
prefs and `ProjectState.Empty`, so an existing installation cannot hide what a new checkout needs; the open
checkout participates as a baseline candidate. It uses the open revision's lock and groups and current user scope.
`WorktreeSetup` changes scope, shares or takes choices, and adopts a baseline under `SharedState.Lock`.
Keeping worktree choices freezes the effective choices currently shown. These actions preserve bookkeeping and
installed files; the next sync applies the choices. Recording older installed skills uses `SnapshotRecorder.Import`.
The window refreshes this view on open, after sync, and after every action; the Worktrees section starts collapsed
when only one checkout exists. Texts live in `SyncText`; the existing hook controls follow the setup view.

`RestoreHook` installs, inspects and removes one repository's managed `post-checkout` block. Its configuration lives
inside the block: the external command DLL, package version, common Git directory and registered project paths.
`RestoreHookReport` exposes the `RestoreHookStatus` and explanation to the window and standalone `hook` commands.
Foreign shell code stays outside the block; paths inside a checkout and unsupported hooks are refused.

`RestoreCommand` builds the installed package's standalone command into the machine state directory by package version.
The window's New worktrees controls call it before installing or updating the hook. The hook calls the standalone
`restore` command with `--hook-output`, which renders the structured restore result as one line and a recovery command
when needed. A null previous HEAD gates restoration to a new checkout; the managed block isolates restore failures
from the remaining hook.

## State

| State | Where | Read by | Written by |
| --- | --- | --- | --- |
| `Lockfile` | `skills-lock.json` (committed, `skills` CLI format) | `SkillSync`, `SyncStatus`, Editor | never by this package |
| `ProjectSyncSettings` (`InstallMode`, `SkillGroups`) | `ProjectSettings/AgentSkillsSync.json` (committed) | `SkillsSyncService`, `StartupNotifier` | `SkillsSyncWindow` (`InstallMode` only; groups are edited in the file and kept by `ProjectSyncSettings.Save`) |
| `LocalPrefs` | `UserSettings/AgentSkillsSync.json` (per machine and checkout) | the ↳ rows' readers, `ProjectScanner` | `PlanExecutor`, `SkillSync`, `SkillsSyncService`, `SkillsSyncWindow`, `StartupNotifier` |
| ↳ folder selection | `LocalPrefs.SelectedFolders` / declined offers | `FolderSelection` | `FolderSelection.Save` |
| ↳ install anyway | `LocalPrefs.InstallAnywaySkills` | `InstallAnywayChoices` | `InstallAnywayChoices.Set` |
| ↳ skill selection | `LocalPrefs.OptedOutSkills` / `OptedInSkills` | `SkillSelection` | `SkillSelection.Set` |
| ↳ synced defaults | `LocalPrefs.LastSyncedDefaultSkills` | `SkillSelection.IsNoLongerSelectedByDefault` | `SkillSelection.RecordSynced` (saved by `SkillSync.Run`) |
| ↳ synced sources | `LocalPrefs.SyncedSources` (selected skills' sources only) | `SourceConsent` | `SourceConsent.RecordSynced` |
| ↳ last sync / decline | `LastSyncedLockHash`, `LastSyncedSelectedSkillsHash`, `DeclinedState` | `StartupCheck.ShouldNotify` | `StartupCheck.RecordSynced` / `RecordDeclined`; `StartupCheck.Seed` backfills the selected hash |
| ↳ seen skills | `LocalPrefs.SeenSkills` | `SeenSkills.New` | `SeenSkills.Seed` (through `StartupCheck.Seed`) / `Mark` / `RecordSynced` |
| ↳ new-skill notice | `LocalPrefs.NotifyNewSkills` | `StartupCheck.NewSkills` | `SkillsSyncWindow` |
| ↳ managed skills | `LocalPrefs.ManagedSkills` | `ProjectScanner` | `PlanExecutor` |
| ↳ choice scope | `LocalPrefs.ChoiceScope` | `SharedChoices`, `WorktreeRestore` | `WorktreeRestore`, `SharedChoices.SetScope` |
| `SharedChoices` | `repos/<key>/choices.json` in `UserEnvironment.StateDirectory` (per machine and repository) | `SharedChoices.Load` (window, notifier, `SkillsSyncService`, `SnapshotRecorder.Import`, the command's `sync`), `WorktreeRestore` | `SharedChoices.Save` (choices, while the checkout follows them), `SharedChoices.SetScope`, `SharedChoices.RecordSync` (synced sources, `LocalPrefs.KnownProjects`), `SharedChoices.Adopt` |
| `SnapshotResolutions` | `repos/<key>/resolutions.json` in the state directory | `SnapshotFetcher`, `SnapshotRecorder` | `SnapshotRecorder` |
| `SnapshotStore` | `snapshots/<hash>/` in the state directory (all repositories) | `SnapshotFetcher` | `SnapshotRecorder` |
| `ManagedStateFile` | block in each skills folder's `.gitignore` (committed) | `ProjectScanner` | `PlanExecutor` |
| `MachineChoices` | in memory, built by `MachineChoices.Read` from `LocalPrefs` + `UserEnvironment` | `SkillSync`, `SyncStatus` | — |

The `Record*`/`Seed`/`Set`/`Save` helpers change a loaded `LocalPrefs`; the caller then calls `LocalPrefs.Save`, or
`SharedChoices.Save` where the change is a choice a checkout following the shared choices shares. Writes to the state
directory take `SharedState.Lock`; snapshots are published by renaming a finished copy.
`UserEnvironment` (home folder + environment variables) resolves each `UserScopeLocation`; tests pass a fake one.

## Seams tests fake

| Seam | Real implementation | Faked in tests by |
| --- | --- | --- |
| `ISkillFetcher` | `GitHubSkillFetcher` (downloads repo archives); `SnapshotFetcher` (a restore) | small fetchers in the test class, or `GitHubSkillFetcher` over `FakeGitHub` |
| `IArchiveDownloader` | `HttpArchiveDownloader` | `FakeGitHub` |
| `ILinkCreator` (`LinkMethod`) | `FallbackLinkCreator.Default`: `SymlinkCreator` → `JunctionCreator` → `CopyLinkCreator` | failing and recording linkers in `LinkFallbackTests` |
| `IInstalledCopyCheck` | `LockedHashCheck` (Pinned), `UpstreamHashCheck` (Latest), `FixedCheck` (Latest's preview and fetch, `SyncStatus`) | an always-stale check in `InstallPlannerTests` |
| `IUserScopeSource` | `UserScopeLocationSource`, `ClaudePluginSource` | a fake source in `UserScopeScannerTests`; temp homes via `UserEnvironment` |

## Editor side

- `SkillsSyncService` (Core) — shared setup for the window, the optional Pipeline `skills_sync` command, the standalone
  command and `WorktreeRestore`: resolves roots,
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
  `SyncStatus`, seeds what prefs from an earlier version lack (`StartupCheck.Seed`: the seen skills, and the
  selected-skills hash while the lock is still the one last synced), and asks `StartupCheck.ShouldNotify`
  whether to offer opening the window. One dialog says both parts (`SyncText.StartupMessage`).
- `StartupCheck` (Core) — the notify decision, in two parts: `StartupCheck.OutOfSync` (with the declined
  fingerprint) and `StartupCheck.NewSkills` (the `SyncStatus.OptionalSkills` not yet seen, unless the notice is off).
  `StartupNotifier` records declines, which also mark the named new skills seen; `SkillsSyncService` records syncs,
  which mark every locked skill seen for the window and the Pipeline command alike.
- `SeenSkills` (Core) — which locked skills the contributor has been shown. A skill is new when it is locked,
  optional (`SkillSelection.OptionalIn`; never one they opted out of) and not seen. Prefs without the
  list have no new skills until they are seeded with the whole lock.
  `StartupCheck.LockChanged` ignores a lock change that touches only skills this machine does not install. A changed
  group default moves no lock hash; it notifies through `SyncStatus.OutOfSyncSkills`.

## Tests

`Packages/com.hissal.agent-skills-sync/Tests/Editor/`, one `*Tests.cs` per Core type or behaviour. Helpers:

- `FakeGitHub` — serves fixture-built repo zips in place of GitHub, or fails as if offline.
- `GitTestRepo` — a real temporary Git repository with linked worktrees, for `WorktreeRestoreTests`.
- `StandaloneCommandTests` build `Tools~/AgentSkillsSync.Cli` with `dotnet` and run it as a process.
- `TempDirectory` — deletes temp trees safely even when they hold symlinks or junctions.
- `Fixtures~/` — skill folders with known `skills` CLI hashes (read by `SkillFolderHashTests`,
  `FakeGitHub` and others).
