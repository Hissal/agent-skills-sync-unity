using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Skills provided by installed, enabled Claude Code plugins: user-scope duplicates for <c>.claude/skills</c> only.
    /// Claude Code shows them namespaced (<c>/unity:ui</c>), but they load beside a project's <c>ui</c> all the same,
    /// so a plugin skill duplicates the project skill of the same folder name. Layout and rules:
    /// <c>docs/claude-code-plugins.md</c>.
    /// </summary>
    /// <remarks>
    /// Reads <c>installed_plugins.json</c> in the plugins root (<c>$CLAUDE_CODE_PLUGIN_CACHE_DIR</c>, else
    /// <c>$CLAUDE_CONFIG_DIR/plugins</c>, else <c>~/.claude/plugins</c>), never the cache folders themselves, so stale
    /// versions left in the cache are ignored. A plugin counts when one of its install records applies (user or managed
    /// scope, or project/local scope for this project) and it is enabled: the first <c>enabledPlugins</c> entry naming it
    /// in the project's <c>.claude/settings.local.json</c>, <c>.claude/settings.json</c>, then the user's
    /// <c>settings.json</c>, else the manifest's <c>defaultEnabled</c> (default true). A plugin whose manifest is present but
    /// unreadable or invalid JSON is skipped (Claude Code fails to load it); other unreadable files count as empty.
    /// </remarks>
    public sealed class ClaudePluginSource : IUserScopeSource
    {
        /// <summary>The project skills folder whose agents (Claude Code) load plugin skills.</summary>
        public const string FolderPath = ".claude/skills";

        const string SkillFileName = UserScopeLocationSource.SkillFileName;

        static readonly bool IgnoreCase = Path.DirectorySeparatorChar == '\\';
        static readonly StringComparer PathComparer = IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        static readonly StringComparison PathComparison = IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        readonly string _projectRoot;

        /// <param name="projectRoot">
        /// The project, for its settings files and project/local-scope installs; null = user settings and user-scope
        /// installs only.
        /// </param>
        public ClaudePluginSource(string projectRoot = null) =>
            _projectRoot = projectRoot == null ? null : Normalize(projectRoot);

        /// <summary>A source that knows no project (see the constructor).</summary>
        public static ClaudePluginSource WithoutProject { get; } = new ClaudePluginSource();

        public IEnumerable<UserScopeCopy> Find(SkillsFolder folder, UserEnvironment environment)
        {
            if (folder.RelativePath != FolderPath) return Enumerable.Empty<UserScopeCopy>();

            var configDir = environment.Variable("CLAUDE_CONFIG_DIR") ?? Path.Combine(environment.HomeDirectory, ".claude");
            var pluginsRoot = environment.Variable("CLAUDE_CODE_PLUGIN_CACHE_DIR") ?? Path.Combine(configDir, "plugins");
            var installed = Member(ReadObject(Path.Combine(pluginsRoot, "installed_plugins.json")), "plugins") as List<KeyValuePair<string, object>>;
            if (installed == null) return Enumerable.Empty<UserScopeCopy>();

            var settings = new List<List<KeyValuePair<string, object>>>();
            if (_projectRoot != null)
            {
                settings.Add(EnabledPlugins(Path.Combine(_projectRoot, ".claude", "settings.local.json")));
                settings.Add(EnabledPlugins(Path.Combine(_projectRoot, ".claude", "settings.json")));
            }
            settings.Add(EnabledPlugins(Path.Combine(configDir, "settings.json")));

            var copies = new List<UserScopeCopy>();
            foreach (var plugin in installed)
            {
                var installPath = InstallPath(plugin.Value);
                if (installPath == null || !Directory.Exists(installPath)) continue;
                if (!TryReadManifest(installPath, out var manifest)) continue;
                if (!IsEnabled(plugin.Key, settings, manifest)) continue;

                var name = Member(manifest, "name") as string ?? plugin.Key.Split('@')[0];
                foreach (var skill in SkillFolders(installPath, manifest))
                {
                    var skillName = PathComparer.Equals(skill, installPath) ? name : Path.GetFileName(skill);
                    copies.Add(new UserScopeCopy(folder, skillName, skill, "plugin " + plugin.Key,
                        $"Claude Code, as /{name}:{skillName}", plugin.Key));
                }
            }
            return copies;
        }

        /// <summary>The install path of the record that applies here: this project's local or project install, else a user or managed one.</summary>
        string InstallPath(object records)
        {
            var list = records is List<object> array ? array : new List<object> { records };
            var objects = list.OfType<List<KeyValuePair<string, object>>>().ToList();
            var chosen =
                objects.FirstOrDefault(r => Member(r, "scope") as string == "local" && IsThisProject(r)) ??
                objects.FirstOrDefault(r => Member(r, "scope") as string == "project" && IsThisProject(r)) ??
                objects.FirstOrDefault(r => Member(r, "scope") is string scope ? scope == "user" || scope == "managed" : true);
            return Member(chosen, "installPath") is string path && path.Length > 0 ? Normalize(path) : null;
        }

        bool IsThisProject(List<KeyValuePair<string, object>> record) =>
            _projectRoot != null && Member(record, "projectPath") is string path && path.Length > 0 &&
            PathComparer.Equals(Normalize(path), _projectRoot);

        static bool IsEnabled(string id, IEnumerable<List<KeyValuePair<string, object>>> settings, List<KeyValuePair<string, object>> manifest)
        {
            foreach (var enabledPlugins in settings)
                if (Member(enabledPlugins, id) is bool enabled)
                    return enabled;
            return !(Member(manifest, "defaultEnabled") is bool byDefault) || byDefault;
        }

        /// <summary>
        /// The default <c>skills/</c> folder's skills, then each manifest <c>skills</c> path's (a folder of skill folders,
        /// or one skill folder); paths outside the plugin are ignored, as Claude Code rejects them.
        /// </summary>
        static IEnumerable<string> SkillFolders(string installPath, List<KeyValuePair<string, object>> manifest)
        {
            var roots = new List<string> { Path.Combine(installPath, "skills") };
            var declared = Member(manifest, "skills");
            var entries = declared is string single ? new List<object> { single } : declared as List<object> ?? new List<object>();
            foreach (var entry in entries.OfType<string>())
            {
                var full = Normalize(Path.Combine(installPath, entry.Replace('/', Path.DirectorySeparatorChar)));
                if (PathComparer.Equals(full, installPath) || full.StartsWith(installPath + Path.DirectorySeparatorChar, PathComparison))
                    roots.Add(full);
            }

            var seen = new HashSet<string>(PathComparer);
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                var skills = File.Exists(Path.Combine(root, SkillFileName)) ? new[] { root } : Children(root);
                foreach (var skill in skills)
                    if (seen.Add(skill))
                        yield return skill;
            }
        }

        static IEnumerable<string> Children(string folder)
        {
            try
            {
                return Directory.GetDirectories(folder)
                    .Where(child => File.Exists(Path.Combine(child, SkillFileName)))
                    .Select(Normalize)
                    .OrderBy(child => child, StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return Enumerable.Empty<string>();
            }
        }

        /// <summary>
        /// The plugin's <c>.claude-plugin/plugin.json</c>: null when there is none (it is optional), false when it is
        /// present but unreadable or not a JSON object, as Claude Code then fails to load the plugin.
        /// </summary>
        static bool TryReadManifest(string installPath, out List<KeyValuePair<string, object>> manifest)
        {
            var path = Path.Combine(installPath, ".claude-plugin", "plugin.json");
            manifest = null;
            if (!File.Exists(path)) return true;
            manifest = ReadObject(path);
            return manifest != null;
        }

        static List<KeyValuePair<string, object>> EnabledPlugins(string settingsPath) =>
            Member(ReadObject(settingsPath), "enabledPlugins") as List<KeyValuePair<string, object>>;

        /// <summary>The JSON object in the file, or null when it is missing, unreadable or not an object.</summary>
        static List<KeyValuePair<string, object>> ReadObject(string path)
        {
            try
            {
                return File.Exists(path) ? JsonReader.Read(File.ReadAllText(path)) as List<KeyValuePair<string, object>> : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException)
            {
                return null;
            }
        }

        static object Member(List<KeyValuePair<string, object>> obj, string key) =>
            obj?.FirstOrDefault(m => m.Key == key).Value;

        static string Normalize(string path) =>
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
