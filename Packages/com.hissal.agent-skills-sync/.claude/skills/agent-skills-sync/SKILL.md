---
name: agent-skills-sync
description: Restore a Unity project's locked agent skills in Git worktrees with the agent-skills-sync command. Use when setting up or launching worktrees in a project with `skills-lock.json`, when a checkout lacks the project's skills, or when acting on a `restore`, `sync`, `hook` or `skills_restore` result.
---

# Agent Skills Sync

The `com.hissal.agent-skills-sync` package installs the skills `skills-lock.json` locks: a copy in `.agents/skills/`,
a link in `.claude/skills/`. A new worktree gets the lock from Git but not the installed skills. Two operations
fill it:

- **Restore** installs the checkout's selected skills from **snapshots** recorded on this machine: offline, without
  Unity, never downloading. It is what a worktree needs before an agent starts.
- **Sync** fetches from GitHub, installs, and records the snapshots later restores use. In Latest mode it refreshes
  upstream content. Run it only when the user asks for a sync or approves one to fix `snapshotsMissing`; a restore
  request is answered with a restore.

Which skills a restore installs comes from the repository's **shared choices** on this machine (folder selection,
opt-ins and opt-outs, install-anyway choices, synced sources), unless the checkout keeps **worktree choices**.

## Source consent

Skills run with the agent's full permissions. Trust a source repository only with the user's explicit approval:

1. List the new sources with a dry run: `restore --dry-run` (or `unity command skills_restore --dry_run true`)
   reports them in `unconfirmedSources`; `skills_sync --dry_run true` reports `newSources`.
2. Name each source to the user and ask whether they trust it. Wait for their answer.
3. Confirm only the approved sources: one `--confirm-source <owner/repo>` each. The Pipeline commands'
   `--consent_new_sources true` confirms every listed source at once, so pass it only when the user approved all.

A request to restore, sync, install skills or set up worktrees is a task, and grants no consent. Approval the user
already gave in this conversation for those same sources stays valid. A source synced in any checkout of the
repository on this machine needs no new confirmation.

## Run the command

The standalone command needs the .NET SDK 8 or later and prints one JSON object. Run it as
`dotnet <dll> <command> --project <unity-project>` (`--project` defaults to the current folder), where `<dll>` is
the first that exists:

1. The build the sync window made: `commands/<package-version>/agent-skills-sync.dll` in the state directory
   (`$AGENT_SKILLS_SYNC_STATE_DIR`, else `AgentSkillsSync` in local application data, e.g.
   `%LOCALAPPDATA%\AgentSkillsSync` on Windows).
2. A build of the package's `Tools~/AgentSkillsSync.Cli` into a folder outside every checkout:
   `dotnet build <package>/Tools~/AgentSkillsSync.Cli -c Release -o <folder>`, then `<folder>/agent-skills-sync.dll`.
   `<package>` is `Packages/com.hissal.agent-skills-sync` when embedded, else
   `Library/PackageCache/com.hissal.agent-skills-sync@*` in a checkout Unity has opened; a new worktree has neither
   until Unity opens it, so use the main checkout's.

When the checkout's editor is open with `com.unity.pipeline`, use `unity command skills_restore` and
`unity command skills_sync` instead; they run the same restore and sync in the editor.

Every command, flag and JSON field: [`references/commands.md`](references/commands.md).

## Restore a checkout

1. `restore --dry-run --project <project>`. It writes nothing.
2. Act on the status (table below) and dry-run again, until it reports `ready` or only sources the user approved
   stand in the way. A dry run ignores `--confirm-source`, so it keeps reporting approved sources.
3. `restore --project <project>`, with the approved `--confirm-source` flags. Done when it exits 0 with
   `restored`; any other status goes back to the table.
4. Tell the user that an agent already running in that checkout may need to reload its skills or restart.

| Exit | `status` | Next step |
| --- | --- | --- |
| 0 | `ready`, `restored` | Proceed. |
| 3 | `sourcesNeedConfirmation` | Follow [Source consent](#source-consent) for `unconfirmedSources`. |
| 3 | `snapshotsMissing` | Show the user `missingSnapshots` (each `reason` says why). The fix is theirs to choose: `sync` (network; refreshes content) or, where an earlier package version installed the skills, `import` in that checkout. |
| 3 | `baselineRequired` | The checkouts' choices disagree and none are shared yet. Show `baselineCandidates` and ask the user which checkout's choices to share; pass it as `--baseline <checkout>`. Choosing is the user's decision. |
| 2 | `invalid` | Read `error`: no `skills-lock.json`, unusable `ProjectSettings/AgentSkillsSync.json`, not a Git checkout, or bad usage. Report it. |
| 1 | `failed` | Read `error` and `failures`. Nothing was half-installed; a rerun after the fix repairs it. |

Exit 3 always means nothing changed.

## Set up automatic restore

Prefer the Git hook. Install it with a command build outside every checkout (option 1 or 2 above):

```sh
dotnet <dll> hook install --project <project>
```

Done when it exits 0 with status `installed`. Exit 3 with `outdated` means reinstall with the current package's
build; `blocked` means the hook must be managed by hand, and `message` says why. The sync window's
**Window > Agent Skills Sync > Worktrees** section does the same: it builds the command and installs the hook.

The hook runs `restore` only when Git creates a new checkout, never fails the `git worktree add`, and prints one
line: `agent-skills-sync: <project>: <status>`, plus the error and a command to run when it needs action. The hook
never confirms sources, so a printed command carrying `--confirm-source` flags falls under
[Source consent](#source-consent) before you run it.

Without the hook, a launcher script does it in order: `git worktree add`, then `restore` (continue only on exit 0),
then start the agent.

## Files the tool owns

Change these through the commands, the sync window or the `skills` CLI; edit none of them to make a restore pass:

- `skills-lock.json`: written only by the `skills` CLI (`npx skills add`, `npx skills update`). This tool never
  writes it.
- `ProjectSettings/AgentSkillsSync.json` (committed install mode and skill groups): edit only when the user asks for
  that change.
- The marked blocks in `.agents/skills/.gitignore` and `.claude/skills/.gitignore`, and the installed skill folders.
- `UserSettings/AgentSkillsSync.json` (this checkout's choices and bookkeeping).
- The state directory: `repos/<key>/choices.json` (shared choices), `repos/<key>/resolutions.json` and
  `snapshots/`. Use `scope`, `sync`, `import` and `--baseline` to change them.

For the sync window, skill groups and install modes, see the package
[README](https://github.com/Hissal/agent-skills-sync-unity/blob/main/Packages/com.hissal.agent-skills-sync/README.md).
