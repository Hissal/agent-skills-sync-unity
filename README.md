# Agent Skills Sync (Unity)

Unity Editor package that installs a project's locked agent skills (`skills-lock.json`) into
`.agents/skills/` and `.claude/skills/`, detecting skills contributors already have at user scope.

Status: in development. See the [spec](https://github.com/Hissal/agent-skills-sync-unity/issues/1).

## Install

Package Manager → **+** → **Install package from git URL…**:

```
https://github.com/Hissal/agent-skills-sync-unity.git?path=Packages/com.hissal.agent-skills-sync
```

## Repository layout

- `Packages/com.hissal.agent-skills-sync/`: the package (embedded in the dev project).
  - `Core/`: engine-free core (`noEngineReferences`), Editor-only.
  - `Editor/`: Unity front end (sync window, startup check).
  - `Tests/Editor/`: EditMode tests.
- `Assets/`, `ProjectSettings/`, `Packages/manifest.json`: minimal Unity 6000.3 host project used
  to develop and test the package. Open the repo root in Unity.
