# Upstream skills

The `Sync skills` workflow (`.github/workflows/sync-skills.yml`) runs every Monday and on demand. It watches every GitHub
source with a skill in `skills-lock.json`.

## The rolling PR

One PR, `chore(skills): sync skills with upstream` on `chore/sync-skills`, is force-pushed on every run. It only changes
`skills-lock.json` and `skills-rejected.json`, and it lists:

- **Updated**: locked skills whose upstream changed, with the upstream commits that touched each one.
- **Added**: skills accepted from proposal issues.
- **Rejected**: skills rejected from proposal issues, with the reason.
- **Removed upstream**: locked skills that no longer exist upstream. They leave the lock, since a missing skill aborts
  every sync.

When there's nothing left to sync, the workflow closes the PR.

## Proposals

Each upstream skill that is neither locked nor in `skills-rejected.json`, and has no open `skill-proposal` issue, gets
an issue titled `chore(skills): adopt <name> from <owner>/<repo>`. It notes any `required_editor_version` or
`required_packages` this project doesn't meet.

- **Accept**: add the `skill-accepted` label. The next run adds the skill to the lock on the rolling PR.
- **Reject**: close the issue as **not planned**. The next run adds it to `skills-rejected.json` with the closing comment
  as the reason, or the issue's URL without one.
- Closing it as completed does nothing, so the skill is proposed again.

Merging the rolling PR closes the accepted and rejected issues. To apply a decision right away, run the workflow from
the Actions tab. Its `dry-run` input prints the PR body and the proposals without writing to GitHub.
