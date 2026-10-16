# Commands

## Standalone command

`dotnet <dll> <command> [--project <unity-project>] [options]`. `--project` defaults to the current folder. Each run
prints one JSON object with a `status`; an unknown command or option prints `invalid` with the `usage` text.

| Command | Does |
| --- | --- |
| `restore [--dry-run] [--confirm-source <owner/repo>]... [--baseline <checkout>]` | Installs the selected skills from recorded snapshots, offline. `--dry-run` reports what a run would do and writes nothing. |
| `sync [--confirm-source <owner/repo>]... [--install-mode latest\|pinned]` | Fetches from GitHub, installs, and records the snapshots later restores install. `--install-mode` overrides the project's mode for this run only. |
| `import` | Records the skills an earlier package version installed in this checkout as snapshots, as found on disk. |
| `scope --shared \| --worktree` | Makes this checkout follow the repository's shared choices, or keep its own (worktree choices). `--shared` where none are shared yet shares this checkout's. |
| `hook install \| status \| uninstall` | Installs, reports or removes this project's restore block in the repository's `post-checkout` hook. |

`--confirm-source` repeats, once per approved source. `--baseline` takes a checkout of the same repository, or the
Unity project in it, whose project has synced.

### Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Done, or (dry run) ready. |
| 1 | Failed (`error`, and for a failed fetch or copy `failures`: `skill`, `failure`, `message`). Nothing half-installed. |
| 2 | Invalid input (`error`): no lockfile, unusable project settings, not a Git checkout, unknown options. |
| 3 | Needs the caller: sources to confirm, snapshots to record, a baseline to name, or a hook that is `outdated` or `blocked`. Nothing changed. |

### `restore` output

| Field | Holds |
| --- | --- |
| `status` | `ready` (dry run), `restored`, `sourcesNeedConfirmation`, `snapshotsMissing`, `baselineRequired` |
| `dryRun` | Whether this was a dry run. |
| `repository` | `commonDirectory` (the Git common directory) and `projectPath` (the Unity project inside the checkout). |
| `installMode` | `latest` or `pinned`. In Pinned mode only snapshots verified against the lock count. |
| `choices` | `scope` (`shared` or `worktree`; null while a baseline is required) and `baselineFrom` (the checkout whose choices this run adopts as shared). |
| `baselineCandidates` | With `baselineRequired`: the checkouts whose choices disagree. |
| `unconfirmedSources` | With `sourcesNeedConfirmation`: source repos (`owner/repo`) new to this repository. |
| `missingSnapshots` | With `snapshotsMissing`: `skill` and `reason` for each skill with no usable snapshot. |
| `actions` | The plan: `kind`, `skill`, `folder`, `linkTarget`. |
| `summary` | After a run: `installed`, `updated`, `removed`, `noLongerSelectedByDefault`, `skipped`, `skippedForUserScope`, `linked`, `unlinked`, `differsFromLock`, `nothingChanged`. |

The checks run in order (baseline, then sources, then snapshots), so fixing one can surface the next. A repeated
restore changes nothing.

### `sync`, `import`, `scope` output

- `sync`: `installMode`; then `sourcesNeedConfirmation` with `unconfirmedSources` (exit 3), or `synced` with a
  `summary` as above.
- `import`: `imported`, listing the recorded skills.
- `scope`: `ok` and the new `scope`.

### `hook` output and behaviour

`status` is `installed`, `notInstalled`, `outdated` or `blocked`, with `hookPath` and a `message` that says what to
do. `install` adds this project to the repository's managed block, or updates the block for every registered
project; `uninstall` removes this project only. Shell code outside the block is kept.

- The dll must live outside every checkout of the repository, or `install` is `blocked`.
- `outdated`: the block, dll or package version differs from the running build. Run `install` again.
- `blocked` also covers a `core.hooksPath` inside a checkout or relative, a symlinked hook or hooks folder, a hook
  that is not `sh`/`bash`/`dash`, and a malformed block. Follow `message`; the hook is left untouched.

The hook runs only for a new checkout (null previous HEAD), calls `restore --hook-output` for each registered
project present, and exits 0 whatever happens. `--hook-output` replaces the JSON with one line,
`agent-skills-sync: <project>: <status>`, plus `; <error>; run <command>` when it needs action. The suggested
command adds `--confirm-source` for each new source, `--baseline <checkout>` for a baseline, or is a `sync` for
missing snapshots; each needs the user's decision first. A checkout without a lockfile prints `skippedNoLockfile`.

## Pipeline commands

In a checkout whose editor runs with `com.unity.pipeline`: `unity command <name> --<arg> <value>`. Statuses and
kinds are PascalCase here (`SourcesNeedConfirmation`).

| Command | Args | Returns |
| --- | --- | --- |
| `skills_restore` | `--dry_run true`, `--consent_new_sources true`, `--baseline <checkout>` | The restore fields above, flattened: `status`, `dryRun`, `installMode`, `scope`, `baselineFrom`, `baselineCandidates`, `unconfirmedSources`, `missingSnapshots`, `actions`, then `installed`, `updated`, `removed`, `skippedForUserScope`, `nothingChanged`. |
| `skills_sync` | `--dry_run true`, `--consent_new_sources true`, `--install_mode latest\|pinned` | Dry run: `newSources`, `hasChanges`, `actions`. Run: the sync summary. Fails while new sources are unconfirmed. |

`--consent_new_sources true` confirms every source the dry run lists, all at once.
