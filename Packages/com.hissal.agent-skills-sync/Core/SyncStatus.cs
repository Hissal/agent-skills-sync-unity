using System;
using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// What the startup check needs to know about a project: the lockfile's hash, the hash of what this machine
    /// installs from it, and which skills a sync would still change (install, link, remove or unlink). Read without fetching anything, and cheap enough
    /// for every editor start: it looks at names, links and the lockfile only, never at skill contents.
    /// </summary>
    public sealed class SyncStatus
    {
        /// <param name="noFolderSelected">The contributor selected no skills folder, so the check stays quiet.</param>
        /// <param name="selectedFolders">Relative paths of the skills folders the status was read for.</param>
        /// <param name="selectedSkillsHash">See <see cref="LockfileHash.OfSelected"/>; null when the lockfile is unusable.</param>
        /// <param name="toRemove">The <paramref name="outOfSyncSkills"/> a sync would only remove or unlink; null = none.</param>
        /// <param name="lockedSkills">Names of the skills the lock lists; null when the lockfile is unusable.</param>
        /// <param name="optionalSkills">See <see cref="SeenSkills.Optional"/>; null = none.</param>
        public SyncStatus(string lockHash, IEnumerable<string> outOfSyncSkills, bool noFolderSelected = false,
            IEnumerable<string> selectedFolders = null, string selectedSkillsHash = null, IEnumerable<string> toRemove = null,
            IEnumerable<string> lockedSkills = null, IEnumerable<string> optionalSkills = null)
        {
            LockedSkills = lockedSkills == null ? null : Sorted(lockedSkills);
            OptionalSkills = Sorted(optionalSkills);
            LockHash = lockHash;
            SelectedSkillsHash = selectedSkillsHash;
            NoFolderSelected = noFolderSelected;
            OutOfSyncSkills = Sorted(outOfSyncSkills);
            SelectedFolders = Sorted(selectedFolders);
            ToRemove = Sorted(toRemove).Where(OutOfSyncSkills.Contains).ToList();
        }

        static IReadOnlyList<string> Sorted(IEnumerable<string> names) =>
            (names ?? Enumerable.Empty<string>()).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();

        /// <summary>See <see cref="LockfileHash.Compute"/>.</summary>
        public string LockHash { get; }

        /// <summary>
        /// See <see cref="LockfileHash.OfSelected"/>: changes only when the lock changes for a skill this machine
        /// installs, unlike <see cref="LockHash"/>. Null when the lockfile is unusable.
        /// </summary>
        public string SelectedSkillsHash { get; }

        /// <summary>
        /// Sorted names of skills a sync would change: selected locked skills missing in at least one skills folder (or
        /// behind a broken link), and managed skills it would remove or unlink (no longer locked, or opted out of).
        /// Entries left alone as foreign do not count.
        /// An installed copy is not hashed, so an outdated one is not listed: a lock change surfaces through
        /// <see cref="LockHash"/> instead.
        /// </summary>
        public IReadOnlyList<string> OutOfSyncSkills { get; }

        /// <summary>
        /// The sorted <see cref="OutOfSyncSkills"/> a sync would only take away from: every change is a removal or an
        /// unlink, as for an installed skill that is no longer selected or locked. The others are missing somewhere.
        /// </summary>
        public IReadOnlyList<string> ToRemove { get; }

        /// <summary>Sorted names of the skills the lock lists for this tool to install; null when the lockfile is unusable.</summary>
        public IReadOnlyList<string> LockedSkills { get; }

        /// <summary>
        /// Sorted names of the locked skills this machine leaves out by their default alone (see
        /// <see cref="SeenSkills.Optional"/>): the ones the startup check tells the contributor about while they are new.
        /// </summary>
        public IReadOnlyList<string> OptionalSkills { get; }

        /// <summary>True when no skills folder is selected: nothing is installed and the startup check never notifies.</summary>
        public bool NoFolderSelected { get; }

        /// <summary>Sorted relative paths of the skills folders this status was read for.</summary>
        public IReadOnlyList<string> SelectedFolders { get; }

        /// <summary>
        /// Identifies this status; a decline holds while the status keeps the same fingerprint. Includes the
        /// selected folders, so changing the selection (such as accepting an offered folder) lifts a decline.
        /// Built on <see cref="SelectedSkillsHash"/>, so a lock change to a skill this machine does not install keeps it.
        /// </summary>
        public string Fingerprint => FingerprintOf(SelectedSkillsHash ?? LockHash);

        /// <summary>
        /// Whether <paramref name="fingerprint"/> identifies this status: its <see cref="Fingerprint"/>, or the form
        /// built on <see cref="LockHash"/> that versions before skill selection recorded.
        /// </summary>
        public bool HasFingerprint(string fingerprint) => fingerprint == Fingerprint || fingerprint == FingerprintOf(LockHash);

        string FingerprintOf(string hash) => hash + "|" + string.Join(",", OutOfSyncSkills) + "|" + string.Join(",", SelectedFolders);

        /// <summary>Shorthand for <see cref="Read(string, MachineChoices)"/> with the choices given one by one.</summary>
        public static SyncStatus Read(string projectRoot, FolderLayout layout = null, IEnumerable<SkillsFolder> selected = null,
            UserScopeState userScope = null, InstallAnywayChoices installAnyway = null) =>
            Read(projectRoot, new MachineChoices(layout, selected, userScope, installAnyway));

        /// <summary>
        /// Reads the project's status; null when there is no lockfile. An unusable lockfile reports
        /// no out-of-sync skills, so only a hash change surfaces it.
        /// </summary>
        /// <param name="choices">
        /// This machine's layout, folder selection, user-scope copies, install-anyway choices and skill selection. A skipped
        /// skill does not count as out of sync, nor does an opted-out skill with no managed entry left.
        /// </param>
        /// <param name="prefsRoot">The folder holding this machine's <see cref="LocalPrefs"/>; null = <paramref name="projectRoot"/>.</param>
        public static SyncStatus Read(string projectRoot, MachineChoices choices, string prefsRoot = null)
        {
            var lockHash = LockfileHash.Compute(projectRoot);
            if (lockHash == null) return null;

            choices = choices ?? MachineChoices.Default;
            var layout = choices.Layout;
            var selection = choices.Selected;
            var noFolderSelected = selection != null && !layout.Folders.Any(f => selection.Any(s => s?.RelativePath == f.RelativePath));
            IEnumerable<string> outOfSync;
            IEnumerable<string> toRemove = null;
            IEnumerable<string> lockedSkills = null;
            IEnumerable<string> optionalSkills = null;
            string selectedSkillsHash = null;
            try
            {
                // The quick scan has no hashes: any installed copy counts as current.
                var project = ProjectScanner.Scan(projectRoot, layout, readContents: false, prefsRoot: prefsRoot);
                var lockfile = Lockfile.Load(projectRoot);
                selectedSkillsHash = LockfileHash.OfSelected(lockfile, choices.Skills);
                lockedSkills = lockfile.Skills.Select(s => s.Name).ToList();
                optionalSkills = SeenSkills.Optional(lockfile, choices.Skills).ToList();
                var plan = InstallPlanner.Plan(lockfile, project, choices, FixedCheck.AlwaysCurrent);
                // A left-alone (foreign) entry is never synced, so it does not count as out of sync.
                var changes = plan.Actions.Where(a => a.ChangesProject).ToList();
                outOfSync = changes.Select(a => a.SkillName);
                toRemove = changes.GroupBy(a => a.SkillName)
                    .Where(g => g.All(a => a.Kind == PlanActionKind.Remove || a.Kind == PlanActionKind.Unlink))
                    .Select(g => g.Key);
            }
            catch (LockfileException)
            {
                outOfSync = null;
            }
            var selectedFolders = layout.Folders
                .Where(f => selection == null || selection.Any(s => s?.RelativePath == f.RelativePath))
                .Select(f => f.RelativePath);
            return new SyncStatus(lockHash, outOfSync, noFolderSelected, selectedFolders, selectedSkillsHash, toRemove,
                lockedSkills, optionalSkills);
        }
    }
}
