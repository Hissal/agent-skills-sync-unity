using System;
using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Explicit per-skill, per-folder overrides of the default to use a found user-scope copy.
    /// Stored in <see cref="LocalPrefs.InstallAnywaySkills"/> on this machine only.
    /// </summary>
    public sealed class InstallAnywayChoices
    {
        readonly Dictionary<string, HashSet<string>> _byFolder;

        /// <param name="byFolder">Skill names per <see cref="SkillsFolder.RelativePath"/>, the shape of <see cref="LocalPrefs.InstallAnywaySkills"/>.</param>
        public InstallAnywayChoices(IReadOnlyDictionary<string, IReadOnlyList<string>> byFolder)
        {
            _byFolder = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            if (byFolder == null) return;
            foreach (var pair in byFolder)
                _byFolder[pair.Key] = new HashSet<string>(pair.Value ?? Array.Empty<string>(), StringComparer.Ordinal);
        }

        /// <summary>No install-anyway overrides.</summary>
        public static InstallAnywayChoices None { get; } = new InstallAnywayChoices(null);

        /// <summary>The choices stored in <paramref name="prefs"/>.</summary>
        public static InstallAnywayChoices From(LocalPrefs prefs) => new InstallAnywayChoices(prefs.InstallAnywaySkills);

        /// <summary>Whether the contributor chose to install the project copy of <paramref name="skillName"/> in <paramref name="folder"/>.</summary>
        public bool IsInstalledAnyway(SkillsFolder folder, string skillName) =>
            _byFolder.TryGetValue(folder.RelativePath, out var names) && names.Contains(skillName);

        /// <summary>
        /// Stores whether to install anyway <paramref name="skillName"/> in <paramref name="folder"/>, leaving every other choice as
        /// it is. Applies on the next sync. Call <see cref="LocalPrefs.Save"/> after.
        /// </summary>
        public static void Set(LocalPrefs prefs, SkillsFolder folder, string skillName, bool installAnyway)
        {
            var all = prefs.InstallAnywaySkills.ToDictionary(p => p.Key, p => new SortedSet<string>(p.Value, StringComparer.Ordinal), StringComparer.Ordinal);
            if (!all.TryGetValue(folder.RelativePath, out var names)) all[folder.RelativePath] = names = new SortedSet<string>(StringComparer.Ordinal);
            if (installAnyway) names.Add(skillName);
            else names.Remove(skillName);
            prefs.InstallAnywaySkills = all.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value.ToList(), StringComparer.Ordinal);
        }
    }
}
