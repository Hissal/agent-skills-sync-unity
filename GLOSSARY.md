# Glossary

- **Use mine**: skip a locked skill's project copy in a folder because that folder's agents already read a detected
  user-scope copy. This is the default while that copy is found, even when its content differs from the lock.
- **Opt out**: the contributor's explicit per-machine choice not to install a locked skill in any skills folder.
  Stored in local prefs on that machine only. Distinct from **use mine**, which skips one folder.
- **Selected skill**: a locked skill this machine installs: the contributor's explicit choice for it (opt out or
  opt in), else the skill's default. A skill that is not selected is never fetched, needs no source consent and
  never makes the project out of sync.
- **Install anyway**: the contributor's explicit per-skill, per-folder override to install the project copy despite
  a detected user-scope copy. Stored in local prefs on that machine only.
