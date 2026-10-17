# Agent Skills Sync (Unity)

Unity Editor package that treats a project's committed `skills-lock.json` as the source of truth. It installs the
locked agent skills into `.agents/skills/` (one copy) and `.claude/skills/` (a link to it), and detects skills
contributors already have at user scope, so every agent gets each skill exactly once. A standalone command restores
them in a new Git worktree before an agent starts there.

Status: in development. See the [spec](https://github.com/Hissal/agent-skills-sync-unity/issues/1).

## Requirements

- Unity 6000.3 or later. The package is Editor-only; its [standalone restore command](#worktrees-restore-skills-before-an-agent-starts)
  runs without Unity and needs the .NET SDK 8 or later.
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
https://github.com/Hissal/agent-skills-sync-unity.git?path=Packages/com.hissal.agent-skills-sync#v0.3.0
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
- **Tell me at editor start when a new optional skill is locked**: turns the
  [new optional skill notice](#new-optional-skills) on or off for this machine.
- **New sources**: a warning, and an "I trust the new source …" toggle for each source repo this machine has not
  synced before.
- **Skills locked in `skills-lock.json`**: when the project defines [skill groups](#skill-groups), one collapsible
  section per group with a checkbox that selects all its skills, then the ungrouped skills under **Other**. A warning
  names any skill a group lists that is not locked. Each skill has an install checkbox, its source repo and what Sync will
  do to it (to install, to update, to link, installed, left alone, skipped). Tags: `NEW` (see
  [New optional skills](#new-optional-skills)), `NEW SOURCE`, `differs from lock`,
  and `can't verify lock hash` / `hash not verifiable` (see [Install mode](#install-mode)). Under each skill there is
  one row per selected folder, with its status there, any copy you already have at user scope, and an install-anyway
  toggle. A skill whose checkbox you cleared has no folder rows and reads "not installed (opted out)" (see
  [Opting out of a skill](#opting-out-of-a-skill)); one its groups leave out by default reads "not installed (not
  selected by default)".
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

The command honours this machine's saved folder selection, user-scope copies, install-anyway choices and
[opted-out skills](#opting-out-of-a-skill) over the project's [skill group](#skill-groups) defaults, including an empty folder selection. Its default mode is the project's setting. `--install_mode latest` or
`pinned` overrides that setting without saving it. The result contains skill-name arrays for `installed`,
`updated`, `removed`, `noLongerSelectedByDefault` (removals caused by a changed group default), `skipped`,
`skippedForUserScope`, `linked`, `unlinked`, and `differsFromLock`, plus
`userScopeDiffers`, `nothingChanged` and an empty `failures` array on success. Aborted or failed syncs fail the
command, and `unity run` exits nonzero with the error details. If skills were applied but saving consent and
notification state failed, the error says that the sync finished so callers can distinguish it from an aborted fetch.

### Startup check

Once per editor session the tool checks the project. If `skills-lock.json` changed since the last sync on this
machine, locked skills are missing, or installed skills are waiting to be removed, it offers to open the sync window. **Not Now** keeps it quiet until something
changes. Nothing is installed automatically; a sync needs consent in the window or through the CLI command.

- The check is cheap: it looks at folder names, links and the lockfile's hash, never at skill contents. An installed
  copy that differs from the lock is caught through the lockfile change that caused it; the window, which hashes
  every copy, shows the details.
- Skills this machine does not install (opted out of, or not selected by default) don't count. Locking, updating or
  removing one does not make the check offer a sync; it only does while the tool's copy of one is still waiting to
  be removed. A newly locked one is announced once instead (see [New optional skills](#new-optional-skills)).
- A change to the project's [skill groups](#skill-groups) that selects a skill you don't have, or stops selecting
  one you have, makes the check offer a sync, naming the skills.
- While no skills folder is selected, it does not offer a sync. It can still ask once whether to add a folder whose
  agent home appeared (see [Choosing skills folders](#choosing-skills-folders-per-machine)).

### New optional skills

A locked skill this machine does not install unless you tick it is **optional**: the project's
[skill groups](#skill-groups) leave it out by default. When the project locks a new one, the startup check tells you
once, names it, and offers to open the sync window, where the skill is tagged `NEW`. Nothing is installed unless you
tick the skill and sync.

- A skill stops being new when you sync, tick or clear its checkbox, or choose **Not Now**. The names you have been
  shown are stored on this machine in `UserSettings/AgentSkillsSync.json` (`seenSkills`).
- If the project is also out of sync, the same dialog says both. **Not Now** then keeps each quiet until it changes:
  another new optional skill is locked, or the out-of-sync state becomes a different one.
- A skill you opted out of is never announced. A newly locked skill you install by default is not announced either:
  it makes the project out of sync instead.
- Skills that were already locked when you first sync, or when you update to a package version that has this notice,
  are not announced.
- **To turn the notice off**, clear **Tell me at editor start when a new optional skill is locked** in the sync
  window. The setting is stored on this machine only (`"notifyNewSkills": false` in the same file), so it does not
  affect your teammates. The `NEW` tag in the window stays.

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
   - `ProjectSettings/AgentSkillsSync.json`, if you changed the install mode or defined [skill groups](#skill-groups).

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
  folders can also cover only some agents. Check **Install anyway** to provide the skill for those agents.
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

## Opting out of a skill

Every locked skill is installed by default, unless the project's [skill groups](#skill-groups) leave it out. To
leave one out on this machine, clear its checkbox in the window's skill list.

- **Per machine, per skill, all folders.** The choice is stored in `UserSettings/AgentSkillsSync.json`
  (`optedOutSkills`; `optedInSkills` for a skill you ticked that is not selected by default) as soon as you change the checkbox, and applies on the next sync. It is never committed, so it
  does not affect your teammates.
- **The next sync removes the skill**: the tool's links to it first, then its copy in `.agents/skills`. A folder
  with the same name that the tool did not install is not touched.
- **An opted-out skill is never downloaded.** Its source repo needs no confirmation and is not recorded as trusted.
- **Tick the checkbox again** to install it on the next sync. If its source repo was never confirmed on this
  machine, the window asks you to confirm it first.
- The committed `.gitignore` blocks still list the skill, so they stay the same on every machine.
- This differs from **use mine** (see [above](#skills-you-already-have-at-user-scope)), which skips a skill in one
  folder because your agents already have their own copy of it.

## Skill groups

A project can sort its locked skills into **skill groups** and decide, per group, whether contributors install its
skills by default. Typical use: a "recommended" group everyone gets, and optional groups ("art", "audio") each
contributor picks from. Groups are defined in `ProjectSettings/AgentSkillsSync.json`, which you commit, next to
`installMode`:

```json
{
  "installMode": "latest",
  "ungroupedSelectedByDefault": true,
  "groups": {
    "recommended": { "selectedByDefault": true, "skills": ["tdd", "code-review"] },
    "art": { "selectedByDefault": false, "skills": ["ui-uitk"] }
  }
}
```

- `groups` is an object. Each key is a group name of your choice; the window lists the groups in file order.
- `skills` lists skill names exactly as they are keyed in `skills-lock.json`. A missing `skills` is an empty group.
- `selectedByDefault` says whether a contributor who made no choice installs the group's skills. Missing means
  `true`.
- `ungroupedSelectedByDefault` is the same for locked skills that no group lists. Missing means `true`, so a
  project without groups installs every locked skill, as before.
- A skill may be in several groups. It is installed once, and by default when **any** of its groups has
  `selectedByDefault: true`.

**To edit the groups** (by hand, or by asking your agent), change this file only; there is no group editor in the
window, and `skills-lock.json` is never touched:

- "Add skill X to group Y": append `"X"` to `groups.Y.skills`. If group `Y` does not exist, add
  `"Y": { "selectedByDefault": true, "skills": ["X"] }` to `groups` (use `false` for an optional group).
- "Move skill X from group Y to group Z": remove `"X"` from `groups.Y.skills` and append it to `groups.Z.skills`.
- "Make group Y optional": set `groups.Y.selectedByDefault` to `false`.
- Keep every other member of the file as it is, and check that `X` is a key of `skills` in `skills-lock.json`.

What contributors see and get:

- The window lists each group as a collapsible section, then the ungrouped skills under **Other**. The checkbox
  beside a group's name selects or clears all its skills at once. A skill in several groups is listed in each, and
  all its rows show the same checkbox state.
- **A contributor's own choice always wins.** Ticking or clearing a skill's checkbox is stored on that machine (see
  [Opting out of a skill](#opting-out-of-a-skill)) and holds whatever the groups say later. Only a choice that
  differs from the skill's default is stored, so a skill you never touched keeps following the project.
- **Changing a group's defaults adds or removes skills on contributors' next sync.** Defaults are read again on
  every sync. Moving a skill into a selected-by-default group installs it for everyone who never chose. Moving it
  out of every selected-by-default group, or setting `ungroupedSelectedByDefault` to `false`, **removes** the tool's
  copy and links of it from every machine that never chose, on that machine's next sync. The window shows such a
  skill as "not selected by default (removed on Sync)" beforehand, and the sync summary names it under "Removed
  because no longer selected by default". A contributor who ticked the skill keeps it. A removal you caused
  yourself (opting out, or clearing the tick of a skill you had opted in to) is not listed there: each sync records
  on this machine which skills it installed by default alone (`lastSyncedDefaultSkills` in
  `UserSettings/AgentSkillsSync.json`) to tell the two apart.
- A skill that is not selected is never downloaded, needs no source confirmation and does not count as out of sync.
  The generated `.gitignore` blocks list every locked skill whatever the groups say.

Mistakes in the file:

- Invalid JSON, or a member of the wrong type (`groups` that is not an object, a `selectedByDefault` that is not
  `true` or `false`, a `skills` that is not a list of names, the same group name twice), is an error: the window
  shows a message naming the member, such as `groups.art.selectedByDefault`, and nothing syncs until it is fixed.
- A name in `skills` that `skills-lock.json` does not have (a typo, or a skill that was removed) is only a warning
  in the window. The sync ignores the name and proceeds.
- A listed lock entry with a source type the tool does not install shows in its group as "not installable by this
  tool".

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
which writes the field and keeps the rest of the file.

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
never synced from is tagged `NEW SOURCE`, and needs its own confirmation as well. A source that only skills you
[opted out of](#opting-out-of-a-skill) come from is not asked about until you opt in to one of them. If the lockfile gains a source
after the window listed the skills, Sync stops and asks you to review again. The same happens if the install mode
changed on disk. Confirmed sources are recorded per machine after a successful sync. The same rules apply in both
install modes. In Latest mode, the window also tells you that skills differing from the lock will be installed.
The [CLI command](#unity-cli-for-agents-and-ci) uses an explicit flag to confirm all new sources for its invocation.

## Worktrees: restore skills before an agent starts

A new Git worktree gets `skills-lock.json` and the generated `.gitignore` blocks from Git, but not the installed
skills (they are ignored) or `UserSettings/`. An agent started there finds none of the project's skills, and Unity
offers to sync only once it opens. The standalone `restore` command prepares the worktree first, without Unity and
without the network.

The order for a worktree launcher (configuring a particular launcher is up to you):

1. Create the checkout (`git worktree add …`).
2. Restore its skills, and go on only when the command exits 0:
   `agent-skills-sync restore --project <worktree>/<unity-project>`.
3. Start the agent.
4. Optionally open Unity. Its startup check finds the checkout in sync.

An agent that was already running when its skills were restored may need to refresh its skill list or restart.

### Installing the command

The command needs the [.NET SDK](https://dotnet.microsoft.com/download) 8 or later. It is the package's
`Tools~/AgentSkillsSync.Cli` project, which compiles the package's Unity-free `Core` sources; Unity ignores the
folder. A new worktree has not imported the package yet, so run it from a copy of the package outside the worktree,
such as a clone of this repository:

```sh
git clone --depth 1 --branch latest https://github.com/Hissal/agent-skills-sync-unity.git ~/agent-skills-sync-tool
dotnet build ~/agent-skills-sync-tool/Packages/com.hissal.agent-skills-sync/Tools~/AgentSkillsSync.Cli -c Release -o ~/agent-skills-sync-tool/bin
dotnet ~/agent-skills-sync-tool/bin/agent-skills-sync.dll restore --project <worktree>
```

To update, `git pull` the clone and build again. Any other copy of the package works too, such as
`Library/PackageCache/com.hissal.agent-skills-sync@…/` in a checkout Unity has opened:
`dotnet run --project <package>/Tools~/AgentSkillsSync.Cli -- restore --project <worktree>`. In a checkout whose
editor runs with `com.unity.pipeline`, `unity command skills_restore` runs the same restore (`--dry_run true`,
`--consent_new_sources true`, `--baseline <checkout>`).

### Set up worktree restore in the window

Open **Window > Agent Skills Sync > Worktrees** to see this checkout's choice scope, its differences from
the shared choices, and whether a new worktree at the current revision can restore offline. Shared choices carry
your folder selection, opt-ins, opt-outs and install-anyway choices between this repository's worktrees on this
machine. The section starts collapsed for a repository with just one checkout.

- **Follow the shared choices** switches between shared choices and this checkout's own choices. Keeping its
  own starts with the choices currently shown; following again takes the current shared choices.
- **Share these choices** publishes this checkout's choices for new worktrees and makes this checkout follow them.
  **Use the shared choices** replaces its choices with the shared ones. Sync to apply either change to installed skills.
- If no choices are shared and checkouts disagree, compare the candidates and choose **Make this the baseline**.
- The snapshot list shows each selected skill's recorded provenance (`sync` or `import`) and lock match. Use
  **Record installed skills** for an older installation, or Sync to create snapshots that are still missing. Import
  records existing managed copies; source approval still requires confirming the source and syncing.
- The readiness summary models an empty new worktree, using shared choices and this revision's lock and groups.
  It names missing snapshots or sources needing approval, even if this checkout already has its skills installed.

Opening or refreshing the section changes no files. Actions report their result in the window's summary.

### New worktrees

Open **Window > Agent Skills Sync > Worktrees > Restore skills in new worktrees** once per clone on
this machine. With the .NET 8 SDK or newer installed, the window builds the command from the installed package into
`AgentSkillsSync/commands/<package-version>/` in local application data. No separate clone or command path is needed.
Sync the existing checkout once (or import an older installation) to record the snapshots that restores need.

The repository's Git `post-checkout` hook then restores every registered Unity project before `git worktree add`
returns, so worktree launchers need no additional setup. Install for each Unity project in a repository; paths are
recorded relative to the checkout. One installation covers existing and future worktrees of that clone. Branch
switches, merges and rebases do not restore skills. `git worktree add --no-checkout` bypasses the hook.

The window shows whether this project's hook is installed, outdated (a different package version or missing command),
or blocked. After upgrading the package, **Update restore hook** builds the new version and updates the command for
all registered projects. Until then the previous build remains outside the checkout and continues working.
**Uninstall for this project** removes its registration; other projects and foreign hook code remain.

For scripted setup, use the built command from a stable location outside every worktree you might delete:

```sh
dotnet /path/to/agent-skills-sync.dll hook install --project /path/to/unity-project
dotnet /path/to/agent-skills-sync.dll hook status --project /path/to/unity-project
dotnet /path/to/agent-skills-sync.dll hook uninstall --project /path/to/unity-project
```

These commands print JSON (`status`, `hookPath`, `message`); status is `notInstalled`, `installed`, `outdated` or
`blocked`. Exit code 3 means an update or manual action is needed. Installing again updates the managed block.
The hook prints one outcome line per existing registered project. When restore needs a new source confirmed, missing
snapshots recorded, or a baseline chosen, the line names the reason and the command to resolve it. A restore failure
leaves checkout successful. Projects without a lockfile are skipped.

The tool keeps existing `sh`, `bash` and `dash` hook code, running the managed block before it. Non-shell hooks,
malformed managed blocks and symbolic links are left for manual setup. It uses Git's configured `core.hooksPath`
when that path is outside all this clone's checkouts. A path inside a checkout (including tracked Husky folders) is
refused without modifying that folder or Git configuration. Relative external paths are also refused because they
resolve to different folders in differently located worktrees; configure an absolute external path instead. An external hooks folder already registered to another
repository is also refused. For a hook you manage yourself, add the equivalent of:

```sh
# Git supplies an all-zero old HEAD only for a new checkout.
case "$1" in
  ''|*[!0]*) ;;
  *) if [ "$3" = 1 ]; then
       dotnet /stable/path/agent-skills-sync.dll restore --project ./path/to/project --hook-output || :
     fi ;;
esac
```

Use `.` for a Unity project at the checkout root. Repeat the restore line for each project. Keep any existing hook's
behavior when placing this snippet. Git's working directory and new-checkout arguments are documented in
[post-checkout](https://git-scm.com/docs/githooks#_post_checkout); relative configured hook paths are resolved from
that working directory ([core.hooksPath](https://git-scm.com/docs/git-config#Documentation/git-config.txt-corehooksPath)).

### Commands

`--project` names the Unity project and defaults to the current folder.

| Command | Does |
| --- | --- |
| `restore [--dry-run] [--confirm-source <owner/repo>]… [--baseline <checkout>]` | Installs the selected skills from recorded snapshots, offline. `--dry-run` reports what a run would do and writes nothing. |
| `sync [--confirm-source <owner/repo>]… [--install-mode latest\|pinned]` | The sync window's sync: fetches from GitHub, installs, and records the snapshots later restores install. |
| `import` | Records the skills an earlier version of this package installed as snapshots, as found on disk. |
| `hook install \| status \| uninstall` | Installs, inspects or removes automatic restore for this project in new worktrees; see [New worktrees](#new-worktrees). |
| `scope --shared \| --worktree` | Makes the checkout follow the repository's shared choices, or keep its own. `--shared` in a repository with no shared choices yet shares this checkout's. |

Each run prints one JSON object with a `status`. A restore also reports `unconfirmedSources`, `missingSnapshots`
(each with a `reason`), `baselineCandidates`, `choices` (`scope` and `baselineFrom`), the planned `actions`, and, once
run, a `summary` of what changed. Exit codes:

| Code | Meaning |
| --- | --- |
| 0 | Restored, synced, or (dry run) ready to restore. |
| 1 | Failed, for example a snapshot could not be copied. A failed restore leaves no skill half-installed. |
| 2 | Invalid input: no lockfile, unusable project settings, a project in no Git checkout, or unknown options. |
| 3 | Needs you: sources to confirm, snapshots to record with `sync` or `import`, or a baseline to name. Nothing changed. |

### What a restore installs

- **Requirements:** the worktree's own `skills-lock.json` and [skill groups](#skill-groups), read on every restore.
  The lockfile is never written.
- **Choices:** this machine's choices for the repository (see [Shared choices](#shared-choices)), over the skill
  groups' defaults. A skill that is not selected is not installed and needs no snapshot or consent.
- **User scope:** a fresh scan of this machine's user-scope copies and enabled plugins. Use mine and install anyway
  apply as in a sync.
- **Consent:** a source synced in any checkout of the repository on this machine needs no new consent. A source new
  to the repository stops the restore with `unconfirmedSources`, before anything is installed. Confirm it only with
  the user's explicit approval, as with [consent](#consent), by passing `--confirm-source <owner/repo>`.
- **Content:** each lock entry's recorded snapshot: the exact bytes the last explicit sync installed for that entry
  (see [Snapshots](#snapshots)). In Latest mode a restore never fetches newer upstream content; a sync, from the
  window, the Pipeline command or `agent-skills-sync sync`, is how content is updated. A lock entry with no recorded
  snapshot stops the restore with `missingSnapshots`, rather than downloading.

The worktree gets its own copies, links within itself, and its own bookkeeping, so removing or syncing one worktree
never touches another. Project-authored skills and entries the tool does not own are kept as in a sync. Repeating a
restore changes nothing.

### Shared choices

A restore keeps the repository's shared choices on this machine, in `AgentSkillsSync/repos/<key>/choices.json` in
your local application data folder (`AGENT_SKILLS_SYNC_STATE_DIR` overrides the folder), never in a checkout:
folder selection, opt-ins and opt-outs, install-anyway choices, and the sources synced so far. Each checkout keeps the
rest itself: which entries it owns, its sync fingerprints, which skills it has seen and what it dismissed.

The key is the repository's Git common directory, which all its worktrees share, plus the Unity project's path in the
checkout. So every worktree of one repository shares choices, and two clones, or two Unity projects in one
repository, share nothing.

A restored checkout follows the shared choices: a choice made in its sync window becomes the shared one and reaches
other worktrees at their next restore. `scope --worktree` makes a checkout keep its own choices, which then win over
the shared ones; `scope --shared` makes it follow them again.

**Migration.** The first restore in a repository with no shared choices adopts the choices its checkouts already
have, when they all agree. If they disagree, it stops with `baselineRequired` and lists them in
`baselineCandidates`; pass `--baseline <checkout>` to choose, or run `scope --shared` in that checkout. The checkout
whose choices were adopted follows the shared choices from then on, so a choice made in its sync window still reaches
new worktrees. Any other checkout whose own existing choices differ from the shared ones keeps them, as if it had run
`scope --worktree`. A dry run reports what it would adopt without saving it.

### Snapshots

Every sync records each selected skill it installed as a snapshot, stored once by content hash in
`AgentSkillsSync/snapshots/` in local application data, and records which snapshot each complete lock entry resolved
to in `repos/<key>/resolutions.json`. Each record names its provenance: `sync` (fetched by a Latest sync, or verified
against the lock by a Pinned one) or `import`, the install mode, and whether the bytes match the lock's
`computedHash` (`lockVerified`, never assumed). In Pinned mode a restore installs only lock-verified snapshots.

Skills installed by an earlier version of this package have no snapshots. Run `agent-skills-sync import` in that
checkout to record them as found, or sync it once.

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
  - `Core/`: engine-free core (`noEngineReferences`): Editor-only in Unity, and compiled outside it by `Tools~/`.
  - `Editor/`: Unity front end (sync window, startup check).
  - `Pipeline/`: optional Editor command, compiled only when `com.unity.pipeline` is installed.
  - `Tools~/AgentSkillsSync.Cli/`: the standalone command (.NET), which Unity does not import.
  - `Tests/Editor/`: EditMode tests.
- `Assets/`, `ProjectSettings/`, `Packages/manifest.json`: minimal Unity 6000.3 host project used
  to develop and test the package. Open the repo root in Unity.
- `docs/`: research notes behind the folder table and plugin detection.
