# Claude Code plugins: where installed plugins and their skills live

Written for [#12](https://github.com/Hissal/agent-skills-sync-unity/issues/12) on 2026-10-06. Sources: the Claude Code docs ([plugin loading](https://code.claude.com/docs/en/plugins/loading), [manifest reference](https://code.claude.com/docs/en/plugins-reference), [install and scopes](https://code.claude.com/docs/en/plugins/install), [env vars](https://code.claude.com/docs/en/env-vars)), and the `~/.claude/plugins` folder on a Windows machine with seven marketplace plugins installed (observed read-only). The code that follows these rules is `ClaudePluginSource` in `Packages/com.hissal.agent-skills-sync/Core/ClaudePluginSource.cs`.

## 1. The plugins root

Everything below sits under one **plugins root**:

1. `$CLAUDE_CODE_PLUGIN_CACHE_DIR` when it is set. Despite the name, it moves the whole root, not just `cache/`.
2. Otherwise `$CLAUDE_CONFIG_DIR/plugins`. `CLAUDE_CONFIG_DIR` moves all of `~/.claude`, settings and plugins included.
3. Otherwise `~/.claude/plugins`.

| Path under the root | What it holds |
| :- | :- |
| `installed_plugins.json` | The install records: which plugins are installed, at which scope, and where. |
| `cache/<marketplace>/<plugin>/<version>/` | One folder per installed version of a marketplace plugin. `<plugin>` is the marketplace entry name. |
| `known_marketplaces.json`, `marketplaces/<name>/` | The marketplaces added (`source`, `installLocation`) and their clones. Read for the marketplace entries, see §3. |
| `data/<id>/` | Each plugin's persistent data (`${CLAUDE_PLUGIN_DATA}`). Holds no skills. |
| `synced/` | Plugins synced from a claude.ai account (`<name>@synced`). Not covered, see §5. |
| `.trash/` | Synced plugins that were turned off. |

**Read the install records, not the cache.** After an update or uninstall, the old version folder stays in `cache/` with an `.orphaned_at` marker and is deleted 14 days later. On the observed machine, `cache/claude-plugins-official/superpowers/` held `6.3.0` and `6.4.1`, while `installed_plugins.json` recorded only `6.4.1`. Plugins that are in use get an `.in_use` marker.

## 2. `installed_plugins.json`

Observed format (`"version": 2`). It maps each plugin id (`<name>@<marketplace>`) to an **array** of install records:

```json
{
  "version": 2,
  "plugins": {
    "unity@unity-agent-plugin": [
      {
        "scope": "user",
        "installPath": "C:\\Users\\me\\.claude\\plugins\\cache\\unity-agent-plugin\\unity\\0.1.6-beta",
        "version": "0.1.6-beta",
        "installedAt": "2026-09-02T10:50:02.471Z",
        "lastUpdated": "2026-09-18T18:21:15.704Z",
        "gitCommitSha": "ba48956ae8bee4ef8a3ef3d75dec03c16aa5cc89"
      }
    ]
  }
}
```

- `installPath` is absolute and points at the version folder (`${CLAUDE_PLUGIN_ROOT}`). For a plugin loaded in place from a local marketplace, it points at the source folder instead.
- `scope` is `user`, `project`, `local` or `managed`. The docs only say that each record carries `scope`, `installPath` and `version`. Project and local installs belong to one repository, so the tool expects a `projectPath` on them. Every observed record was `user` scope, so that field is **unverified**. The tool counts a project or local record only when its `projectPath` is this project.
- The tool also accepts a single object in place of the array, in case an older format used one. A missing or unreadable file means no plugins.

## 3. Whether an installed plugin is enabled

Installed is not the same as loaded. `enabledPlugins` in the settings files maps an id to `true` or `false`. The sources merge id by id, not as one whole value: for each id, the highest-precedence source that mentions it wins, and a source that doesn't mention the id leaves the lower source's value in effect ([find where a plugin is enabled](https://code.claude.com/docs/en/plugins/loading#find-where-a-plugin-is-enabled)). This differs from the general settings rule, where a higher file's key replaces the lower one whole.

| Source (low → high) | File |
| :- | :- |
| `--add-dir` | `.claude/settings*.json` of an added directory (session only, `true` only) |
| user | `~/.claude/settings.json` (`$CLAUDE_CONFIG_DIR/settings.json`) |
| project | `<project>/.claude/settings.json` (committed) |
| local | `<project>/.claude/settings.local.json` |
| flag | `--settings` at launch |
| managed | managed settings (`true` forces, `false` blocks) |

When no source mentions the id, `defaultEnabled` applies (default `true`). The plugin's marketplace entry's `defaultEnabled` overrides the manifest's ([metadata precedence](https://code.claude.com/docs/en/plugins-reference#metadata-precedence)). On the observed machine, four of the seven installed plugins were set to `false` in user settings, so they hold skills on disk that Claude Code does not load.

**The tool's rule:** it reads local, then project, then user settings, and falls back to `defaultEnabled`: the marketplace entry's, else the manifest's. The session-only sources (`--add-dir`, `--settings`, `--plugin-dir`) and managed settings are not read. A project skill can never see them, and managed paths are per OS.

**Dependencies.** A plugin's `dependencies` (in `plugin.json` or its marketplace entry) name the plugins that must be enabled for it to work: `"name"` (same marketplace), `"name@marketplace"`, or `{ "name", "marketplace", "version" }` ([dependencies](https://code.claude.com/docs/en/plugins/dependencies)). They change the enabled state both ways:

- A plugin that an enabled plugin depends on starts enabled regardless of its own `defaultEnabled` ([defaultEnabled](https://code.claude.com/docs/en/plugins-reference#defaultenabled)).
- A plugin whose dependency is not installed, or is installed but turned off, stays disabled with `Dependency "<dep>" is not installed` or `is disabled` ([dependency errors](https://code.claude.com/docs/en/plugins/troubleshooting#dependency-errors)).

The tool computes this over the installed plugins: a plugin is loadable when each dependency is installed (an applicable install record with a valid manifest), not set to `false`, and loadable itself. The plugins enabled by a setting or by default that are loadable are enabled, and so is every dependency they reach. The docs don't say how `plugin.json` and entry `dependencies` combine, so the tool takes both.

**Where the marketplace entry is.** `known_marketplaces.json` (under the plugins root) maps each marketplace name to its `source` and `installLocation` ([find plugins on disk](https://code.claude.com/docs/en/plugins/loading#find-plugins-on-disk)). The entry is the item in `marketplace.json`'s `plugins` array whose `name` is the id's name part. Where `marketplace.json` is ([marketplace sources](https://code.claude.com/docs/en/plugins/marketplace-reference#marketplace-sources)):

| `source.source` | `marketplace.json` |
| :- | :- |
| `github`, `git` | `<installLocation>/<source.path>`, default `.claude-plugin/marketplace.json` (`installLocation` is the clone under `marketplaces/<name>/`) |
| `directory` | `<installLocation>/.claude-plugin/marketplace.json` (`installLocation` is the path given) |
| `file` | `installLocation` itself (the path given) |

A `url` marketplace is a download under `marketplaces/<name>/` whose file name is undocumented, and a claude.ai marketplace has no local copy (`known_marketplaces_claudeai.json`), so for those the tool uses the manifest's value (see §5). The clone is the marketplace's current state, which can be newer than the installed plugin version.

## 4. Where a plugin's skills are

The plugin root is the `installPath`. The manifest is `.claude-plugin/plugin.json`, and it is optional. A manifest that is present but fails validation (invalid JSON, a type mismatch) stops the plugin from loading ([validate the manifest](https://code.claude.com/docs/en/plugins-reference#validate-the-manifest)), so the tool skips a plugin whose manifest it can't read or parse as a JSON object. It doesn't run the rest of the validation.

- **Default:** `skills/<name>/SKILL.md`, one folder per skill. All seven observed plugins use it.
- **Manifest `skills`:** a path or an array of paths, each starting with `./`, such as `"./extra-skills/"`, or `"."` for the root. Each path is either a folder of `<name>/SKILL.md` folders or one folder holding `SKILL.md` directly. The listed paths **add to** `skills/`; they do not replace it. Observed example: `andrej-karpathy-skills` sets `"skills": ["./skills/karpathy-guidelines"]`. A path that resolves outside the plugin root is rejected.
- **A root `SKILL.md`, with no `skills/` folder and no `skills` key:** the plugin loads as a single skill. The tool does not handle this case.
- **Marketplace entry `skills`:** a marketplace entry can add skills to a plugin (non-strict entries), or limit which ones load for a plugin whose source is the marketplace root. The tool reads marketplace entries for `defaultEnabled` and `dependencies` (§3), not for `skills`. None of the observed marketplaces did this.

**Namespacing.** Plugin skills appear as `/<manifest name>:<skill folder>`, for example `/unity:ui`. Without a manifest, the name comes from the marketplace entry. The namespace uses the manifest `name`, while the id in `enabledPlugins` uses the marketplace entry name, and the two can differ. The namespaced skill loads **beside** a same-named skill in `~/.claude/skills` or the project's `.claude/skills`; it does not shadow them. So `unity:ui` and a project `ui` both load, and Claude sees the same skill twice. That is why the tool counts a plugin skill as a user-scope duplicate of the project skill with the same folder name, for `.claude/skills` only.

## 5. Not covered

- **Synced plugins** (`<name>@synced`, under `synced/<account bucket>/<name>~<suffix>/`). They come from a claude.ai account and have no install record. The folder layout is undocumented.
- **Skills-directory plugins:** a plugin folder with `.claude-plugin/plugin.json` saved under `~/.claude/skills/` or `.claude/skills/` (`<name>@skills-dir`).
- **Session-only plugins:** `--plugin-dir`, `--plugin-url` and `CLAUDE_CODE_PLUGIN_DIRS` (`@inline`).
- **Seed directories** (`CLAUDE_CODE_PLUGIN_SEED_DIR`): read-only, pre-populated plugin roots for containers.
- **Dependency version ranges:** a dependency outside the declaring plugin's `version` range also leaves that plugin disabled (`Requires "<dep>" <range>, installed <version>`). The tool doesn't check ranges, so it can report such a plugin's skills.
- **Marketplace entries of `url` and claude.ai marketplaces:** no documented local `marketplace.json`, so their `defaultEnabled` override and entry `dependencies` are not applied; the manifest's values are used.
- **Plugins read by other agents.** Amp also reads `~/.claude/plugins/cache/`. The tool reports plugin skills only for `.claude/skills`, in line with how the folder layout leaves cross-reads out.
