using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Records a checkout's installed skills as snapshots (<see cref="SnapshotStore"/>) and resolutions
    /// (<see cref="SnapshotResolutions"/>), so a restore elsewhere can install the same bytes. A sync records what it
    /// just installed; existing installations are recorded only by an explicit <see cref="Import"/>.
    /// </summary>
    public static class SnapshotRecorder
    {
        /// <summary>
        /// After a successful sync: records the managed canonical copy of each selected skill. In Latest mode the sync
        /// has just fetched every one; in Pinned mode each matches the lock, except a skill whose lock hash cannot be
        /// verified, which is left unrecorded since its bytes were neither fetched nor checked.
        /// </summary>
        internal static void RecordSync(SharedState state, string skillsRoot, string prefsRoot, Lockfile lockfile,
            MachineChoices choices, InstallMode mode) =>
            Record(state, skillsRoot, prefsRoot, lockfile, choices, mode, SnapshotProvenance.Sync, keepExisting: false);

        /// <summary>
        /// Seeds the snapshots of an existing installation, made before snapshots were recorded: records the managed
        /// canonical copy of each selected skill that has no resolution yet, as found on disk, with
        /// <see cref="SnapshotProvenance.Import"/>. A copy is lock-verified only when it really hashes to the lock.
        /// </summary>
        /// <returns>The names of the skills recorded, in lock order.</returns>
        /// <exception cref="LockfileException">There is no usable lockfile.</exception>
        /// <exception cref="RepositoryException">The project is in no Git checkout.</exception>
        public static IReadOnlyList<string> Import(string unityRoot, UserEnvironment environment = null)
        {
            environment = environment ?? UserEnvironment.Current;
            var state = SharedChoices.StateOf(unityRoot, environment) ?? throw RepositoryException.NotInCheckout(unityRoot);
            var skillsRoot = SkillsSyncService.FindSkillsRoot(unityRoot);
            var lockfile = Lockfile.Load(skillsRoot);
            var settings = ProjectSyncSettings.Load(unityRoot);
            var choices = MachineChoices.Read(skillsRoot, SharedChoices.Load(unityRoot, environment), environment,
                groups: settings.Groups);
            return Record(state, skillsRoot, unityRoot, lockfile, choices, settings.InstallMode, SnapshotProvenance.Import,
                keepExisting: true);
        }

        static IReadOnlyList<string> Record(SharedState state, string skillsRoot, string prefsRoot, Lockfile lockfile,
            MachineChoices choices, InstallMode mode, SnapshotProvenance provenance, bool keepExisting)
        {
            var layout = choices.Layout;
            var canonical = ProjectScanner.Scan(skillsRoot, layout, prefsRoot: prefsRoot).For(layout.Canonical);
            var canonicalPath = Paths.InProject(skillsRoot, layout.Canonical.RelativePath);
            var installed = choices.Skills.SelectedIn(lockfile)
                .Where(s => canonical.InstalledHash(s.Name) != null)
                .Where(s => provenance == SnapshotProvenance.Import || mode == InstallMode.Latest || LockVerification.CanVerify(s))
                .ToList();
            if (installed.Count == 0) return new string[0];

            var recorded = new List<string>();
            using (state.Lock())
            {
                var resolutions = state.LoadResolutions();
                var changed = false;
                foreach (var skill in installed)
                {
                    var existing = resolutions.Find(skill);
                    if (keepExisting && existing != null) continue;
                    // Published before it is referenced, so a resolution never names a missing snapshot.
                    var snapshot = state.Snapshots.Add(Path.Combine(canonicalPath, skill.Name));
                    recorded.Add(skill.Name);
                    if (existing != null && existing.Snapshot == snapshot && existing.Provenance == provenance) continue;
                    resolutions.Record(skill, new SnapshotResolution(snapshot, provenance, mode,
                        LockVerification.MatchesLock(skill, snapshot), SnapshotResolutions.Now()));
                    changed = true;
                }
                if (changed) resolutions.Save();
            }
            return recorded;
        }
    }
}
