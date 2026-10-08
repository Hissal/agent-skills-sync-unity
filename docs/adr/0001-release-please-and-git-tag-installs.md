# Release with release-please and install by git tag

Versions, `CHANGELOG.md` and GitHub Releases are produced by release-please from the Conventional Commit titles of
squash-merged PRs. A long-lived release PR collects changes until it is merged. We chose it over semantic-release,
which releases on every merge with no way to batch changes, and over changesets, which needs a change file in every PR
and is built for npm monorepos. The cost is that changelog entries are only as good as PR titles, which a title check
enforces and an override block in the PR body can enrich.

The package is installed from its git URL with `?path=`, not from a registry or a `upm` branch. Each release is a
`vX.Y.Z` tag that users can pin. A `latest` branch, moved to each release by the release workflow, is the default
install, so the Package Manager's Update button only ever installs released versions. A plain `main` URL also gets
unreleased changes. Tag names, the `latest` branch and the `?path=` stay fixed once people have installed them, so
moving the package or renaming tags breaks existing installs. OpenUPM can be added later on top of the same tags.
