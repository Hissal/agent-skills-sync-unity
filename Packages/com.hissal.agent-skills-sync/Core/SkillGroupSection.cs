using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
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
