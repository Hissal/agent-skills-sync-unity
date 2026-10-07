using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class LinkFallbackTests
    {
        static readonly string FixturesRoot =
            Path.GetFullPath("Packages/com.hissal.agent-skills-sync/Tests/Editor/Fixtures~/SkillFolderHash");

        // The fetched skill is the `minimal` hash fixture, locked at its real hash so re-runs plan no Update.
        static readonly string Fetched = Path.Combine(FixturesRoot, "minimal");

        static readonly string FetchedSkillMd = File.ReadAllText(Path.Combine(Fetched, "SKILL.md"));

        string _root;
        string _project;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            _project = Path.Combine(_root, "project");
            Directory.CreateDirectory(_project);
        }

        [TearDown]
        public void TearDown()
        {
            TempDirectory.Delete(_root);
        }

        sealed class FailingLinker : ILinkCreator
        {
            public LinkMethod CreateDirectoryLink(string linkPath, string targetPath) =>
                throw new IOException("forced failure");
        }

        sealed class RecordingLinker : ILinkCreator
        {
            readonly List<string> _calls;
            readonly string _name;
            readonly LinkMethod? _result;

            public RecordingLinker(List<string> calls, string name, LinkMethod? result)
            {
                _calls = calls;
                _name = name;
                _result = result;
            }

            public LinkMethod CreateDirectoryLink(string linkPath, string targetPath)
            {
                _calls.Add(_name);
                if (_result == null) throw new IOException(_name + " failed");
                return _result.Value;
            }
        }

        static ILinkCreator ForceJunction() =>
            new FallbackLinkCreator(new FailingLinker(), new JunctionCreator(), new CopyLinkCreator());

        static ILinkCreator ForceCopy() =>
            new FallbackLinkCreator(new FailingLinker(), new FailingLinker(), new CopyLinkCreator());

        static Lockfile LockTdd(string hash) => new Lockfile(new[]
        {
            new LockedSkill("tdd", "owner/repo", "github", "skills/tdd/SKILL.md", hash),
        });

        InstallPlan PlanNow(string hash) =>
            InstallPlanner.Plan(LockTdd(hash), ProjectScanner.Scan(_project, FolderLayout.Default), FolderLayout.Default);

        SyncSummary Sync(ILinkCreator linker) =>
            new PlanExecutor(linker).Execute(_project, PlanNow(FakeGitHub.MinimalHash), new Dictionary<string, string> { ["tdd"] = Fetched });

        /// <summary>Re-locks tdd at the `nested` fixture and syncs, so the canonical copy is updated.</summary>
        SyncSummary SyncUpdatedToNested(ILinkCreator linker) =>
            new PlanExecutor(linker).Execute(_project, PlanNow(FakeGitHub.NestedHash),
                new Dictionary<string, string> { ["tdd"] = Path.Combine(FixturesRoot, "nested") });

        string InProject(string relativePath) => Path.Combine(_project, relativePath);

        string Link => InProject(".claude/skills/tdd");

        string Canonical => InProject(".agents/skills/tdd");

        static void RequireWindows()
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("Junctions exist only on Windows.");
        }

        static bool IsReparsePoint(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

        IEnumerable<string> ManagedLines(string folder) =>
            File.ReadAllLines(InProject(folder + "/.gitignore")).Where(l => l.Length > 0 && !l.StartsWith("#"));

        [Test]
        public void FallbackLinkCreator_TriesEachMethodInOrderUntilOneSucceeds()
        {
            var calls = new List<string>();
            var linker = new FallbackLinkCreator(
                new RecordingLinker(calls, "symlink", null),
                new RecordingLinker(calls, "junction", LinkMethod.Junction),
                new RecordingLinker(calls, "copy", LinkMethod.Copy));

            var method = linker.CreateDirectoryLink("link", "target");

            Assert.That(method, Is.EqualTo(LinkMethod.Junction));
            Assert.That(calls, Is.EqualTo(new[] { "symlink", "junction" }));
        }

        [Test]
        public void FallbackLinkCreator_AllMethodsFail_Throws()
        {
            var linker = new FallbackLinkCreator(new FailingLinker(), new FailingLinker());

            Assert.Throws<IOException>(() => linker.CreateDirectoryLink("link", "target"));
        }

        [Test]
        public void Execute_DefaultLinker_LinksWithoutFallbackWhereSymlinksWork()
        {
            var summary = Sync(null);

            Assert.That(IsReparsePoint(Link), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(Link, "SKILL.md")), Is.EqualTo(FetchedSkillMd));
            Assert.That(summary.LinkMethods.Values, Has.All.EqualTo(LinkMethod.Symlink).Or.All.EqualTo(LinkMethod.Junction));
        }

        [Test]
        public void Execute_SymlinkFails_FallsBackToJunctionThatShowsTheCanonicalCopy()
        {
            RequireWindows();

            var summary = Sync(ForceJunction());

            Assert.That(IsReparsePoint(Link), Is.True, "expected a junction, not a copy");
            File.WriteAllText(Path.Combine(Canonical, "SKILL.md"), "# edited");
            Assert.That(File.ReadAllText(Path.Combine(Link, "SKILL.md")), Is.EqualTo("# edited"));
            Assert.That(summary.LinkMethods.Values, Is.EqualTo(new[] { LinkMethod.Junction }));
            Assert.That(ManagedLines(".claude/skills"), Is.EqualTo(new[] { "/tdd" }));
        }

        [Test]
        public void Execute_SymlinkAndJunctionFail_FallsBackToCopy()
        {
            var summary = Sync(ForceCopy());

            Assert.That(IsReparsePoint(Link), Is.False, "expected a plain copy");
            Assert.That(File.ReadAllText(Path.Combine(Link, "SKILL.md")), Is.EqualTo(FetchedSkillMd));
            Assert.That(summary.LinkMethods.Values, Is.EqualTo(new[] { LinkMethod.Copy }));
            Assert.That(ManagedLines(".claude/skills"), Is.EqualTo(new[] { "/tdd" }));
        }

        [Test]
        public void Execute_SymlinkSucceeds_SummaryRecordsSymlink()
        {
            var calls = new List<string>();
            var summary = Sync(new RecordingLinker(calls, "symlink", LinkMethod.Symlink));

            var link = summary.Applied.Single(a => a.Kind == PlanActionKind.Link);
            Assert.That(summary.LinkMethods[link], Is.EqualTo(LinkMethod.Symlink));
        }

        [Test]
        public void Rerun_OverJunction_IsNoOp()
        {
            RequireWindows();
            Sync(ForceJunction());

            var summary = Sync(ForceJunction());

            Assert.That(summary.NothingChanged, Is.True);
            Assert.That(ManagedLines(".claude/skills"), Is.EqualTo(new[] { "/tdd" }));
        }

        [Test]
        public void Rerun_OverCopy_IsNoOp()
        {
            Sync(ForceCopy());

            var summary = Sync(ForceCopy());

            Assert.That(summary.NothingChanged, Is.True);
            Assert.That(ManagedLines(".claude/skills"), Is.EqualTo(new[] { "/tdd" }));
        }

        /// <summary>Points the managed .claude/skills/tdd link at a copy of the `nested` fixture outside the project.</summary>
        string RepointLinkElsewhere(ILinkCreator linker)
        {
            var elsewhere = Path.Combine(_root, "elsewhere", "tdd");
            Paths.CopyDirectory(Path.Combine(FixturesRoot, "nested"), elsewhere);
            DirectoryLink.Remove(Link);
            linker.CreateDirectoryLink(Link, elsewhere);
            return elsewhere;
        }

        void AssertLinkShowsTheCanonicalCopy()
        {
            File.WriteAllText(Path.Combine(Canonical, "SKILL.md"), "# edited");
            Assert.That(File.ReadAllText(Path.Combine(Link, "SKILL.md")), Is.EqualTo("# edited"));
        }

        [Test]
        public void Rerun_JunctionPointsAtAnotherFolder_RelinksItToTheCanonicalCopy()
        {
            RequireWindows();
            Sync(ForceJunction());
            var elsewhere = RepointLinkElsewhere(new JunctionCreator());

            var plan = PlanNow(FakeGitHub.MinimalHash);
            Sync(ForceJunction());

            Assert.That(plan.Actions.Select(a => (a.Kind, a.SkillName, a.Folder.RelativePath)),
                Is.EqualTo(new[] { (PlanActionKind.Link, "tdd", ".claude/skills") }));
            Assert.That(Sync(ForceJunction()).NothingChanged, Is.True, "a re-linked junction is current");
            Assert.That(SkillFolderHash.Compute(elsewhere), Is.EqualTo(FakeGitHub.NestedHash), "the other folder is left alone");
            AssertLinkShowsTheCanonicalCopy();
        }

        [Test]
        public void Rerun_SymlinkPointsAtAnotherFolder_RelinksItToTheCanonicalCopy()
        {
            try { Sync(new SymlinkCreator()); }
            catch (IOException e) { Assert.Ignore("This machine cannot create symlinks: " + e.Message); }
            RepointLinkElsewhere(new SymlinkCreator());

            var plan = PlanNow(FakeGitHub.MinimalHash);
            Sync(new SymlinkCreator());

            Assert.That(plan.Actions.Select(a => (a.Kind, a.SkillName)), Is.EqualTo(new[] { (PlanActionKind.Link, "tdd") }));
            AssertLinkShowsTheCanonicalCopy();
        }

        [Test]
        public void Rerun_LinkIsBroken_RelinksIt()
        {
            RequireWindows();
            Sync(ForceJunction());
            var elsewhere = RepointLinkElsewhere(new JunctionCreator());
            Directory.Delete(elsewhere, recursive: true);

            var plan = PlanNow(FakeGitHub.MinimalHash);
            Sync(ForceJunction());

            Assert.That(plan.Actions.Select(a => (a.Kind, a.SkillName)), Is.EqualTo(new[] { (PlanActionKind.Link, "tdd") }));
            AssertLinkShowsTheCanonicalCopy();
        }

        [Test]
        public void Rerun_OverSymlink_IsNoOp()
        {
            try { Sync(new SymlinkCreator()); }
            catch (IOException e) { Assert.Ignore("This machine cannot create symlinks: " + e.Message); }

            Assert.That(Sync(new SymlinkCreator()).NothingChanged, Is.True);
        }

        [Test]
        public void Update_OverCopy_RefreshesTheCopiedLink()
        {
            Sync(ForceCopy());

            var summary = SyncUpdatedToNested(ForceCopy());

            Assert.That(summary.Updated, Is.EqualTo(new[] { "tdd" }));
            Assert.That(IsReparsePoint(Link), Is.False, "expected the link to stay a plain copy");
            Assert.That(SkillFolderHash.Compute(Link), Is.EqualTo(FakeGitHub.NestedHash));
            Assert.That(SyncUpdatedToNested(ForceCopy()).NothingChanged, Is.True);
        }

        [Test]
        public void Update_OverCopy_RefreshFailsPartway_NextSyncRelinksTheSkill()
        {
            Sync(ForceCopy());
            // Copies into the link folder write one file and then fail, like a full disk; other copies work.
            var failingRefresh = new PlanExecutor(ForceCopy(), (source, destination) =>
            {
                var inLinkFolder = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(destination))) == ".claude";
                if (!inLinkFolder)
                {
                    Paths.CopyDirectory(source, destination);
                    return;
                }
                Directory.CreateDirectory(destination);
                File.WriteAllText(Path.Combine(destination, "SKILL.md"), "partial");
                throw new IOException("disk full");
            });

            Assert.Throws<IOException>(() => failingRefresh.Execute(_project, PlanNow(FakeGitHub.NestedHash),
                new Dictionary<string, string> { ["tdd"] = Path.Combine(FixturesRoot, "nested") }));

            var retry = PlanNow(FakeGitHub.NestedHash);
            Assert.That(retry.Actions.Where(a => a.Kind == PlanActionKind.Link).Select(a => a.SkillName), Is.EqualTo(new[] { "tdd" }));
            SyncUpdatedToNested(ForceCopy());
            Assert.That(SkillFolderHash.Compute(Link), Is.EqualTo(FakeGitHub.NestedHash));
        }

        [Test]
        public void Update_OverJunction_KeepsTheJunctionShowingTheNewCopy()
        {
            RequireWindows();
            Sync(ForceJunction());

            SyncUpdatedToNested(ForceJunction());

            Assert.That(IsReparsePoint(Link), Is.True, "expected the junction to be kept");
            Assert.That(SkillFolderHash.Compute(Link), Is.EqualTo(FakeGitHub.NestedHash));
        }

        [Test]
        public void Remove_Junction_RemovesOnlyTheLink()
        {
            RequireWindows();
            Sync(ForceJunction());

            DirectoryLink.Remove(Link);

            Assert.That(Directory.Exists(Link), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(Canonical, "SKILL.md")), Is.EqualTo(FetchedSkillMd));
        }

        [Test]
        public void Remove_Copy_RemovesOnlyTheLink()
        {
            Sync(ForceCopy());

            DirectoryLink.Remove(Link);

            Assert.That(Directory.Exists(Link), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(Canonical, "SKILL.md")), Is.EqualTo(FetchedSkillMd));
        }

        [Test]
        public void Remove_Symlink_RemovesOnlyTheLink()
        {
            try { Sync(new SymlinkCreator()); }
            catch (IOException e) { Assert.Ignore("This machine cannot create symlinks: " + e.Message); }

            DirectoryLink.Remove(Link);

            Assert.That(Directory.Exists(Link), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(Canonical, "SKILL.md")), Is.EqualTo(FetchedSkillMd));
        }
    }
}
