using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// One sync of a project: read the lock, scan the folders, plan, fetch everything the plan needs (Install and Update), then apply it.
    /// Fetching (and hash verification) finishes before the first filesystem change, so a failed fetch (offline, or a
    /// source changed since it was locked) leaves the project untouched. Every skill is attempted so all failures are reported.
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
        /// <exception cref="SyncAbortedException">One or more skills could not be fetched or verified; nothing was changed.</exception>
        public SyncSummary Run()
        {
            var plan = Plan();
            var fetched = new Dictionary<string, string>();
            var failures = new Dictionary<string, SkillFetchException>();
            foreach (var action in plan.Actions.Where(a => a.NeedsFetch))
            {
                try
                {
                    fetched[action.SkillName] = _fetcher.Fetch(action.Skill);
                }
                catch (SkillFetchException e)
                {
                    failures[action.SkillName] = e;
                }
            }

            if (failures.Count > 0) throw new SyncAbortedException(failures);
            return _executor.Execute(_projectRoot, plan, fetched);
        }
    }
}
