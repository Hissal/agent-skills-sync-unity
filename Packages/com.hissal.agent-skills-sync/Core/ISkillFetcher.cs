using System;

namespace Hissal.AgentSkillsSync
{
    /// <summary>Gets a locked skill's contents onto local disk. The seam tests fake to keep the network out.</summary>
    public interface ISkillFetcher
    {
        /// <summary>Returns a local folder holding the skill's files (its <c>SKILL.md</c> at the top).</summary>
        /// <exception cref="SkillFetchException">The skill could not be fetched.</exception>
        string Fetch(LockedSkill skill);
    }

    /// <summary>A skill could not be fetched; the message names the skill or source and the reason.</summary>
    public sealed class SkillFetchException : Exception
    {
        public SkillFetchException(string message, Exception inner = null) : base(message, inner) { }
    }
}
