using System;
using System.Linq;
using Hissal.AgentSkillsSync.Editor;
using Unity.Pipeline.Commands;

namespace Hissal.AgentSkillsSync.Pipeline
{
    /// <summary>Optional Pipeline entry point. Exceptions fail both connected and headless commands.</summary>
    internal static class SkillsSyncCommand
    {
        // Attribute API: com.unity.pipeline's Runtime/Attributes/CliCommandAttribute.cs and CliArgAttribute.cs.
        // CLI reference: unity skill show --path references/integration-advanced.md
        [CliCommand("skills_sync",
            "Sync locked agent skills using this machine's folder and user-scope choices. " +
            "Agents must obtain explicit user approval for the listed new source repositories before passing " +
            "--consent_new_sources true. A request to sync or install skills does not grant source consent. " +
            "Use --dry_run true to list new sources and an offline plan. " +
            "Approval already given for those sources in the current conversation remains valid.")]
        public static object Run(
            [CliArg("consent_new_sources", "Trust all source repos new to this machine for this sync. " +
                "Agents must first list them with --dry_run true and obtain explicit user approval for those sources. " +
                "A sync/install request alone is insufficient; approval already given in this conversation remains valid.")] bool consentNewSources = false,
            [CliArg("install_mode", "Override latest or pinned for this invocation without saving project settings.")] string installMode = "",
            [CliArg("dry_run", "Return the offline plan without fetching skills or saving any state.")] bool dryRun = false)
        {
            InstallMode? mode = null;
            if (!string.IsNullOrEmpty(installMode))
            {
                if (string.Equals(installMode, "latest", StringComparison.OrdinalIgnoreCase)) mode = InstallMode.Latest;
                else if (string.Equals(installMode, "pinned", StringComparison.OrdinalIgnoreCase)) mode = InstallMode.Pinned;
                else throw new ArgumentException("install_mode must be latest or pinned.");
            }

            var service = new SkillsSyncService(SkillsSyncService.UnityProjectRoot, modeOverride: mode);
            if (dryRun)
            {
                var plan = service.Plan();
                return new
                {
                    dryRun = true,
                    installMode = service.Mode.ToString().ToLowerInvariant(),
                    newSources = service.NewSources,
                    hasChanges = plan.HasChanges,
                    actions = plan.Actions.Select(Action).ToArray(),
                    managedNames = plan.ManagedNames.ToDictionary(p => p.Key.RelativePath, p => p.Value),
                    ignoredNames = plan.IgnoredNames.ToDictionary(p => p.Key.RelativePath, p => p.Value),
                };
            }

            var summary = service.Run(consentNewSources ? service.NewSources : Array.Empty<string>());
            return new
            {
                dryRun = false,
                installMode = service.Mode.ToString().ToLowerInvariant(),
                installed = summary.Installed,
                updated = summary.Updated,
                removed = summary.Removed,
                skipped = summary.Skipped,
                skippedForUserScope = summary.SkippedForUserScope,
                linked = summary.Linked,
                unlinked = summary.Unlinked,
                failures = Array.Empty<object>(),
                differsFromLock = summary.DiffersFromLock,
                userScopeDiffers = summary.UserScopeDiffers.Select(Action).ToArray(),
                nothingChanged = summary.NothingChanged,
            };
        }

        static object Action(PlanAction action) => new
        {
            kind = action.Kind.ToString(),
            skill = action.SkillName,
            folder = action.Folder.RelativePath,
            linkTarget = action.LinkTarget?.RelativePath,
            userScopeCopies = action.UserScopeCopies,
        };
    }
}
