using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>
    /// The planner warns when a skipped skill's user-scope copy differs from the lock, using real fixture folders as
    /// the copies. It reports only a verifiable difference, never one it can't judge.
    /// </summary>
    public class UserScopeDiffersTests
    {
        static readonly string FixturesRoot =
            Path.GetFullPath("Packages/com.hissal.agent-skills-sync/Tests/Editor/Fixtures~/SkillFolderHash");

        const string AgentsPath = ".agents/skills";
        const string ClaudePath = ".claude/skills";

        static FolderLayout Table => FolderLayout.Default;

        string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown() => TempDirectory.Delete(_root);

        static Lockfile Lock(string hash, string source = "owner/repo") =>
            new Lockfile(new[] { new LockedSkill("tdd", source, "github", "skills/tdd/SKILL.md", hash) });

        /// <summary>A user-scope copy of the <c>minimal</c> fixture, seen by the agents reading <paramref name="folder"/>.</summary>
        UserScopeCopy MinimalCopy(string folder, string foundIn = "~/.claude/skills")
        {
            var path = Path.Combine(_root, "home", Guid.NewGuid().ToString("N"), "tdd");
            Paths.CopyDirectory(Path.Combine(FixturesRoot, "minimal"), path);
            return new UserScopeCopy(Table.Find(folder), "tdd", path, foundIn);
        }

        static void Edit(UserScopeCopy copy) => File.AppendAllText(Path.Combine(copy.Path, "SKILL.md"), "\nmy own tweak\n");

        static SkipChoices Skip(params string[] folders) =>
            new SkipChoices(folders.ToDictionary(f => f, f => (IReadOnlyList<string>)new[] { "tdd" }));

        static InstallPlan Plan(Lockfile lockfile, SkipChoices skips, params UserScopeCopy[] copies) =>
            InstallPlanner.Plan(lockfile, ProjectState.Empty, Table, userScope: new UserScopeState(copies), skips: skips);

        static IEnumerable<string> Describe(InstallPlan plan) =>
            plan.Actions.Select(a => $"{a.Kind} {a.Folder.RelativePath}/{a.SkillName}");

        static IEnumerable<PlanAction> Warnings(InstallPlan plan) =>
            plan.Actions.Where(a => a.Kind == PlanActionKind.WarnUserScopeDiffers);

        [Test]
        public void Plan_SkippedCopyMatchesTheLock_NoWarning()
        {
            var plan = Plan(Lock(FakeGitHub.MinimalHash), Skip(ClaudePath), MinimalCopy(ClaudePath));

            Assert.That(Describe(plan), Is.EqualTo(new[] { "Install .agents/skills/tdd", "SkipUserScope .claude/skills/tdd" }));
        }

        [Test]
        public void Plan_SkippedCopyChanged_WarnsRightAfterTheSkip()
        {
            var copy = MinimalCopy(ClaudePath);
            Edit(copy);

            var plan = Plan(Lock(FakeGitHub.MinimalHash), Skip(ClaudePath), copy);

            Assert.That(Describe(plan), Is.EqualTo(new[]
            {
                "Install .agents/skills/tdd", "SkipUserScope .claude/skills/tdd", "WarnUserScopeDiffers .claude/skills/tdd",
            }));
            var warning = Warnings(plan).Single();
            Assert.That(warning.UserScopeCopies, Is.EqualTo(new[] { copy }));
            Assert.That(warning.Skill.Name, Is.EqualTo("tdd"));
            Assert.That(warning.ChangesProject, Is.False);
            Assert.That(plan.HasChanges, Is.True, "the Install still changes the project");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Plan_LockHasNoHash_NoWarning(string hash)
        {
            // Nothing to compare against, so the copy can't be judged to differ.
            var copy = MinimalCopy(ClaudePath);
            Edit(copy);

            var plan = Plan(Lock(hash), Skip(ClaudePath), copy);

            Assert.That(Warnings(plan), Is.Empty);
        }

        [Test]
        public void Plan_ChangedCopyNotSkipped_NoWarning()
        {
            // The project copy is installed there, so the agents run what teammates run.
            var copy = MinimalCopy(ClaudePath);
            Edit(copy);

            var plan = Plan(Lock(FakeGitHub.MinimalHash), SkipChoices.None, copy);

            Assert.That(Warnings(plan), Is.Empty);
        }

        [Test]
        public void Plan_TwoCopiesOneChanged_WarnsNamingOnlyTheChangedOne()
        {
            var same = MinimalCopy(ClaudePath, "~/.claude/skills");
            var changed = MinimalCopy(ClaudePath, "plugin unity@claude-plugins");
            Edit(changed);

            var plan = Plan(Lock(FakeGitHub.MinimalHash), Skip(ClaudePath), same, changed);

            Assert.That(Warnings(plan).Single().UserScopeCopies.Select(c => c.FoundIn), Is.EqualTo(new[] { "plugin unity@claude-plugins" }));
        }

        [Test]
        public void Plan_BothFoldersSkippedWithChangedCopies_WarnsPerFolder()
        {
            var agents = MinimalCopy(AgentsPath, "~/.codex/skills");
            var claude = MinimalCopy(ClaudePath);
            Edit(agents);
            Edit(claude);

            var plan = Plan(Lock(FakeGitHub.MinimalHash), Skip(AgentsPath, ClaudePath), agents, claude);

            Assert.That(Describe(plan), Is.EqualTo(new[]
            {
                "SkipUserScope .claude/skills/tdd", "WarnUserScopeDiffers .claude/skills/tdd",
                "SkipUserScope .agents/skills/tdd", "WarnUserScopeDiffers .agents/skills/tdd",
            }));
        }

        [Test]
        public void Plan_SkillsShHashedSource_NoWarning()
        {
            // A skills.sh server hash is a different algorithm: a mismatch says nothing about the content.
            var plan = Plan(Lock(FakeGitHub.MinimalHash, "vercel-labs/agent-skills"), Skip(ClaudePath), MinimalCopy(ClaudePath));

            Assert.That(Warnings(plan), Is.Empty);
        }

        [Test]
        public void Plan_LockedFromACrlfCheckout_LfCopy_NoWarning()
        {
            // The CLI hashed a core.autocrlf=true checkout; the copy holds the same text with LF line endings.
            var crlf = Path.Combine(_root, "crlf");
            Directory.CreateDirectory(crlf);
            var lf = File.ReadAllText(Path.Combine(FixturesRoot, "minimal", "SKILL.md"));
            File.WriteAllText(Path.Combine(crlf, "SKILL.md"), lf.Replace("\n", "\r\n"), new UTF8Encoding(false));
            var crlfHash = SkillFolderHash.Compute(crlf);
            Assume.That(crlfHash, Is.Not.EqualTo(FakeGitHub.MinimalHash));

            var plan = Plan(Lock(crlfHash), Skip(ClaudePath), MinimalCopy(ClaudePath));

            Assert.That(Warnings(plan), Is.Empty);
        }

        [Test]
        public void Plan_ChangedCopyWithANonAsciiPath_NoWarning()
        {
            // Path order (and so the hash) is not exact for non-ASCII names, so the mismatch can't be judged.
            var copy = MinimalCopy(ClaudePath);
            File.WriteAllText(Path.Combine(copy.Path, "café.md"), "x");

            var plan = Plan(Lock(FakeGitHub.MinimalHash), Skip(ClaudePath), copy);

            Assert.That(Warnings(plan), Is.Empty);
        }

        [Test]
        public void Plan_CopyFolderGoneSinceTheScan_NoWarning()
        {
            var copy = MinimalCopy(ClaudePath);
            TempDirectory.Delete(copy.Path);

            var plan = Plan(Lock(FakeGitHub.MinimalHash), Skip(ClaudePath), copy);

            Assert.That(Describe(plan), Is.EqualTo(new[] { "Install .agents/skills/tdd", "SkipUserScope .claude/skills/tdd" }));
        }

        [Test]
        public void Summary_ListsTheWarningPerSkillAndFolder()
        {
            var copy = MinimalCopy(ClaudePath);
            Edit(copy);
            var plan = Plan(Lock(FakeGitHub.MinimalHash), Skip(ClaudePath), copy);

            var summary = new SyncSummary(plan.Actions);

            Assert.That(summary.UserScopeDiffers.Select(a => $"{a.Folder.RelativePath}/{a.SkillName}"),
                Is.EqualTo(new[] { ".claude/skills/tdd" }));
            Assert.That(new SyncSummary(Plan(Lock(FakeGitHub.MinimalHash), Skip(ClaudePath), MinimalCopy(ClaudePath)).Actions)
                .UserScopeDiffers, Is.Empty);
        }
    }
}
