using System;
using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>A sync stopped before changing anything because one or more skills could not be fetched or verified.</summary>
    public sealed class SyncAbortedException : Exception
    {
        public SyncAbortedException(IReadOnlyDictionary<string, SkillFetchException> failures)
            : base(Describe(failures)) => Failures = failures;

        /// <summary>The failure of each skill that could not be fetched, by skill name.</summary>
        public IReadOnlyDictionary<string, SkillFetchException> Failures { get; }

        static string Describe(IReadOnlyDictionary<string, SkillFetchException> failures) =>
            $"Sync aborted, nothing was changed: {failures.Count} skill(s) could not be fetched.\n" +
            string.Join("\n", failures.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => "- " + f.Value.Message));
    }
}
