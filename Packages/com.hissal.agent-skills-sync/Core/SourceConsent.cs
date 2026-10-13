using System;
using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Skills run with the agent's full permissions, so a source repo the project has never synced
    /// from is new and the contributor must confirm it before a sync. The synced sources live in
    /// <see cref="LocalPrefs.SyncedSources"/>. Sources compare ignoring case (GitHub repo names do).
    /// Only the skills this machine installs (<see cref="SkillSelection"/>) count: a source that only
    /// unselected skills come from is neither asked about nor recorded, so opting in to one later asks.
    /// </summary>
    public static class SourceConsent
    {
        static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

        /// <summary>Sorted distinct sources of the selected skills in <paramref name="lockfile"/> never synced before.</summary>
        public static IReadOnlyList<string> NewSources(Lockfile lockfile, LocalPrefs prefs, SkillSelection selection)
        {
            var synced = new HashSet<string>(prefs.SyncedSources, Comparer);
            return selection.SelectedIn(lockfile)
                .Select(s => s.Source)
                .Where(s => !synced.Contains(s))
                .Distinct(Comparer)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// The new sources of <paramref name="lockfile"/> that are not in <paramref name="confirmed"/>;
        /// a sync may run only while this is empty.
        /// </summary>
        public static IReadOnlyList<string> Unconfirmed(Lockfile lockfile, LocalPrefs prefs, IEnumerable<string> confirmed, SkillSelection selection)
        {
            var ok = new HashSet<string>(confirmed ?? Enumerable.Empty<string>(), Comparer);
            return NewSources(lockfile, prefs, selection).Where(s => !ok.Contains(s)).ToList();
        }

        /// <summary>
        /// Adds the sources of the selected skills of a successfully synced <paramref name="lockfile"/> to the
        /// synced set (never removes one). Call only after the sync succeeded, with the selection it ran with,
        /// then <see cref="LocalPrefs.Save"/>.
        /// </summary>
        public static void RecordSynced(LocalPrefs prefs, Lockfile lockfile, SkillSelection selection)
        {
            prefs.SyncedSources = prefs.SyncedSources
                .Concat(selection.SelectedIn(lockfile).Select(s => s.Source))
                .Distinct(Comparer)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();
        }
    }
}
