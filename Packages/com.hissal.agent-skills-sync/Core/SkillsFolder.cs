using System;
using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>What the tool puts into a skills folder.</summary>
    public enum SkillsFolderRole
    {
        /// <summary>Holds the canonical copy of each skill.</summary>
        Canonical,

        /// <summary>Holds a link per skill, pointing at the canonical copy.</summary>
        Link,
    }

    /// <summary>
    /// One entry of the folder table: a project-relative folder a set of agents reads skills from, e.g.
    /// <c>.claude/skills</c>, with every user-scope folder those agents read.
    /// </summary>
    public sealed class SkillsFolder
    {
        /// <param name="label">The agents that read the folder, for display; defaults to <paramref name="agent"/>.</param>
        /// <param name="userScopeLocations">Every user-scope folder any of those agents reads (see <see cref="UserScopeLocations"/>).</param>
        public SkillsFolder(string agent, string relativePath, SkillsFolderRole role, string label = null,
            IEnumerable<UserScopeLocation> userScopeLocations = null)
        {
            Agent = agent;
            RelativePath = relativePath;
            Role = role;
            Label = label ?? agent;
            UserScopeLocations = (userScopeLocations ?? Enumerable.Empty<UserScopeLocation>()).ToList();
        }

        /// <summary>Short agent-family id, e.g. <c>agents</c> or <c>claude</c>.</summary>
        public string Agent { get; }

        /// <summary>Path from the project root, with forward slashes.</summary>
        public string RelativePath { get; }

        public SkillsFolderRole Role { get; }

        /// <summary>The agents that read this folder, for display, e.g. <c>Claude Code</c>.</summary>
        public string Label { get; }

        /// <summary>
        /// Every user-scope skills folder an agent reading this project folder reads from its own home (e.g.
        /// <c>~/.codex/skills</c> for Codex), in table order. Locations that agents read only as a cross-read of
        /// another table entry's home (Cursor reading <c>~/.claude/skills</c>) are not listed here.
        /// </summary>
        public IReadOnlyList<UserScopeLocation> UserScopeLocations { get; }

        /// <summary>The resolved agent homes of <see cref="UserScopeLocations"/>, distinct, in table order.</summary>
        public IReadOnlyList<string> UserScopeHomes(UserEnvironment environment) =>
            Distinct(UserScopeLocations.Select(l => l.ResolveHome(environment)));

        /// <summary>The resolved user-scope skills folders of <see cref="UserScopeLocations"/>, distinct, in table order.</summary>
        public IReadOnlyList<string> UserScopeSkillsFolders(UserEnvironment environment) =>
            Distinct(UserScopeLocations.Select(l => l.ResolveSkillsFolder(environment)));

        static IReadOnlyList<string> Distinct(IEnumerable<string> paths) =>
            paths.Distinct(Paths.Comparer).ToList();

        public override string ToString() => RelativePath;
    }

    /// <summary>
    /// The skills folders the tool keeps in sync: exactly one canonical folder plus any number of link folders.
    /// Everything downstream iterates this table rather than naming folders.
    /// </summary>
    public sealed partial class FolderLayout
    {
        public FolderLayout(IEnumerable<SkillsFolder> folders)
        {
            Folders = folders.ToList();
            var canonical = Folders.Where(f => f.Role == SkillsFolderRole.Canonical).ToList();
            if (canonical.Count != 1)
                throw new ArgumentException("A folder layout needs exactly one canonical folder.", nameof(folders));
            Canonical = canonical[0];
        }

        /// <summary>Every folder, canonical first as declared.</summary>
        public IReadOnlyList<SkillsFolder> Folders { get; }

        public SkillsFolder Canonical { get; }

        /// <summary>The folder with this <see cref="SkillsFolder.RelativePath"/>, or null.</summary>
        public SkillsFolder Find(string relativePath) => Folders.FirstOrDefault(f => f.RelativePath == relativePath);
    }
}
