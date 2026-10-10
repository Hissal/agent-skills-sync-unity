# Codex plugin skills

`CodexPluginSource` reports installed, configured Codex plugin skills as user-scope copies for
`.agents/skills` only. It reads local files without installing plugins or changing Codex settings.

## Sources checked

Verified on 2026-10-10 against OpenAI's [plugin packaging docs](https://developers.openai.com/plugins/build/plugins)
and Codex source at commit `806d9732c974bc8a51b8317c1bd8985544fe627c`:

- [Installed-version selection](https://github.com/openai/codex/blob/806d9732c974bc8a51b8317c1bd8985544fe627c/codex-rs/core-plugin-common/src/installed.rs),
  `active_plugin_version` and `compare_plugin_versions`.
- [Plugin loading](https://github.com/openai/codex/blob/806d9732c974bc8a51b8317c1bd8985544fe627c/codex-rs/core-plugins/src/loader.rs),
  `load_plugins` and `load_plugin_from_config`.
- [Configured plugin selection](https://github.com/openai/codex/blob/806d9732c974bc8a51b8317c1bd8985544fe627c/codex-rs/core-plugins/src/marketplace_policy.rs),
  `configured_plugins_from_stack`.

## Cache and active version

The cache is `<CODEX_HOME>/plugins/cache/<marketplace>/<plugin>/<version>/`, with `CODEX_HOME` defaulting to
`~/.codex`. The scanner uses that home for both the cache and `config.toml`.

Codex selects `local` if that version directory exists. Otherwise it sorts the version directory names and
selects the greatest. It compares semantic versions when both names parse, including prerelease and build
identifiers; otherwise it compares the original strings. It accepts only ASCII letters, digits, `.`, `-`, `_`
and `+` in a version name, excluding `.` and `..`.

Selection happens before reading the manifest. An empty lower version does not interfere with a newer install.
An empty selected version prevents loading; the scanner does not fall back to an older copy. A selected version
needs a readable JSON object in `plugin.json` or, when that file is absent, `.codex-plugin/plugin.json`.

The scanner reports each direct `skills/<name>/SKILL.md` folder, matched by folder name. A Codex plugin copy
covers Codex alone, even when another agent reads plain skills from `~/.codex/skills`.

## Enabled state

A plugin needs an entry in the effective plugins configuration. Merely leaving a plugin in the cache does not
enable it. For a configured plugin, an absent `enabled` key defaults to true; an absent plugin table does not load
the cached plugin. Explicit false disables it.

```toml
[plugins."unity@unity-agent-plugin"]
enabled = true
```

OpenAI documents that trusted project `.codex/config.toml` settings override user settings. The scanner reads
the supplied project's file after the user file and merges `enabled` per plugin. A project table without an
`enabled` key preserves the user's value. Missing config files contribute no entries. Unreadable files or
malformed table/key syntax, strings, containers or enabled values make this source return no copies.

## Detection limits

This is an offline subset of Codex's loading rules. It assumes the supplied project is trusted. It reads that
project's config, rather than walking ancestor project configs, and does not resolve system, cloud-managed,
CLI or workspace-managed plugin policy. Remote installation metadata can add plugins independently of local
config; those installs are outside this source. Manifest validation checks for a JSON object, not Codex's full
manifest schema. Custom skill roots, recursive nested skills and per-skill disabling are not detected.

`CodexPluginConfig` reads plugin tables and their `enabled` boolean without a TOML dependency. It skips unrelated
values, including multiline arrays, inline tables and strings. It does not validate unrelated scalar types or
support inline/dotted assignments that replace the plugin tables. Such configuration may need a project copy
even when Codex itself loads a plugin.

## Shared-folder skips

The window compares the agents listed by the folder's user-scope locations with `UserScopeCopy.Agents` for
the copies found. Its install-anyway toggle names the agents without a copy. This also applies to plain
user-scope folders. A fully covered folder shows no warning. The project canonical copy still stays when
another selected folder needs it as a link target.
