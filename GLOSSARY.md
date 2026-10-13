# Glossary

- **Use mine**: skip a locked skill's project copy in a folder because that folder's agents already read a detected
  user-scope copy. This is the default while that copy is found, even when its content differs from the lock.
- **Opt out**: the contributor's explicit per-machine choice not to install a locked skill in any skills folder.
  Stored in local prefs on that machine only. Distinct from **use mine**, which skips one folder.
- **Skill group**: a named set of locked skills the project defines, which says whether its skills are selected by
  default. A skill may be in several groups, and is selected by default when any of them selects by default; a
  skill in no group follows the project's default for ungrouped skills.
  _Avoid_: category, tag, profile.
- **Selected skill**: a locked skill this machine installs: the contributor's explicit choice for it (opt out or
  opt in), else the default its **skill groups** give it. A skill that is not selected is never fetched, needs no
  source consent and never makes the project out of sync.
- **Optional skill**: a locked skill that is not a **selected skill** on this machine by its default alone: its
  **skill groups** leave it out and the contributor made no choice for it. A skill the contributor **opted out** of
  is not optional, and one they opted in to is selected.
  _Avoid_: unselected skill (which also covers an opt-out), extra skill.
- **New skill**: an **optional skill** the contributor has not been shown yet. The startup check announces it once
  and the window tags it; it is seen once they sync, choose for it in the window, or dismiss the notice.
  _Avoid_: unseen skill, optional-skill alert.
- **Install anyway**: the contributor's explicit per-skill, per-folder override to install the project copy despite
  a detected user-scope copy. Stored in local prefs on that machine only.
