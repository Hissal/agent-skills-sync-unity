using System;
using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// What the startup check needs to know about a project: the lockfile's hash and which skills a
    /// sync would still change (install, update, link, remove or unlink). Read without fetching anything.
    /// </summary>
    public sealed class SyncStatus
    {
        public SyncStatus(string lockHash, IEnumerable<string> missingSkills)
        {
            LockHash = lockHash;
            MissingSkills = (missingSkills ?? Enumerable.Empty<string>()).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        /// <summary>See <see cref="LockfileHash.Compute"/>.</summary>
        public string LockHash { get; }

        /// <summary>
        /// Sorted names of skills a sync would change: locked skills missing or out of date in at least one skills
        /// folder, and managed skills it would remove or unlink. Entries left alone as foreign do not count.
        /// </summary>
        public IReadOnlyList<string> MissingSkills { get; }

        /// <summary>Identifies this status; a decline holds while the status keeps the same fingerprint.</summary>
        public string Fingerprint => LockHash + "|" + string.Join(",", MissingSkills);

        /// <summary>
        /// Reads the project's status; null when there is no lockfile. An unusable lockfile reports
        /// no missing skills, so only a hash change surfaces it.
        /// </summary>
        public static SyncStatus Read(string projectRoot, FolderLayout layout = null)
        {
            var lockHash = LockfileHash.Compute(projectRoot);
            if (lockHash == null) return null;

            layout = layout ?? FolderLayout.Default;
            IEnumerable<string> missing;
            try
            {
                var plan = InstallPlanner.Plan(Lockfile.Load(projectRoot), ProjectScanner.Scan(projectRoot, layout), layout);
                // A left-alone (foreign) entry is never synced, so it does not count as out of sync.
                missing = plan.Actions.Where(a => a.ChangesProject).Select(a => a.SkillName);
            }
            catch (LockfileException)
            {
                missing = null;
            }
            return new SyncStatus(lockHash, missing);
        }
    }
}
