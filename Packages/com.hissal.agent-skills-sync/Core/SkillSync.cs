using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// One sync of a project: read the lock, scan the folders, plan, fetch everything the plan needs (Install and Update), then apply it.
    /// Fetching (and hash verification) finishes before the first filesystem change, so a failed fetch (offline, or a
    /// source changed since it was locked) leaves the project untouched. Every skill is attempted so all failures are reported.
    /// </summary>
    /// <remarks>
    /// The <see cref="InstallMode"/> decides what a source that changed since it was locked means.
    /// <see cref="InstallMode.Pinned"/> refuses it. <see cref="InstallMode.Latest"/> installs it and reports it in
    /// <see cref="SyncSummary.DiffersFromLock"/>; there every managed copy is fetched and compared with upstream, not the
    /// lock, so re-running with unchanged upstream is a no-op. Neither mode writes <c>skills-lock.json</c>.
    /// </remarks>
    public sealed class SkillSync
    {
        readonly string _projectRoot;
        readonly ISkillFetcher _fetcher;
        readonly MachineChoices _choices;
        readonly PlanExecutor _executor;
        readonly InstallModeStrategy _strategy;

        /// <summary>Shorthand for the <see cref="MachineChoices"/> constructor with the choices given one by one (see there).</summary>
        public SkillSync(string projectRoot, ISkillFetcher fetcher, FolderLayout layout = null, ILinkCreator linker = null,
            IEnumerable<SkillsFolder> selected = null, UserScopeState userScope = null, SkipChoices skips = null,
            InstallMode mode = InstallMode.Pinned)
            : this(projectRoot, fetcher, new MachineChoices(layout, selected, userScope, skips), linker, mode)
        {
        }

        /// <param name="choices">This machine's layout, folder selection, user-scope copies and skips (see <see cref="MachineChoices.Read"/>).</param>
        /// <param name="mode">
        /// Defaults to <see cref="InstallMode.Pinned"/>, the behaviour without install modes. The project's own choice
        /// (default Latest) is in <see cref="ProjectSyncSettings"/>; callers pass it here and to the fetcher.
        /// </param>
        public SkillSync(string projectRoot, ISkillFetcher fetcher, MachineChoices choices, ILinkCreator linker = null,
            InstallMode mode = InstallMode.Pinned)
        {
            _projectRoot = projectRoot;
            _fetcher = fetcher;
            _choices = choices ?? MachineChoices.Default;
            _executor = new PlanExecutor(linker);
            Mode = mode;
            _strategy = InstallModeStrategy.For(mode);
        }

        public InstallMode Mode { get; }

        /// <summary>
        /// What a sync would do now, without doing it or touching the network. In Latest mode an installed copy counts
        /// as current, since only a sync's fetch can tell whether upstream moved.
        /// </summary>
        /// <exception cref="LockfileException">The lockfile is missing or unusable.</exception>
        public InstallPlan Plan() => Plan(Lockfile.Load(_projectRoot));

        /// <summary>
        /// Locked skills whose managed canonical copy does not hash to the locked <c>computedHash</c> (as is or as a CRLF
        /// checkout), in lock order. Skills where a mismatch can't tell are never listed: a skills.sh-hashed source
        /// (<see cref="GitHubSkillFetcher.CanVerify"/>) or a copy with non-ASCII paths.
        /// </summary>
        /// <exception cref="LockfileException">The lockfile is missing or unusable.</exception>
        public IReadOnlyList<string> InstalledDiffersFromLock() => InstalledDiffersFromLock(Lockfile.Load(_projectRoot));

        /// <summary>Like <see cref="InstalledDiffersFromLock()"/>, against exactly <paramref name="lockfile"/>.</summary>
        public IReadOnlyList<string> InstalledDiffersFromLock(Lockfile lockfile)
        {
            var layout = _choices.Layout;
            var canonical = ProjectScanner.Scan(_projectRoot, layout).For(layout.Canonical);
            var canonicalPath = Paths.InProject(_projectRoot, layout.Canonical.RelativePath);
            return lockfile.Skills
                .Where(s => canonical.InstalledHash(s.Name) != null // a managed copy
                            && GitHubSkillFetcher.DiffersFromLock(s, Path.Combine(canonicalPath, s.Name)))
                .Select(s => s.Name)
                .ToList();
        }

        /// <summary>What syncing <paramref name="lockfile"/> would do now, without doing it (see <see cref="Plan()"/>).</summary>
        public InstallPlan Plan(Lockfile lockfile) =>
            PlanWith(lockfile, ProjectScanner.Scan(_projectRoot, _choices.Layout), _strategy.PreviewCheck);

        /// <summary>Plans with this sync's folder selection, user-scope copies and skips.</summary>
        InstallPlan PlanWith(Lockfile lockfile, ProjectState project, IInstalledCopyCheck check) =>
            InstallPlanner.Plan(lockfile, project, _choices, check);

        /// <summary>Loads the lockfile, plans and applies. Call only after the contributor consented.</summary>
        /// <exception cref="LockfileException">The lockfile is missing or unusable; nothing was changed.</exception>
        /// <exception cref="SyncAbortedException">One or more skills could not be fetched or verified; nothing was changed.</exception>
        public SyncSummary Run() => Run(Lockfile.Load(_projectRoot));

        /// <summary>
        /// Plans and applies exactly <paramref name="lockfile"/>, not whatever is on disk now. Pass the instance the
        /// contributor's consent was checked against, so a lockfile changed after the check cannot run unconfirmed.
        /// </summary>
        /// <exception cref="SyncAbortedException">One or more skills could not be fetched or verified; nothing was changed.</exception>
        public SyncSummary Run(Lockfile lockfile)
        {
            var project = ProjectScanner.Scan(_projectRoot, _choices.Layout);

            // Latest compares every managed copy with upstream, so it fetches them all; Pinned only what the lock says is stale.
            var toFetch = PlanWith(lockfile, project, _strategy.FetchCheck);

            var fetched = new Dictionary<string, string>();
            var upstreamHashes = new Dictionary<string, string>();
            var failures = new Dictionary<string, SkillFetchException>();
            foreach (var action in toFetch.Actions.Where(a => a.NeedsFetch))
            {
                try
                {
                    var folder = _fetcher.Fetch(action.Skill);
                    var hash = SkillFolderHash.Compute(folder);
                    _strategy.Accept(action.Skill, hash);
                    fetched[action.SkillName] = folder;
                    upstreamHashes[action.SkillName] = hash;
                }
                catch (SkillFetchException e)
                {
                    failures[action.SkillName] = e;
                }
            }

            if (failures.Count > 0) throw new SyncAbortedException(failures);

            var differs = _strategy.DiffersFromLock(lockfile, fetched);
            var applyCheck = _strategy.ApplyCheck(upstreamHashes);
            var plan = applyCheck == null ? toFetch : PlanWith(lockfile, project, applyCheck);
            var summary = _executor.Execute(_projectRoot, plan, fetched);
            return differs == null ? summary : new SyncSummary(summary.Applied, summary.LinkMethods, differs);
        }
    }
}
