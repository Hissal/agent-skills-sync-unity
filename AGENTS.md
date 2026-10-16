# Agent Skills Sync (Unity)

UPM package that installs a project's locked agent skills: Unity Editor adapters over an engine-free `Core`, plus a standalone .NET restore command (`Tools~/AgentSkillsSync.Cli`) for new worktrees. Spec: [#1](https://github.com/Hissal/agent-skills-sync-unity/issues/1).

## Agent skills

### Issue tracker

Issues live in GitHub Issues on Hissal/agent-skills-sync-unity (`gh` CLI). See `docs/agents/issue-tracker.md`.

### Triage labels

Default vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `GLOSSARY.md` + `docs/adr/` at the repo root. See `docs/agents/domain.md`.

### Unity

Host project at `.` with the embedded package at `Packages/com.hissal.agent-skills-sync` (shape: both). See `docs/agents/unity.md`.

### Cursor Cloud

The Cloud Agent image has the .NET SDK 8, Node.js 22, `python` (Python 3), the Unity CLI, and the Editor version named in `ProjectSettings/ProjectVersion.txt`. `bash scripts/test-editmode.sh` is the EditMode suite. On Linux, `RestoreHookTests` cases whose foreign hook is a CRLF `#!/bin/sh` script fail, because the kernel cannot execute that shebang; the LF cases pass. `StandaloneCommandTests` build and run the CLI with `dotnet`. A Unity Personal license is activated on boot from the user session in the keyring. `UNITY_SERVICE_ACCOUNT_ID` and `UNITY_SERVICE_ACCOUNT_SECRET` sign the CLI in as a service account, which cannot activate that license, so license commands unset them.

### Code map

Before exploring or changing the package code, read `docs/code-map.md`.

### Releases

release-please builds versions and the changelog from squash-merged PR titles: write each PR title as a user-facing Conventional Commit, with `!` for breaking changes. See `docs/releasing.md`.
