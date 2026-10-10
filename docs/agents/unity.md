# Unity config

This repo's Unity facts and policies, only where they differ from the Unity skills' defaults: a missing field means the owning skill's default. Written by `/setup-matt-pocock-skills`, which keeps hand-written lines when re-run.

When the repo contradicts a fact here (a moved doc, script, workflow or project folder), trust the repo, say so once, and suggest re-running `/setup-matt-pocock-skills`.

## Project

- **Path**: `.` (host project); `Packages/com.hissal.agent-skills-sync` (package)
- **Shape**: `both`

## Verification

Run from Git Bash on Windows with `unity` and Python 3 (`python`) on `PATH`.
Close this project's editor before running the headless command.

- **Targeted tests**: `bash scripts/test-editmode.sh 'Namespace.TestFixture'`
- **Full EditMode suite**: `bash scripts/test-editmode.sh`

The optional filter passes through to `unity test --filter` and is case-sensitive.
The script resolves the host project from its own location, writes NUnit results
to a temporary file, and prints total / passed / failed counts and failure details.
A failed launch or compile, invalid report, failing test, or zero tests exits nonzero.

This is an Editor-only package with no PlayMode tests and no player build.
Local verification stops at the full EditMode suite. CI is tracked in
[#42](https://github.com/Hissal/agent-skills-sync-unity/issues/42).
