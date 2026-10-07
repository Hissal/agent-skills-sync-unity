using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class PlanExecutorTests
    {
        string _root;
        string _project;
        string _fetched;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            _project = Path.Combine(_root, "project");
            _fetched = Path.Combine(_root, "fetched", "tdd");
            Directory.CreateDirectory(_project);
            Directory.CreateDirectory(Path.Combine(_fetched, "references"));
            File.WriteAllText(Path.Combine(_fetched, "SKILL.md"), "# tdd");
            File.WriteAllText(Path.Combine(_fetched, "references", "tests.md"), "good tests");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        static Lockfile LockTdd() => new Lockfile(new[]
        {
            new LockedSkill("tdd", "owner/repo", "github", "skills/tdd/SKILL.md", "hash"),
        });

        void ExecuteFreshPlan()
        {
            var plan = InstallPlanner.Plan(LockTdd(), ProjectScanner.Scan(_project, FolderLayout.Default), FolderLayout.Default);
            new PlanExecutor().Execute(_project, plan, new Dictionary<string, string> { ["tdd"] = _fetched });
        }

        string InProject(string relativePath) => Path.Combine(_project, relativePath);

        [Test]
        public void Execute_EmptyProject_CreatesCanonicalCopy()
        {
            ExecuteFreshPlan();

            Assert.That(File.ReadAllText(InProject(".agents/skills/tdd/SKILL.md")), Is.EqualTo("# tdd"));
            Assert.That(File.ReadAllText(InProject(".agents/skills/tdd/references/tests.md")), Is.EqualTo("good tests"));
        }

        [Test]
        public void Execute_EmptyProject_LinksClaudeFolderToCanonicalCopy()
        {
            ExecuteFreshPlan();

            var link = InProject(".claude/skills/tdd");
            Assert.That(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint), Is.True, "expected a link, not a copy");
            Assert.That(File.ReadAllText(Path.Combine(link, "SKILL.md")), Is.EqualTo("# tdd"));

            File.WriteAllText(InProject(".agents/skills/tdd/SKILL.md"), "# edited");
            Assert.That(File.ReadAllText(Path.Combine(link, "SKILL.md")), Is.EqualTo("# edited"));
        }

        [TestCase(".agents/skills")]
        [TestCase(".claude/skills")]
        public void Execute_EmptyProject_WritesManagedGitignoreListingTheSkill(string folder)
        {
            ExecuteFreshPlan();

            var lines = File.ReadAllLines(InProject(folder + "/.gitignore"));
            Assert.That(lines.Where(l => l.Length > 0 && !l.StartsWith("#")), Is.EqualTo(new[] { "/tdd" }));
        }

        [Test]
        public void Execute_CompletedPlan_RescanPlansNothing()
        {
            ExecuteFreshPlan();

            var plan = InstallPlanner.Plan(LockTdd(), ProjectScanner.Scan(_project, FolderLayout.Default), FolderLayout.Default);

            Assert.That(plan.Actions, Is.Empty);
            Assert.That(plan.ManagedNames.Values, Has.All.EqualTo(new[] { "tdd" }));
        }
    }
}
