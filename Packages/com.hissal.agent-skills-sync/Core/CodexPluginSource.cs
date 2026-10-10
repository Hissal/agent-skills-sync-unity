using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Installed, configured Codex plugin skills for .agents/skills. Cache selection and configuration rules
    /// are recorded with upstream sources in docs/codex-plugins.md. Unreadable state yields no copies.
    /// </summary>
    public sealed class CodexPluginSource : IUserScopeSource
    {
        public const string FolderPath = ".agents/skills";
        readonly string _projectRoot;

        public CodexPluginSource(string projectRoot = null) => _projectRoot = projectRoot;

        public IEnumerable<UserScopeCopy> Find(SkillsFolder folder, UserEnvironment environment)
        {
            if (folder.RelativePath != FolderPath) return Array.Empty<UserScopeCopy>();
            try
            {
                var home = environment.Variable("CODEX_HOME") ?? Path.Combine(environment.HomeDirectory, ".codex");
                var config = CodexPluginConfig.Read(Path.Combine(home, "config.toml"));
                if (_projectRoot != null)
                {
                    var projectConfig = Path.Combine(_projectRoot, ".codex", "config.toml");
                    if (File.Exists(projectConfig))
                        foreach (var entry in CodexPluginConfig.Read(projectConfig))
                            config[entry.Key] = entry.Value ?? (config.TryGetValue(entry.Key, out var prior) ? prior : null);
                }
                var copies = new List<UserScopeCopy>();
                foreach (var entry in config.OrderBy(e => e.Key, StringComparer.Ordinal))
                {
                    if (entry.Value == false) continue;
                    var id = entry.Key.Split('@');
                    if (id.Length != 2 || !Segment(id[0]) || !Segment(id[1])) continue;
                    var root = Path.Combine(home, "plugins", "cache", id[1], id[0]);
                    var versions = Children(root).Where(p => Segment(Path.GetFileName(p))).ToList();
                    versions.Sort((a, b) => CompareVersions(Path.GetFileName(a), Path.GetFileName(b)));
                    var active = versions.FirstOrDefault(p => Path.GetFileName(p) == "local") ?? versions.LastOrDefault();
                    if (active == null || !HasManifest(active)) continue;
                    foreach (var skill in Children(Path.Combine(active, "skills")))
                        if (SkillName.IsSafe(Path.GetFileName(skill)) && File.Exists(Path.Combine(skill, UserScopeLocationSource.SkillFileName)))
                            copies.Add(new UserScopeCopy(folder, Path.GetFileName(skill), skill,
                                "Codex plugin " + entry.Key, "Codex", entry.Key));
                }
                return copies;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException ||
                                      e is ArgumentException || e is NotSupportedException)
            {
                return Array.Empty<UserScopeCopy>();
            }
        }

        static bool Segment(string value) => value != "." && value != ".." &&
            Regex.IsMatch(value, @"\A[a-zA-Z0-9_.+\-]+\z");

        static IEnumerable<string> Children(string path)
        {
            try { return Directory.Exists(path) ? Directory.GetDirectories(path) : Array.Empty<string>(); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return Array.Empty<string>(); }
        }

        static bool HasManifest(string root)
        {
            var path = Path.Combine(root, "plugin.json");
            if (!File.Exists(path)) path = Path.Combine(root, ".codex-plugin", "plugin.json");
            try { return JsonReader.Read(File.ReadAllText(path)) is List<KeyValuePair<string, object>>; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException) { return false; }
        }

        // Codex compares semver::Version when both names parse, otherwise the original strings.
        static int CompareVersions(string left, string right)
        {
            const string semver = @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z";
            var a = Regex.Match(left, semver);
            var b = Regex.Match(right, semver);
            if (!ValidSemver(a) || !ValidSemver(b)) return string.CompareOrdinal(left, right);
            for (var i = 1; i <= 3; i++)
            {
                var compared = CompareNumber(a.Groups[i].Value, b.Groups[i].Value);
                if (compared != 0) return compared;
            }
            var pre = CompareIdentifiers(a.Groups[4].Value, b.Groups[4].Value, true);
            return pre != 0 ? pre : CompareIdentifiers(a.Groups[5].Value, b.Groups[5].Value, false);
        }

        static bool ValidSemver(Match value) => value.Success && value.Groups[4].Value.Split('.')
            .All(part => !Numeric(part) || part.Length == 1 || part[0] != '0');

        static bool Numeric(string value) => value.Length > 0 && value.All(c => c >= '0' && c <= '9');
        static int CompareNumber(string a, string b) => a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);

        static int CompareIdentifiers(string a, string b, bool prerelease)
        {
            if (a == b) return 0;
            if (a.Length == 0 || b.Length == 0)
                return (a.Length == 0 ? -1 : 1) * (prerelease ? -1 : 1);
            var left = a.Split('.');
            var right = b.Split('.');
            for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
            {
                var an = Numeric(left[i]);
                var bn = Numeric(right[i]);
                var compared = an && bn ? CompareNumber(left[i].TrimStart('0'), right[i].TrimStart('0'))
                    : an != bn ? (an ? -1 : 1) : string.CompareOrdinal(left[i], right[i]);
                if (compared != 0) return compared;
                if (!prerelease && left[i] != right[i]) return string.CompareOrdinal(left[i], right[i]);
            }
            return left.Length.CompareTo(right.Length);
        }
    }
}
