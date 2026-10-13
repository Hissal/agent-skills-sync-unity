using System;
using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Which locked skills this machine installs: the contributor's explicit choice for a skill, else the default the
    /// project's <see cref="SkillGroups"/> give it.
    /// Only the choices are stored (<see cref="LocalPrefs.OptedOutSkills"/> and <see cref="LocalPrefs.OptedInSkills"/>, on
    /// this machine only), so every other skill follows its default as it is on each sync. A skill that is not selected is
    /// left out of every skills folder: never fetched, never asked consent for, and never out of sync.
    /// </summary>
    public sealed class SkillSelection
    {
        readonly HashSet<string> _optedOut;
        readonly HashSet<string> _optedIn;
        readonly SkillGroups _groups;
        readonly HashSet<string> _syncedByDefault;

        /// <param name="optedOut">Names the contributor opted out of; null = none. Wins over <paramref name="optedIn"/>.</param>
        /// <param name="optedIn">Names the contributor opted in to; null = none.</param>
        /// <param name="groups">The project's skill groups, which give a skill without a choice its default; null = every skill is installed.</param>
        /// <param name="syncedByDefault">
        /// Names the last sync selected by their default alone (<see cref="LocalPrefs.LastSyncedDefaultSkills"/>); null =
        /// not recorded.
        /// </param>
        public SkillSelection(IEnumerable<string> optedOut = null, IEnumerable<string> optedIn = null,
            SkillGroups groups = null, IEnumerable<string> syncedByDefault = null)
        {
            _optedOut = new HashSet<string>(optedOut ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            _optedIn = new HashSet<string>(optedIn ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            _groups = groups ?? SkillGroups.None;
            _syncedByDefault = syncedByDefault == null ? null : new HashSet<string>(syncedByDefault, StringComparer.Ordinal);
        }

        /// <summary>Every skill selected: no choices, and every default is to install.</summary>
        public static SkillSelection All { get; } = new SkillSelection();

        /// <summary>
        /// The choices stored in <paramref name="prefs"/>, over the defaults of <paramref name="groups"/> (null = every
        /// skill is installed by default).
        /// </summary>
        public static SkillSelection From(LocalPrefs prefs, SkillGroups groups = null) =>
            new SkillSelection(prefs.OptedOutSkills, prefs.OptedInSkills, groups, prefs.LastSyncedDefaultSkills);

        /// <summary>Whether this machine installs <paramref name="skillName"/>.</summary>
        public bool IsSelected(string skillName) =>
            !_optedOut.Contains(skillName) && (_optedIn.Contains(skillName) || _groups.IsSelectedByDefault(skillName));

        /// <summary>
        /// Whether the contributor opted out of <paramref name="skillName"/>. A skill that is not selected and not opted
        /// out is optional (<see cref="IsOptional"/>).
        /// </summary>
        public bool IsOptedOut(string skillName) => _optedOut.Contains(skillName);

        /// <summary>
        /// Whether <paramref name="skillName"/> is an optional skill: this machine leaves it out by its default alone.
        /// A skill the contributor opted out of is not optional, since the opt-out is their answer to it.
        /// </summary>
        public bool IsOptional(string skillName) => !IsSelected(skillName) && !IsOptedOut(skillName);

        /// <summary>
        /// Whether <paramref name="skillName"/> is left out because its default changed, not by the contributor's doing:
        /// it is optional now, and the last sync selected it by its default alone. A skill the contributor had opted in
        /// to and then let follow its default again is therefore not one. While no sync has recorded those names yet
        /// (prefs an earlier version wrote), every optional skill counts.
        /// </summary>
        public bool IsNoLongerSelectedByDefault(string skillName) =>
            IsOptional(skillName) && (_syncedByDefault == null || _syncedByDefault.Contains(skillName));

        /// <summary>The skills of <paramref name="lockfile"/> this machine installs, in lockfile order.</summary>
        public IEnumerable<LockedSkill> SelectedIn(Lockfile lockfile) => lockfile.Skills.Where(s => IsSelected(s.Name));

        /// <summary>The names of the optional skills of <paramref name="lockfile"/> (<see cref="IsOptional"/>), in lockfile order.</summary>
        public IEnumerable<string> OptionalIn(Lockfile lockfile) => lockfile.Skills.Select(s => s.Name).Where(IsOptional);

        /// <summary>
        /// Records which skills of the <paramref name="lockfile"/> just synced this selection installs by their default
        /// alone (<see cref="LocalPrefs.LastSyncedDefaultSkills"/>), for the next sync's
        /// <see cref="IsNoLongerSelectedByDefault"/>. Call <see cref="LocalPrefs.Save"/> after.
        /// </summary>
        public void RecordSynced(LocalPrefs prefs, Lockfile lockfile) =>
            prefs.LastSyncedDefaultSkills = lockfile.Skills.Select(s => s.Name)
                .Where(name => IsSelected(name) && !_optedIn.Contains(name)).ToList();

        /// <summary>
        /// Stores whether this machine installs each of <paramref name="skillNames"/> (one skill, or a whole skill
        /// group), leaving every other choice as it is. A choice equal to the skill's default in
        /// <paramref name="groups"/> (null = installed) is not stored, so the skill follows its default again.
        /// Applies on the next sync. Call <see cref="LocalPrefs.Save"/> after.
        /// </summary>
        public static void Set(LocalPrefs prefs, IEnumerable<string> skillNames, bool selected, SkillGroups groups = null)
        {
            groups = groups ?? SkillGroups.None;
            var optedOut = new HashSet<string>(prefs.OptedOutSkills, StringComparer.Ordinal);
            var optedIn = new HashSet<string>(prefs.OptedInSkills, StringComparer.Ordinal);
            foreach (var skillName in skillNames)
            {
                optedOut.Remove(skillName);
                optedIn.Remove(skillName);
                if (selected != groups.IsSelectedByDefault(skillName)) (selected ? optedIn : optedOut).Add(skillName);
            }
            prefs.OptedOutSkills = optedOut.ToList();
            prefs.OptedInSkills = optedIn.ToList();
        }
    }
}
