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
                if (folder.Role == SkillsFolderRole.Canonical && !kinds.Contains(PlanActionKind.SkipUserScope))
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
            copy.Plugin != null ? $"provided by plugin {copy.Plugin}" : $"at {copy.FoundIn}";

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
