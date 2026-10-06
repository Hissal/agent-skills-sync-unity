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

    /// <summary>A project-relative folder one agent family reads skills from, e.g. <c>.claude/skills</c>.</summary>
    public sealed class SkillsFolder
    {
        public SkillsFolder(string agent, string relativePath, SkillsFolderRole role)
        {
            Agent = agent;
            RelativePath = relativePath;
            Role = role;
        }

        /// <summary>Short agent-family id, e.g. <c>agents</c> or <c>claude</c>.</summary>
        public string Agent { get; }

        /// <summary>Path from the project root, with forward slashes.</summary>
        public string RelativePath { get; }

        public SkillsFolderRole Role { get; }

        public override string ToString() => RelativePath;
    }

    /// <summary>
    /// The skills folders the tool keeps in sync: exactly one canonical folder plus any number of link folders.
    /// Everything downstream iterates this table rather than naming folders.
    /// </summary>
    public sealed class FolderLayout
    {
        public FolderLayout(IEnumerable<SkillsFolder> folders)
        {
            Folders = folders.ToList();
            var canonical = Folders.Where(f => f.Role == SkillsFolderRole.Canonical).ToList();
            if (canonical.Count != 1)
                throw new ArgumentException("A folder layout needs exactly one canonical folder.", nameof(folders));
            Canonical = canonical[0];
        }

        /// <summary><c>.agents/skills</c> holds the copies; <c>.claude/skills</c> links to them.</summary>
        public static FolderLayout Default { get; } = new FolderLayout(new[]
        {
            new SkillsFolder("agents", ".agents/skills", SkillsFolderRole.Canonical),
            new SkillsFolder("claude", ".claude/skills", SkillsFolderRole.Link),
        });

        /// <summary>Every folder, canonical first as declared.</summary>
        public IReadOnlyList<SkillsFolder> Folders { get; }

        public SkillsFolder Canonical { get; }
    }
}
