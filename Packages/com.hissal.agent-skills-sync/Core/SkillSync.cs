using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// One sync of a project: read the lock, scan the folders, plan, fetch everything the plan needs, then apply it.
    /// Fetching finishes before the first filesystem change, so a failed fetch (e.g. offline) leaves the project untouched.
    /// </summary>
    public sealed class SkillSync
    {
        readonly string _projectRoot;
        readonly ISkillFetcher _fetcher;
        readonly FolderLayout _layout;
        readonly PlanExecutor _executor;

        public SkillSync(string projectRoot, ISkillFetcher fetcher, FolderLayout layout = null, ILinkCreator linker = null)
        {
            _projectRoot = projectRoot;
            _fetcher = fetcher;
            _layout = layout ?? FolderLayout.Default;
            _executor = new PlanExecutor(linker);
        }

        /// <summary>What a sync would do now, without doing it.</summary>
        /// <exception cref="LockfileException">The lockfile is missing or unusable.</exception>
        public InstallPlan Plan() =>
            InstallPlanner.Plan(Lockfile.Load(_projectRoot), ProjectScanner.Scan(_projectRoot, _layout), _layout);

        /// <summary>Plans and applies. Call only after the contributor consented.</summary>
        /// <exception cref="LockfileException">The lockfile is missing or unusable; nothing was changed.</exception>
        /// <exception cref="SkillFetchException">A skill could not be fetched; nothing was changed.</exception>
        public SyncSummary Run()
        {
            var plan = Plan();
            var fetched = new Dictionary<string, string>();
            foreach (var action in plan.Actions.Where(a => a.Kind == PlanActionKind.Install))
                fetched[action.SkillName] = _fetcher.Fetch(action.Skill);
            return _executor.Execute(_projectRoot, plan, fetched);
        }
    }
}
