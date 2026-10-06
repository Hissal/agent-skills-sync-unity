using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Which skills folders of the folder table this machine installs into: a multi-select (possibly empty) kept in
    /// <see cref="LocalPrefs"/>. Until the contributor chooses, the selection is the shallow <see cref="Autofill"/>.
    /// Later, a folder whose user-scope home appears is offered once (<see cref="Offers"/>); a declined offer is
    /// remembered per folder, and a folder whose home disappears is never deselected automatically.
    /// </summary>
    public static class FolderSelection
    {
        /// <summary>
        /// Shallow autofill: the folders, in table order, for which any user-scope location's home exists on this
        /// machine (e.g. <c>~/.codex</c> pre-selects <c>.agents/skills</c>). No per-agent install detection.
        /// </summary>
        public static IReadOnlyList<SkillsFolder> Autofill(FolderLayout table, UserEnvironment environment) =>
            table.Folders.Where(folder => HasHome(folder, environment)).ToList();

        /// <summary>Whether any of the folder's user-scope homes exists on this machine.</summary>
        public static bool HasHome(SkillsFolder folder, UserEnvironment environment) =>
            folder.UserScopeHomes(environment).Any(Directory.Exists);

        /// <summary>Whether the contributor has chosen a selection (possibly none) on this machine.</summary>
        public static bool IsChosen(LocalPrefs prefs) => prefs.SelectedFolders != null;

        /// <summary>
        /// The folders to install into, in table order: the chosen selection, or the <see cref="Autofill"/> while
        /// nothing was chosen. Stored folders the table no longer has are ignored.
        /// </summary>
        public static IReadOnlyList<SkillsFolder> Effective(LocalPrefs prefs, FolderLayout table, UserEnvironment environment)
        {
            var stored = prefs.SelectedFolders;
            if (stored == null) return Autofill(table, environment);
            var chosen = new HashSet<string>(stored, StringComparer.Ordinal);
            return table.Folders.Where(f => chosen.Contains(f.RelativePath)).ToList();
        }

        /// <summary>
        /// Folders to offer adding: home present, not in the <see cref="Effective"/> selection, not declined. Empty while
        /// nothing was chosen, because the autofill already includes every folder with a home.
        /// </summary>
        public static IReadOnlyList<SkillsFolder> Offers(LocalPrefs prefs, FolderLayout table, UserEnvironment environment)
        {
            var selected = new HashSet<string>(Effective(prefs, table, environment).Select(f => f.RelativePath), StringComparer.Ordinal);
            var declined = new HashSet<string>(prefs.DeclinedFolders, StringComparer.Ordinal);
            return table.Folders
                .Where(f => !selected.Contains(f.RelativePath) && !declined.Contains(f.RelativePath) && HasHome(f, environment))
                .ToList();
        }

        /// <summary>
        /// Stores <paramref name="selected"/> as the chosen selection. A folder left out whose home exists counts as
        /// declined (the contributor saw it and chose not to), so it is not offered later; a selected folder's decline
        /// is cleared. Call <see cref="LocalPrefs.Save"/> after.
        /// </summary>
        public static void Save(LocalPrefs prefs, FolderLayout table, IEnumerable<SkillsFolder> selected, UserEnvironment environment)
        {
            var chosen = new HashSet<string>(selected.Where(f => f != null).Select(f => f.RelativePath), StringComparer.Ordinal);
            prefs.SelectedFolders = table.Folders.Where(f => chosen.Contains(f.RelativePath)).Select(f => f.RelativePath).ToList();

            var declined = new SortedSet<string>(prefs.DeclinedFolders, StringComparer.Ordinal);
            foreach (var folder in table.Folders)
            {
                if (chosen.Contains(folder.RelativePath)) declined.Remove(folder.RelativePath);
                else if (HasHome(folder, environment)) declined.Add(folder.RelativePath);
            }
            prefs.DeclinedFolders = declined.ToList();
        }

        /// <summary>Adds an offered folder to the <see cref="Effective"/> selection and stores it. Call <see cref="LocalPrefs.Save"/> after.</summary>
        public static void Accept(LocalPrefs prefs, FolderLayout table, SkillsFolder folder, UserEnvironment environment) =>
            Save(prefs, table, Effective(prefs, table, environment).Concat(new[] { folder }), environment);

        /// <summary>Remembers that the contributor declined adding <paramref name="folder"/>. Call <see cref="LocalPrefs.Save"/> after.</summary>
        public static void Decline(LocalPrefs prefs, SkillsFolder folder) =>
            prefs.DeclinedFolders = prefs.DeclinedFolders.Concat(new[] { folder.RelativePath })
                .Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList();
    }
}
