using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class SkillSyncTests
    {
        /// <summary>Fakes the network: each skill's folder is written into the temp dir; listed names fail.</summary>
        sealed class FakeFetcher : ISkillFetcher
        {
            readonly string _root;
            public readonly HashSet<string> Failing = new HashSet<string>();

            public FakeFetcher(string root) => _root = root;

            public string Fetch(LockedSkill skill)
            {
                if (Failing.Contains(skill.Name)) throw new SkillFetchException($"Could not download {skill.Source}.");
                var folder = Path.Combine(_root, "fetched", skill.Name);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "SKILL.md"), "# " + skill.Name);
                return folder;
            }
        }

        const string Lock = @"{
  ""version"": 1,
  ""skills"": {
    ""tdd"": { ""source"": ""owner/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/tdd/SKILL.md"", ""computedHash"": ""a"" },
    ""code-review"": { ""source"": ""owner/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/code-review/SKILL.md"", ""computedHash"": ""b"" }
  }
}";

        string _root;
        string _project;
        FakeFetcher _fetcher;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            _project = Path.Combine(_root, "project");
            Directory.CreateDirectory(_project);
            File.WriteAllText(Path.Combine(_project, Lockfile.FileName), Lock);
            _fetcher = new FakeFetcher(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        [Test]
        public void Run_EmptyProject_InstallsAndLinksEveryLockedSkill()
        {
            var summary = new SkillSync(_project, _fetcher).Run();

            Assert.That(summary.Installed, Is.EqualTo(new[] { "tdd", "code-review" }));
            Assert.That(summary.Linked, Is.EqualTo(new[] { "tdd", "code-review" }));
            Assert.That(File.ReadAllText(Path.Combine(_project, ".claude/skills/code-review/SKILL.md")), Is.EqualTo("# code-review"));
        }

        [Test]
        public void Run_FetchFails_ChangesNothingInTheProject()
        {
            _fetcher.Failing.Add("code-review");

            Assert.Throws<SkillFetchException>(() => new SkillSync(_project, _fetcher).Run());

            Assert.That(Directory.Exists(Path.Combine(_project, ".agents")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(_project, ".claude")), Is.False);
        }

        [Test]
        public void Run_Twice_SecondRunChangesNothing()
        {
            new SkillSync(_project, _fetcher).Run();

            var summary = new SkillSync(_project, _fetcher).Run();

            Assert.That(summary.NothingChanged, Is.True);
        }
    }
}
