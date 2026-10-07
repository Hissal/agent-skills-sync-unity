using System;
using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Which locked skills the contributor skips per skills folder because they already have a user-scope copy the
    /// folder's agents read. Per folder: skipping a skill for <c>.claude/skills</c> does not skip it for
    /// <c>.agents/skills</c>, and the reverse. Stored in <see cref="LocalPrefs.SkippedSkills"/> (never committed).
    /// </summary>
    /// <remarks>
    /// A stored skip only takes effect while a user-scope copy is actually found (see <see cref="InstallPlanner"/>):
    /// drop the user-scope copy and the project copy comes back on the next sync, while the choice stays stored.
    /// </remarks>
    public sealed class SkipChoices
    {
        readonly Dictionary<string, HashSet<string>> _byFolder;

        /// <param name="byFolder">Skill names per <see cref="SkillsFolder.RelativePath"/>, the shape of <see cref="LocalPrefs.SkippedSkills"/>.</param>
        public SkipChoices(IReadOnlyDictionary<string, IReadOnlyList<string>> byFolder)
        {
            _byFolder = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            if (byFolder == null) return;
            foreach (var pair in byFolder)
                _byFolder[pair.Key] = new HashSet<string>(pair.Value ?? Array.Empty<string>(), StringComparer.Ordinal);
        }

        /// <summary>Nothing skipped.</summary>
        public static SkipChoices None { get; } = new SkipChoices(null);

        /// <summary>The choices stored in <paramref name="prefs"/>.</summary>
        public static SkipChoices From(LocalPrefs prefs) => new SkipChoices(prefs.SkippedSkills);

        /// <summary>Whether the contributor chose to skip the project copy of <paramref name="skillName"/> in <paramref name="folder"/>.</summary>
        public bool IsSkipped(SkillsFolder folder, string skillName) =>
            _byFolder.TryGetValue(folder.RelativePath, out var names) && names.Contains(skillName);

        /// <summary>
        /// Stores whether to skip <paramref name="skillName"/> in <paramref name="folder"/>, leaving every other choice as
        /// it is. Applies on the next sync. Call <see cref="LocalPrefs.Save"/> after.
        /// </summary>
        public static void Set(LocalPrefs prefs, SkillsFolder folder, string skillName, bool skip)
        {
            var all = prefs.SkippedSkills.ToDictionary(p => p.Key, p => new SortedSet<string>(p.Value, StringComparer.Ordinal), StringComparer.Ordinal);
            if (!all.TryGetValue(folder.RelativePath, out var names)) all[folder.RelativePath] = names = new SortedSet<string>(StringComparer.Ordinal);
            if (skip) names.Add(skillName);
            else names.Remove(skillName);
            prefs.SkippedSkills = all.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value.ToList(), StringComparer.Ordinal);
        }
    }
}
