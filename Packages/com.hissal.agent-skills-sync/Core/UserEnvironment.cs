using System;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// The contributor's user home and environment variables, the inputs that resolve a
    /// <see cref="UserScopeLocation"/>. Tests pass a temp folder and a fake variable lookup.
    /// </summary>
    public sealed class UserEnvironment
    {
        readonly Func<string, string> _getVariable;

        /// <param name="homeDirectory">The OS user home, what <c>~</c> means.</param>
        /// <param name="getVariable">Environment variable lookup; null or empty means unset.</param>
        public UserEnvironment(string homeDirectory, Func<string, string> getVariable)
        {
            HomeDirectory = homeDirectory;
            _getVariable = getVariable ?? (_ => null);
        }

        /// <summary>This machine's user home and process environment.</summary>
        public static UserEnvironment Current =>
            new UserEnvironment(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable);

        public string HomeDirectory { get; }

        /// <summary>The variable's value, or null when it is unset or empty.</summary>
        public string Variable(string name)
        {
            var value = _getVariable(name);
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
