using System;
using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// What the startup check needs to know about a project: the lockfile's hash and which skills a
    /// sync would still change (install, link, remove or unlink). Read without fetching anything, and cheap enough
    /// for every editor start: it looks at names, links and the lockfile only, never at skill contents.
    /// </summary>
    public sealed class SyncStatus
    {
        /// <param name="noFolderSelected">The contributor selected no skills folder, so the check stays quiet.</param>
        /// <param name="selectedFolders">Relative paths of the skills folders the status was read for.</param>
        public SyncStatus(string lockHash, IEnumerable<string> missingSkills, bool noFolderSelected = false,
            IEnumerable<string> selectedFolders = null)
        {
            LockHash = lockHash;
            NoFolderSelected = noFolderSelected;
            MissingSkills = Sorted(missingSkills);
            SelectedFolders = Sorted(selectedFolders);
        }

        static IReadOnlyList<string> Sorted(IEnumerable<string> names) =>
            (names ?? Enumerable.Empty<string>()).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();

        /// <summary>See <see cref="LockfileHash.Compute"/>.</summary>
        public string LockHash { get; }

        /// <summary>
        /// Sorted names of skills a sync would change: locked skills missing in at least one skills folder (or behind
        /// a broken link), and managed skills it would remove or unlink. Entries left alone as foreign do not count.
        /// An installed copy is not hashed, so an outdated one is not listed: a lock change surfaces through
        /// <see cref="LockHash"/> instead.
        /// </summary>
        public IReadOnlyList<string> MissingSkills { get; }

        /// <summary>True when no skills folder is selected: nothing is installed and the startup check never notifies.</summary>
        public bool NoFolderSelected { get; }

        /// <summary>Sorted relative paths of the skills folders this status was read for.</summary>
        public IReadOnlyList<string> SelectedFolders { get; }

        /// <summary>
        /// Identifies this status; a decline holds while the status keeps the same fingerprint. Includes the
        /// selected folders, so changing the selection (such as accepting an offered folder) lifts a decline.
        /// </summary>
        public string Fingerprint => LockHash + "|" + string.Join(",", MissingSkills) + "|" + string.Join(",", SelectedFolders);

        /// <summary>
        /// Reads the project's status; null when there is no lockfile. An unusable lockfile reports
        /// no missing skills, so only a hash change surfaces it.
        /// </summary>
        /// <param name="selected">The folders this machine installs into (see <see cref="FolderSelection.Effective"/>); null = every folder in the layout.</param>
        /// <param name="userScope">The user-scope copies found (see <see cref="UserScopeScanner"/>); null = none.</param>
        /// <param name="skips">The contributor's per-folder skip choices; null = none. A skipped skill does not count as missing.</param>
        public static SyncStatus Read(string projectRoot, FolderLayout layout = null, IEnumerable<SkillsFolder> selected = null,
            UserScopeState userScope = null, SkipChoices skips = null)
        {
            var lockHash = LockfileHash.Compute(projectRoot);
            if (lockHash == null) return null;

            layout = layout ?? FolderLayout.Default;
            var selection = selected?.ToList();
            var noFolderSelected = selection != null && !layout.Folders.Any(f => selection.Any(s => s?.RelativePath == f.RelativePath));
            IEnumerable<string> missing;
            try
            {
                var project = ProjectScanner.Scan(projectRoot, layout, readContents: false);
                var plan = InstallPlanner.Plan(Lockfile.Load(projectRoot), project, layout, PresentCopyIsCurrent.Instance,
                    selected: selection, userScope: userScope, skips: skips);
                // A left-alone (foreign) entry is never synced, so it does not count as out of sync.
                missing = plan.Actions.Where(a => a.ChangesProject).Select(a => a.SkillName);
            }
            catch (LockfileException)
            {
                missing = null;
            }
            var selectedFolders = layout.Folders
                .Where(f => selection == null || selection.Any(s => s?.RelativePath == f.RelativePath))
                .Select(f => f.RelativePath);
            return new SyncStatus(lockHash, missing, noFolderSelected, selectedFolders);
        }

        /// <summary>The quick scan has no hashes: any installed copy counts as current.</summary>
        sealed class PresentCopyIsCurrent : IInstalledCopyCheck
        {
            public static readonly PresentCopyIsCurrent Instance = new PresentCopyIsCurrent();

            public bool IsCurrent(LockedSkill skill, string installedHash) => true;
        }
    }
}
