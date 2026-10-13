using System;
using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Which locked skills the contributor has already been shown (<see cref="LocalPrefs.SeenSkills"/>, on this machine
    /// only). A skill is new when it is locked, optional (<see cref="SkillSelection.IsOptional"/>) and not seen; the startup check tells the
    /// contributor about it once and the sync window tags it. It becomes seen when they sync, choose for it in the
    /// window, or dismiss the notice.
    /// </summary>
    /// <remarks>
    /// Prefs without the list have no new skills: they are seeded with every locked skill (<see cref="Seed"/> at editor
    /// start, <see cref="RecordSynced"/> on a sync), so upgrading or syncing for the first time tells nobody about
    /// skills that were already there. A name stays seen after it leaves the lock.
    /// </remarks>
    public static class SeenSkills
    {
        /// <summary>The sorted names among <paramref name="optionalSkills"/> the contributor has not seen; none while unseeded.</summary>
        public static IReadOnlyList<string> New(LocalPrefs prefs, IEnumerable<string> optionalSkills)
        {
            var seen = prefs.SeenSkills;
            if (seen == null) return new string[0];
            return optionalSkills.Except(seen).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        /// <summary>The sorted new skills of <paramref name="lockfile"/> for this machine's <paramref name="skills"/>.</summary>
        public static IReadOnlyList<string> New(LocalPrefs prefs, Lockfile lockfile, SkillSelection skills) =>
            New(prefs, skills.OptionalIn(lockfile));

        /// <summary>
        /// Seeds prefs that have no seen skills yet with <paramref name="lockedSkills"/>; seeded prefs, and a null list
        /// (an unusable lockfile), are left alone. Call <see cref="LocalPrefs.Save"/> after when it returns true.
        /// </summary>
        /// <returns>Whether the prefs changed.</returns>
        public static bool Seed(LocalPrefs prefs, IEnumerable<string> lockedSkills)
        {
            if (lockedSkills == null || prefs.SeenSkills != null) return false;
            prefs.SeenSkills = lockedSkills.ToList();
            return true;
        }

        /// <summary>
        /// Marks <paramref name="skillNames"/> seen. Does nothing while the prefs are unseeded: seeding them with only
        /// these names would make every other locked skill new. Call <see cref="LocalPrefs.Save"/> after.
        /// </summary>
        public static void Mark(LocalPrefs prefs, IEnumerable<string> skillNames)
        {
            var seen = prefs.SeenSkills;
            if (seen != null) prefs.SeenSkills = seen.Concat(skillNames).ToList();
        }

        /// <summary>
        /// Marks every skill of the lock just synced seen, seeding prefs that had none. A null list (an unusable
        /// lockfile) changes nothing. Call <see cref="LocalPrefs.Save"/> after.
        /// </summary>
        public static void RecordSynced(LocalPrefs prefs, IEnumerable<string> lockedSkills)
        {
            if (lockedSkills == null) return;
            prefs.SeenSkills = (prefs.SeenSkills ?? new string[0]).Concat(lockedSkills).ToList();
        }
    }
}
