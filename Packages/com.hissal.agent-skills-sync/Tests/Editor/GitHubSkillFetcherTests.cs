using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class GitHubSkillFetcherTests
    {
        string _cache;
        FakeGitHub _github;

        [SetUp]
        public void SetUp()
        {
            _cache = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            _github = new FakeGitHub()
                .File("owner/skills", "README.md", "repo readme")
                .Fixture("owner/skills", "skills/tdd", "minimal")
                .Fixture("owner/skills", "skills/code-review", "nested");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_cache)) Directory.Delete(_cache, recursive: true);
        }

        static LockedSkill Skill(string name, string hash, string source = "owner/skills") =>
            new LockedSkill(name, source, "github", $"skills/{name}/SKILL.md", hash);

        static LockedSkill Tdd => Skill("tdd", FakeGitHub.MinimalHash);
        static LockedSkill CodeReview => Skill("code-review", FakeGitHub.NestedHash);

        static IEnumerable<string> FilesUnder(string folder) =>
            Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => f.Substring(folder.Length + 1).Replace('\\', '/'))
                .OrderBy(f => f, StringComparer.Ordinal);

        GitHubSkillFetcher Fetcher() => new GitHubSkillFetcher(_cache, _github);

        [Test]
        public void Fetch_ReturnsOnlyTheSkillFolderFromTheRepoArchive()
        {
            var folder = Fetcher().Fetch(Tdd);

            Assert.That(FilesUnder(folder), Is.EqualTo(new[] { "SKILL.md" }));
        }

        [Test]
        public void Fetch_TwoSkillsFromOneRepo_DownloadsTheArchiveOnce()
        {
            var fetcher = Fetcher();

            fetcher.Fetch(Tdd);
            fetcher.Fetch(CodeReview);

            Assert.That(_github.Requested, Is.EqualTo(new[] { "https://github.com/owner/skills/archive/HEAD.zip" }));
        }

        [Test]
        public void Fetch_SkillMissingFromArchive_ThrowsNamingSkillAndSource()
        {
            var error = Assert.Throws<SkillFetchException>(() => Fetcher().Fetch(Skill("absent", FakeGitHub.MinimalHash)));

            Assert.That(error.Message, Does.Contain("absent").And.Contain("owner/skills"));
            Assert.That(error.Failure, Is.EqualTo(SkillFetchFailure.SourceUnusable));
        }

        [Test]
        public void Fetch_Offline_ThrowsDownloadFailure()
        {
            _github.Offline = true;

            var error = Assert.Throws<SkillFetchException>(() => Fetcher().Fetch(Tdd));

            Assert.That(error.Failure, Is.EqualTo(SkillFetchFailure.Download));
            Assert.That(error.SkillName, Is.EqualTo("tdd"));
            Assert.That(error.Message, Does.Contain("owner/skills").And.Contain("network"));
        }

        [Test]
        public void Fetch_RepoDownloadFailed_LaterSkillsFromThatRepoFailWithoutRetrying()
        {
            _github.Offline = true;
            var fetcher = Fetcher();
            Assert.Throws<SkillFetchException>(() => fetcher.Fetch(Tdd));

            var error = Assert.Throws<SkillFetchException>(() => fetcher.Fetch(CodeReview));

            Assert.That(error.Failure, Is.EqualTo(SkillFetchFailure.Download));
            Assert.That(error.SkillName, Is.EqualTo("code-review"));
            Assert.That(_github.Requested, Has.Count.EqualTo(1));
        }

        [Test]
        public void Fetch_ContentDiffersFromLockedHash_ThrowsHashMismatchTellingToUpdateTheLock()
        {
            _github.File("owner/skills", "skills/tdd/SKILL.md", "# tdd, edited upstream after locking");

            var error = Assert.Throws<SkillFetchException>(() => Fetcher().Fetch(Tdd));

            Assert.That(error.Failure, Is.EqualTo(SkillFetchFailure.HashMismatch));
            Assert.That(error.SkillName, Is.EqualTo("tdd"));
            Assert.That(error.Message, Does.Contain("owner/skills")
                .And.Contain("changed since it was locked")
                .And.Contain("npx skills update")
                .And.Contain(Lockfile.FileName));
        }

        [Test]
        public void Fetch_LockHasNoHash_ThrowsHashMismatch()
        {
            var error = Assert.Throws<SkillFetchException>(() => Fetcher().Fetch(Skill("tdd", null)));

            Assert.That(error.Failure, Is.EqualTo(SkillFetchFailure.HashMismatch));
        }

        [Test]
        public void Fetch_HashMismatch_LeavesNoFetchedCopyBehind()
        {
            Assert.Throws<SkillFetchException>(() => Fetcher().Fetch(Skill("tdd", FakeGitHub.NestedHash)));

            Assert.That(Directory.Exists(Path.Combine(_cache, "skills", "owner", "skills", "tdd")), Is.False);
        }

        [TestCase("vercel-labs/agent-skills")]
        [TestCase("Vercel/skills")]
        [TestCase("heygen-com/skills")]
        [TestCase("remotion-dev/skills")]
        [TestCase("zapier/connectors")]
        public void Fetch_SourceLockedWithASkillsShServerHash_RefusesAsUnverifiable(string source)
        {
            _github.Fixture(source, "skills/tdd", "minimal");
            var skill = Skill("tdd", "server-hash-of-another-algorithm", source);

            var error = Assert.Throws<SkillFetchException>(() => Fetcher().Fetch(skill));

            Assert.That(error.Failure, Is.EqualTo(SkillFetchFailure.Unverifiable));
            Assert.That(error.SkillName, Is.EqualTo("tdd"));
            Assert.That(error.Message, Does.Contain(source).And.Contain("can't verify"));
            Assert.That(GitHubSkillFetcher.CanVerify(skill), Is.False);
            Assert.That(Directory.Exists(Path.Combine(_cache, "skills")), Is.False, "nothing unverified may be extracted");
        }

        // The skills CLI hashes a git checkout. Locked on a machine with core.autocrlf=true, text files had CRLF
        // endings; the archive has the repo's LF. Hash computed independently from the fixture (LF -> CRLF in
        // every file without a NUL or CR byte, i.e. not in assets/binary.bin or crlf.txt).
        const string ByteExactCrlfCheckoutHash = "1ef11b466bb0ad8cb6eec71a1a735f1851045762121f50f124f21d66c7ceee8f";

        [Test]
        public void Fetch_LockHashedFromACrlfCheckout_Accepts()
        {
            _github.Fixture("owner/skills", "skills/byte-exact", "byte-exact");

            var folder = Fetcher().Fetch(Skill("byte-exact", ByteExactCrlfCheckoutHash));

            Assert.That(FilesUnder(folder), Does.Contain("SKILL.md").And.Contain("assets/binary.bin"));
        }

        // Otherwise the installed copy would never hash to the lock, and every re-sync would see it as outdated.
        [Test]
        public void Fetch_LockHashedFromACrlfCheckout_ReturnsTheCopyAsItWasLocked()
        {
            _github.Fixture("owner/skills", "skills/byte-exact", "byte-exact");

            var folder = Fetcher().Fetch(Skill("byte-exact", ByteExactCrlfCheckoutHash));

            Assert.That(SkillFolderHash.Compute(folder), Is.EqualTo(ByteExactCrlfCheckoutHash));
        }

        [Test]
        public void Fetch_NonAsciiPathAndHashDiffers_RefusesAsUnverifiableNotChanged()
        {
            _github
                .File("owner/skills", "skills/intl/SKILL.md", "# intl")
                .File("owner/skills", "skills/intl/résumé.md", "cv");

            var error = Assert.Throws<SkillFetchException>(() => Fetcher().Fetch(Skill("intl", FakeGitHub.MinimalHash)));

            Assert.That(error.Failure, Is.EqualTo(SkillFetchFailure.Unverifiable));
            Assert.That(error.Message, Does.Contain("can't verify"));
            Assert.That(Directory.Exists(Path.Combine(_cache, "skills", "owner", "skills", "intl")), Is.False);
        }

        [TestCase("owner/skills")]
        [TestCase("zapier/other-repo")]
        [TestCase("vercel-labs-fork/skills")]
        public void CanVerify_OtherSources_IsTrue(string source)
        {
            Assert.That(GitHubSkillFetcher.CanVerify(Skill("tdd", "x", source)), Is.True);
        }
    }
}
