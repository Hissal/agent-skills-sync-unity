using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// A repository's shared choices on this machine: the folder selection, opt-outs, opt-ins and install-anyway choices
    /// every checkout of the repository follows unless it keeps its own (<see cref="LocalPrefs.ChoiceScope"/>), and the
    /// sources synced in any of its checkouts, which no checkout asks consent for again. Kept in
    /// <see cref="SharedState"/>, keyed by <see cref="RepositoryIdentity"/>, so unrelated repositories never share them.
    /// Each checkout's own prefs keep everything else: ownership, sync fingerprints, synced defaults and notices.
    /// A checkout in no Git repository has no shared choices.
    /// </summary>
    public static class SharedChoices
    {
        /// <summary>
        /// The checkout's prefs as a sync reads them: the shared choices in place of its own while it follows them, and
        /// the sources synced anywhere in the repository added to its own. Changes nothing on disk.
        /// </summary>
        public static LocalPrefs Load(string unityRoot, UserEnvironment environment)
        {
            var prefs = LocalPrefs.Load(unityRoot);
            var state = StateOf(unityRoot, environment);
            if (state == null) return prefs;

            var shared = state.LoadChoices();
            if (Follows(prefs, shared, unityRoot) && shared.HasChoices) prefs.CopyChoicesFrom(shared);
            prefs.SyncedSources = Union(prefs.SyncedSources, shared.SyncedSources);
            return prefs;
        }

        /// <summary>
        /// Saves the checkout's prefs and, while it follows the shared choices, makes its choices the shared ones, so
        /// a choice made in one checkout reaches every checkout following them at its next restore or sync. Call it
        /// right after the choice is made: the choices saved are the ones <paramref name="prefs"/> holds.
        /// </summary>
        public static void Save(LocalPrefs prefs, string unityRoot, UserEnvironment environment)
        {
            prefs.Save();
            var state = StateOf(unityRoot, environment);
            if (state == null || !Follows(prefs, state.LoadChoices(), unityRoot)) return;
            using (state.Lock())
            {
                var shared = state.LoadChoices();
                if (shared.HasChoices && shared.SameChoicesAs(prefs)) return;
                shared.CopyChoicesFrom(prefs);
                shared.Save();
            }
        }

        /// <summary>
        /// Sets whether the checkout follows the shared choices (<see cref="ChoiceScope.Shared"/>) or keeps its own
        /// (<see cref="ChoiceScope.Worktree"/>). Following them again takes them at the next restore or sync; while the
        /// repository has none yet, the checkout's own become them.
        /// </summary>
        public static void SetScope(string unityRoot, ChoiceScope scope, UserEnvironment environment = null)
        {
            var prefs = LocalPrefs.Load(unityRoot);
            prefs.ChoiceScope = scope;
            prefs.Save();
            var state = StateOf(unityRoot, environment ?? UserEnvironment.Current);
            if (scope != ChoiceScope.Shared || state == null || !prefs.HasChoices) return;
            using (state.Lock())
            {
                var shared = state.LoadChoices();
                if (shared.HasChoices) return;
                shared.CopyChoicesFrom(prefs);
                shared.Save();
            }
        }

        /// <summary>
        /// Makes <paramref name="choices"/>, the choices of the project at <paramref name="baselineProject"/>, the shared
        /// ones unless another checkout shared some first, and remembers that project as the baseline (see
        /// <see cref="LocalPrefs.BaselineProject"/>).
        /// </summary>
        internal static void Adopt(SharedState state, string baselineProject, LocalPrefs choices)
        {
            using (state.Lock())
            {
                var shared = state.LoadChoices();
                if (shared.BaselineProject != null) return;
                if (!shared.HasChoices) shared.CopyChoicesFrom(choices);
                shared.BaselineProject = Path.GetFullPath(baselineProject);
                shared.Save();
            }
        }

        /// <summary>
        /// Whether a checkout with <paramref name="prefs"/> follows the shared choices: it was told to, or it never
        /// chose and its choices are the baseline the shared ones were made from.
        /// </summary>
        internal static bool Follows(LocalPrefs prefs, LocalPrefs shared, string unityRoot) =>
            prefs.ChoiceScope == ChoiceScope.Shared ||
            (prefs.ChoiceScope == null && shared.BaselineProject != null &&
             Paths.Comparer.Equals(Trim(shared.BaselineProject), Trim(Path.GetFullPath(unityRoot))));

        /// <summary>
        /// After a sync or import in the project at <paramref name="unityRoot"/>: adds <paramref name="sources"/> to the
        /// repository's synced sources, and the project to its known projects (<see cref="LocalPrefs.KnownProjects"/>).
        /// No-op outside a Git repository.
        /// </summary>
        internal static void RecordSync(string unityRoot, UserEnvironment environment, IEnumerable<string> sources)
        {
            var state = StateOf(unityRoot, environment);
            if (state == null) return;
            var list = sources.ToList();
            var project = Trim(Path.GetFullPath(unityRoot));
            using (state.Lock())
            {
                var shared = state.LoadChoices();
                var union = Union(shared.SyncedSources, list);
                var known = shared.KnownProjects.Any(p => Paths.Comparer.Equals(Trim(p), project));
                if (union.SequenceEqual(shared.SyncedSources) && known) return;
                shared.SyncedSources = union;
                if (!known) shared.KnownProjects = shared.KnownProjects.Concat(new[] { project }).ToList();
                shared.Save();
            }
        }

        internal static SharedState StateOf(string unityRoot, UserEnvironment environment)
        {
            var identity = RepositoryIdentity.Find(unityRoot);
            return identity == null ? null : new SharedState(identity, environment);
        }

        internal static IReadOnlyList<string> Union(IEnumerable<string> a, IEnumerable<string> b) =>
            a.Concat(b).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.Ordinal).ToList();

        static string Trim(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
