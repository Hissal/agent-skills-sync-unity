using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Restores a checkout's selected skills before an agent starts in it, without Unity and without the network: the
    /// checkout's own lock and skill groups, the repository's shared choices (<see cref="SharedChoices"/>) unless the
    /// checkout keeps its own, a fresh user-scope scan, and the snapshots its lock entries last resolved to
    /// (<see cref="SnapshotFetcher"/>). It runs the same sync as every other entry point (<see cref="SkillsSyncService"/>),
    /// so the checkout ends up owning its installation and its bookkeeping, and Unity's startup check finds it in sync.
    /// </summary>
    /// <remarks>
    /// <see cref="Plan"/> is offline and writes nothing. <see cref="Run"/> checks everything a run needs before its first
    /// write: an unapproved source, a missing snapshot or an undecided baseline returns its status and changes nothing.
    /// A finished restore repeated changes nothing.
    /// </remarks>
    public sealed class WorktreeRestore
    {
        readonly string _unityRoot;
        readonly UserEnvironment _environment;
        readonly string _baseline;
        readonly SharedState _state;

        /// <param name="unityRoot">The Unity project to restore, in any checkout of its repository.</param>
        /// <param name="environment">This machine's home, variables and state directory; null = <see cref="UserEnvironment.Current"/>.</param>
        /// <param name="baseline">
        /// The checkout (or the Unity project in it) whose choices become the shared choices when the repository has
        /// none yet and its checkouts' choices disagree; null = none named.
        /// </param>
        /// <exception cref="RepositoryException">The project is in no Git checkout.</exception>
        public WorktreeRestore(string unityRoot, UserEnvironment environment = null, string baseline = null)
        {
            _unityRoot = Path.GetFullPath(unityRoot);
            _environment = environment ?? UserEnvironment.Current;
            _baseline = baseline;
            _state = SharedChoices.StateOf(_unityRoot, _environment) ?? throw RepositoryException.NotInCheckout(_unityRoot);
        }

        public RepositoryIdentity Identity => _state.Identity;

        /// <summary>What <see cref="Run"/> would do now, without the network and without writing anything.</summary>
        /// <exception cref="LockfileException">The lockfile is missing or unusable.</exception>
        /// <exception cref="ProjectSyncSettingsException">The project settings are unusable.</exception>
        /// <exception cref="RepositoryException">The baseline is not this project in a checkout of the repository.</exception>
        public RestoreReport Plan() => Evaluate(null, dryRun: true);

        /// <summary>
        /// Restores the checkout once every source its selected skills come from was synced somewhere in the repository
        /// or is in <paramref name="confirmedSources"/>, every skill to install has a recorded snapshot and the shared
        /// choices are decided. Otherwise returns the status that says which, having changed nothing.
        /// </summary>
        /// <exception cref="LockfileException">The lockfile is missing or unusable.</exception>
        /// <exception cref="ProjectSyncSettingsException">The project settings are unusable.</exception>
        /// <exception cref="RepositoryException">The baseline is not this project in a checkout of the repository.</exception>
        /// <exception cref="SyncAbortedException">A snapshot could not be installed; nothing was changed.</exception>
        public RestoreReport Run(IEnumerable<string> confirmedSources = null) => Evaluate(confirmedSources, dryRun: false);

        RestoreReport Evaluate(IEnumerable<string> confirmedSources, bool dryRun)
        {
            var confirmed = (confirmedSources ?? Enumerable.Empty<string>()).ToList();
            var report = new RestoreReport { DryRun = dryRun, Project = _unityRoot, Identity = Identity };

            var local = LocalPrefs.Load(_unityRoot);
            var shared = _state.LoadChoices();
            var siblings = SiblingPrefs();
            var decision = DecideChoices(local, shared, siblings);
            report.Scope = decision.Scope;
            report.BaselineFrom = decision.AdoptFrom;
            report.BaselineCandidates = decision.Candidates;
            if (decision.BaselineRequired)
                return report.With(RestoreStatus.BaselineRequired);

            // The checkout's prefs as the restore leaves them: its bookkeeping, the decided choices, and every source
            // synced anywhere in the repository.
            var effective = LocalPrefs.Load(_unityRoot);
            if (decision.Choices != null) effective.CopyChoicesFrom(decision.Choices);
            effective.ChoiceScope = decision.Scope;
            effective.SyncedSources = SharedChoices.Union(
                new[] { local, shared }.Concat(siblings.Select(s => s.Prefs)).SelectMany(p => p.SyncedSources),
                Enumerable.Empty<string>());

            var resolutions = _state.LoadResolutions();
            var service = new SkillsSyncService(_unityRoot, _environment,
                mode => new SnapshotFetcher(_state.Snapshots, resolutions, mode), prefs: effective, recordSnapshots: false);
            report.Mode = service.Mode;

            report.UnconfirmedSources = SourceConsent.Unconfirmed(service.Lock, effective, confirmed, service.Choices.Skills);
            if (report.UnconfirmedSources.Count > 0) return report.With(RestoreStatus.SourcesNeedConfirmation);

            var fetcher = new SnapshotFetcher(_state.Snapshots, resolutions, service.Mode);
            var missing = new List<MissingSnapshot>();
            foreach (var skill in service.ToFetch())
                if (fetcher.Locate(skill, out var reason) == null) missing.Add(new MissingSnapshot(skill.Name, reason));
            report.MissingSnapshots = missing;
            if (missing.Count > 0) return report.With(RestoreStatus.SnapshotsMissing);

            report.Plan = service.Plan();
            if (dryRun) return report.With(RestoreStatus.Ready);

            if (decision.AdoptFrom != null) AdoptBaseline(decision.Choices);
            effective.Save();
            report.Summary = service.Run(confirmed);
            return report.With(RestoreStatus.Restored);
        }

        /// <summary>Makes <paramref name="choices"/> the shared choices, unless another checkout shared some meanwhile.</summary>
        void AdoptBaseline(LocalPrefs choices)
        {
            using (_state.Lock())
            {
                var shared = _state.LoadChoices();
                if (shared.HasChoices) return;
                shared.CopyChoicesFrom(choices);
                shared.Save();
            }
        }

        /// <summary>The prefs of this project in every other checkout of the repository.</summary>
        IReadOnlyList<(string Project, LocalPrefs Prefs)> SiblingPrefs() =>
            Identity.Checkouts()
                .Select(Identity.ProjectIn)
                .Where(p => !Paths.Comparer.Equals(p, _unityRoot.TrimEnd(Path.DirectorySeparatorChar)))
                .Where(p => File.Exists(LocalPrefs.PathFor(p)))
                .Select(p => (p, LocalPrefs.Load(p)))
                .ToList();

        sealed class ChoiceDecision
        {
            public ChoiceScope Scope;
            public LocalPrefs Choices; // null = the checkout's own (or none at all: every default)
            public string AdoptFrom;
            public IReadOnlyList<string> Candidates = new string[0];
            public bool BaselineRequired;
        }

        /// <summary>
        /// Which choices the checkout installs by. A checkout keeping its own keeps them. Otherwise it follows the shared
        /// choices; while the repository has none, the checkouts' existing choices become them when they all agree or
        /// the baseline names one, and a checkout whose existing choices differ from them keeps its own.
        /// </summary>
        ChoiceDecision DecideChoices(LocalPrefs local, LocalPrefs shared, IReadOnlyList<(string Project, LocalPrefs Prefs)> siblings)
        {
            var decision = new ChoiceDecision();
            if (local.ChoiceScope == ChoiceScope.Worktree)
            {
                decision.Scope = ChoiceScope.Worktree;
                return decision;
            }

            var reference = shared.HasChoices ? shared : null;
            if (reference == null)
            {
                // Checkouts with choices, this one first, that do not keep them to themselves.
                var candidates = new[] { (Project: _unityRoot, Prefs: local) }.Concat(siblings)
                    .Where(c => c.Prefs.HasChoices && c.Prefs.ChoiceScope != ChoiceScope.Worktree)
                    .ToList();
                if (_baseline != null)
                {
                    var baseline = BaselineProject();
                    var chosen = candidates.FirstOrDefault(c => Paths.Comparer.Equals(c.Project, baseline));
                    reference = chosen.Prefs ?? throw new RepositoryException(
                        $"The baseline {_baseline} has no choices of its own to share; name a checkout whose project has synced.");
                    decision.AdoptFrom = chosen.Project;
                }
                else if (candidates.Count > 0)
                {
                    var distinct = candidates.Where((c, i) => candidates.Take(i).All(o => !o.Prefs.SameChoicesAs(c.Prefs))).ToList();
                    if (distinct.Count > 1)
                    {
                        decision.BaselineRequired = true;
                        decision.Candidates = candidates.Select(c => c.Project).ToList();
                        return decision;
                    }
                    reference = candidates[0].Prefs;
                    decision.AdoptFrom = candidates[0].Project;
                }
            }

            if (local.ChoiceScope == null && local.HasChoices && reference != null && !local.SameChoicesAs(reference))
            {
                decision.Scope = ChoiceScope.Worktree;
                return decision;
            }
            decision.Scope = ChoiceScope.Shared;
            decision.Choices = reference;
            return decision;
        }

        /// <summary>The Unity project the baseline names: itself, or the project in the checkout it names.</summary>
        string BaselineProject()
        {
            var full = Path.GetFullPath(_baseline).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var checkout in Identity.Checkouts())
            {
                var project = Identity.ProjectIn(checkout);
                if (Paths.Comparer.Equals(project, full) || Paths.Comparer.Equals(checkout.TrimEnd(Path.DirectorySeparatorChar), full))
                    return project;
            }
            throw new RepositoryException($"The baseline {_baseline} is not a checkout of this repository.");
        }
    }

    /// <summary>The outcome of a <see cref="WorktreeRestore"/>.</summary>
    public enum RestoreStatus
    {
        /// <summary>A dry run found nothing in the way: a run would restore the checkout.</summary>
        Ready,

        /// <summary>The checkout was restored (possibly with nothing to change).</summary>
        Restored,

        /// <summary>Selected skills come from sources never synced in this repository; confirm them explicitly.</summary>
        SourcesNeedConfirmation,

        /// <summary>A skill to install has no recorded snapshot; run an explicit sync (or import) first.</summary>
        SnapshotsMissing,

        /// <summary>The repository has no shared choices yet and its checkouts disagree; name a baseline checkout.</summary>
        BaselineRequired,
    }

    /// <summary>A skill a restore cannot install because no usable snapshot is recorded for it.</summary>
    public sealed class MissingSnapshot
    {
        public MissingSnapshot(string skill, string reason)
        {
            Skill = skill;
            Reason = reason;
        }

        public string Skill { get; }

        /// <summary>Why, and what to do about it.</summary>
        public string Reason { get; }
    }

    /// <summary>What a <see cref="WorktreeRestore"/> found or did. Lists are empty, never null.</summary>
    public sealed class RestoreReport
    {
        public RestoreStatus Status { get; private set; }

        /// <summary>Whether this is a <see cref="RestoreStatus.Ready"/>-or-not dry run that changed nothing.</summary>
        public bool DryRun { get; internal set; }

        public bool Succeeded => Status == RestoreStatus.Ready || Status == RestoreStatus.Restored;

        public string Project { get; internal set; }

        public RepositoryIdentity Identity { get; internal set; }

        /// <summary>The install mode the restore used; null when it stopped before reading the settings.</summary>
        public InstallMode? Mode { get; internal set; }

        /// <summary>Whether the checkout follows the shared choices or keeps its own, after this restore.</summary>
        public ChoiceScope Scope { get; internal set; }

        /// <summary>The checkout's project whose choices become (or would become) the shared ones; null when none is adopted.</summary>
        public string BaselineFrom { get; internal set; }

        /// <summary>For <see cref="RestoreStatus.BaselineRequired"/>, the projects whose choices could be the baseline.</summary>
        public IReadOnlyList<string> BaselineCandidates { get; internal set; } = new string[0];

        public IReadOnlyList<string> UnconfirmedSources { get; internal set; } = new string[0];

        public IReadOnlyList<MissingSnapshot> MissingSnapshots { get; internal set; } = new MissingSnapshot[0];

        /// <summary>The offline plan, once the restore got that far; null before.</summary>
        public InstallPlan Plan { get; internal set; }

        /// <summary>What the run did, for <see cref="RestoreStatus.Restored"/>; null otherwise.</summary>
        public SyncSummary Summary { get; internal set; }

        internal RestoreReport With(RestoreStatus status)
        {
            Status = status;
            return this;
        }
    }
}
