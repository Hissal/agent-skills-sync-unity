# Coding standards

Each rule is a judgement call that no tool can check. A rule a linter, a type or a test can enforce lives in that tool.

## Every parse path

Validate an external file (one a user or another tool writes) on every path that parses it, and test the invalid case
for each path.

Why: one validated path gives false confidence when a second path reads the same file. In #27, a lock entry with a blank
`computedHash` was only rejected on fetch, so a skill skipped in every folder reached the hash comparison and its
user-scope copy was reported as differing.

## Atomic filesystem changes

When a multi-step filesystem change fails partway, leave either the old state or the new one, and report success or
failure to match what is actually on disk.

Why: a half-done change that looks finished is never repaired. In #20, a failed copy left a partial skill folder behind,
and the next sync saw it as current.

## Upstream source

When copying another tool's file format or behaviour, link its source documentation in a comment or in `docs/`, and
handle each precedence and default rule it states, or record why one is skipped.

Why: these rules are easy to miss without the source to hand. In #26, the fallback to `defaultEnabled` read only the
plugin manifest, but Claude Code's plugin reference says the marketplace entry's value overrides it.

## Claims from the code

Check a doc's claim about how code behaves against the current code it describes, including the callers and
exception paths involved. An earlier comment, PR or doc is a lead to check, not proof.

Why: a claim copied from an earlier source repeats that source's mistakes. In #63, an example taken from an old review
comment said `enabledPlugins` takes the whole value from the highest-precedence settings file, but Claude Code merges
it by plugin ID, as `ClaudePluginSource` and `docs/claude-code-plugins.md` already did
([review finding](https://github.com/Hissal/agent-skills-sync-unity/pull/63#discussion_r4222347561)).
