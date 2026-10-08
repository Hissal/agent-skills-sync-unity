# Coding standards

Judgement rules only. Anything a linter, a type or a test can enforce belongs there instead.

## Validate external files on every parse path

Validate data read from a file that a user or another tool writes, on every path that parses it, and test the invalid case for each path.

Why: one validated path gives false confidence when a second path reads the same file. In #36, non-GitHub lock entries skipped the name check that GitHub entries got, so an unsafe name reached the managed `.gitignore`.

## Leave the old state or the new one, and report what is on disk

When a multi-step filesystem change fails partway, it leaves either the old state or the new one, and reports success or failure to match what is actually on disk.

Why: a half-done change that looks finished is never repaired. In #20, a failed copy left a partial skill folder behind, and the next sync saw it as current.

## Link the source when copying another tool's format or behaviour

When copying another tool's file format or behaviour, link its source documentation in a comment or in `docs/`, and handle each precedence and default rule it states, or record why one is skipped.

Why: these rules are easy to miss without the source to hand. In #26, `enabledPlugins` was merged key by key across settings files, but Claude Code's settings precedence takes the whole value from the highest-precedence file.
