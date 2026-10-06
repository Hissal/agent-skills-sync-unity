using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class PlanExecutorTests
    {
        static readonly string FixturesRoot =
            Path.GetFullPath("Packages/com.hissal.agent-skills-sync/Tests/Editor/Fixtures~/SkillFolderHash");

        /// <summary>The "fetched" contents of each locked skill: fixture name and its CLI hash.</summary>
        static readonly Dictionary<string, (string Fixture, string Hash)> Versions = new Dictionary<string, (string, string)>
        {
            ["v1"] = ("minimal", FakeGitHub.MinimalHash),
            ["v2"] = ("nested", FakeGitHub.NestedHash),
        };

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
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        /// <summary>Locks each skill at a version ("name=v1") and syncs: plan from a fresh scan, then execute.</summary>
        SyncSummary Sync(params string[] lockedAt)
        {
            var skills = lockedAt.Select(s => s.Split('=')).Select(s =>
                new LockedSkill(s[0], "owner/repo", "github", $"skills/{s[0]}/SKILL.md", Versions[s[1]].Hash)).ToList();
            var fetched = lockedAt.Select(s => s.Split('=')).ToDictionary(s => s[0],
                s => Path.Combine(FixturesRoot, Versions[s[1]].Fixture));

            return new PlanExecutor().Execute(_project, Plan(skills), fetched);
        }

        InstallPlan Plan(IReadOnlyList<LockedSkill> skills) =>
            InstallPlanner.Plan(new Lockfile(skills), ProjectScanner.Scan(_project, FolderLayout.Default), FolderLayout.Default);

        string InProject(string relativePath) => Path.Combine(_project, relativePath);

        string[] ManagedLines(string folder) =>
            File.ReadAllLines(InProject(folder + "/.gitignore")).Where(l => l.Length > 0 && !l.StartsWith("#")).ToArray();

        static string Fixture(string fixture, string file) => File.ReadAllText(Path.Combine(FixturesRoot, fixture, file));

        /// <summary>A folder the tool does not manage, holding one file.</summary>
        void Foreign(string relativePath)
        {
            Directory.CreateDirectory(InProject(relativePath));
            File.WriteAllText(InProject(relativePath + "/SKILL.md"), "# mine");
        }

        // Install

        [Test]
        public void Execute_EmptyProject_CreatesCanonicalCopy()
        {
            Sync("tdd=v2");

            Assert.That(File.ReadAllText(InProject(".agents/skills/tdd/SKILL.md")), Is.EqualTo(Fixture("nested", "SKILL.md")));
            Assert.That(File.ReadAllText(InProject(".agents/skills/tdd/references/api.md")), Is.EqualTo(Fixture("nested", "references/api.md")));
        }

        [Test]
        public void Execute_EmptyProject_LinksClaudeFolderToCanonicalCopy()
        {
            Sync("tdd=v1");

            var link = InProject(".claude/skills/tdd");
            Assert.That(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint), Is.True, "expected a link, not a copy");
            Assert.That(File.ReadAllText(Path.Combine(link, "SKILL.md")), Is.EqualTo(Fixture("minimal", "SKILL.md")));

            File.WriteAllText(InProject(".agents/skills/tdd/SKILL.md"), "# edited");
            Assert.That(File.ReadAllText(Path.Combine(link, "SKILL.md")), Is.EqualTo("# edited"));
        }

        [TestCase(".agents/skills")]
        [TestCase(".claude/skills")]
        public void Execute_EmptyProject_WritesManagedGitignoreListingTheSkill(string folder)
        {
            Sync("tdd=v1");

            Assert.That(ManagedLines(folder), Is.EqualTo(new[] { "/tdd" }));
        }

        // Idempotent re-run

        [Test]
        public void Execute_CompletedSync_RescanPlansNoChanges()
        {
            Sync("tdd=v1", "code-review=v2");

            var plan = Plan(new[]
            {
                new LockedSkill("tdd", "owner/repo", "github", "skills/tdd/SKILL.md", FakeGitHub.MinimalHash),
                new LockedSkill("code-review", "owner/repo", "github", "skills/code-review/SKILL.md", FakeGitHub.NestedHash),
            });

            Assert.That(plan.Actions, Is.Empty);
        }

        [Test]
        public void Execute_SyncTwice_SecondRunChangesNothing()
        {
            Sync("tdd=v1", "code-review=v2");
            var gitignore = File.ReadAllText(InProject(".agents/skills/.gitignore"));

            var summary = Sync("tdd=v1", "code-review=v2");

            Assert.That(summary.NothingChanged, Is.True);
            Assert.That(File.ReadAllText(InProject(".agents/skills/.gitignore")), Is.EqualTo(gitignore));
        }

        // Update

        [Test]
        public void Execute_LockedHashChanged_ReplacesTheCanonicalCopy()
        {
            Sync("tdd=v1");

            var summary = Sync("tdd=v2");

            Assert.That(summary.Updated, Is.EqualTo(new[] { "tdd" }));
            Assert.That(SkillFolderHash.Compute(InProject(".agents/skills/tdd")), Is.EqualTo(FakeGitHub.NestedHash));
            Assert.That(File.ReadAllText(InProject(".claude/skills/tdd/references/api.md")), Is.EqualTo(Fixture("nested", "references/api.md")));
        }

        [Test]
        public void Execute_InstalledCopyEditedLocally_UpdateRestoresTheLockedVersion()
        {
            Sync("tdd=v1");
            File.WriteAllText(InProject(".agents/skills/tdd/extra.md"), "local edit");

            var summary = Sync("tdd=v1");

            Assert.That(summary.Updated, Is.EqualTo(new[] { "tdd" }));
            Assert.That(File.Exists(InProject(".agents/skills/tdd/extra.md")), Is.False);
        }

        // Remove

        [Test]
        public void Execute_SkillNoLongerLocked_RemovesCopyAndLink()
        {
            Sync("tdd=v1", "code-review=v2");

            var summary = Sync("tdd=v1");

            Assert.That(summary.Removed, Is.EqualTo(new[] { "code-review" }));
            Assert.That(Directory.Exists(InProject(".agents/skills/code-review")), Is.False);
            Assert.That(File.Exists(InProject(".claude/skills/code-review")) || Directory.Exists(InProject(".claude/skills/code-review")), Is.False);
        }

        [TestCase(".agents/skills")]
        [TestCase(".claude/skills")]
        public void Execute_SkillNoLongerLocked_DropsItFromTheManagedGitignore(string folder)
        {
            Sync("tdd=v1", "code-review=v2");

            Sync("tdd=v1");

            Assert.That(ManagedLines(folder), Is.EqualTo(new[] { "/tdd" }));
        }

        [Test]
        public void Execute_RemovingALink_LeavesWhatItPointsAt()
        {
            Sync("tdd=v1");
            // The contributor took the canonical copy over: no longer managed there, still linked from Claude.
            ManagedStateFile.Write(InProject(".agents/skills"), new string[0]);

            Sync();

            Assert.That(File.Exists(InProject(".claude/skills/tdd/SKILL.md")), Is.False);
            Assert.That(File.ReadAllText(InProject(".agents/skills/tdd/SKILL.md")), Is.EqualTo(Fixture("minimal", "SKILL.md")));
        }

        // LeaveForeign

        [Test]
        public void Execute_ForeignEntriesInBothFolders_AreUntouchedAcrossInstallUpdateAndRemove()
        {
            Foreign(".agents/skills/mine");
            Foreign(".claude/skills/mine");
            Foreign(".claude/skills/tdd");

            Sync("tdd=v1", "code-review=v1");
            Sync("tdd=v2", "code-review=v2");
            var summary = Sync("tdd=v2");

            foreach (var path in new[] { ".agents/skills/mine", ".claude/skills/mine", ".claude/skills/tdd" })
            {
                Assert.That(File.GetAttributes(InProject(path)).HasFlag(FileAttributes.ReparsePoint), Is.False, path);
                Assert.That(Directory.GetFileSystemEntries(InProject(path)).Select(Path.GetFileName), Is.EqualTo(new[] { "SKILL.md" }), path);
                Assert.That(File.ReadAllText(InProject(path + "/SKILL.md")), Is.EqualTo("# mine"), path);
            }
            Assert.That(ManagedLines(".agents/skills"), Is.EqualTo(new[] { "/tdd" }));
            Assert.That(ManagedLines(".claude/skills"), Is.Empty);
            Assert.That(summary.Skipped, Is.EqualTo(new[] { "tdd" }));
        }

        // Summary

        [Test]
        public void Execute_Summary_CountsInstalledUpdatedRemovedAndSkipped()
        {
            Foreign(".claude/skills/kept");
            Sync("tdd=v1", "old=v1");

            var summary = Sync("tdd=v2", "new=v1", "kept=v1");

            Assert.That(summary.Installed, Is.EquivalentTo(new[] { "new", "kept" }));
            Assert.That(summary.Updated, Is.EqualTo(new[] { "tdd" }));
            Assert.That(summary.Removed, Is.EqualTo(new[] { "old" }));
            Assert.That(summary.Skipped, Is.EqualTo(new[] { "kept" }));
            Assert.That(summary.NothingChanged, Is.False);
        }
    }
}
