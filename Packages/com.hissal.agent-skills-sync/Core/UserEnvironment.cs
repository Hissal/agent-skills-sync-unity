using System;
using System.IO;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// The contributor's user home and environment variables, the inputs that resolve a
    /// <see cref="UserScopeLocation"/>, and the folder holding this machine's state shared by every checkout.
    /// Tests pass a temp folder and a fake variable lookup.
    /// </summary>
    public sealed class UserEnvironment
    {
        /// <summary>Overrides <see cref="StateDirectory"/> for <see cref="Current"/>.</summary>
        public const string StateDirectoryVariable = "AGENT_SKILLS_SYNC_STATE_DIR";

        readonly Func<string, string> _getVariable;

        /// <param name="homeDirectory">The OS user home, what <c>~</c> means.</param>
        /// <param name="getVariable">Environment variable lookup; null or empty means unset.</param>
        /// <param name="stateDirectory">See <see cref="StateDirectory"/>; null = <c>.agent-skills-sync</c> in the home.</param>
        public UserEnvironment(string homeDirectory, Func<string, string> getVariable, string stateDirectory = null)
        {
            HomeDirectory = homeDirectory;
            _getVariable = getVariable ?? (_ => null);
            StateDirectory = stateDirectory ?? Path.Combine(homeDirectory, ".agent-skills-sync");
        }

        /// <summary>
        /// This machine's user home and process environment. Its state directory is <see cref="StateDirectoryVariable"/>
        /// when set, else <c>AgentSkillsSync</c> in local application data, beside the fetch cache.
        /// </summary>
        public static UserEnvironment Current
        {
            get
            {
                var state = Environment.GetEnvironmentVariable(StateDirectoryVariable);
                if (string.IsNullOrEmpty(state)) state = Path.GetDirectoryName(GitHubSkillFetcher.DefaultCacheRoot);
                return new UserEnvironment(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    Environment.GetEnvironmentVariable, state);
            }
        }

        public string HomeDirectory { get; }

        /// <summary>
        /// The folder for this machine's state that every checkout shares: each repository's shared choices and
        /// recorded snapshot resolutions (<see cref="SharedState"/>), and the skill snapshots (<see cref="SnapshotStore"/>).
        /// Never inside a project.
        /// </summary>
        public string StateDirectory { get; }

        /// <summary>The variable's value, or null when it is unset or empty.</summary>
        public string Variable(string name)
        {
            var value = _getVariable(name);
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
