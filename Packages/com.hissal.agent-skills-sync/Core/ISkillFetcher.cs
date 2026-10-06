using System;

namespace Hissal.AgentSkillsSync
{
    /// <summary>Gets a locked skill's contents onto local disk. The seam tests fake to keep the network out.</summary>
    public interface ISkillFetcher
    {
        /// <summary>
        /// Returns a local folder holding the skill's files (its <c>SKILL.md</c> at the top), verified against the
        /// locked <c>computedHash</c> where the fetcher can reproduce it.
        /// </summary>
        /// <exception cref="SkillFetchException">The skill could not be fetched or failed verification.</exception>
        string Fetch(LockedSkill skill);
    }

    /// <summary>Why a skill could not be fetched; tells a network problem apart from a changed source.</summary>
    public enum SkillFetchFailure
    {
        /// <summary>Anything not classified below.</summary>
        Other,

        /// <summary>The source could not be downloaded: offline, network error, or the repo is gone or private.</summary>
        Download,

        /// <summary>The downloaded skill does not hash to the locked <c>computedHash</c>: the source changed since it was locked.</summary>
        HashMismatch,

        /// <summary>The source was downloaded but is unusable: not an owner/repo source, the skill is missing, or it cannot be extracted.</summary>
        SourceUnusable,
    }

    /// <summary>A skill could not be fetched; the message names the skill or source, the reason, and what to do.</summary>
    public sealed class SkillFetchException : Exception
    {
        public SkillFetchException(string message, Exception inner = null)
            : this(null, SkillFetchFailure.Other, message, inner) { }

        public SkillFetchException(string skillName, SkillFetchFailure failure, string message, Exception inner = null)
            : base(message, inner)
        {
            SkillName = skillName;
            Failure = failure;
        }

        /// <summary>The skill that failed, or null when the thrower did not say.</summary>
        public string SkillName { get; }

        public SkillFetchFailure Failure { get; }
    }
}
