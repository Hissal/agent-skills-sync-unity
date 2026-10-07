using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>What a sync did, for the window's summary.</summary>
    public sealed class SyncSummary
    {
        public SyncSummary(IReadOnlyList<PlanAction> applied) => Applied = applied;

        /// <summary>Every action applied, in order.</summary>
        public IReadOnlyList<PlanAction> Applied { get; }

        /// <summary>Names of skills given a canonical copy.</summary>
        public IReadOnlyList<string> Installed => NamesOf(PlanActionKind.Install);

        /// <summary>Names of skills linked into at least one link folder.</summary>
        public IReadOnlyList<string> Linked => NamesOf(PlanActionKind.Link);

        public bool NothingChanged => Applied.Count == 0;

        IReadOnlyList<string> NamesOf(PlanActionKind kind) =>
            Applied.Where(a => a.Kind == kind).Select(a => a.SkillName).Distinct().ToList();
    }
}
