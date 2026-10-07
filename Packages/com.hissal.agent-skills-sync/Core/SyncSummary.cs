using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>What a sync did, for the window's summary.</summary>
    public sealed class SyncSummary
    {
        public SyncSummary(IReadOnlyList<PlanAction> applied, IReadOnlyDictionary<PlanAction, LinkMethod> linkMethods = null)
        {
            Applied = applied;
            LinkMethods = linkMethods ?? new Dictionary<PlanAction, LinkMethod>();
        }

        /// <summary>Every action applied, in order, including LeaveForeign (which changes nothing).</summary>
        public IReadOnlyList<PlanAction> Applied { get; }

        /// <summary>How each applied Link action was made.</summary>
        public IReadOnlyDictionary<PlanAction, LinkMethod> LinkMethods { get; }

        /// <summary>Names of skills linked into at least one link folder by <paramref name="method"/>.</summary>
        public IReadOnlyList<string> LinkedBy(LinkMethod method) =>
            Applied.Where(a => a.Kind == PlanActionKind.Link && LinkMethods.TryGetValue(a, out var m) && m == method)
                .Select(a => a.SkillName).Distinct().ToList();

        /// <summary>Names of skills given a canonical copy.</summary>
        public IReadOnlyList<string> Installed => NamesOf(PlanActionKind.Install);

        /// <summary>Names of skills whose canonical copy was replaced with the locked version.</summary>
        public IReadOnlyList<string> Updated => NamesOf(PlanActionKind.Update);

        /// <summary>Names of skills whose managed copy or links were deleted (no longer locked, or no folder selected).</summary>
        public IReadOnlyList<string> Removed => NamesOf(PlanActionKind.Remove);

        /// <summary>Names of locked skills left alone in at least one folder because the entry there is not the tool's.</summary>
        public IReadOnlyList<string> Skipped => NamesOf(PlanActionKind.LeaveForeign);

        /// <summary>Names of skills linked into at least one link folder.</summary>
        public IReadOnlyList<string> Linked => NamesOf(PlanActionKind.Link);

        /// <summary>Names of skills whose link was removed from at least one link folder.</summary>
        public IReadOnlyList<string> Unlinked => NamesOf(PlanActionKind.Unlink);

        public bool NothingChanged => !Applied.Any(a => a.ChangesProject);

        IReadOnlyList<string> NamesOf(PlanActionKind kind) =>
            Applied.Where(a => a.Kind == kind).Select(a => a.SkillName).Distinct().ToList();
    }
}
