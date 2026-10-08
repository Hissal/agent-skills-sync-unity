# Agent Skills Sync (Unity)

Editor-only UPM package that installs a project's locked agent skills. Spec: [#1](https://github.com/Hissal/agent-skills-sync-unity/issues/1).

## Agent skills

### Issue tracker

Issues live in GitHub Issues on Hissal/agent-skills-sync-unity (`gh` CLI). See `docs/agents/issue-tracker.md`.

### Triage labels

Default vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `GLOSSARY.md` + `docs/adr/` at the repo root. See `docs/agents/domain.md`.

### Unity

Host project at `.` with the embedded package at `Packages/com.hissal.agent-skills-sync` (shape: both). See `docs/agents/unity.md`.

### Releases

release-please builds versions and the changelog from squash-merged PR titles: write each PR title as a user-facing Conventional Commit, with `!` for breaking changes. See `docs/releasing.md`.
