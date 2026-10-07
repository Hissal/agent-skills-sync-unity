using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// What this machine brings to a sync besides the lockfile and the project: the folder layout, the skills folders
    /// it installs into, the user-scope copies found for them, and the contributor's skip choices. Planning, syncing
    /// and the startup status all read the same choices, so build them once with <see cref="Read"/>.
    /// </summary>
    public sealed class MachineChoices
    {
        /// <param name="layout">The folder layout; null = <see cref="FolderLayout.Default"/>.</param>
        /// <param name="selected">The folders this machine installs into (see <see cref="FolderSelection.Effective"/>); null = every folder in the layout.</param>
        /// <param name="userScope">The user-scope copies found (see <see cref="UserScopeScanner"/>); null = none.</param>
        /// <param name="skips">The contributor's per-folder skip choices (see <see cref="SkipChoices.From"/>); null = none.</param>
        public MachineChoices(FolderLayout layout = null, IEnumerable<SkillsFolder> selected = null,
            UserScopeState userScope = null, SkipChoices skips = null)
        {
            Layout = layout ?? FolderLayout.Default;
            Selected = selected?.ToList();
            UserScope = userScope ?? UserScopeState.Empty;
            Skips = skips ?? SkipChoices.None;
        }

        /// <summary>Every folder of the default layout selected, nothing found at user scope, nothing skipped.</summary>
        public static MachineChoices Default { get; } = new MachineChoices();

        /// <summary>
        /// This machine's choices for the project: the <see cref="FolderSelection.Effective"/> selection and skips stored
        /// in <paramref name="prefs"/>, and the user-scope copies <paramref name="environment"/> holds for that selection
        /// (from <see cref="UserScopeScanner.DefaultSourcesFor"/> the project).
        /// </summary>
        public static MachineChoices Read(string projectRoot, LocalPrefs prefs, UserEnvironment environment, FolderLayout layout = null)
        {
            layout = layout ?? FolderLayout.Default;
            var selected = FolderSelection.Effective(prefs, layout, environment);
            var userScope = UserScopeScanner.Scan(selected, environment, UserScopeScanner.DefaultSourcesFor(projectRoot));
            return new MachineChoices(layout, selected, userScope, SkipChoices.From(prefs));
        }

        public FolderLayout Layout { get; }

        /// <summary>The folders this machine installs into, in the order given; null = every folder in <see cref="Layout"/>.</summary>
        public IReadOnlyList<SkillsFolder> Selected { get; }

        /// <summary>The user-scope copies found; never null.</summary>
        public UserScopeState UserScope { get; }

        /// <summary>The contributor's per-folder skip choices; never null.</summary>
        public SkipChoices Skips { get; }
    }
}
