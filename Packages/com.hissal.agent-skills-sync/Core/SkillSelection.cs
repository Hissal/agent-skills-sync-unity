using System;
using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Which locked skills this machine installs: the contributor's explicit choice for a skill, else the skill's default.
    /// Only the choices are stored (<see cref="LocalPrefs.OptedOutSkills"/> and <see cref="LocalPrefs.OptedInSkills"/>, on
    /// this machine only), so every other skill follows its default as it is on each sync. A skill that is not selected is
    /// left out of every skills folder: never fetched, never asked consent for, and never out of sync.
    /// </summary>
    public sealed class SkillSelection
    {
        readonly HashSet<string> _optedOut;
        readonly HashSet<string> _optedIn;
        readonly Func<string, bool> _selectedByDefault;

        /// <param name="optedOut">Names the contributor opted out of; null = none. Wins over <paramref name="optedIn"/>.</param>
        /// <param name="optedIn">Names the contributor opted in to; null = none.</param>
        /// <param name="selectedByDefault">Whether a skill without a choice is installed; null = every skill is.</param>
        public SkillSelection(IEnumerable<string> optedOut = null, IEnumerable<string> optedIn = null,
            Func<string, bool> selectedByDefault = null)
        {
            _optedOut = new HashSet<string>(optedOut ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            _optedIn = new HashSet<string>(optedIn ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            _selectedByDefault = selectedByDefault ?? (_ => true);
        }

        /// <summary>Every skill selected: no choices, and every default is to install.</summary>
        public static SkillSelection All { get; } = new SkillSelection();

        /// <summary>The choices stored in <paramref name="prefs"/>, over the given defaults (null = every skill is installed by default).</summary>
        public static SkillSelection From(LocalPrefs prefs, Func<string, bool> selectedByDefault = null) =>
            new SkillSelection(prefs.OptedOutSkills, prefs.OptedInSkills, selectedByDefault);

        /// <summary>Whether this machine installs <paramref name="skillName"/>.</summary>
        public bool IsSelected(string skillName) =>
            !_optedOut.Contains(skillName) && (_optedIn.Contains(skillName) || _selectedByDefault(skillName));

        /// <summary>
        /// Whether the contributor opted out of <paramref name="skillName"/>. A skill that is not selected and not opted
        /// out is left out by its default alone.
        /// </summary>
        public bool IsOptedOut(string skillName) => _optedOut.Contains(skillName);

        /// <summary>The skills of <paramref name="lockfile"/> this machine installs, in lockfile order.</summary>
        public IEnumerable<LockedSkill> SelectedIn(Lockfile lockfile) => lockfile.Skills.Where(s => IsSelected(s.Name));

        /// <summary>
        /// Stores whether this machine installs <paramref name="skillName"/>, leaving every other choice as it is. A choice
        /// equal to <paramref name="selectedByDefault"/> is not stored, so the skill follows its default again.
        /// Applies on the next sync. Call <see cref="LocalPrefs.Save"/> after.
        /// </summary>
        public static void Set(LocalPrefs prefs, string skillName, bool selected, bool selectedByDefault = true)
        {
            var optedOut = new HashSet<string>(prefs.OptedOutSkills, StringComparer.Ordinal);
            var optedIn = new HashSet<string>(prefs.OptedInSkills, StringComparer.Ordinal);
            optedOut.Remove(skillName);
            optedIn.Remove(skillName);
            if (selected != selectedByDefault) (selected ? optedIn : optedOut).Add(skillName);
            prefs.OptedOutSkills = optedOut.ToList();
            prefs.OptedInSkills = optedIn.ToList();
        }
    }
}
