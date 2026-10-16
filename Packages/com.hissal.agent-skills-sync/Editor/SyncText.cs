using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Hissal.AgentSkillsSync.Editor
{
    /// <summary>The sync window's status and summary texts, built from plans and sync results only (no UI).</summary>
    internal static class SyncText
    {
        public const string WorktreesTitle = "Worktrees";
        public const string WorktreesOutsideGit = "Worktrees: this project is not in a Git checkout, so shared choices and worktree restore are unavailable.";
        public const string SharedChoicesExplanation = "Shared choices carry your folder and skill selections to other worktrees of this repository on this machine; each checkout can keep its own instead.";
        public const string FollowSharedChoices = "Follow the shared choices";
        public const string ShareTheseChoices = "Share these choices";
        public const string UseSharedChoices = "Use the shared choices";
        public const string SharedTheseChoices = "Shared this checkout's choices. New worktrees will follow them; this checkout follows them too.";
        public const string UsingSharedChoices = "This checkout now follows the shared choices. Sync to apply them to its installed skills.";
        public const string KeepingOwnChoices = "This checkout now keeps its own choices, starting with the choices shown here. Sync to apply them.";
        public const string BaselineExplanation = "No choices are shared yet and the checkouts disagree. Compare each candidate with this checkout, then choose the baseline new worktrees should follow.";
        public const string MakeBaseline = "Make this the baseline";
        public const string SelectedSkillSnapshots = "Snapshots for this checkout's selected skills";
        public const string NoSelectedSkills = "No locked skills are selected in this checkout.";
        public const string RecordInstalledSkills = "Record installed skills";
        public const string AutomaticRestore = "Automatic restore in new worktrees";

        public static string WorktreeIdentity(RepositoryIdentity identity) =>
            $"Repository: {identity.CommonDirectory}\nProject in repository: {identity.ProjectPath}\nThis checkout: {identity.Checkout}";

        public static string WorktreeScope(WorktreeSetupStatus setup) =>
            (setup.Scope == null ? "Choice scope has never been set. " : "") +
            (setup.FollowsShared ? "This checkout follows the shared choices."
                : "This checkout keeps its own choices.") +
            (setup.HasSharedChoices ? "" : " No choices are shared yet.");

        public static string WorktreeDifferences(IReadOnlyList<WorktreeChoiceDifference> differences) =>
            differences.Count == 0 ? "This checkout's choices match the shared choices."
                : "Differences from the shared choices:\n" + DescribeChoices(differences, "This checkout", "Shared");

        public static string BaselineDifferences(IReadOnlyList<WorktreeChoiceDifference> differences) =>
            differences.Count == 0 ? "Same choices as this checkout."
                : DescribeChoices(differences, "Candidate", "This checkout");

        static string DescribeChoices(IReadOnlyList<WorktreeChoiceDifference> differences, string local, string shared) =>
            string.Join("\n", differences.Select(d => $"{ChoiceKind(d.Kind)} — {local}: {ChoiceValues(d.Local)}; {shared}: {ChoiceValues(d.Shared)}"));

        static string ChoiceKind(WorktreeChoiceKind kind) => kind switch
        {
            WorktreeChoiceKind.Folders => "Folders",
            WorktreeChoiceKind.DeclinedFolders => "Declined folders",
            WorktreeChoiceKind.OptIns => "Opt-ins",
            WorktreeChoiceKind.OptOuts => "Opt-outs",
            _ => "Install anyway",
        };

        static string ChoiceValues(IReadOnlyList<string> values) => values == null ? "(automatic)"
            : values.Count == 0 ? "(none)" : string.Join(", ", values);

        public static string AdoptedBaseline(string project) => $"Made {project} the baseline for new worktrees. No installed skills were changed.";
        public static string WorktreeActionFailed(string message) => "Could not change worktree setup: " + message;
        public static string RecordedInstalledSkills(IReadOnlyList<string> skills) => skills.Count == 0
            ? "No additional installed skills could be recorded. Sync here to create any snapshots still needed."
            : $"Recorded installed skills: {string.Join(", ", skills)}. Their provenance is import; source approval still requires a sync.";

        public static string WorktreeSnapshot(WorktreeSnapshotStatus snapshot)
        {
            var resolution = snapshot.Resolution;
            var lockMatch = snapshot.MatchesLock == true ? "matches lock"
                : snapshot.MatchesLock == false ? "differs from lock" : "lock match cannot be verified";
            var provenance = resolution == null ? "no resolution recorded for this lock entry"
                : $"{resolution.Provenance.ToString().ToLowerInvariant()}, {lockMatch}";
            if (resolution == null && snapshot.Available) provenance = "lock-verified snapshot available, without a resolution for this entry";
            var available = snapshot.Available ? "available for restore" : "unavailable for restore";
            return $"{snapshot.Skill}: {available}; {provenance}." +
                (snapshot.Available && resolution != null && resolution.Snapshot != snapshot.RestoreSnapshot
                    ? " Restore will use a separate snapshot that matches the lock." : "") +
                (snapshot.Problem == null ? "" : " " + snapshot.Problem);
        }

        public static string NewWorktreeRestore(RestoreReport report)
        {
            const string prefix = "A new worktree at this revision: ";
            switch (report.Status)
            {
                case RestoreStatus.BaselineRequired:
                    return prefix + "needs a baseline. Choose one above.";
                case RestoreStatus.SourcesNeedConfirmation:
                    return prefix + "needs source approval: " + string.Join(", ", report.UnconfirmedSources) + ". Confirm these sources and sync here first.";
                case RestoreStatus.SnapshotsMissing:
                    return prefix + "needs snapshots.\n" + string.Join("\n", report.MissingSnapshots.Select(s => s.Skill + ": " + s.Reason));
                default:
                    var install = report.Plan.Actions.Where(a => a.NeedsFetch).Select(a => a.SkillName).Distinct().ToList();
                    var skipped = report.Plan.Actions.Where(a => a.Kind == PlanActionKind.SkipUserScope).Select(a => a.SkillName).Distinct().ToList();
                    return prefix + "ready to restore offline. " +
                        (install.Count == 0 ? "No project copies need installing." : "Install: " + string.Join(", ", install) + ".") +
                        (skipped.Count == 0 ? "" : " Use user-scope copies where found: " + string.Join(", ", skipped) + ".") +
                        (report.BaselineFrom == null ? "" : " Baseline: " + report.BaselineFrom + ".");
            }
        }

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
            plan.Actions.Any(a => a.SkillName == skillName && a.Withdraws)
                ? "opted out (removed on Sync)"
                : "not installed (opted out)";

        /// <summary>
        /// The row status of a skill this machine does not install: opted out of, or left out by its default alone, and
        /// whether Sync still has a managed copy or link to remove.
        /// </summary>
        public static string UnselectedLabel(InstallPlan plan, string skillName, SkillSelection skills)
        {
            if (skills.IsOptedOut(skillName)) return OptedOutLabel(plan, skillName);
            return plan.Actions.Any(a => a.SkillName == skillName && a.Withdraws)
                ? "not selected by default (removed on Sync)"
                : "not installed (not selected by default)";
        }

        /// <summary>
        /// The startup notification: why the project is out of sync, which new optional skills were locked, or both,
        /// and the offer to open the sync window. Names only the parts <see cref="StartupCheck"/> notifies for.
        /// </summary>
        public static string StartupMessage(SyncStatus status, LocalPrefs prefs)
        {
            var newSkills = StartupCheck.NewSkills(status, prefs);
            var one = newSkills.Count == 1;
            var added = newSkills.Count == 0 ? null
                : $"{(one ? "A new optional skill was" : "New optional skills were")} locked: {string.Join(", ", newSkills)}. " +
                  $"This machine does not install {(one ? "it" : "them")} unless you select {(one ? "it" : "them")}.";
            if (added != null && !StartupCheck.OutOfSync(status, prefs))
                return added + $"\n\nOpen the sync window to review {(one ? "it" : "them")}? If you choose Not Now, " +
                       $"you won't be told about {(one ? "it" : "them")} again. You can turn this notice off in the sync window.";

            return OutOfSyncReason(status, prefs) + (added == null ? "" : " " + added) +
                   "\n\nOpen the sync window to review and sync them? If you choose Not Now, " +
                   "you won't be asked again until something changes.";
        }

        static string OutOfSyncReason(SyncStatus status, LocalPrefs prefs)
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
            return reason;
        }

        /// <summary>
        /// The warning for skill groups listing names the lock does not have, one sentence per group; empty when none does.
        /// </summary>
        public static string UnknownGroupSkillsMessage(IReadOnlyList<SkillGroupSection> sections)
        {
            var groups = sections.Where(s => s.Unknown.Count > 0).ToList();
            if (groups.Count == 0) return "";
            var many = groups.Sum(s => s.Unknown.Count) > 1;
            return string.Join(" ", groups.Select(s =>
                       $"Group \"{s.Name}\" lists {(s.Unknown.Count > 1 ? "skills that are" : "a skill that is")} not in " +
                       $"{Lockfile.FileName}: {string.Join(", ", s.Unknown)}.")) +
                   $" Sync ignores {(many ? "them" : "it")}; fix or remove the {(many ? "names" : "name")} in {ProjectSyncSettings.RelativePath}.";
        }

        /// <summary>The row of a lock entry a skill group lists but this tool does not install.</summary>
        public static string UnsupportedInGroupLabel(UnsupportedSkill skill) =>
            $"not installable by this tool ({skill.SourceType} source)";

        /// <summary>A group section's header: its name, how many of its skills this machine installs, and its default.</summary>
        public static string GroupHeader(SkillGroupSection section, SkillSelection skills) =>
            $"{section.Name ?? "Other"}  ({section.Skills.Count(s => skills.IsSelected(s.Name))} of {section.Skills.Count} selected; " +
            $"{(section.SelectedByDefault ? "selected" : "not selected")} by default)";

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
