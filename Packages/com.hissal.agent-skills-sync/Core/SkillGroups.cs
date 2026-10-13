using System;
using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>One skill group of the project settings: a freely chosen name, the lock names it lists and whether they are installed by default.</summary>
    public sealed class SkillGroup
    {
        /// <param name="skills">Lock names, in file order; null = none. A repeated name counts once.</param>
        public SkillGroup(string name, bool selectedByDefault = true, IEnumerable<string> skills = null)
        {
            Name = name;
            SelectedByDefault = selectedByDefault;
            Skills = (skills ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal).ToList();
        }

        public string Name { get; }

        /// <summary>Whether a machine without a choice for one of the group's skills installs it.</summary>
        public bool SelectedByDefault { get; }

        /// <summary>The lock names the group lists, in file order. A name the lock does not have is kept (see <see cref="SkillGroupSection.Unknown"/>).</summary>
        public IReadOnlyList<string> Skills { get; }
    }

    /// <summary>
    /// The project's skill groups (<see cref="ProjectSyncSettings.Groups"/>): which locked skills a machine installs
    /// when the contributor made no choice for them. A skill is selected by default when any group listing it selects
    /// by default; a skill no group lists follows <see cref="UngroupedSelectedByDefault"/>. Groups only set defaults and
    /// how the window lists skills: a skill in several groups is still one skill, installed once.
    /// </summary>
    public sealed class SkillGroups
    {
        /// <param name="groups">The groups, in file order; null = none.</param>
        /// <param name="ungroupedSelectedByDefault">Whether a skill no group lists is installed by default.</param>
        public SkillGroups(IEnumerable<SkillGroup> groups = null, bool ungroupedSelectedByDefault = true)
        {
            Groups = (groups ?? Enumerable.Empty<SkillGroup>()).ToList();
            UngroupedSelectedByDefault = ungroupedSelectedByDefault;
        }

        /// <summary>No groups: every skill is ungrouped and selected by default.</summary>
        public static SkillGroups None { get; } = new SkillGroups();

        /// <summary>The groups, in file order.</summary>
        public IReadOnlyList<SkillGroup> Groups { get; }

        public bool UngroupedSelectedByDefault { get; }
    }
}
