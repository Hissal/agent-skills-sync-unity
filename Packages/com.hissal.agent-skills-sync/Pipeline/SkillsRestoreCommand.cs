using System.Linq;
using Hissal.AgentSkillsSync.Editor;
using Unity.Pipeline.Commands;

namespace Hissal.AgentSkillsSync.Pipeline
{
    /// <summary>
    /// Optional Pipeline entry point to the same restore as the standalone command (<see cref="WorktreeRestore"/>), for
    /// a checkout whose editor is already running. Exceptions fail both connected and headless commands.
    /// </summary>
    internal static class SkillsRestoreCommand
    {
        [CliCommand("skills_restore",
            "Restore this checkout's selected skills from the snapshots its lock entries last synced to, offline. " +
            "Never downloads: a skill with no recorded snapshot is reported for an explicit skills_sync. " +
            "Agents must obtain explicit user approval for the listed new source repositories before passing " +
            "--consent_new_sources true. Use --dry_run true to list them, the baseline candidates and the plan.")]
        public static object Run(
            [CliArg("consent_new_sources", "Trust the source repos new to this repository for this restore. " +
                "Agents must first list them with --dry_run true and obtain explicit user approval.")] bool consentNewSources = false,
            [CliArg("baseline", "The checkout whose choices become the shared ones when checkouts disagree.")] string baseline = "",
            [CliArg("dry_run", "Report what a restore would do without writing anything.")] bool dryRun = false)
        {
            var restore = new WorktreeRestore(SkillsSyncWindow.UnityProjectRoot, baseline: string.IsNullOrEmpty(baseline) ? null : baseline);
            var plan = restore.Plan();
            var report = dryRun ? plan : restore.Run(consentNewSources ? plan.UnconfirmedSources : null);
            return new
            {
                status = report.Status.ToString(),
                dryRun = report.DryRun,
                installMode = report.Mode?.ToString().ToLowerInvariant(),
                scope = report.Status == RestoreStatus.BaselineRequired ? null : report.Scope.ToString().ToLowerInvariant(),
                baselineFrom = report.BaselineFrom,
                baselineCandidates = report.BaselineCandidates,
                unconfirmedSources = report.UnconfirmedSources,
                missingSnapshots = report.MissingSnapshots.Select(m => new { skill = m.Skill, reason = m.Reason }).ToArray(),
                installed = report.Summary?.Installed,
                updated = report.Summary?.Updated,
                removed = report.Summary?.Removed,
                skippedForUserScope = report.Summary?.SkippedForUserScope,
                nothingChanged = report.Summary?.NothingChanged,
            };
        }
    }
}
