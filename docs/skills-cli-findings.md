# `skills` CLI findings

Spike for [#2](https://github.com/Hissal/agent-skills-sync-unity/issues/2). Read against the npm package `skills` **1.7.0** (published 2026-09-17, repo `vercel-labs/skills`), lockfile `version: 1`, on 2026-10-06. Line numbers refer to `dist/cli.mjs` in that tarball.

## 1. How `computedHash` is computed

`computeSkillFolderHash(skillDir)` (line 1138). The function is byte-identical in 1.5.18, 1.5.26, 1.6.0 and 1.7.0.

1. Walk `skillDir` recursively (`readdir` with `withFileTypes`).
   - Skip folders named `.git` or `node_modules`, at any depth.
   - Take entries where `Dirent.isFile()` is true. Symlinks and junctions are neither file nor folder to `Dirent`, so the walk skips them and never follows them.
   - Empty folders add nothing. `dist`, `build` and `__pycache__` **are** hashed: only skill discovery skips those names, not the hash.
2. Each file's key is its path relative to `skillDir`, with `/` separators (`SKILL.md`, `scripts/run.sh`).
3. Sort the files with `a.relativePath.localeCompare(b.relativePath)`.
4. SHA-256 over, for each file in order, the UTF-8 bytes of the relative path followed by the file's raw bytes. There are no separators or lengths, and no newline normalisation.
5. The result is lowercase hex with no `sha256:` prefix.

**Which folder gets hashed.** For a GitHub source the CLI shallow-clones the repo (`git clone --depth 1`) and hashes `dirname(skillPath)` inside the clone. `skillPath` is the repo path of the skill's `SKILL.md`, so a root-level `SKILL.md` means the whole repo minus `.git`. For a local source it hashes the source folder. When the skill is installed by copy, the copy hashes to the same value, so the hasher also works on an installed `.agents/skills/<name>` or a user-scope copy. Checked: `experimental_install` copy → same hash as the lock.

### Reproduced in C#

`Hissal.AgentSkillsSync.SkillFolderHash.Compute(string skillDirectory)` in the engine-free core (`Packages/com.hissal.agent-skills-sync/Core/SkillFolderHash.cs`). EditMode tests (`Tests/Editor/SkillFolderHashTests.cs`) check it against hashes produced by the real CLI (see §4).

The only hard part is step 3. `localeCompare` uses ICU collation for the process's default locale, which is neither ordinal nor .NET's culture-aware order. For example, ICU puts `scripts/x` before `SKILL.md` (case-insensitive first), punctuation before digits before letters, and `a/b` after `a.b` and before `a1`. The C# comparer reimplements ICU root collation for printable ASCII: primary weights over the whole string, in the order ``␠ _ - , ; : ! ? . ' " ( ) [ ] { } @ * / \ & # % ` ^ + < = > | ~ $ 0-9 a-z`` with case folded, and then lowercase before uppercase. It agreed with Node's `localeCompare` on 500,000 random printable-ASCII string pairs (0 mismatches).

**Real-world check.** The 14 `Unity-Technologies/skills` entries in RaveRampage-Unity's `skills-lock.json` (locked 2026-09-23) all match the algorithm when it runs over the GitHub **archive** (`codeload.github.com/.../tar.gz/<sha>`) of upstream commit `a851b67`. Against current upstream HEAD they all differ, because upstream has moved on. So a GitHub archive gives the same bytes as the CLI's clone, at least with `core.autocrlf=false`; see the caveats.

### Limits and caveats (where the hash is not reproducible)

- **Non-ASCII paths.** ICU collation of non-ASCII characters is locale-tailored. In `fi`, `ä`/`å`/`ö` sort after `z`; in `en`, they sort next to `a`/`o`. The C# comparer sorts them after all ASCII by code point, which matches neither. A skill with non-ASCII file names may therefore fail verification even though nothing changed.
- **Locale also affects ASCII.** Under `da`/`nb`, `aa` sorts after `z`. Under `cs`/`cy`, `ch` is a single letter after `h`. Under `tr`, `i`/`I` change. A lock written on such a machine can disagree with ours whenever path order depends on those letter sequences. `en`, `fi`, `de`, `sv` and `ja` order printable ASCII identically.
- **Case-only differences** (`A/x` vs `a/x`) are ordered lowercase-first like ICU. A case-insensitive file system cannot hold both, so the fixtures cannot test this.
- **Line endings at lock time.** The CLI hashes its working-tree clone, so a maintainer with `core.autocrlf=true`, or a repo whose `.gitattributes` forces `eol=crlf`, records CRLF bytes. A GitHub archive (raw blobs) then hashes differently.
- **Symlinks in the source repo.** A clone with `core.symlinks=false` (the Windows default) turns symlinks into small text files, which are then hashed. With symlinks enabled they are skipped.
- **Blob-installed sources use a different hash.** For GitHub owners `vercel`, `vercel-labs`, `heygen-com`, `remotion-dev` and the repo `zapier/connectors`, the CLI first tries a snapshot download from `skills.sh` (`tryBlobInstall`, line 4101; `SKILLS_DOWNLOAD_URL` overrides the host). In that case `computedHash` is the **server-supplied** `hash` of the snapshot, not `computeSkillFolderHash`. Observed for `vercel-labs/agent-skills/web-design-guidelines`: the server hash is `sha256(path ‖ \0 ‖ contents ‖ \0 …)`, which has NUL separators. When the CLI filters the snapshot's files, it instead uses `computeSnapshotHash` (no separators). Snapshots also drop `metadata.json` and `__pycache__`/`__pypackages__` folders, and can lag GitHub. `SkillFolderHash` does not reproduce these. For those sources the fetcher should use the fallback below.
- `ref` / `sourceUrl` / `subagents` fields can appear in lock entries. The 1.7.0 lock schema is `{ source, sourceType, sourceUrl?, ref?, skillPath?, computedHash, subagents? }`. RaveRampage's lock also carries an `installedHash` the CLI never writes, which comes from that project's own tooling. The parser should ignore unknown fields.

**Fallback** when a hash can't be reproduced (cases above): compare a freshly fetched copy with the installed copy. Run `SkillFolderHash.Compute` over both, and treat the lock's hash as advisory.

## 2. Lockfile version and mismatch behaviour

- `CURRENT_VERSION = 1`. `readLocalLock` treats a missing or non-numeric `version`, a missing `skills`, a version **below** 1, or invalid JSON as an **empty lock**, silently. A version **above** 1 is accepted as is. Our sync should be stricter, as the spec says, and reject unknown versions.
- `writeLocalLock` sorts skills by name (`Object.keys().sort()`, ordinal) and writes `JSON.stringify(lock, null, 2) + "\n"`.

### What `npx skills experimental_install` does on a hash mismatch

It **never reads `computedHash`**. `runInstallFromLock` (line 6538) groups lock entries by source and calls the normal `add` flow (`runAdd([source], { skill: [...names], agent: <every .agents/skills agent>, yes: true })`). That flow:

1. clones the source at its default branch (or `ref` if locked), with no commit pinning;
2. installs the **current upstream** content into `.agents/skills/<name>` only (no `.claude/skills` link, because only the "universal" `.agents/skills` agents are targeted);
3. rewrites the lock entry with the **new** `computedHash`.

Observed on 2026-10-06 with a lock entry for `Unity-Technologies/skills` `localization` at a stale hash: no warning, exit 0, "Installed 1 skill", and `skills-lock.json` rewritten from `7a4c4a94…` to `ca07f23b…`. Failures per source are only logged (`Failed to install from <source>`), and the loop continues.

**Implication for the sync.** Matching the CLI exactly would mean silently installing whatever upstream has now and dirtying the committed lockfile. The spec's behaviour is safer and should stay: verify, refuse on mismatch with "source changed since it was locked; run `npx skills update` and commit the lockfile", and never write the lockfile. The CLI has no stricter mode to match.

## 3. Skills folders and the agents that read them

The CLI's agent table (`agents`, line 1432) gives each agent one project folder (`skillsDir`) and one user folder (`globalSkillsDir`). 21 agents share `.agents/skills` (the "universal" set that `experimental_install` targets). Only Claude Code uses `.claude/skills`. Every other agent has its own folder (`.cline/skills`, `.windsurf/skills`, `.kiro/skills` and so on), and those are out of scope (spec #1).

The table is often stale or incomplete, so the user-scope column below comes from each agent's own docs or source where available. `~` is the user's home (`%USERPROFILE%` on Windows). "Table" means the CLI agent table only, not confirmed elsewhere.

### `.claude/skills` (project)

| Agent | User-scope locations it reads | Env-var override | Source |
|---|---|---|---|
| **Claude Code** | `~/.claude/skills/`, plus `~/.claude/skills/synced/` (claude.ai-synced, managed). Enterprise: `.claude/skills/` in the managed settings dir (`C:\Program Files\ClaudeCode\`, `/Library/Application Support/ClaudeCode/`, `/etc/claude-code/`). Plugins: `<plugin>/skills/`, namespaced `/plugin:skill`, cached under `~/.claude/plugins`. | `CLAUDE_CONFIG_DIR` replaces `~/.claude` ("All settings, session history, and plugins are stored under this path"; the CLI table applies it to skills too). `CLAUDE_CODE_PLUGIN_CACHE_DIR` moves the plugin cache. | [docs: skills](https://code.claude.com/docs/en/skills), [env vars](https://code.claude.com/docs/en/env-vars), [managed settings](https://code.claude.com/docs/en/managed-settings) |

Claude Code facts that matter for the sync:
- Precedence is enterprise > **personal > project**: a same-named `~/.claude/skills/x` **shadows** the project's `.claude/skills/x`. Plugin skills are namespaced, so both load.
- Claude Code does **not** read `.agents/skills`.
- Project skills load from `.claude/skills` in the start directory and every parent up to the repo root. Symlinked skill folders are supported and de-duplicated by target. In a linked git worktree with no root `.claude/skills`, Claude Code (v2.1.277+) loads the **main checkout's** project skills.

Other agents that also read `.claude/skills` (project and/or user): Cursor, OpenCode, GitHub Copilot, Amp, Kilo Code, Cline, Warp, Deep Agents, Firebender, Kimi Code (all in the table below). So a project `.claude/skills` link can double-load for them alongside `.agents/skills`; most de-duplicate by name.

### `.agents/skills` (project)

| Agent | User-scope locations it reads | Env-var override | Source |
|---|---|---|---|
| **Codex** | `~/.agents/skills` (documented); `$CODEX_HOME/skills` (default `~/.codex/skills`), deprecated but still loaded; `$CODEX_HOME/skills/.system` (bundled system skills); admin `/etc/codex/skills`. Project also reads `<repo>/.codex/skills`. | `CODEX_HOME` moves only the `~/.codex/skills` root. `~/.agents/skills` always uses the OS home. | [docs](https://learn.chatgpt.com/docs/build-skills) (lists only `$HOME/.agents/skills`); source `codex-rs/ext/skills/src/host_roots.rs` @ `c0c230e` ("Deprecated user skills location (`$CODEX_HOME/skills`), kept for backward compatibility"); observed behaviour reported in #2. CLI table lists only `$CODEX_HOME/skills`. |
| **Cursor** | `~/.agents/skills`, `~/.cursor/skills`, `~/.claude/skills`, `~/.codex/skills` | none documented | [docs](https://cursor.com/docs/context/skills). Table: `~/.cursor/skills` only. |
| **GitHub Copilot** (CLI, coding agent) | `~/.copilot/skills`, `~/.agents/skills` | `COPILOT_HOME` replaces `~/.copilot` (CLI) | [docs: create skills](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/create-skills), [CLI reference](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-command-reference) |
| **GitHub Copilot in VS Code** | `~/.copilot/skills`, `~/.claude/skills`, `~/.agents/skills` (`chat.agentSkillsLocations` is deprecated) | none documented | [VS Code docs](https://code.visualstudio.com/docs/agent-customization/agent-skills) |
| **Gemini CLI** | `~/.gemini/skills`, `~/.agents/skills` (the alias wins on a name clash at the same tier). Workspace: `.gemini/skills`, `.agents/skills`. | `GEMINI_CLI_HOME` replaces the home dir for **both** `~/.gemini` and `~/.agents` | [docs](https://github.com/google-gemini/gemini-cli/blob/main/docs/cli/skills.md); source `packages/core/src/config/storage.ts`, `utils/paths.ts` |
| **OpenCode** | `~/.config/opencode/skills`, `~/.claude/skills`, `~/.agents/skills` | `OPENCODE_CONFIG_DIR` adds an extra config dir; `OPENCODE_DISABLE_CLAUDE_CODE_SKILLS` / `OPENCODE_DISABLE_CLAUDE_CODE` turn off `.claude/skills`; `XDG_CONFIG_HOME` moves `~/.config` (table) | [docs: skills](https://opencode.ai/docs/skills/), [CLI env vars](https://opencode.ai/docs/cli/) |
| **Amp** | `~/.config/agents/skills`, `~/.agents/skills`, `~/.config/amp/skills`, `~/.claude/skills`, `~/.claude/plugins/cache/`, plus `amp.skills.path` | none documented (table resolves `~/.config` via `XDG_CONFIG_HOME`) | [docs](https://ampcode.com/docs/customize/skills) |
| **Cline** | `~/.cline/skills`, `~/.agents/skills`. Project: `.clinerules/skills`, `.cline/skills`, `.claude/skills`, `.agents/skills`. | `CLINE_DIR` replaces `~/.cline` (CLI/SDK) | [docs](https://docs.cline.bot/features/skills) (omit `.agents`); source `apps/vscode/src/core/storage/skill-directories.ts`, `sdk/packages/shared/src/storage/paths.ts` |
| **Kilo Code** | `~/.kilo/skills`, `~/.agents/skills`, `~/.claude/skills` (when Claude Code compatibility is on), plus `skills.paths` in `kilo.jsonc` | none documented | [docs](https://kilo.ai/docs/customize/skills). Table: `~/.kilo/skills` only. |
| **Antigravity** (AGY / IDE) | `~/.gemini/config/skills` (current), `~/.gemini/antigravity/skills` (legacy). Project also accepts `.agent/skills`. | none documented | [docs](https://antigravity.google/docs/skills/); [observed by M. Atamel, 2026-07](https://atamel.dev/posts/2026/07-01_where_agy_agent_skills/). Table: legacy path only. |
| **Antigravity CLI** | `~/.gemini/antigravity-cli/skills`, `~/.gemini/config/skills`, `~/.gemini/skills` (observed), plugins under `~/.gemini/antigravity-cli/plugins/<name>/skills/` | none documented | same as above |
| **Droid** (Factory) | `~/.factory/skills`, `~/.agents/skills`, `~/.agent/skills` (compat). Project: `.factory/skills`, `.agents/skills`, `.agent/skills`. | none documented | [docs](https://docs.factory.com/cli/configuration/skills) |
| **Deep Agents** (LangChain CLI) | `$DEEPAGENTS_HOME/<agent>/skills` (default `~/.deepagents/<agent>/skills`), `~/.agents/skills`, `~/.claude/skills` (experimental) | `DEEPAGENTS_HOME` | [docs](https://docs.langchain.com/oss/python/deepagents/cli/memory-and-skills). Table: `~/.deepagents/agent/skills`. |
| **Firebender** | `~/.firebender/skills`, `~/.goose/skills`, `~/.claude/skills`, `~/.codex/skills`, `~/.cursor/skills`, `~/.agents/skills` | none documented | [docs](https://docs.firebender.com/multi-agent/skills) (project folder documented as `.firebender/skills`; `.agents/skills` from the table) |
| **Kimi Code CLI** | `$KIMI_CODE_HOME/skills` (default `~/.kimi-code/skills`), `~/.agents/skills`, plus `extra_skill_dirs` in `config.toml` | `KIMI_CODE_HOME` (does not move `~/.agents/skills`) | [docs](https://www.kimi.com/code/docs/en/kimi-code-cli/customization/skills.html) |
| **Warp** | `~/.agents/skills`, `~/.warp/skills`, `~/.claude/skills`, `~/.codex/skills`, `~/.cursor/skills`, `~/.gemini/skills`, `~/.copilot/skills`, `~/.factory/skills`, `~/.github/skills`, `~/.opencode/skills` | `WARP_SKILL_DIRS` (extra dirs, cloud agents) | [docs](https://docs.warp.dev/agent-platform/agent/skills) |
| **Zed** | `~/.agents/skills` only ("custom search paths are not supported") | none | [docs](https://zed.dev/docs/ai/skills) |
| **Replit** | none local (workspace skills live in Replit settings) | n/a | [docs](https://docs.replit.com/core-concepts/agent/skills) |
| **Dexto** | `~/.agents/skills` | none known | Table only (not verified) |
| **Loaf** | `~/.agents/skills` | none known | Table only (not verified) |
| **Sarvam Code** | `~/.agents/skills` | table defines `SARVAM_HOME`, but not for this path | Table only (not verified) |
| **PromptScript** | none (`globalSkillsDir: undefined`) | n/a | Table only |
| *"Universal"* (CLI pseudo-agent) | `$XDG_CONFIG_HOME/agents/skills` (default `~/.config/agents/skills`) | `XDG_CONFIG_HOME` | Table only. This is where `npx skills add -g` puts universal skills. Amp reads it. |

### What this means for per-machine folder selection

- **Claude side**: one user location to check, `$CLAUDE_CONFIG_DIR/skills` (default `~/.claude/skills`), plus installed plugins (see [claude-code-plugins.md](claude-code-plugins.md)).
- **`.agents` side**: no single answer. `~/.agents/skills` is read by Codex, Copilot, Gemini CLI, OpenCode, Amp, Cline, Kilo, Droid, Deep Agents, Firebender, Kimi, Warp, Zed and Cursor, but not by Antigravity. Agent-specific homes also count for their own agent: `~/.codex/skills` (Codex, Cursor, Warp, Firebender), `~/.config/agents/skills` (Amp, CLI `-g`), `~/.copilot/skills`, `~/.gemini/skills`, and others. The env overrides to honour are `CODEX_HOME` (only for `~/.codex/skills`), `GEMINI_CLI_HOME` (which also moves `~/.agents`), `COPILOT_HOME`, `XDG_CONFIG_HOME` and `CLAUDE_CONFIG_DIR`.
- Several `.agents` agents also read `~/.claude/skills` (Cursor, OpenCode, Copilot in VS Code, Amp, Kilo, Warp, Firebender, Deep Agents). A Claude user-scope copy can therefore also cover them.

## 4. Fixtures

`Packages/com.hissal.agent-skills-sync/Tests/Editor/Fixtures~/SkillFolderHash/` (the `~` suffix keeps Unity from importing them or writing `.meta` files):

| Fixture | Covers |
|---|---|
| `minimal` | a lone `SKILL.md` |
| `nested` | sub-folders, mixed-case names (`README.md`, `Zeta.md`, `alpha.md`), `build/` being hashed |
| `punctuation-names` | file and folder names with every Windows-legal ASCII punctuation, digits, and leading `_`/`-`/`0`. ICU and ordinal order differ here. |
| `byte-exact` | CRLF, no trailing newline, an empty file, a UTF-8 BOM with non-ASCII content, and non-UTF-8 binary |

The `.git`/`node_modules`/empty-folder exclusion test builds its tree at run time, because a `.git` folder can't be committed. Its expected value is the `nested` hash, and `skills@1.7.0` gave that same hash for the same tree, checked by hand.

**How the hashes were generated:** run `node generate-hashes.mjs` in that folder (Node 22, `en-FI` locale, Windows 11). For each fixture it runs `npx skills@1.7.0 add <fixture> -y --agent codex --copy` in a temporary project and prints `computedHash` from the resulting `skills-lock.json`. `.gitattributes` marks the fixtures `-text`, so git never rewrites their line endings.
