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

        /// <summary>
        /// Whether a machine without a choice for <paramref name="skillName"/> installs it: any group listing it selects
        /// by default, or no group lists it and <see cref="UngroupedSelectedByDefault"/> is set.
        /// </summary>
        public bool IsSelectedByDefault(string skillName)
        {
            var listing = Groups.Where(g => g.Skills.Contains(skillName)).ToList();
            return listing.Count == 0 ? UngroupedSelectedByDefault : listing.Any(g => g.SelectedByDefault);
        }

        /// <summary>
        /// How the window lists <paramref name="lockfile"/>: one section per group in file order, each with the locked
        /// skills it lists (a skill in several groups is in each), then the skills no group lists as a section without a
        /// name. That last section is left out when it would be empty, unless there are no groups at all.
        /// </summary>
        public IReadOnlyList<SkillGroupSection> Sections(Lockfile lockfile)
        {
            var sections = new List<SkillGroupSection>();
            var grouped = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in Groups)
            {
                grouped.UnionWith(group.Skills);
                var skills = group.Skills.Select(n => lockfile.Skills.FirstOrDefault(s => s.Name == n)).Where(s => s != null);
                var unsupported = group.Skills.Select(n => lockfile.Unsupported.FirstOrDefault(s => s.Name == n)).Where(s => s != null);
                var unknown = group.Skills.Where(n => lockfile.Skills.All(s => s.Name != n) && lockfile.Unsupported.All(s => s.Name != n));
                sections.Add(new SkillGroupSection(group.Name, group.SelectedByDefault, skills, unsupported, unknown));
            }

            var ungrouped = lockfile.Skills.Where(s => !grouped.Contains(s.Name)).ToList();
            if (ungrouped.Count > 0 || Groups.Count == 0)
                sections.Add(new SkillGroupSection(null, UngroupedSelectedByDefault, ungrouped));
            return sections;
        }
    }

    /// <summary>One section of the window's skill list: a skill group's entries, or the skills no group lists.</summary>
    public sealed class SkillGroupSection
    {
        public SkillGroupSection(string name, bool selectedByDefault, IEnumerable<LockedSkill> skills,
            IEnumerable<UnsupportedSkill> unsupported = null, IEnumerable<string> unknown = null)
        {
            Name = name;
            SelectedByDefault = selectedByDefault;
            Skills = skills.ToList();
            Unsupported = (unsupported ?? Enumerable.Empty<UnsupportedSkill>()).ToList();
            Unknown = (unknown ?? Enumerable.Empty<string>()).ToList();
        }

        /// <summary>The group's name; null for the skills no group lists.</summary>
        public string Name { get; }

        /// <summary>Whether a machine without a choice installs the section's skills, unless another group of a skill says otherwise.</summary>
        public bool SelectedByDefault { get; }

        /// <summary>The locked skills this tool installs, in the group's order (lock order for the ungrouped section).</summary>
        public IReadOnlyList<LockedSkill> Skills { get; }

        /// <summary>Listed lock entries this tool does not install (see <see cref="UnsupportedSkill"/>).</summary>
        public IReadOnlyList<UnsupportedSkill> Unsupported { get; }

        /// <summary>Listed names the lock does not have: a typo or a stale entry. A sync ignores them.</summary>
        public IReadOnlyList<string> Unknown { get; }
    }
}
