using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class GitHubSkillFetcherTests
    {
        /// <summary>Serves a zip shaped like a GitHub repo archive (one top-level "repo-ref/" folder) instead of downloading.</summary>
        sealed class FakeDownloader : IArchiveDownloader
        {
            public readonly List<string> Requested = new List<string>();
            public Dictionary<string, string> Files = new Dictionary<string, string>();

            public void Download(string url, string destinationPath)
            {
                Requested.Add(url);
                using (var zip = ZipFile.Open(destinationPath, ZipArchiveMode.Create))
                {
                    foreach (var file in Files)
                        using (var writer = new StreamWriter(zip.CreateEntry("skills-main/" + file.Key).Open()))
                            writer.Write(file.Value);
                }
            }
        }

        string _cache;
        FakeDownloader _downloader;

        [SetUp]
        public void SetUp()
        {
            _cache = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            _downloader = new FakeDownloader
            {
                Files =
                {
                    ["README.md"] = "repo readme",
                    ["skills/tdd/SKILL.md"] = "# tdd",
                    ["skills/tdd/references/tests.md"] = "good tests",
                    ["skills/code-review/SKILL.md"] = "# code-review",
                },
            };
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_cache)) Directory.Delete(_cache, recursive: true);
        }

        static LockedSkill Skill(string name, string source = "owner/skills") =>
            new LockedSkill(name, source, "github", $"skills/{name}/SKILL.md", "hash");

        static IEnumerable<string> FilesUnder(string folder) =>
            Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => f.Substring(folder.Length + 1).Replace('\\', '/'))
                .OrderBy(f => f, StringComparer.Ordinal);

        [Test]
        public void Fetch_ReturnsOnlyTheSkillFolderFromTheRepoArchive()
        {
            var folder = new GitHubSkillFetcher(_cache, _downloader).Fetch(Skill("tdd"));

            Assert.That(FilesUnder(folder), Is.EqualTo(new[] { "SKILL.md", "references/tests.md" }));
            Assert.That(File.ReadAllText(Path.Combine(folder, "SKILL.md")), Is.EqualTo("# tdd"));
        }

        [Test]
        public void Fetch_TwoSkillsFromOneRepo_DownloadsTheArchiveOnce()
        {
            var fetcher = new GitHubSkillFetcher(_cache, _downloader);

            fetcher.Fetch(Skill("tdd"));
            fetcher.Fetch(Skill("code-review"));

            Assert.That(_downloader.Requested, Is.EqualTo(new[] { "https://github.com/owner/skills/archive/HEAD.zip" }));
        }

        [Test]
        public void Fetch_SkillMissingFromArchive_ThrowsNamingSkillAndSource()
        {
            var error = Assert.Throws<SkillFetchException>(() =>
                new GitHubSkillFetcher(_cache, _downloader).Fetch(Skill("absent")));

            Assert.That(error.Message, Does.Contain("absent").And.Contain("owner/skills"));
        }

        [Test]
        public void Fetch_DownloadFails_ThrowsSkillFetchException()
        {
            var fetcher = new GitHubSkillFetcher(_cache, new FailingDownloader());

            Assert.Throws<SkillFetchException>(() => fetcher.Fetch(Skill("tdd")));
        }

        sealed class FailingDownloader : IArchiveDownloader
        {
            public void Download(string url, string destinationPath) => throw new IOException("offline");
        }
    }
}
