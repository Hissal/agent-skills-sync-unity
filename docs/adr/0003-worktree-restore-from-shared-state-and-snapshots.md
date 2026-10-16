# Restore worktrees from machine-local shared choices and recorded snapshots

A new worktree is restored by a standalone .NET command (`Tools~/AgentSkillsSync.Cli`) that compiles the package's
engine-free `Core` sources, not by Unity: a worktree launcher must prepare skills before the agent starts, often before
the destination has imported the package or Unity has run at all. The command runs from a copy of the package outside
the worktree (a clone of this repository, or any checkout's package cache); we did not publish a NuGet tool, which
would need a feed and a second release channel next to the git-tag installs of ADR-0001.

Contributor choices that should carry between worktrees (folder selection, opt-ins and opt-outs, install-anyway
choices, synced sources) live in machine-local state outside every checkout, keyed by Git's common directory plus the
Unity project's path in the checkout. Bookkeeping stays per checkout (owned entries, sync fingerprints, synced
defaults, seen skills, dismissals), because copying it would claim files and syncs the destination does not have. We
rejected keying by checkout name, origin URL or the main checkout's location: worktrees live anywhere, clones of one
remote are separate repositories, and a repository can hold several Unity projects. ADR-0002 still holds: group
defaults are never copied into shared state, and an explicit choice (shared or the checkout's own) wins over them.

A restore installs, per complete lock entry, the snapshot its last explicit sync recorded, never the current upstream.
Without that, Latest mode would hand each new worktree whatever upstream holds that minute, so two worktrees of one
commit could run different instructions, and a restore would need the network. Snapshots are content-addressed and
copied into each worktree rather than linked, so removing one worktree cannot break another. Bytes found on disk from
before snapshots existed are recorded only by an explicit import, labelled as such, and called lock-verified only when
they hash to the lock. The cost is a second copy of each installed skill in local application data, and that content
moves forward only when someone syncs explicitly.
