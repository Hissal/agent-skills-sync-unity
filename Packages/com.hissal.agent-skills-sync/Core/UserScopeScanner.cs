using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// One kind of place a user-scope duplicate of a project skill can come from, e.g. the folder layout's user-scope
    /// locations (<see cref="UserScopeLocationSource"/>). The set is open: another source (installed Claude Code
    /// plugins) reports its copies the same way.
    /// </summary>
    public interface IUserScopeSource
    {
        /// <summary>Every skill this source gives the agents reading <paramref name="folder"/>.</summary>
        IEnumerable<UserScopeCopy> Find(SkillsFolder folder, UserEnvironment environment);
    }

    /// <summary>The environment scanner: which skills the contributor already has at user scope, per project skills folder.</summary>
    public static class UserScopeScanner
    {
        /// <summary>
        /// The sources <see cref="Scan"/> uses when given none: the layout's locations and installed Claude Code and Codex
        /// plugins, read without a project (user settings only; see <see cref="DefaultSourcesFor"/>).
        /// </summary>
        public static IReadOnlyList<IUserScopeSource> DefaultSources { get; } =
            new IUserScopeSource[] { UserScopeLocationSource.Instance, ClaudePluginSource.WithoutProject, new CodexPluginSource() };

        /// <summary>
        /// <see cref="DefaultSources"/> for one project: Claude plugins use its settings and install records;
        /// Codex plugins also read its <c>.codex/config.toml</c> enabled overrides.
        /// </summary>
        public static IReadOnlyList<IUserScopeSource> DefaultSourcesFor(string projectRoot) =>
            new IUserScopeSource[] { UserScopeLocationSource.Instance, new ClaudePluginSource(projectRoot), new CodexPluginSource(projectRoot) };

        /// <summary>Asks every source about every folder in <paramref name="folders"/> (normally the selected ones).</summary>
        /// <param name="sources">Where to look; null = <see cref="DefaultSources"/>.</param>
        public static UserScopeState Scan(IEnumerable<SkillsFolder> folders, UserEnvironment environment,
            IEnumerable<IUserScopeSource> sources = null)
        {
            var sourceList = (sources ?? DefaultSources).ToList();
            return new UserScopeState(folders
                .Where(f => f != null)
                .SelectMany(folder => sourceList.SelectMany(source => source.Find(folder, environment))));
        }
    }

    /// <summary>
    /// The folder layout's user-scope locations (<see cref="SkillsFolder.UserScopeLocations"/>): every sub-folder holding
    /// a <c>SKILL.md</c> is a skill. Locations resolving to the same folder are read once.
    /// </summary>
    public sealed class UserScopeLocationSource : IUserScopeSource
    {
        public const string SkillFileName = "SKILL.md";

        public static UserScopeLocationSource Instance { get; } = new UserScopeLocationSource();

        UserScopeLocationSource() { }

        public IEnumerable<UserScopeCopy> Find(SkillsFolder folder, UserEnvironment environment)
        {
            var seen = new HashSet<string>(Paths.Comparer);
            foreach (var location in folder.UserScopeLocations)
            {
                var skillsFolder = location.ResolveSkillsFolder(environment);
                if (!seen.Add(skillsFolder) || !Directory.Exists(skillsFolder)) continue;
                foreach (var skill in SkillsIn(skillsFolder))
                    yield return new UserScopeCopy(folder, Path.GetFileName(skill), skill, Describe(location, environment), location.Agents);
            }
        }

        static IEnumerable<string> SkillsIn(string skillsFolder)
        {
            string[] children;
            try
            {
                children = Directory.GetDirectories(skillsFolder);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return Enumerable.Empty<string>();
            }
            return children.Where(child => File.Exists(Path.Combine(child, SkillFileName)))
                .OrderBy(child => child, StringComparer.Ordinal);
        }

        /// <summary>The location as the layout writes it, or its resolved folder when an environment variable moved it.</summary>
        static string Describe(UserScopeLocation location, UserEnvironment environment) =>
            location.EnvVar != null && environment.Variable(location.EnvVar) != null
                ? location.ResolveSkillsFolder(environment)
                : $"{location.Home}/{location.SkillsPath}";
    }
}
