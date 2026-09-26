# Spec: Agent Skills Sync — a Unity package that installs a project's locked agent skills

## Problem Statement

Unity projects that use agentic workflows want every contributor to have the same agent skills
(e.g. a Unity-flavoured fork of mattpocock/skills), for both Claude Code (`.claude/skills/`) and
Codex-style agents (`.agents/skills/`).

Today the options are all bad:

- **Committing vendored skills** bloats the repo and duplicates skills for contributors who already
  have them installed at user scope — the duplicate wastes context, and the user-scope copy may
  silently shadow the project's version, so the contributor runs different skills than the team.
- **User-scope installs / plugins** leak into every other project on the machine, and teammates
  have no prompt telling them the project expects the skills at all.
- **The `skills` CLI restore** (`npx skills experimental_install`) needs Node on every machine,
  is run by hand, doesn't know about user-scope copies, and gives Unity-only teammates no in-editor
  cue.

There is no way to say "this project uses these skills; make sure everyone has them, exactly once".

## Solution

A standalone, open-source Unity Editor package (installed via git URL into any project) that treats
the committed `skills-lock.json` as the source of truth and keeps the project's skill folders in
sync with it:

- On editor load, if the lockfile changed or skills are missing, it notifies the contributor and
  offers to sync.
- It installs every locked skill at project scope — the canonical copy in `.agents/skills/<name>`,
  and a link at `.claude/skills/<name>` pointing to it.
- It detects skills the contributor already has at user scope (per agent), tells them, and lets
  them skip the project copy for that agent — remembered per machine, never committed.
- It tells the contributor when a user-scope copy they chose to keep differs from the locked
  version.
- It keeps git clean by maintaining ignore files that list exactly the skills it manages, so
  project-authored skills committed alongside them stay tracked.
- Project-authored skills committed in `.agents/skills/` are also linked into `.claude/skills/`,
  so both agent families see them from one copy.

## User Stories

1. As a new contributor opening the project, I want to be told the project uses agent skills, so that I know my agent setup is incomplete.
2. As a new contributor, I want one click to install all locked skills, so that I don't need to learn a CLI.
3. As a contributor without Node installed, I want the sync to work anyway, so that tooling prerequisites don't block me.
4. As a contributor, I want to see the list of skills and their sources before anything is installed, so that I can consent to what will run with my agent's permissions.
5. As a contributor who declines, I want the prompt to stay quiet until something changes, so that I'm not nagged every editor launch.
6. As a contributor, I want a menu item to open the sync window at any time, so that I can install later or re-check.
7. As a contributor, I want the sync to add newly locked skills after a pull, so that I get team additions without doing anything special.
8. As a contributor, I want the sync to update skills whose locked version changed, so that I run what the team runs.
9. As a contributor, I want skills removed from the lockfile to be removed from my project folders, so that stale skills don't linger.
10. As a contributor, I want the sync to never touch skills it didn't install, so that project-authored and hand-added skills are safe.
11. As a contributor with a skill already in `~/.claude/skills`, I want to be told the project also provides it, so that I can avoid loading it twice.
12. As that contributor, I want to skip only the Claude link for that skill while still getting the `.agents` copy if my Codex setup lacks it, so that each agent gets the skill exactly once.
13. As a contributor with a skill in `~/.agents/skills`, I want the same per-agent choice for the `.agents` side, so that Codex-style agents don't see duplicates.
14. As a contributor with the skills provided by an installed Claude Code plugin, I want that detected as well, so that plugin-namespaced duplicates are also surfaced.
15. As a contributor keeping user-scope copies, I want to be told when my copy differs from the locked version, so that I know I'm not running what teammates run.
16. As a contributor, I want my skip choices remembered on this machine only, so that my personal setup never leaks into the repo.
17. As a contributor, I want to change my skip choices later, so that I can switch to the project copies if I drop my user-scope install.
18. As a contributor on Windows without Developer Mode, I want linking to still work, so that I'm not forced to change OS settings.
19. As a contributor working offline, I want the sync to fail gracefully and keep my existing skills, so that I can keep working.
20. As a contributor, I want a clear error if the download or hash check fails, so that I can tell a network problem from a tampered or changed source.
21. As a maintainer, I want the lockfile to stay compatible with the `skills` CLI, so that people can still add or update skills with `npx skills add` / `update`.
22. As a maintainer, I want the sync to refuse an unknown lockfile format version with a clear message, so that an upstream format change fails loudly instead of corrupting folders.
23. As a maintainer, I want the ignore rules for vendored skills generated automatically, so that adding a skill to the lock never means hand-editing `.gitignore`.
24. As a maintainer, I want project-authored skills in `.agents/skills/` tracked by default, so that forgetting a step results in a commit, not a lost skill.
25. As a maintainer, I want project-authored skills linked into `.claude/skills/` automatically, so that Claude users see them without a committed symlink.
26. As a maintainer, I want the package installable into any Unity project by git URL, so that I don't rebuild this tool per project.
27. As a maintainer, I want the tool to be editor-only, so that it never ships in a player build.
28. As a contributor, I want the startup check to be cheap and run once per editor session, so that it doesn't slow down domain reloads.
29. As a contributor, I want a summary after each sync (installed / updated / removed / skipped), so that I can see what changed.
30. As an open-source user of the package, I want it to work with any GitHub-hosted skill source in the lockfile, so that it isn't tied to one skill collection.

