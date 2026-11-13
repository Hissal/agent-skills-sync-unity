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

Cloud Agents have the .NET SDK 8 and Python 3 (`python`). Node.js 22.23.3 is `/usr/local/bin/node`; use that binary for the skills scripts, which need 22.20 or newer. Agents do not install the Unity Editor or activate a license, because a Personal seat cannot stay valid across concurrent agents. Build and run the engine-free command from `Packages/com.hissal.agent-skills-sync/Tools~/AgentSkillsSync.Cli`. EditMode tests (`bash scripts/test-editmode.sh`) need a Unity Editor and a license on that machine, so they are outside this environment.

### Code map

Before exploring or changing the package code, read `docs/code-map.md`.

### Releases

release-please builds versions and the changelog from squash-merged PR titles: write each PR title as a user-facing Conventional Commit, with `!` for breaking changes. See `docs/releasing.md`.
