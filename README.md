# Agent Skills Sync (Unity)

Editor-only Unity package that treats a project's committed `skills-lock.json` as the source of truth. It installs
the locked agent skills into `.agents/skills/` (one copy) and `.claude/skills/` (a link to it), and detects skills
contributors already have at user scope, so every agent gets each skill exactly once.

Status: in development (0.1.0). See the [spec](https://github.com/Hissal/agent-skills-sync-unity/issues/1).

## Requirements

- Unity 6000.3 or later. The package is Editor-only.
- A `skills-lock.json` written by the [`skills` CLI](https://github.com/vercel-labs/skills) (lockfile `version` 1),
  in the Unity project folder or the folder directly above it (a repo that keeps its Unity project in a subfolder).
  The skills folders are installed next to the lock; the sync window shows which lock it uses. Every skill must come
  from a public GitHub repo (`sourceType` `github`).
- Network access to github.com when you sync. Node is **not** needed to sync; only maintainers who edit the lockfile
  need it.
- [Git](https://git-scm.com/) 2.14 or later on your `PATH`, to install the package from its git URL. Unity's Package
  Manager needs it for any [git dependency](https://docs.unity3d.com/6000.3/Documentation/Manual/upm-git.html#req).

## Install

Package Manager → **+** → **Install package from git URL…**:

```
https://github.com/Hissal/agent-skills-sync-unity.git?path=Packages/com.hissal.agent-skills-sync
```

Or add it to `Packages/manifest.json`:

```json
"com.hissal.agent-skills-sync": "https://github.com/Hissal/agent-skills-sync-unity.git?path=Packages/com.hissal.agent-skills-sync"
```

Append `#<tag or commit>` to the URL to pin a version.

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
  one row per selected folder, with its status there, any copy you already have at user scope, and a skip toggle.
- **I trust these sources. Skills run with my agent's permissions.**: the consent box. **Sync** stays disabled until
  it is ticked and every new source is confirmed. Consent and confirmations are not saved: they reset on Refresh and
  after each sync.
- **Sync** and **Refresh**, then a summary of the last sync.

### Startup check

Once per editor session the tool checks the project. If `skills-lock.json` changed since the last sync on this
machine, or locked skills are missing, it offers to open the sync window. **Not Now** keeps it quiet until something
changes. Nothing is installed without the window's consent.

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
already has one. Its marked block lists every locked skill, and in `.claude/skills` also the project-authored skills
it links there:

```gitignore
# >>> Agent Skills Sync: managed from skills-lock.json; do not edit this block.
# Lists every skill the tool may install or link here, on any machine. Anything else stays tracked.
/some-skill
# <<< Agent Skills Sync
```

- Installed skills stay out of git, and project-authored skills beside them stay tracked.
- The block depends only on `skills-lock.json` and the committed project-authored skills, never on one machine's
  folder selection or skips. Every teammate's sync writes the same block, so it never shows up as a local change.
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
user scope: the folders in the table above, and for `.claude/skills` the skills of installed, enabled Claude Code
plugins. The tool works out "enabled" from `enabledPlugins` in the project's `.claude/settings.local.json`, then
`.claude/settings.json`, then your user `settings.json`. A plugin skill matches by its folder name, so the plugin's
`/unity:ui` matches a project skill `ui`. For details, see
[`docs/claude-code-plugins.md`](https://github.com/Hissal/agent-skills-sync-unity/blob/main/docs/claude-code-plugins.md).

When it finds one, the skill's folder row says where ("you already have it at `~/.claude/skills`" or "provided by
plugin `unity@…`") and offers **Skip the project copy in `<folder>` (use mine)**:

- A skip is per skill **and** per folder. Skipping the Claude link does not skip the `.agents` copy, and the reverse
  is also true.
- The choice is stored on this machine only (`UserSettings/AgentSkillsSync.json`) and applied on the next sync. The
  sync removes the tool's link or copy from that folder only. Un-skipping installs it again.
- The `.agents/skills` copy stays while a selected `.claude/skills` folder still links to it. The row then says
  "kept: another selected folder links to it".
- A skip only takes effect while your user-scope copy is found. If the copy disappears, the project copy is installed
  again, and your choice is kept for when the copy comes back.
- **Differs warning.** If a user-scope copy you skip in favour of verifiably differs from the locked version, the row
  and the sync summary warn that the agents reading that folder don't run what your teammates run. The tool cannot
  tell whether your copy is ahead or behind. It gives no warning when the difference can't be checked: a skills.sh
  hash, or a copy with non-ASCII file names.

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
skills count as user-scope copies for `.claude/skills`, and you can skip project skills of the same name. The tool
does not change plugin settings itself.

## Repository layout

- `Packages/com.hissal.agent-skills-sync/`: the package (embedded in the dev project).
  - `Core/`: engine-free core (`noEngineReferences`), Editor-only.
  - `Editor/`: Unity front end (sync window, startup check).
  - `Tests/Editor/`: EditMode tests.
- `Assets/`, `ProjectSettings/`, `Packages/manifest.json`: minimal Unity 6000.3 host project used
  to develop and test the package. Open the repo root in Unity.
- `docs/`: research notes behind the folder table and plugin detection.
