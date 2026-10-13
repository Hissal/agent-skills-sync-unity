using System.Linq;
using Hissal.AgentSkillsSync.Editor;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>What the sync window says about a skill this machine opted out of.</summary>
    public class SyncTextOptOutTests
    {
        static readonly FolderLayout Layout = FolderLayout.Default;

        static InstallPlan PlanWithTddOptedOut(params string[] installed)
        {
            var lockfile = new Lockfile(new[] { new LockedSkill("tdd", "owner/repo", "github", "skills/tdd/SKILL.md", "hash") });
            var project = new ProjectState(Layout.Folders.Select(f => new FolderState(f, installed, installed)));
            return InstallPlanner.Plan(lockfile, project, new MachineChoices(Layout, skills: new SkillSelection(optedOut: new[] { "tdd" })));
        }

        [Test]
        public void OptedOutLabel_NothingInstalled_SaysNotInstalled()
        {
            Assert.That(SyncText.OptedOutLabel(PlanWithTddOptedOut(), "tdd"), Is.EqualTo("not installed (opted out)"));
        }

        [Test]
        public void OptedOutLabel_ManagedCopyStillThere_SaysSyncRemovesIt()
        {
            Assert.That(SyncText.OptedOutLabel(PlanWithTddOptedOut("tdd"), "tdd"), Is.EqualTo("opted out (removed on Sync)"));
        }
    }
}
