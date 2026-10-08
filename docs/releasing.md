# Releasing

Releases are automated with [release-please](https://github.com/googleapis/release-please). Why: [ADR 0001](adr/0001-release-please-and-git-tag-installs.md).

## How a release happens

1. PRs are squash-merged into `main`. The squash commit's title is the PR title, and its body is blank.
2. On every push to `main`, release-please keeps one release PR (`chore(main): release X.Y.Z`) open and up to date. That PR bumps
   `version` in the package's `package.json`, adds a section to its `CHANGELOG.md`, and updates the pinned install URL
   in both READMEs.
3. Merging the release PR tags the merge commit `vX.Y.Z`, publishes a GitHub Release with the same notes, and moves
   the `latest` branch to it. The README's main install line follows `latest`, so the Package Manager's Update button
   only ever installs releases.

There's no deadline for merging the release PR: it collects changes until you decide they make a release.

Only commits that touch `Packages/com.hissal.agent-skills-sync/` count. A PR that only changes the host project,
`docs/` or `.github/` never causes a release.

## PR titles are the changelog

The `PR title` workflow requires every PR title to be a
[Conventional Commit](https://www.conventionalcommits.org/en/v1.0.0/) with a lowercase description. The title decides
the version bump and becomes the changelog entry, so write it for users of the package.

| Title | Bump | Changelog section |
|---|---|---|
| `fix: …` | patch | Fixed |
| `feat: …` | minor | Added |
| `perf: …` | patch | Changed |
| `revert: …` | patch | Reverted |
| `docs:`, `refactor:`, `style:`, `test:`, `build:`, `ci:`, `chore:` | none | hidden |
| any type with `!` (`feat!: …`, `refactor!: …`) | major; minor before 1.0 | ⚠ BREAKING CHANGES (shown even for hidden types) |

The largest bump among the commits since the last release wins.

## Changing what a release says

- **A richer entry, or a fix for a wrong title:** edit the merged PR's body and add a block like the one below.
  release-please uses it instead of the commit message, including footers. It can hold several commit messages.

  ```
  BEGIN_COMMIT_OVERRIDE
  feat: install skills from a lockfile above the Unity project

  fix: keep unsupported lock entries out of the generated .gitignore
  END_COMMIT_OVERRIDE
  ```

- **Choosing the version:** add a `Release-As: 1.0.0` footer to a commit, either in the squash dialog while merging or
  in an override block. 1.0.0 is only released this way.
- **Last-minute edits** to the release PR's `CHANGELOG.md` are allowed. release-please rewrites the PR whenever `main`
  moves, though, so make them right before you merge.

## Repo settings this relies on

- Squash merge only, with the PR title as the default commit title and a blank default message. Otherwise a
  conventional-looking branch commit in the squash body would be read as an extra changelog entry.
- Actions → General → "Allow GitHub Actions to create and approve pull requests" is on. release-please uses the
  built-in `GITHUB_TOKEN`, so its PR doesn't trigger other workflows such as `PR title`. That's fine as long as no
  check is required. If one is added, switch to a GitHub App or a fine-grained PAT.