## Implementation Decisions

**Distribution**
- Standalone public repo, UPM package, installed via git URL. Editor-only assembly. Package name TBD
  (e.g. `com.hissal.agent-skills-sync`).
- Pure C# — no Node dependency. Reads the `skills` CLI's lockfile but does not shell out to it.
- **Engine-free core.** Modules 1–7 live in an assembly with no `UnityEngine`/`UnityEditor`
  references (`noEngineReferences`). Only module 8 (Editor UI) depends on Unity. This keeps the
  option open to later extract the core into a generic .NET library + `dotnet tool` CLI for
  non-Unity projects, with this package becoming the Unity front end over it. Not built now.

**Modules** (deep modules, small interfaces; the planner is the core)
1. **Lockfile** — parses `skills-lock.json` into `LockedSkill { name, source, sourceType, skillPath, computedHash }`.
   Rejects unknown `version` values and non-`github` source types with explicit errors.
2. **Environment scanner** — reports what already exists, per agent:
   user-scope Claude skills dir, user-scope `.agents` skills dir, installed Claude Code plugins,
   and project `.agents/skills` / `.claude/skills` contents (distinguishing tool-managed from
   foreign entries via the managed lists in module 6).
3. **Source fetcher** — given a `LockedSkill`, returns a local folder with its contents: downloads the
   GitHub repo archive into a per-user cache, extracts the skill folder, computes the content hash,
   and compares it with `computedHash`. Behind an interface so tests can fake it.
4. **Install planner** — a pure function:
   `Plan(lock, projectState, userScopeState, localPrefs) -> InstallPlan`.
   The plan is a list of per-skill, per-agent actions: `Install`, `Update`, `Link`, `Unlink`,
   `Remove`, `SkipUserScope`, `WarnUserScopeDiffers`, `LeaveForeign`. All decision rules live here:
   - Canonical copy goes in `.agents/skills/<name>` if **either** agent still needs the project copy.
   - `.claude/skills/<name>` link is created unless Claude has it at user scope and the prefs say skip.
   - Skills no longer in the lock but listed as managed → `Remove`.
   - Anything not in the managed lists → `LeaveForeign` (never modified).
   - Project-authored skills (committed, not in the lock) → `Link` into `.claude/skills/`.
5. **Plan executor** — applies an `InstallPlan` to the filesystem. Links use symlink →
   directory junction → copy fallback. Idempotent; re-running a completed plan is a no-op.
