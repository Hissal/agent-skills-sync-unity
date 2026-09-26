# Agent Skills Sync

Editor-only Unity package that treats a project's committed `skills-lock.json` as the source of
truth and installs the locked agent skills into `.agents/skills/` (canonical copy) and
`.claude/skills/` (link), detecting skills contributors already have at user scope.

Status: in development. See the [spec](https://github.com/Hissal/agent-skills-sync-unity/blob/main/docs/specs/agent-skills-sync.md).

## Install

Package Manager → **+** → **Install package from git URL…**:

```
https://github.com/Hissal/agent-skills-sync-unity.git?path=Packages/com.hissal.agent-skills-sync
```
