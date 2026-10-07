using System;
using System.IO;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// One user-scope skills folder some agents read, e.g. <c>~/.codex/skills</c>: an agent home (<c>~/.codex</c>)
    /// plus the skills path inside it, with an optional environment variable that moves the home.
    /// </summary>
    /// <remarks>
    /// <see cref="Home"/> starts with <c>~</c>. When <see cref="EnvVar"/> is set, its value replaces the
    /// <see cref="EnvReplaces"/> prefix of <see cref="Home"/>: by default the whole home (<c>CODEX_HOME</c> replaces
    /// <c>~/.codex</c>), or e.g. <c>~</c> (<c>GEMINI_CLI_HOME</c> stands in for the user home, giving
    /// <c>$GEMINI_CLI_HOME/.gemini</c>) or <c>~/.config</c> (<c>XDG_CONFIG_HOME</c>).
    /// </remarks>
    public sealed class UserScopeLocation
    {
        /// <param name="agents">Who reads this location, for display, e.g. <c>Codex, Cursor</c>.</param>
        /// <param name="home">The agent home, starting with <c>~</c> and using forward slashes, e.g. <c>~/.codex</c>.</param>
        /// <param name="skillsPath">The skills folder inside the home, forward slashes.</param>
        /// <param name="envVar">Environment variable that moves the home; null for none.</param>
        /// <param name="envReplaces">The prefix of <paramref name="home"/> the variable's value replaces; null = all of it.</param>
        public UserScopeLocation(string agents, string home, string skillsPath = "skills", string envVar = null, string envReplaces = null)
        {
            if (home == null || !home.StartsWith("~", StringComparison.Ordinal))
                throw new ArgumentException("A user-scope home starts with ~.", nameof(home));
            envReplaces = envReplaces ?? home;
            if (!home.StartsWith(envReplaces, StringComparison.Ordinal))
                throw new ArgumentException("envReplaces must be a prefix of home.", nameof(envReplaces));

            Agents = agents;
            Home = home;
            SkillsPath = skillsPath;
            EnvVar = envVar;
            EnvReplaces = envReplaces;
        }

        /// <summary>Who reads this location, for display.</summary>
        public string Agents { get; }

        /// <summary>The agent home as written in the table, e.g. <c>~/.codex</c>.</summary>
        public string Home { get; }

        /// <summary>The skills folder inside <see cref="Home"/>, e.g. <c>skills</c>.</summary>
        public string SkillsPath { get; }

        /// <summary>The environment variable that moves the home, or null.</summary>
        public string EnvVar { get; }

        /// <summary>The prefix of <see cref="Home"/> that <see cref="EnvVar"/>'s value replaces.</summary>
        public string EnvReplaces { get; }

        /// <summary>The absolute agent home on this machine (whether or not it exists).</summary>
        public string ResolveHome(UserEnvironment environment)
        {
            var overridden = EnvVar == null ? null : environment.Variable(EnvVar);
            var root = overridden ?? Path.Combine(environment.HomeDirectory, Native(EnvReplaces.Substring(1)));
            return Path.GetFullPath(Path.Combine(root, Native(Home.Substring(EnvReplaces.Length))));
        }

        /// <summary>The absolute user-scope skills folder on this machine (whether or not it exists).</summary>
        public string ResolveSkillsFolder(UserEnvironment environment) =>
            Path.GetFullPath(Path.Combine(ResolveHome(environment), Native(SkillsPath)));

        public override string ToString() =>
            EnvVar == null ? $"{Home}/{SkillsPath}" : $"{Home}/{SkillsPath} (${EnvVar})";

        static string Native(string path) => path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
    }
}
