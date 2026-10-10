# Agent Skills Sync (Unity)

Editor-only Unity package that treats a project's committed `skills-lock.json` as the source of truth. It installs
the locked agent skills into `.agents/skills/` (one copy) and `.claude/skills/` (a link to it), and detects skills
contributors already have at user scope, so every agent gets each skill exactly once.

Status: in development. See the [spec](https://github.com/Hissal/agent-skills-sync-unity/issues/1).

## Requirements

- Unity 6000.3 or later. The package is Editor-only.
- A `skills-lock.json` written by the [`skills` CLI](https://github.com/vercel-labs/skills) (lockfile `version` 1),
  in the Unity project folder or the folder directly above it (a repo that keeps its Unity project in a subfolder).
  The skills folders are installed next to the lock; the sync window shows which lock it uses. The tool installs
  skills from public GitHub repos (`sourceType` `github`). Entries of any other source type, such as the
  `unity-package` skill a Unity package installs itself, are listed in the sync window and left alone.
- Network access to github.com when you sync. Node is **not** needed to sync; only maintainers who edit the lockfile
  need it.
- [Git](https://git-scm.com/) 2.14 or later on your `PATH`, to install the package from its git URL. Unity's Package
  Manager needs it for any [git dependency](https://docs.unity3d.com/6000.3/Documentation/Manual/upm-git.html#req).

## Install

Package Manager → **+** → **Install package from git URL…**:

```
https://github.com/Hissal/agent-skills-sync-unity.git?path=Packages/com.hissal.agent-skills-sync#latest
```

Or add it to `Packages/manifest.json`:

```json
"com.hissal.agent-skills-sync": "https://github.com/Hissal/agent-skills-sync-unity.git?path=Packages/com.hissal.agent-skills-sync#latest"
```

`latest` is a branch that moves to each new release, so the Package Manager's **Update** button only ever installs a
released version. See the [releases](https://github.com/Hissal/agent-skills-sync-unity/releases) for what changed.

To pin a version, put its tag in place of `latest`:

<!-- x-release-please-start-version -->
```
https://github.com/Hissal/agent-skills-sync-unity.git?path=Packages/com.hissal.agent-skills-sync#v0.1.0
```
<!-- x-release-please-end -->

To follow unreleased changes on `main`, leave the `#…` off.

## How it works

- `skills-lock.json` is the source of truth. The tool never writes it.
- Each locked skill is installed once per project: the copy goes in `.agents/skills/<name>`, and
  `.claude/skills/<name>` is a link to that copy. A link is a symlink, then a directory junction (Windows), then a
  plain copy, whichever works first. A plain copy does not follow edits, so sync again after changing a skill.
- Skills are downloaded from their GitHub repo at the lock entry's `ref` (a branch, tag or commit) when it has one,
  else from the default branch (`archive/HEAD.zip`), into a per-user cache (`AgentSkillsSync/Cache` in your local
  application data folder).
- A sync downloads everything it needs first. If any skill fails (offline, a missing skill, or a refusal in Pinned
  mode), the sync aborts and **nothing** in the project changes. The window marks each failed skill with its error.
- A re-sync adds newly locked skills, updates changed ones, and removes skills that left the lockfile. It also
  re-links a link of its own that is broken or points anywhere but this project's `.agents/skills` copy. It never
  touches entries it did not install.
- **Project-authored skills**, folders with a `SKILL.md` committed in `.agents/skills/` that are not in the lockfile,
  stay tracked in git and are linked into `.claude/skills/`, so Claude Code sees them as well. A folder without a
  `SKILL.md` is not a skill and is never linked.

### The sync window

Open it from **Window → Agent Skills Sync**. From top to bottom:

- **Skills folders to install into on this machine**: one toggle per skills folder (see
  [Choosing skills folders](#choosing-skills-folders-per-machine)).
- **Install mode**: Latest or Pinned, with a note on what the mode does (see [Install mode](#install-mode)).
- **New sources**: a warning, and an "I trust the new source …" toggle for each source repo this machine has not
  synced before.
- **Skills locked in `skills-lock.json`**: each skill with its source repo and what Sync will do to it (to install,
  to update, to link, installed, left alone, skipped). Tags: `NEW SOURCE`, `differs from lock`, and
  `can't verify lock hash` / `hash not verifiable` (see [Install mode](#install-mode)). Under each skill there is
  one row per selected folder, with its status there, any copy you already have at user scope, and an install-anyway toggle.
- **I trust these sources. Skills run with my agent's permissions.**: the consent box. **Sync** stays disabled until
  it is ticked and every new source is confirmed. Consent and confirmations are not saved: they reset on Refresh and
  after each sync.
- **Sync** and **Refresh**, then a summary of the last sync.

### Unity CLI for agents and CI

With the optional `com.unity.pipeline` package installed, `skills_sync` runs the same sync as the window.
The package has no Pipeline dependency; projects without Pipeline use the window as before.

```sh
# Offline preview. No consent, downloads, or saved state.
unity run . --command skills_sync --format json -- --dry_run true

# After explicit user approval, trust all new source repos in the current lock and sync.
unity run . --command skills_sync --format json -- --consent_new_sources true

# Override the mode for this run only.
unity run . --command skills_sync --format json -- --consent_new_sources true --install_mode pinned

# In an open editor, use the connected command instead.
unity command skills_sync --consent_new_sources true
```

Run `unity command` in a connected editor to find the command and its argument descriptions. Pipeline and the
Unity CLI are experimental; this integration is verified with Pipeline `0.8.0-exp.1`.

`--consent_new_sources true` confirms every source repo new to this machine for that invocation. Without it,
a sync with new sources fails and lists them before fetching or changing the project. Sources are remembered
only after a successful sync. A dry run needs no consent and returns the actions and managed/ignored names from
`SkillSync.Plan()`. In Latest mode it cannot predict upstream updates without a download.

Agents must obtain explicit user approval for the listed new source repositories before passing
`--consent_new_sources true`. A request to sync or install skills does not itself grant source consent.
Run with `--dry_run true` to list new sources, then ask the user to approve them. Approval already given
for those sources in the current conversation remains valid. If the lock gains another source, obtain
approval for that source before consenting again.

The command honours this machine's saved folder selection, user-scope copies and install-anyway choices,
including an empty folder selection. Its default mode is the project's setting. `--install_mode latest` or
`pinned` overrides that setting without saving it. The result contains skill-name arrays for `installed`,
`updated`, `removed`, `skipped`, `skippedForUserScope`, `linked`, `unlinked`, and `differsFromLock`, plus
`userScopeDiffers`, `nothingChanged` and an empty `failures` array on success. Aborted or failed syncs fail the
command, and `unity run` exits nonzero with the error details. If skills were applied but saving consent and
notification state failed, the error says that the sync finished so callers can distinguish it from an aborted fetch.

### Startup check

Once per editor session the tool checks the project. If `skills-lock.json` changed since the last sync on this
machine, or locked skills are missing, it offers to open the sync window. **Not Now** keeps it quiet until something
changes. Nothing is installed automatically; a sync needs consent in the window or through the CLI command.

- The check is cheap: it looks at folder names, links and the lockfile's hash, never at skill contents. An installed
  copy that differs from the lock is caught through the lockfile change that caused it; the window, which hashes
  every copy, shows the details.
- While no skills folder is selected, it does not offer a sync. It can still ask once whether to add a folder whose
  agent home appeared (see [Choosing skills folders](#choosing-skills-folders-per-machine)).

## Set up a project

1. Add or update skills with the `skills` CLI. This writes `skills-lock.json`:

   ```sh
   npx skills add <owner>/<repo>     # add skills from a repo
   npx skills update                 # move locked skills to their current upstream
   ```

   The CLI also installs its own copy of each skill it adds. A skill folder that this tool did not install is left
   alone (and once the generated `.gitignore` lists its name, git ignores it unless it is already committed). Delete
   the copies the CLI made in `.agents/skills/` and `.claude/skills/`, then sync, so the tool installs and manages
   them.
2. Install the package, open **Window → Agent Skills Sync**, tick the consent box and **Sync**.
3. Commit:
   - `skills-lock.json`;
   - the generated `.agents/skills/.gitignore` and `.claude/skills/.gitignore`;
   - `ProjectSettings/AgentSkillsSync.json`, if you changed the install mode.

Teammates install the package with the project, and the startup check offers them the sync.

### The generated `.gitignore` files

The tool writes a `.gitignore` in each skills folder it installs into, and keeps it current in any skills folder that
already has one. Its marked block lists every locked `github` skill, and in `.claude/skills` also the project-authored
skills it links there:

```gitignore
# >>> Agent Skills Sync: managed from skills-lock.json; do not edit this block.
# Lists every skill the tool may install or link here, on any machine. Anything else stays tracked.
/some-skill
# <<< Agent Skills Sync
```

- Installed skills stay out of git, and project-authored skills beside them stay tracked.
- A lock entry with another source type (such as a `unity-package` skill) is not listed, since the tool neither
  installs nor links it. Whatever places it (a commit, the package itself) decides whether git tracks it.
- The block depends only on `skills-lock.json` and the committed project-authored skills, never on one machine's
  folder selection or install-anyway choices. Every teammate's sync writes the same block, so it never shows up as a local change.
- What the tool installed **on this machine**, and therefore owns, is recorded in `UserSettings/AgentSkillsSync.json`.
  An entry it did not install is never modified, even when the block lists its name. On the first sync after
  upgrading from a version without that record, the names in the existing block count as the tool's.
- Lines outside the block are yours. The tool keeps them, and never reads them.

## Choosing skills folders per machine

The window lists every skills folder the tool supports, with the agents that read it:

| Project folder | Read by | User-scope folders checked | Env vars honoured |
| :- | :- | :- | :- |
| `.agents/skills` | Codex, Cursor, GitHub Copilot, Gemini CLI, OpenCode, Amp, Cline, Zed and more | `~/.agents/skills`, `~/.codex/skills`, `~/.cursor/skills`, `~/.copilot/skills`, `~/.gemini/skills`, `~/.gemini/{config,antigravity,antigravity-cli}/skills`, `~/.config/{opencode,agents,amp}/skills`, `~/.cline/skills`, `~/.kilo/skills`, `~/.factory/skills`, `~/.agent/skills`, `~/.deepagents/agent/skills`, `~/.firebender/skills`, `~/.kimi-code/skills`, `~/.warp/skills` | `CODEX_HOME`, `COPILOT_HOME`, `GEMINI_CLI_HOME`, `XDG_CONFIG_HOME`, `CLINE_DIR`, `DEEPAGENTS_HOME`, `KIMI_CODE_HOME` |
| `.claude/skills` | Claude Code | `~/.claude/skills`, `~/.claude/skills/synced`, and installed Claude Code plugins | `CLAUDE_CONFIG_DIR`, `CLAUDE_CODE_PLUGIN_CACHE_DIR` |

Hover a folder's toggle in the window to see which agent reads each location. The tool reads environment variables
from the Unity editor process. For the sources behind this table, see
[`docs/skills-cli-findings.md`](https://github.com/Hissal/agent-skills-sync-unity/blob/main/docs/skills-cli-findings.md).

- **Autofill.** Until you choose, a folder is pre-selected when any of its agents' homes (`~/.claude`, `~/.codex`
  and so on) exists on this machine. Nothing is installed until you sync.
- **Your choice is stored per machine** in `UserSettings/AgentSkillsSync.json`, which Unity projects do not commit.
  It is saved when you change a toggle, or when you sync, which confirms the autofilled selection.
- **Selecting only `.claude/skills`** still puts the copies in `.agents/skills`, because the links point there.
- **Deselecting a folder** removes the tool's skills from it on the next sync. Other entries are not touched.
- **No folder selected**: Sync installs nothing, and the startup check does not offer a sync (it can still offer
  to add a folder whose agent home appeared, see below).
- **A new agent home appears** after your selection is stored (for example, you install Codex later): at the next
  editor start the tool asks once whether to add that folder. **Not Now** is remembered; you can still select the folder in the window.

### Skills you already have at user scope

For each selected folder, the tool looks for skills with the same name that the folder's agents already load from
user scope: the folders in the table above, Codex plugins for `.agents/skills`, and Claude Code plugins for
`.claude/skills`. A Codex plugin must be installed in `<CODEX_HOME>/plugins/cache` and configured in
`<CODEX_HOME>/config.toml`, with `CODEX_HOME` defaulting to `~/.codex`. A configured plugin defaults to enabled
unless `enabled = false`; the project's `.codex/config.toml` can override that setting. The row names it as
"provided by Codex plugin `<name>@<marketplace>`". Cache-version selection and detection limits are in
[`docs/codex-plugins.md`](https://github.com/Hissal/agent-skills-sync-unity/blob/main/docs/codex-plugins.md).

For Claude Code, the tool works out "enabled" from `enabledPlugins` in the project's `.claude/settings.local.json`, then
`.claude/settings.json`, then your user `settings.json`. A plugin skill matches by its folder name, so the plugin's
`/unity:ui` matches a project skill `ui`. For details, see
[`docs/claude-code-plugins.md`](https://github.com/Hissal/agent-skills-sync-unity/blob/main/docs/claude-code-plugins.md).

When it finds one, the tool defaults to **use mine** for that skill in that folder. The row says
"using yours (found in ...)" and offers **Install the project copy in `<folder>` anyway**.

- The default and the override are per skill and per folder. A Claude user-scope copy does not affect `.agents`
  unless a copy is found for that folder too.
- An install-anyway choice is stored on this machine only in `UserSettings/AgentSkillsSync.json` and applied on
  the next sync. Clearing it withdraws the tool's managed link or copy from that folder. Foreign entries stay untouched.
- The `.agents/skills` project copy stays while another selected folder needs to link to it. Its row explains why.
- A shared folder's install-anyway toggle names agents that would lose the skill if you skip the project copy.
  A Codex plugin covers Codex alone; other agents reading `.agents/skills` still need a copy. Plain user-scope
  folders can also cover only some agents. Keep **Install anyway** checked to provide the skill for those agents.
  No warning appears when the copies found cover every agent listed for that folder.
- If the user-scope copy disappears, the next sync installs the project copy again regardless of stored choices.
  The install-anyway choice is kept for when a user-scope copy returns.
- A user-scope copy that verifiably differs from the lock still defaults to use mine. The row and sync summary warn
  that agents reading that folder don't run what your teammates run. The tool cannot tell whether your copy is
  ahead or behind. It gives no warning when the difference can't be checked, such as a skills.sh hash or non-ASCII
  file names.

Upgrading changes the default for everyone who has a detected user-scope copy, including contributors who previously
un-skipped a skill. Legacy `skippedSkills` choices are ignored and removed on the next prefs save. The first sync
withdraws managed project copies and links where they are no longer needed. Choose install anyway before syncing
to retain them. The committed `.gitignore` blocks do not change because of these machine choices.

## Install mode

The lockfile stores a content hash (`computedHash`), not a commit. The tool can therefore only check whether
upstream still matches the lock. It can never fetch the locked version. The install mode decides what happens when
upstream has changed. It is set for the whole project in `ProjectSettings/AgentSkillsSync.json`, which you commit:

```json
{
  "installMode": "latest"
}
```

When the file or the field is missing, the mode is **Latest**. Change it in the window's **Install mode** field,
which writes the file.

- **Latest** (default): installs each skill's current upstream copy. A skill whose upstream changed since it was
  locked is installed and marked **differs from lock** in the window and the sync summary. Every Latest sync
  downloads every installed skill to compare it with upstream, so **each sync needs the network**. A re-sync with
  unchanged upstream changes nothing. Before a sync, the window cannot know whether upstream moved, so it never
  shows "to update" in this mode.
- **Pinned**: installs or updates a skill only if its upstream still matches the locked hash. Otherwise the sync
  aborts, and the skill's row says it changed since it was locked: run `npx skills update` and commit
  `skills-lock.json`, or switch to Latest. Pinned checks upstream only for skills the sync has to download (to
  install, or to update a copy that no longer matches the lock). A copy already installed and matching the lock is
  kept without contacting upstream, so a sync is not an audit of whether upstream moved on.

**In both modes, `skills-lock.json` is never rewritten.** To lock what upstream has now, run `npx skills update` and
commit the lockfile.

Some lock hashes can't be checked:

- **Sources locked with a skills.sh server hash:** the GitHub owners `vercel`, `vercel-labs`, `heygen-com` and
  `remotion-dev`, and the repo `zapier/connectors`. The CLI installs those from skills.sh snapshots and records a
  hash this tool can't reproduce. Pinned **refuses** them ("can't verify lock hash"). Latest installs them without a
  check ("hash not verifiable").
- **Skills with non-ASCII file names** whose hash doesn't match. The CLI orders such names by locale, so a mismatch
  doesn't prove a change. Pinned refuses them as "can't verify lock hash". Latest installs them.

## Consent

Skills run with your agent's full permissions. Before every sync, you tick
**I trust these sources. Skills run with my agent's permissions.** in the window. A source repo this machine has
never synced from is tagged `NEW SOURCE`, and needs its own confirmation as well. If the lockfile gains a source
after the window listed the skills, Sync stops and asks you to review again. The same happens if the install mode
changed on disk. Confirmed sources are recorded per machine after a successful sync. The same rules apply in both
install modes. In Latest mode, the window also tells you that skills differing from the lock will be installed.
The [CLI command](#unity-cli-for-agents-and-ci) uses an explicit flag to confirm all new sources for its invocation.

## Migrating a project that commits vendored skills

1. Make sure `skills-lock.json` lists every vendored skill that came from a GitHub repo. If the skills were added with
   `npx skills add`, it already does. Otherwise add them with `npx skills add <owner>/<repo>`.
2. Add this package (see [Install](#install)).
3. Delete the vendored copies of the locked skills from both folders, for example
   `git rm -r .agents/skills/<name> .claude/skills/<name>`. Keep project-authored skills, which are not in the
   lockfile. The tool leaves an existing folder alone and does not manage it, so a locked skill that stays committed
   never becomes the tool's.
4. Open **Window → Agent Skills Sync** and **Sync**.
5. Commit the deletions, `skills-lock.json`, and the generated `.agents/skills/.gitignore` and
   `.claude/skills/.gitignore`.

## Recommended: scope the official Unity Claude plugin to Unity projects

The official Unity plugin for Claude Code adds about 30 skills. Enabled at user scope, it loads them in every repo
you open, Unity or not. Instead, keep it installed but disabled at user scope, and enable it per Unity project.

In `~/.claude/settings.json`:

```json
{
  "enabledPlugins": {
    "unity@unity-agent-plugin": false
  }
}
```

In each Unity project's `.claude/settings.json` (committed):

```json
{
  "enabledPlugins": {
    "unity@unity-agent-plugin": true
  }
}
```

Use the plugin id that `/plugin` shows on your machine, if it differs. Project settings override user settings, so
the plugin loads only in those projects. The tool reads the same settings: in a project that enables the plugin, its
skills count as user-scope copies for `.claude/skills`, and project skills of the same name default to use mine. The tool
does not change plugin settings itself.

## Repository layout

- `Packages/com.hissal.agent-skills-sync/`: the package (embedded in the dev project).
  - `Core/`: engine-free core (`noEngineReferences`), Editor-only.
  - `Editor/`: Unity front end (sync window, startup check).
  - `Pipeline/`: optional Editor command, compiled only when `com.unity.pipeline` is installed.
  - `Tests/Editor/`: EditMode tests.
- `Assets/`, `ProjectSettings/`, `Packages/manifest.json`: minimal Unity 6000.3 host project used
  to develop and test the package. Open the repo root in Unity.
- `docs/`: research notes behind the folder table and plugin detection.
