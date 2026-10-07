using System;

namespace Hissal.AgentSkillsSync
{
    /// <summary>The lockfile cannot be used; the message says why, in words a contributor can act on.</summary>
    public sealed class LockfileException : Exception
    {
        public LockfileException(string message) : base(message) { }
    }
}
