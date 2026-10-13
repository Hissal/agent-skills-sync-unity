using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Hissal.AgentSkillsSync.Editor
{
    /// <summary>The sync window's status and summary texts, built from plans and sync results only (no UI).</summary>
    internal static class SyncText
    {
        /// <summary>What Sync would do to the skill, most significant first, for its row in the skill list.</summary>
        public static string PendingLabel(InstallPlan plan, string skillName)
        {
            var kinds = plan.Actions.Where(a => a.SkillName == skillName).Select(a => a.Kind).ToList();
            if (kinds.Contains(PlanActionKind.Install)) return "to install";
            if (kinds.Contains(PlanActionKind.Update)) return "to update";
            if (kinds.Contains(PlanActionKind.Link)) return "to link";
            if (kinds.Contains(PlanActionKind.LeaveForeign)) return "left alone (not managed)";
            if (kinds.Contains(PlanActionKind.SkipUserScope))
                return kinds.Contains(PlanActionKind.WarnUserScopeDiffers)
                    ? "skipped where you have your own copy (differs from the lock)"
                    : "skipped where you have your own copy";
            return "installed";
        }

        /// <summary>The row status of a skill this machine opted out of: whether Sync still has a managed copy or link to remove.</summary>
        public static string OptedOutLabel(InstallPlan plan, string skillName) =>
            plan.Actions.Any(a => a.SkillName == skillName && (a.Kind == PlanActionKind.Remove || a.Kind == PlanActionKind.Unlink))
                ? "opted out (removed on Sync)"
                : "not installed (opted out)";

        /// <summary>
        /// The row status of a skill this machine does not install: opted out of, or left out by its default alone, and
        /// whether Sync still has a managed copy or link to remove.
        /// </summary>
        public static string UnselectedLabel(InstallPlan plan, string skillName, SkillSelection skills)
        {
            if (skills.IsOptedOut(skillName)) return OptedOutLabel(plan, skillName);
            return plan.Actions.Any(a => a.SkillName == skillName && (a.Kind == PlanActionKind.Remove || a.Kind == PlanActionKind.Unlink))
                ? "not selected by default (removed on Sync)"
                : "not installed (not selected by default)";
        }

        /// <summary>The startup notification: why the project is out of sync, and the offer to open the sync window.</summary>
        public static string StartupMessage(SyncStatus status, LocalPrefs prefs)
        {
            string reason;
            if (prefs.LastSyncedLockHash == null)
                reason = "This project's agent skills have not been synced on this machine yet.";
            else if (StartupCheck.LockChanged(status, prefs))
                reason = $"{Lockfile.FileName} changed since the last sync.";
            else
            {
                // A skill only waiting for removal is not missing: the project or the contributor stopped selecting it.
                var missing = status.OutOfSyncSkills.Except(status.ToRemove).ToList();
                var parts = new List<string>();
                if (missing.Count > 0) parts.Add($"Some locked skills are missing: {string.Join(", ", missing)}.");
                if (status.ToRemove.Count > 0)
                    parts.Add("Some installed skills are no longer selected and will be removed on the next sync: " +
                              $"{string.Join(", ", status.ToRemove)}.");
                reason = string.Join(" ", parts);
            }
            return reason + "\n\nOpen the sync window to review and sync them? If you choose Not Now, " +
                   "you won't be asked again until something changes.";
        }

        /// <summary>Names the lock entries whose source type this tool does not install, and says it leaves them alone.</summary>
        public static string UnsupportedMessage(IReadOnlyList<UnsupportedSkill> unsupported) =>
            "Not installed by this tool (only \"github\" sources are; these are left to whatever installs them): " +
            string.Join(", ", unsupported.Select(s => $"{s.Name} ({s.SourceType})")) + ".";

        /// <summary>What Sync does to the skill in one folder, from the plan.</summary>
        public static string FolderStatus(InstallPlan plan, LockedSkill skill, SkillsFolder folder, bool installAnywayStored, IReadOnlyList<UserScopeCopy> copies)
        {
            var kinds = plan.Actions.Where(a => a.SkillName == skill.Name && a.Folder.RelativePath == folder.RelativePath)
                .Select(a => a.Kind).ToList();
            if (!installAnywayStored && copies.Count > 0)
            {
                var status = "using yours (found in " + string.Join(", ", copies.Select(c => c.FoundIn)) + ")";
                if (kinds.Contains(PlanActionKind.Unlink) || kinds.Contains(PlanActionKind.Remove))
                    return status + "; project copy removed on Sync";
                if (folder.Role == SkillsFolderRole.Canonical &&
                    (plan.ManagedNames[folder].Contains(skill.Name) || kinds.Contains(PlanActionKind.LeaveForeign)))
                    return status + "; project copy kept for another selected folder";
                return status;
            }
            if (kinds.Contains(PlanActionKind.Install)) return "to install";
            if (kinds.Contains(PlanActionKind.Update)) return "to update";
            if (kinds.Contains(PlanActionKind.Link)) return "to link";
            if (kinds.Contains(PlanActionKind.LeaveForeign)) return "left alone (not managed)";
            return "installed";
        }

        /// <summary>Where a user-scope copy comes from, as the end of "you already have it ...".</summary>
        public static string Where(UserScopeCopy copy) =>
            copy.Plugin != null ? $"provided by {(copy.Agents == "Codex" ? "Codex " : "")}plugin {copy.Plugin}" : $"at {copy.FoundIn}";

        /// <summary>Names agents left without a skill if its shared-folder project copy is skipped.</summary>
        public static string SkipCoverageMessage(SkillsFolder folder, IReadOnlyList<UserScopeCopy> copies)
        {
            if (copies.Count == 0) return "";
            var covered = new HashSet<string>(copies.SelectMany(c => AgentNames(c.Agents)));
            var missing = folder.UserScopeLocations.SelectMany(l => AgentNames(l.Agents)).Distinct()
                .Where(agent => !covered.Contains(agent)).ToList();
            if (missing.Count == 0) return "";
            var providers = copies.Where(c => !string.IsNullOrEmpty(c.Agents)).Select(c =>
            {
                var agents = AgentNames(c.Agents).ToList();
                return string.Join(", ", agents) + (agents.Count == 1 ? " has it" : " have it") +
                       (c.Plugin == null ? $" (at {c.FoundIn})" : $" (plugin {c.Plugin})");
            }).Distinct();
            return string.Join(". ", providers) + ". " + string.Join(", ", missing) +
                   $" read {folder.RelativePath} and would not have it if you skip the project copy. " +
                   "Check Install anyway to provide it for them. A project copy kept for another selected folder still serves them.";
        }

        static IEnumerable<string> AgentNames(string agents) => (agents ?? "").Split(',')
            .Select(a => a.Trim()).Where(a => a.Length > 0 && !a.StartsWith("as /", System.StringComparison.Ordinal))
            .Select(a => a.Replace(" (synced from Claude.ai)", "").Replace(" (legacy)", ""));

        /// <summary>The warning for a skipped skill whose user-scope copy differs from the lock.</summary>
        public static string DiffersMessage(PlanAction warning) =>
            $"Your {warning.SkillName} {string.Join(", ", warning.UserScopeCopies.Select(Where))} differs from the version " +
            $"locked in {Lockfile.FileName}, so agents reading {warning.Folder.RelativePath} don't run what your teammates run.";

        /// <summary>Short label for why a skill could not be fetched.</summary>
        public static string FailureLabel(SkillFetchFailure failure)
        {
            switch (failure)
            {
                case SkillFetchFailure.Download: return "download failed";
                case SkillFetchFailure.HashMismatch: return "changed since locked";
                case SkillFetchFailure.Unverifiable: return "can't verify lock hash";
                case SkillFetchFailure.SourceUnusable: return "not found in source";
                default: return "fetch failed";
            }
        }

        /// <summary>The summary shown after a successful sync.</summary>
        public static string Describe(SyncSummary summary)
        {
            var text = new StringBuilder();
            if (summary.NothingChanged) text.AppendLine("Everything is already in sync.");
            else DescribeChanges(summary, text);
            if (summary.DiffersFromLock.Count > 0)
                text.AppendLine($"Differs from lock ({summary.DiffersFromLock.Count}): {string.Join(", ", summary.DiffersFromLock)}. " +
                                $"Installed from upstream; run `npx skills update` and commit {Lockfile.FileName} to lock them.");
            foreach (var warning in summary.UserScopeDiffers)
                text.AppendLine("Warning: " + DiffersMessage(warning));
            return text.ToString().TrimEnd();
        }

        static void DescribeChanges(SyncSummary summary, StringBuilder text)
        {
            Line(text, "Installed", summary.Installed);
            Line(text, "Updated", summary.Updated);
            Line(text, "Removed", summary.Removed);
            if (summary.NoLongerSelectedByDefault.Count > 0)
                text.AppendLine($"Removed because no longer selected by default ({summary.NoLongerSelectedByDefault.Count}): " +
                                $"{string.Join(", ", summary.NoLongerSelectedByDefault)}. Check a skill in the list to keep it installed.");
            Line(text, "Skipped (not managed by the tool)", summary.Skipped);
            if (summary.SkippedForUserScope.Count > 0)
                text.AppendLine($"Skipped in favour of your user-scope copy ({summary.SkippedForUserScope.Count}): {string.Join(", ", summary.SkippedForUserScope)}");
            if (summary.Linked.Count > 0) text.AppendLine($"Linked ({summary.Linked.Count}): {string.Join(", ", summary.Linked)}");
            if (summary.Unlinked.Count > 0) text.AppendLine($"Unlinked ({summary.Unlinked.Count}): {string.Join(", ", summary.Unlinked)}");
            var junctions = summary.LinkedBy(LinkMethod.Junction);
            if (junctions.Count > 0) text.AppendLine($"Linked as junctions (symlinks unavailable): {string.Join(", ", junctions)}");
            var copies = summary.LinkedBy(LinkMethod.Copy);
            if (copies.Count > 0) text.AppendLine($"Linked as plain copies (symlinks and junctions unavailable; re-sync after edits): {string.Join(", ", copies)}");
        }

        static void Line(StringBuilder text, string label, IReadOnlyList<string> names)
        {
            text.Append($"{label}: {names.Count}");
            if (names.Count > 0) text.Append(" (").Append(string.Join(", ", names)).Append(')');
            text.AppendLine();
        }
    }
}
