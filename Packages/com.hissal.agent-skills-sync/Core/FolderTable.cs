namespace Hissal.AgentSkillsSync
{
    public sealed partial class FolderLayout
    {
        /// <summary>
        /// The folder table: every skills folder a contributor can choose to install into. Data only; supporting another
        /// folder or agent means adding an entry here. User-scope locations come from <c>docs/skills-cli-findings.md</c> §3:
        /// each agent's own home only (cross-reads of another entry's home, such as Cursor reading
        /// <c>~/.claude/skills</c>, are left out so that home does not pre-select this entry).
        /// </summary>
        /// <remarks>
        /// <c>.agents/skills</c> holds the canonical copies; every other selected folder links to them.
        /// Admin and enterprise locations (<c>/etc/codex/skills</c>, managed settings) are not user scope and not listed.
        /// </remarks>
        public static FolderLayout Default { get; } = new FolderLayout(new[]
        {
            new SkillsFolder("agents", ".agents/skills", SkillsFolderRole.Canonical,
                "Codex, Cursor, GitHub Copilot, Gemini CLI, OpenCode, Amp, Cline, Kilo Code, Droid, Antigravity, Deep Agents, " +
                "Firebender, Kimi Code, Warp, Zed and other agents reading .agents/skills",
                new[]
                {
                    new UserScopeLocation("Codex, Cursor, GitHub Copilot, Gemini CLI, OpenCode, Amp, Cline, Kilo Code, Droid, " +
                                          "Deep Agents, Firebender, Kimi Code, Warp, Zed, Dexto, Loaf, Sarvam Code", "~/.agents"),
                    new UserScopeLocation("Gemini CLI", "~/.agents", envVar: "GEMINI_CLI_HOME", envReplaces: "~"),
                    new UserScopeLocation("Codex, Cursor, Warp, Firebender", "~/.codex", envVar: "CODEX_HOME"),
                    new UserScopeLocation("Cursor, Warp, Firebender", "~/.cursor"),
                    new UserScopeLocation("GitHub Copilot, Warp", "~/.copilot", envVar: "COPILOT_HOME"),
                    new UserScopeLocation("Gemini CLI, Antigravity CLI, Warp", "~/.gemini", envVar: "GEMINI_CLI_HOME", envReplaces: "~"),
                    new UserScopeLocation("Antigravity", "~/.gemini", "config/skills"),
                    new UserScopeLocation("Antigravity (legacy)", "~/.gemini", "antigravity/skills"),
                    new UserScopeLocation("Antigravity CLI", "~/.gemini", "antigravity-cli/skills"),
                    new UserScopeLocation("OpenCode", "~/.config/opencode", envVar: "XDG_CONFIG_HOME", envReplaces: "~/.config"),
                    new UserScopeLocation("Amp, skills CLI (-g)", "~/.config/agents", envVar: "XDG_CONFIG_HOME", envReplaces: "~/.config"),
                    new UserScopeLocation("Amp", "~/.config/amp", envVar: "XDG_CONFIG_HOME", envReplaces: "~/.config"),
                    new UserScopeLocation("Cline", "~/.cline", envVar: "CLINE_DIR"),
                    new UserScopeLocation("Kilo Code", "~/.kilo"),
                    new UserScopeLocation("Droid, Warp", "~/.factory"),
                    new UserScopeLocation("Droid", "~/.agent"),
                    new UserScopeLocation("Deep Agents", "~/.deepagents", "agent/skills", envVar: "DEEPAGENTS_HOME"),
                    new UserScopeLocation("Firebender", "~/.firebender"),
                    new UserScopeLocation("Kimi Code", "~/.kimi-code", envVar: "KIMI_CODE_HOME"),
                    new UserScopeLocation("Warp", "~/.warp"),
                }),
            new SkillsFolder("claude", ".claude/skills", SkillsFolderRole.Link, "Claude Code",
                new[]
                {
                    new UserScopeLocation("Claude Code", "~/.claude", envVar: "CLAUDE_CONFIG_DIR"),
                }),
        });
    }
}
