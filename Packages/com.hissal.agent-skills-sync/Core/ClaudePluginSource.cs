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
    /// <c>settings.json</c>, else the marketplace entry's <c>defaultEnabled</c> when its <c>marketplace.json</c> is on disk, else
    /// the manifest's (default true). A plugin that an enabled plugin depends on is enabled whatever its default, and a
    /// plugin with a dependency that is missing or set to false is not. A plugin whose manifest is present but
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

            var marketplaces = new Marketplaces(pluginsRoot);
            var plugins = new List<Plugin>();
            foreach (var record in installed)
            {
                var installPath = InstallPath(record.Value);
                if (installPath == null || !Directory.Exists(installPath)) continue;
                if (!TryReadManifest(installPath, out var manifest)) continue;
                plugins.Add(new Plugin(record.Key, installPath, manifest, marketplaces.Entry(record.Key), settings));
            }

            var copies = new List<UserScopeCopy>();
            var enabled = Enabled(plugins);
            foreach (var plugin in plugins.Where(p => enabled.Contains(p.Id)))
            {
                var name = Member(plugin.Manifest, "name") as string ?? plugin.Id.Split('@')[0];
                foreach (var skill in SkillFolders(plugin.InstallPath, plugin.Manifest))
                {
                    var skillName = PathComparer.Equals(skill, plugin.InstallPath) ? name : Path.GetFileName(skill);
                    copies.Add(new UserScopeCopy(folder, skillName, skill, "plugin " + plugin.Id,
                        $"Claude Code, as /{name}:{skillName}", plugin.Id));
                }
            }
            return copies;
        }

        /// <summary>
        /// The ids Claude Code loads, over the installed plugins' dependency graph. A plugin loads only when each of its
        /// dependencies is installed, not set to false, and loadable itself (else Claude Code leaves it disabled with
        /// "Dependency ... is not installed / is disabled"). The roots are the loadable plugins enabled by a setting or
        /// by default; the dependencies of each enabled plugin are enabled too, whatever their own default.
        /// </summary>
        static HashSet<string> Enabled(List<Plugin> plugins)
        {
            var byId = new Dictionary<string, Plugin>();
            foreach (var plugin in plugins)
                if (!byId.ContainsKey(plugin.Id))
                    byId.Add(plugin.Id, plugin);

            var loadable = new Dictionary<string, bool>();
            bool Loadable(Plugin plugin)
            {
                if (loadable.TryGetValue(plugin.Id, out var known)) return known;
                loadable[plugin.Id] = true; // assumed while its own dependencies are checked, so a cycle ends
                var result = plugin.Dependencies.All(id =>
                    byId.TryGetValue(id, out var dependency) && dependency.Setting != false && Loadable(dependency));
                loadable[plugin.Id] = result;
                return result;
            }

            var enabled = new HashSet<string>();
            var pending = new Stack<Plugin>(byId.Values.Where(p => (p.Setting ?? p.ByDefault) && Loadable(p)));
            while (pending.Count > 0)
            {
                var plugin = pending.Pop();
                if (!enabled.Add(plugin.Id)) continue;
                foreach (var id in plugin.Dependencies)
                    pending.Push(byId[id]); // installed and loadable, as the plugin itself is loadable
            }
            return enabled;
        }

        /// <summary>An installed plugin whose install record applies here and whose manifest, if any, is valid.</summary>
        sealed class Plugin
        {
            public readonly string Id;
            public readonly string InstallPath;
            public readonly List<KeyValuePair<string, object>> Manifest;

            /// <summary>The first <c>enabledPlugins</c> value naming the plugin, or null when no settings file does.</summary>
            public readonly bool? Setting;

            /// <summary>The marketplace entry's <c>defaultEnabled</c>, else the manifest's, else true.</summary>
            public readonly bool ByDefault;

            /// <summary>Ids of the plugins that the manifest and the marketplace entry declare as dependencies.</summary>
            public readonly List<string> Dependencies;

            public Plugin(string id, string installPath, List<KeyValuePair<string, object>> manifest,
                List<KeyValuePair<string, object>> entry, IEnumerable<List<KeyValuePair<string, object>>> settings)
            {
                Id = id;
                InstallPath = installPath;
                Manifest = manifest;
                Setting = settings.Select(enabledPlugins => Member(enabledPlugins, id)).OfType<bool>().Cast<bool?>().FirstOrDefault();
                ByDefault = Member(entry, "defaultEnabled") as bool? ?? Member(manifest, "defaultEnabled") as bool? ?? true;
                var at = id.LastIndexOf('@');
                var marketplace = at < 0 ? "" : id.Substring(at + 1);
                Dependencies = DependencyIds(manifest, marketplace).Concat(DependencyIds(entry, marketplace)).Distinct().ToList();
            }

            /// <summary>
            /// The <c>dependencies</c> items as ids: <c>"name"</c> (in this plugin's marketplace), <c>"name@marketplace"</c>,
            /// or <c>{ "name", "marketplace", "version" }</c>. Version constraints are not checked.
            /// </summary>
            static IEnumerable<string> DependencyIds(List<KeyValuePair<string, object>> manifest, string marketplace)
            {
                if (!(Member(manifest, "dependencies") is List<object> items)) yield break;
                foreach (var item in items)
                {
                    if (item is string text && text.Length > 0)
                        yield return text.Contains("@") ? text : text + "@" + marketplace;
                    else if (item is List<KeyValuePair<string, object>> spec && Member(spec, "name") is string name && name.Length > 0)
                        yield return name + "@" + (Member(spec, "marketplace") as string ?? marketplace);
                }
            }
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

        /// <summary>
        /// The marketplaces' plugin entries, read from each marketplace's <c>marketplace.json</c> on disk. Found through
        /// <c>known_marketplaces.json</c> for the source types whose file location is documented: <c>github</c> and
        /// <c>git</c> (the clone at <c>installLocation</c>, file at the source's <c>path</c>, default
        /// <c>.claude-plugin/marketplace.json</c>), <c>directory</c> (<c>installLocation</c> is the root) and <c>file</c>
        /// (<c>installLocation</c> is the file). Other sources (<c>url</c>, claude.ai) give no entry.
        /// </summary>
        sealed class Marketplaces
        {
            const string DefaultFile = ".claude-plugin/marketplace.json";

            readonly List<KeyValuePair<string, object>> _known;
            readonly Dictionary<string, List<object>> _plugins = new Dictionary<string, List<object>>();

            public Marketplaces(string pluginsRoot) =>
                _known = ReadObject(Path.Combine(pluginsRoot, "known_marketplaces.json"));

            /// <summary>The entry for plugin id <c>name@marketplace</c>, or null when it can't be found.</summary>
            public List<KeyValuePair<string, object>> Entry(string id)
            {
                var at = id.LastIndexOf('@');
                if (at <= 0) return null;
                var name = id.Substring(0, at);
                return Plugins(id.Substring(at + 1))
                    .OfType<List<KeyValuePair<string, object>>>()
                    .FirstOrDefault(entry => Member(entry, "name") as string == name);
            }

            List<object> Plugins(string marketplace)
            {
                if (!_plugins.TryGetValue(marketplace, out var plugins))
                {
                    try
                    {
                        var file = MarketplaceFile(Member(_known, marketplace) as List<KeyValuePair<string, object>>);
                        plugins = file == null ? null : Member(ReadObject(file), "plugins") as List<object>;
                    }
                    catch (Exception e) when (e is ArgumentException || e is NotSupportedException)
                    {
                        plugins = null; // a malformed path in known_marketplaces.json
                    }
                    plugins = plugins ?? new List<object>();
                    _plugins[marketplace] = plugins;
                }
                return plugins;
            }

            static string MarketplaceFile(List<KeyValuePair<string, object>> known)
            {
                if (!(Member(known, "installLocation") is string location) || location.Length == 0) return null;
                var source = Member(known, "source") as List<KeyValuePair<string, object>>;
                switch (Member(source, "source") as string)
                {
                    case "github":
                    case "git":
                        var path = Member(source, "path") as string;
                        return Combine(location, string.IsNullOrEmpty(path) ? DefaultFile : path);
                    case "directory":
                        return Combine(location, DefaultFile);
                    case "file":
                        return location;
                    default:
                        return null;
                }
            }

            static string Combine(string root, string relative) =>
                Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
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
