# Keep skill groups in the project settings, not in the lockfile

Skill groups and their defaults live in `ProjectSettings/AgentSkillsSync.json`, beside the install mode, and refer to
locked skills by name. We did not put them in `skills-lock.json`, although one file per skill would keep a group and
its skills from drifting apart: the lockfile is the `skills` CLI's format, and the CLI rewrites it on `add`, `update`
and `remove`, so members it does not know could be dropped or could break it. The cost is that a group can list a name
the lock no longer has. That is reported as a warning in the sync window rather than an error, so removing a skill
with the CLI never blocks a sync.

The settings stay in the Unity project's `ProjectSettings/` even when the lockfile sits in the folder above it. They
hold defaults only: a contributor's opt-out or opt-in is stored per machine and wins, and there is no way to mark a
group required. Defaults are resolved on every sync instead of being copied into each machine's prefs, so moving a
skill between groups reaches everyone who never chose, including removing it from them.
