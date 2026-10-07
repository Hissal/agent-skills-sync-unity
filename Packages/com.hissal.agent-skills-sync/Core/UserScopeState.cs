using System;
using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// A skill the contributor already has at user scope, outside the project, that an agent reading a project skills
    /// folder also sees: a duplicate of the project copy in that folder.
    /// </summary>
    public sealed class UserScopeCopy
    {
        /// <param name="folder">The project skills folder whose agents see this copy.</param>
        /// <param name="skillName">The skill name it duplicates.</param>
        /// <param name="path">Absolute path of the copy's folder.</param>
        /// <param name="foundIn">Where it was found, for display (e.g. <c>~/.codex/skills</c>, or a plugin's name).</param>
        /// <param name="agents">Who reads it there, for display.</param>
        /// <param name="plugin">The Claude Code plugin id providing it (<c>unity@unity-agent-plugin</c>); null when not from a plugin.</param>
        public UserScopeCopy(SkillsFolder folder, string skillName, string path, string foundIn, string agents = null, string plugin = null)
        {
            Folder = folder ?? throw new ArgumentNullException(nameof(folder));
            SkillName = skillName ?? throw new ArgumentNullException(nameof(skillName));
            Path = path;
            FoundIn = foundIn;
            Agents = agents;
            Plugin = plugin;
        }

        /// <summary>The Claude Code plugin id (<c>name@marketplace</c>) providing this copy, or null when it is not from a plugin.</summary>
        public string Plugin { get; }

        /// <summary>The project skills folder whose agents see this copy.</summary>
        public SkillsFolder Folder { get; }

        /// <summary>The skill name this copy duplicates.</summary>
        public string SkillName { get; }

        /// <summary>Absolute path of the copy's folder, e.g. to hash it.</summary>
        public string Path { get; }

        /// <summary>
        /// Where it was found, for display: a table location as written (<c>~/.codex/skills</c>), its resolved folder
        /// when an environment variable moved it, or whatever another <see cref="IUserScopeSource"/> names (a plugin).
        /// </summary>
        public string FoundIn { get; }

        /// <summary>Who reads the copy there, for display; may be null.</summary>
        public string Agents { get; }

        public override string ToString() => $"{Folder.RelativePath}/{SkillName} at {FoundIn}";
    }

    /// <summary>What the <see cref="UserScopeScanner"/> found: every user-scope copy, per project skills folder and skill.</summary>
    public sealed class UserScopeState
    {
        readonly Dictionary<string, List<UserScopeCopy>> _byKey = new Dictionary<string, List<UserScopeCopy>>(StringComparer.Ordinal);

        public UserScopeState(IEnumerable<UserScopeCopy> copies)
        {
            Copies = (copies ?? Enumerable.Empty<UserScopeCopy>()).ToList();
            foreach (var copy in Copies)
            {
                var key = Key(copy.Folder, copy.SkillName);
                if (!_byKey.TryGetValue(key, out var list)) _byKey[key] = list = new List<UserScopeCopy>();
                list.Add(copy);
            }
        }

        /// <summary>Nothing found at user scope.</summary>
        public static UserScopeState Empty { get; } = new UserScopeState(null);

        /// <summary>Every copy found, in scan order (folders as given, then sources, then locations in table order).</summary>
        public IReadOnlyList<UserScopeCopy> Copies { get; }

        /// <summary>The copies of <paramref name="skillName"/> the agents reading <paramref name="folder"/> see; empty when none.</summary>
        public IReadOnlyList<UserScopeCopy> CopiesOf(SkillsFolder folder, string skillName) =>
            _byKey.TryGetValue(Key(folder, skillName), out var list) ? list : (IReadOnlyList<UserScopeCopy>)Array.Empty<UserScopeCopy>();

        /// <summary>Whether the agents reading <paramref name="folder"/> already have <paramref name="skillName"/> at user scope.</summary>
        public bool Has(SkillsFolder folder, string skillName) => _byKey.ContainsKey(Key(folder, skillName));

        static string Key(SkillsFolder folder, string skillName) => folder.RelativePath + "\n" + skillName;
    }
}