6. **Managed-state files** — the tool writes a `.gitignore` inside `.agents/skills/` and inside
   `.claude/skills/`, each listing exactly the names the tool manages there. These double as the
   "managed" record for module 2. `.agents/skills/.gitignore` itself is committed so vendored
   skills stay ignored for everyone; tool-managed Claude links are ignored the same way.
7. **Local prefs** — per-machine skip choices and the last-synced lockfile hash, stored in the
   project's `UserSettings/` folder (per-user, not committed by Unity convention).
8. **Editor UI** — a sync window (skill list, source, per-agent status and skip toggles, consent,
   Sync button, last result summary), a menu item to open it, and an `InitializeOnLoad` startup
   check that runs once per session (session-state guarded) and only notifies when the lockfile
   hash changed or managed skills are missing.

**Behavioural decisions**
- **Never install without consent.** The first install and any new source require explicit confirmation.
- **User-scope detection is per agent.** Skipping Claude doesn't skip `.agents`, and the reverse.
- **"Differs" is reported, not "outdated".** The tool can't tell whether a user-scope copy is ahead or behind (e.g. a
  checkout of the fork), so it only reports that the copy differs from the locked hash.
- **Offline.** The sync aborts before any filesystem change and keeps existing skills.
- **Hash compatibility.** `computedHash` must be reproduced exactly as the `skills` CLI computes it. This is the main
  technical risk; see Further Notes.

## Testing Decisions

- Test external behaviour only: given inputs, what plan / what filesystem result — not internal calls.
- **Primary seam: the install planner** (pure). Covered by table-driven EditMode tests over
  lock × project state × user-scope state × prefs. This is where nearly all tests live.
- **Executor**: a small number of tests against a temp directory, checking idempotency, the link
  fallback chain (junction/copy paths forced via a seam), and that foreign entries are untouched.
- **Lockfile**: fixture-based tests including unknown version, unknown source type, and malformed JSON.
- **Fetcher**: hash computation tested against fixtures whose hashes come from the real `skills` CLI,
  which proves compatibility. Network download is faked in all other tests.
- **UI**: not unit-tested; smoke-tested by hand in a sample project.
- **Prior art:** none — new repo. Use Unity Test Framework EditMode tests.

## Out of Scope

- Adding, removing or updating entries in the lockfile (use `npx skills add` / `update`).
- Enabling or disabling Claude Code plugins, e.g. scoping the official Unity plugin per project.
  That's done in project `.claude/settings.json`, not by this tool.
- Non-GitHub sources and private repositories / authentication.
- Determining whether a user-scope copy is ahead of or behind the locked version.
- Committed symlinks, and any non-Unity front end (CLI, CI) — the engine-free core keeps this possible later.
  For non-Unity projects the `skills` CLI already covers restore; consider contributing per-agent
  user-scope detection upstream to it before building a generic tool.
- Agents other than Claude Code and `.agents/`-compatible ones.

## Further Notes

- **Hash algorithm (spike first).** Find how the `skills` CLI computes `computedHash`
  (npm package `skills`, open source) and pin the package to lockfile `version: 1`.
  If the hash can't be reproduced, fall back to comparing against a freshly fetched copy.
- **Pinning.** The lockfile stores a content hash, not a commit. The fetcher can therefore only
  *verify* that upstream still matches the lock, not fetch the old version. A mismatch should say
  "source changed since it was locked; run `npx skills update` and commit the lockfile". Confirm how
  `experimental_install` behaves in this case and match it.
- **Security.** Skills run with the agent's full permissions. The consent UI should show the
  source repo per skill and flag sources new since the last sync.
- **Known consumer.** RaveRampage-Unity (currently commits vendored skills). Migrating it means:
  delete the vendored folders, commit the lockfile and the generated ignore files, and add the package.
- **Official Unity Claude plugin.** Recommend in the README: disable it at user scope and enable it
  in Unity projects' `.claude/settings.json`, to keep its ~30 skills out of non-Unity repos.
