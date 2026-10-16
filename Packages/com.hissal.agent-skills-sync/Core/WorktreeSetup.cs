using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>The offline setup of worktree restore for the open project. Reading it changes nothing.</summary>
    public sealed class WorktreeSetupStatus
    {
        public RepositoryIdentity Identity { get; private set; }
        public ChoiceScope? Scope { get; private set; }
        public bool FollowsShared { get; private set; }
        public bool HasSharedChoices { get; private set; }
        public IReadOnlyList<WorktreeChoiceDifference> Differences { get; private set; } = new WorktreeChoiceDifference[0];
        public IReadOnlyList<WorktreeBaselineCandidate> Candidates { get; private set; } = new WorktreeBaselineCandidate[0];
        public IReadOnlyList<WorktreeSnapshotStatus> Snapshots { get; private set; } = new WorktreeSnapshotStatus[0];
        public RestoreReport NewWorktree { get; private set; }

        public static WorktreeSetupStatus Read(string unityRoot, UserEnvironment environment = null)
        {
            environment = environment ?? UserEnvironment.Current;
            var identity = RepositoryIdentity.Find(unityRoot);
            if (identity == null) return new WorktreeSetupStatus();
            var local = LocalPrefs.Load(unityRoot);
            var state = new SharedState(identity, environment);
            var shared = state.LoadChoices();
            var follows = SharedChoices.Follows(local, shared, unityRoot);
            var effective = SharedChoices.Load(unityRoot, environment);
            var restore = new WorktreeRestore(unityRoot, environment).PlanNewWorktree();
            var service = new SkillsSyncService(unityRoot, environment);
            var resolutions = state.LoadResolutions();
            var fetcher = new SnapshotFetcher(state.Snapshots, resolutions, service.Mode);
            return new WorktreeSetupStatus
            {
                Identity = identity,
                Scope = local.ChoiceScope,
                FollowsShared = follows,
                HasSharedChoices = shared.HasChoices,
                Differences = WorktreeChoiceDifference.Between(effective, shared),
                Candidates = restore.BaselineCandidates.Select(project => new WorktreeBaselineCandidate
                {
                    Project = project,
                    Differences = WorktreeChoiceDifference.Between(LocalPrefs.Load(project), effective),
                }).ToList(),
                Snapshots = service.Choices.Skills.SelectedIn(service.Lock).Select(skill =>
                {
                    var folder = fetcher.Locate(skill, out var problem);
                    var resolution = resolutions.Find(skill);
                    var hash = resolution?.Snapshot ?? (folder == null ? null : Path.GetFileName(folder));
                    return new WorktreeSnapshotStatus
                    {
                        Skill = skill.Name,
                        Resolution = resolution,
                        Available = folder != null,
                        MatchesLock = hash == null || !LockVerification.CanVerify(skill) ? (bool?)null
                            : LockVerification.MatchesLock(skill, hash),
                        Problem = problem,
                        RestoreSnapshot = folder == null ? null : Path.GetFileName(folder),
                    };
                }).ToList(),
                NewWorktree = restore,
            };
        }
    }

    /// <summary>The choice members shared between worktrees; folder selection includes its automatic, never-set state.</summary>
    public enum WorktreeChoiceKind { Folders, DeclinedFolders, OptIns, OptOuts, InstallAnyway }

    /// <summary>One kind of choice that differs. A null folder list means automatic selection, distinct from choosing none.</summary>
    public sealed class WorktreeChoiceDifference
    {
        public WorktreeChoiceKind Kind { get; internal set; }
        public IReadOnlyList<string> Local { get; internal set; }
        public IReadOnlyList<string> Shared { get; internal set; }

        /// <summary>Exactly the choice members compared by LocalPrefs.SameChoicesAs, with both sides for display.</summary>
        internal static IReadOnlyList<WorktreeChoiceDifference> Between(LocalPrefs local, LocalPrefs shared)
        {
            var differences = new List<WorktreeChoiceDifference>();
            Add(WorktreeChoiceKind.Folders, local.SelectedFolders, shared.SelectedFolders);
            Add(WorktreeChoiceKind.DeclinedFolders, local.DeclinedFolders, shared.DeclinedFolders);
            Add(WorktreeChoiceKind.OptIns, local.OptedInSkills, shared.OptedInSkills);
            Add(WorktreeChoiceKind.OptOuts, local.OptedOutSkills, shared.OptedOutSkills);
            Add(WorktreeChoiceKind.InstallAnyway, Flatten(local.InstallAnywaySkills), Flatten(shared.InstallAnywaySkills));
            return differences;

            void Add(WorktreeChoiceKind kind, IEnumerable<string> here, IEnumerable<string> there)
            {
                var left = here?.OrderBy(v => v, StringComparer.Ordinal).ToArray();
                var right = there?.OrderBy(v => v, StringComparer.Ordinal).ToArray();
                if ((left == null) != (right == null) || (left != null && !left.SequenceEqual(right)))
                    differences.Add(new WorktreeChoiceDifference { Kind = kind, Local = left, Shared = right });
            }
        }

        static IEnumerable<string> Flatten(IReadOnlyDictionary<string, IReadOnlyList<string>> choices) =>
            choices.SelectMany(folder => folder.Value.Select(skill => folder.Key + "/" + skill));
    }

    /// <summary>A checkout offered by the restore's baseline decision, compared with the open checkout.</summary>
    public sealed class WorktreeBaselineCandidate
    {
        public string Project { get; internal set; }
        public IReadOnlyList<WorktreeChoiceDifference> Differences { get; internal set; }
    }

    /// <summary>The recorded snapshot for a selected lock entry and whether the current install mode can restore it.</summary>
    public sealed class WorktreeSnapshotStatus
    {
        public string Skill { get; internal set; }
        public SnapshotResolution Resolution { get; internal set; }
        public bool Available { get; internal set; }
        public bool? MatchesLock { get; internal set; }
        public string Problem { get; internal set; }
        /// <summary>The content hash a restore would use; null when this mode cannot restore the skill.</summary>
        public string RestoreSnapshot { get; internal set; }
    }

    /// <summary>Explicit actions for setting up restore; reading the status never calls them.</summary>
    public static class WorktreeSetup
    {
        /// <summary>Keeping own choices freezes the effective choices the window shows, including recent shared edits.</summary>
        public static void SetScope(string project, ChoiceScope scope, UserEnvironment environment = null)
        {
            environment = environment ?? UserEnvironment.Current;
            if (scope != ChoiceScope.Worktree && scope != ChoiceScope.Shared) throw new ArgumentOutOfRangeException(nameof(scope));
            var ownState = State(project, environment);
            using (ownState.Lock())
            {
                var prefs = SharedChoices.Load(project, environment);
                var shared = ownState.LoadChoices();
                if (scope == ChoiceScope.Shared && shared.HasChoices) prefs.CopyChoicesFrom(shared);
                else MaterializeFolders(prefs, environment);
                if (scope == ChoiceScope.Shared && !shared.HasChoices) Publish(shared, prefs, project);
                prefs.ChoiceScope = scope;
                prefs.Save();
            }
        }

        /// <summary>Publishes the open checkout's effective choices and makes it follow them. Installs no skills.</summary>
        public static void Share(string project, UserEnvironment environment = null)
        {
            environment = environment ?? UserEnvironment.Current;
            var state = State(project, environment);
            using (state.Lock())
            {
                var prefs = SharedChoices.Load(project, environment);
                MaterializeFolders(prefs, environment);
                var shared = state.LoadChoices();
                Publish(shared, prefs, project);
                prefs.ChoiceScope = ChoiceScope.Shared;
                prefs.Save();
            }
        }

        /// <summary>Takes the current shared choices, preserving the checkout's installation and bookkeeping until sync.</summary>
        public static void UseShared(string project, UserEnvironment environment = null)
        {
            environment = environment ?? UserEnvironment.Current;
            var state = State(project, environment);
            using (state.Lock())
            {
                var shared = state.LoadChoices();
                if (!shared.HasChoices) throw new RepositoryException("No choices are shared yet. Share this checkout's choices or choose a baseline first.");
                var prefs = LocalPrefs.Load(project);
                prefs.CopyChoicesFrom(shared);
                prefs.ChoiceScope = ChoiceScope.Shared;
                prefs.Save();
            }
        }

        /// <summary>Makes a currently offered candidate the first baseline; changes no checkout's installed files.</summary>
        public static void AdoptBaseline(string project, string candidate, UserEnvironment environment = null)
        {
            environment = environment ?? UserEnvironment.Current;
            var state = State(project, environment);
            using (state.Lock())
            {
                if (state.LoadChoices().HasChoices) throw new RepositoryException("Choices have already been shared. Refresh to review them.");
                var report = new WorktreeRestore(project, environment).PlanNewWorktree();
                var full = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var offered = report.BaselineCandidates.FirstOrDefault(p => Paths.Comparer.Equals(p, full));
                if (offered == null) throw new RepositoryException("This checkout is no longer a baseline candidate. Refresh to review the candidates.");
                var shared = state.LoadChoices();
                shared.CopyChoicesFrom(LocalPrefs.Load(offered));
                shared.BaselineProject = offered;
                shared.Save();
            }
        }

        static SharedState State(string project, UserEnvironment environment) =>
            SharedChoices.StateOf(project, environment) ?? throw RepositoryException.NotInCheckout(project);

        static void Publish(LocalPrefs shared, LocalPrefs prefs, string project)
        {
            shared.CopyChoicesFrom(prefs);
            if (shared.BaselineProject == null) shared.BaselineProject = Path.GetFullPath(project);
            shared.Save();
        }

        static void MaterializeFolders(LocalPrefs prefs, UserEnvironment environment)
        {
            if (prefs.SelectedFolders == null)
                prefs.SelectedFolders = FolderSelection.Effective(prefs, FolderLayout.Default, environment).Select(f => f.RelativePath).ToArray();
        }
    }
}
