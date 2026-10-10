using System.Linq;
using Hissal.AgentSkillsSync.Editor;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class SkipCoverageTests
    {
        [Test]
        public void CodexPlugin_LeavesOtherSharedFolderAgentsWithoutTheSkill()
        {
            var folder = FolderLayout.Default.Find(".agents/skills");
            var copy = new UserScopeCopy(folder, "ui", "/unused", "Codex plugin unity@market", "Codex", "unity@market");
            var message = SyncText.SkipCoverageMessage(folder, new[] { copy });
            Assert.That(message, Does.Contain("Codex has it (plugin unity@market)"));
            Assert.That(message, Does.Contain("Cursor, GitHub Copilot, Gemini CLI, OpenCode"));
            Assert.That(message, Does.Contain("would not have it"));
        }

        [Test]
        public void PlainCodexHomeCopy_CoversOnlyItsLocationAgents()
        {
            var folder = FolderLayout.Default.Find(".agents/skills");
            var copy = new UserScopeCopy(folder, "ui", "/unused", "~/.codex/skills", "Codex, Cursor, Warp, Firebender");
            var message = SyncText.SkipCoverageMessage(folder, new[] { copy });
            Assert.That(message, Does.Contain("Codex, Cursor, Warp, Firebender have it"));
            Assert.That(message, Does.Contain("GitHub Copilot, Gemini CLI, OpenCode"));
            Assert.That(message, Does.Not.Contain("Cursor, GitHub Copilot"));
        }

        [Test]
        public void AllLocationAgentsCovered_NoWarning()
        {
            var folder = FolderLayout.Default.Find(".agents/skills");
            var copies = folder.UserScopeLocations.Select(l => new UserScopeCopy(folder, "ui", "/unused", l.ToString(), l.Agents)).ToList();
            Assert.That(SyncText.SkipCoverageMessage(folder, copies), Is.Empty);
        }

        [Test]
        public void ClaudePluginNamespace_DoesNotProduceASharedFolderWarning()
        {
            var folder = FolderLayout.Default.Find(".claude/skills");
            var copy = new UserScopeCopy(folder, "ui", "/unused", "plugin unity@market", "Claude Code, as /unity:ui", "unity@market");
            Assert.That(SyncText.SkipCoverageMessage(folder, new[] { copy }), Is.Empty);
        }
    }
}
