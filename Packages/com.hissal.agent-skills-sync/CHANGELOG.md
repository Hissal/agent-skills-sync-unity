# Changelog

All notable changes to this package are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this package
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Package skeleton: engine-free `Core` assembly, `Editor` assembly and EditMode tests.
- Sync window (**Window → Agent Skills Sync**) that installs the skills locked in `skills-lock.json` into
  `.agents/skills/` and links them into `.claude/skills/` (symlink, then junction, then copy), after consent.
- Hash verification compatible with the `skills` CLI's `computedHash`; a failed fetch aborts the sync with no changes.
- Re-sync updates, removes and leaves foreign skills alone; project-authored skills are linked into `.claude/skills/`.
- Managed `.gitignore` block in each skills folder, listing every locked skill (the same on every machine); what
  this machine installed is recorded in `UserSettings/AgentSkillsSync.json`.
- Lock entries' `ref` is honoured: the skill is fetched at that branch, tag or commit.
- Once-per-session startup check that offers to sync when the lockfile changed or skills are missing.
- Confirmation for source repos new since the last sync.
- Per-machine choice of skills folders, with autofill from the agent homes found on the machine.
- Detection of skills already present at user scope or provided by enabled Claude Code plugins, with per-folder
  skips and a warning when a kept user-scope copy differs from the lock.
- Project install mode (`ProjectSettings/AgentSkillsSync.json`): Latest (default) installs current upstream and
  reports skills that differ from the lock; Pinned refuses them. The lockfile is never rewritten.
- README: install, project setup, folder selection and skips, install mode, migration from vendored skills, and
  scoping the official Unity Claude plugin per project.
