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

        static LockedSkill Skill(string name, string hash, string source = "owner/skills", string reference = null) =>
            new LockedSkill(name, source, "github", $"skills/{name}/SKILL.md", hash, reference);

        static LockedSkill SkillV2(string name, string hash, string source = "owner/skills") =>
            new LockedSkill(name, source, "github", $"skills/{name}/SKILL.md", hash, hashAlgorithm: SkillHashAlgorithm.Version2);

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
        public void Fetch_SkillLockedToARef_FetchesThatRefsArchive()
        {
            _github.Fixture("owner/skills@release/v1", "skills/tdd", "nested");

            var folder = Fetcher().Fetch(Skill("tdd", FakeGitHub.NestedHash, reference: "release/v1"));

            Assert.That(_github.Requested, Is.EqualTo(new[] { "https://github.com/owner/skills/archive/release/v1.zip" }));
            Assert.That(SkillFolderHash.Compute(folder), Is.EqualTo(FakeGitHub.NestedHash));
        }

        [Test]
        public void Fetch_SkillsAtDifferentRefsOfOneRepo_DownloadsEachRefsArchive()
        {
            _github.Fixture("owner/skills@v1", "skills/code-review", "minimal");
            var fetcher = Fetcher();

            var atHead = fetcher.Fetch(Tdd);
            var atRef = fetcher.Fetch(Skill("code-review", FakeGitHub.MinimalHash, reference: "v1"));

            Assert.That(_github.Requested, Is.EqualTo(new[]
            {
                "https://github.com/owner/skills/archive/HEAD.zip",
                "https://github.com/owner/skills/archive/v1.zip",
            }));
            Assert.That(SkillFolderHash.Compute(atHead), Is.EqualTo(FakeGitHub.MinimalHash));
            Assert.That(SkillFolderHash.Compute(atRef), Is.EqualTo(FakeGitHub.MinimalHash));
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
        public void Fetch_LatestMode_ContentDiffersFromLockedHash_ReturnsTheUpstreamCopy()
        {
            _github.File("owner/skills", "skills/tdd/SKILL.md", "# tdd, edited upstream after locking");

            var folder = new GitHubSkillFetcher(_cache, _github, InstallMode.Latest).Fetch(Tdd);

            Assert.That(File.ReadAllText(Path.Combine(folder, "SKILL.md")), Does.Contain("edited upstream"));
        }

        [Test]
        public void Fetch_LockHasNoHash_ThrowsHashMismatch()
        {
            var error = Assert.Throws<SkillFetchException>(() => Fetcher().Fetch(Skill("tdd", null)));

            Assert.That(error.Failure, Is.EqualTo(SkillFetchFailure.HashMismatch));
        }

        [Test]
        public void Fetch_Version2Lock_ContentMatches_ReturnsTheCopy()
        {
            var folder = Fetcher().Fetch(SkillV2("code-review", FakeGitHub.NestedHashV2));

            Assert.That(SkillFolderHash.Compute(folder, SkillHashAlgorithm.Version2), Is.EqualTo(FakeGitHub.NestedHashV2));
        }

        [Test]
        public void Fetch_Version2Lock_ContentChanged_ThrowsHashMismatch()
        {
            _github.File("owner/skills", "skills/tdd/SKILL.md", "# tdd, edited upstream after locking");

            var error = Assert.Throws<SkillFetchException>(() => Fetcher().Fetch(SkillV2("tdd", FakeGitHub.MinimalHashV2)));

            Assert.That(error.Failure, Is.EqualTo(SkillFetchFailure.HashMismatch));
            Assert.That(error.Message, Does.Contain("changed since it was locked"));
            Assert.That(Directory.Exists(Path.Combine(_cache, "skills", "owner", "skills", "tdd")), Is.False);
        }

        // Only the lock's algorithm counts: a version 1 hash in a version 2 lock is a mismatch, never a fallback.
        [Test]
        public void Fetch_Version2Lock_HoldingTheVersion1Hash_ThrowsHashMismatch()
        {
            var error = Assert.Throws<SkillFetchException>(() => Fetcher().Fetch(SkillV2("tdd", FakeGitHub.MinimalHash)));

            Assert.That(error.Failure, Is.EqualTo(SkillFetchFailure.HashMismatch));
        }

        [TestCase("")]
        [TestCase(null)]
        public void Fetch_Version2Lock_NoHash_RefusesNamingTheMigrationAndTheFix(string hash)
        {
            var error = Assert.Throws<SkillFetchException>(() => Fetcher().Fetch(SkillV2("tdd", hash)));

            Assert.That(error.Failure, Is.EqualTo(SkillFetchFailure.Unverifiable));
            Assert.That(error.Message, Does.Contain("no content hash")
                .And.Contain("migration")
                .And.Contain("npx skills add owner/skills --skill tdd")
                .And.Contain("commit " + Lockfile.FileName));
            Assert.That(_github.Requested, Is.Empty, "nothing is downloaded for a hash that can't be checked");
        }

        [Test]
        public void Fetch_Version2Lock_NoHash_LatestModeFetches()
        {
            var folder = new GitHubSkillFetcher(_cache, _github, InstallMode.Latest).Fetch(SkillV2("tdd", ""));

            Assert.That(FilesUnder(folder), Is.EqualTo(new[] { "SKILL.md" }));
        }

        // A GitHub archive of generated-trees.json's "symlinks" tree (links as git archives them, outside files beside
        // the skill folder) fetches to a copy holding the linked content, which hashes to the CLI's version 2 vector.
        [Test]
        public void Fetch_Version2Lock_ArchivedSymlinks_InstallTheLinkedContentAndMatchTheCliHash()
        {
            var tree = SkillFolderHashTests.GeneratedTree("symlinks");
            _github.Fixture("owner/skills", "skills/symlinks", (string)SkillFolderHashTests.Value(tree, "base"));
            foreach (var link in SkillFolderHashTests.Member(tree, "links"))
                _github.Symlink("owner/skills", "skills/symlinks/" + link.Key, (string)link.Value);
            foreach (var file in SkillFolderHashTests.Member(tree, "outside"))
                _github.File("owner/skills", "skills/" + file.Key, (string)file.Value);

            var folder = Fetcher().Fetch(SkillV2("symlinks", SkillFolderHashTests.SymlinksTreeHashV2));

            Assert.That(File.ReadAllText(Path.Combine(folder, "alpha-link.md")), Is.EqualTo(File.ReadAllText(Path.Combine(folder, "alpha.md"))));
            Assert.That(File.Exists(Path.Combine(folder, "build-link", "out.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(folder, "outside-link.md")), Is.False, "a link out of the skill folder is left out");
            Assert.That(File.Exists(Path.Combine(folder, "dangling.md")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(folder, "loop")), Is.False, "a link to the folder holding it is a cycle");
            Assert.That(FilesUnder(folder).Any(f => DirectoryLink.IsLink(Path.Combine(folder, f))), Is.False);
        }

        // A folder link whose target holds another folder link is copied after that inner link is written.
        [TestCase("a", "real/nested")]
        [TestCase("real/nested", "a")]
        public void Fetch_Version2Lock_FolderLinkHoldingAnotherFolderLink_CopiesTheInnerContent(string first, string second)
        {
            _github.Fixture("owner/skills", "skills/tdd", "minimal")
                .File("owner/skills", "skills/tdd/real/base.txt", "base\n")
                .File("owner/skills", "skills/tdd/other/value.txt", "value\n");
            var targets = new Dictionary<string, string> { ["a"] = "real", ["real/nested"] = "../other" };
            foreach (var link in new[] { first, second }) _github.Symlink("owner/skills", "skills/tdd/" + link, targets[link]);

            var folder = new GitHubSkillFetcher(_cache, _github, InstallMode.Latest).Fetch(SkillV2("tdd", ""));

            Assert.That(File.ReadAllText(Path.Combine(folder, "a", "nested", "value.txt")), Is.EqualTo("value\n"));
            Assert.That(File.ReadAllText(Path.Combine(folder, "real", "nested", "value.txt")), Is.EqualTo("value\n"));
            Assert.That(File.ReadAllText(Path.Combine(folder, "a", "base.txt")), Is.EqualTo("base\n"));
        }

        // Two folder links into each other's targets: the CLI's installer copies one level of each, then its branch
        // check stops. Hash and layout from `skills@1.8.1 add` over the same tree with real symlinks.
        [Test]
        public void Fetch_Version2Lock_FolderLinksIntoEachOther_CopyOneLevelOfEachAsTheCli()
        {
            _github.Fixture("owner/skills", "skills/tdd", "minimal")
                .File("owner/skills", "skills/tdd/x/file.txt", "x\n")
                .File("owner/skills", "skills/tdd/y/file.txt", "y\n")
                .Symlink("owner/skills", "skills/tdd/x/to-y", "../y")
                .Symlink("owner/skills", "skills/tdd/y/to-x", "../x");

            var folder = Fetcher().Fetch(SkillV2("tdd", "352b3fd20829a715853beb076940f3d83119b896e5095d5964e1e1cf45080618"));

            Assert.That(FilesUnder(folder),
                Is.EqualTo(new[] { "SKILL.md", "x/file.txt", "x/to-y/file.txt", "y/file.txt", "y/to-x/file.txt" }));
        }

        // What the version 2 hash leaves out is not installed either, as the CLI's installer leaves it out: an upstream
        // change there can't reach a Pinned copy unverified.
        [Test]
        public void Fetch_Version2Lock_LeavesOutWhatTheHashExcludes()
        {
            _github.File("owner/skills", "skills/code-review/metadata.json", "{ \"changed\": true }")
                .File("owner/skills", "skills/code-review/scripts/__pycache__/run.cpython-312.pyc", "not checked")
                .File("owner/skills", "skills/code-review/__pypackages__/3.12/lib/x.py", "import os")
                .File("owner/skills", "skills/code-review/.git/HEAD", "ref")
                .Symlink("owner/skills", "skills/code-review/docs/metadata.json", "../alpha.md")
                .Symlink("owner/skills", "skills/code-review/__pypackages__", "scripts");

            var folder = Fetcher().Fetch(SkillV2("code-review", FakeGitHub.NestedHashV2));

            Assert.That(FilesUnder(folder), Is.EqualTo(FilesUnder(Path.GetFullPath(
                "Packages/com.hissal.agent-skills-sync/Tests/Editor/Fixtures~/SkillFolderHash/nested"))));
        }

        // A link with an allowed name counts as its target's content even when the target's own name is excluded. The
        // hash is skills@1.8.1's computeSkillFolderHash over the same tree with real symlinks.
        [Test]
        public void Fetch_Version2Lock_LinkToAnExcludedFile_InstallsItsContentUnderTheLinkName()
        {
            _github.File("owner/skills", "skills/tdd/metadata.json", "{ \"a\": 1 }")
                .Symlink("owner/skills", "skills/tdd/alias.md", "metadata.json")
                .File("owner/skills", "skills/tdd/__pycache__/x.pyc", "pyc")
                .Symlink("owner/skills", "skills/tdd/cache-link", "__pycache__");

            var folder = Fetcher().Fetch(SkillV2("tdd", "9a8e7fcb5787b771b3d0039701146dd323f09561aba1d08eba4bcf10c8369198"));

            Assert.That(FilesUnder(folder), Is.EqualTo(new[] { "SKILL.md", "alias.md", "cache-link/x.pyc" }));
            Assert.That(File.ReadAllText(Path.Combine(folder, "alias.md")), Is.EqualTo("{ \"a\": 1 }"));
        }

        // Windows trims a trailing space, so ".. " climbs. The entry alone stays inside the skill folder; copied through
        // a link from a shallower folder it would land outside the fetch cache.
        [Test]
        public void Fetch_Version2Lock_LinkedCopyThatWouldClimbOut_IsNotWritten()
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("Only Windows trims trailing spaces from path names.");
            _github.File("owner/skills", "skills/tdd/b/c/.. /.. /escaped.txt", "x")
                .Symlink("owner/skills", "skills/tdd/a", "b/c");

            new GitHubSkillFetcher(_cache, _github, InstallMode.Latest).Fetch(SkillV2("tdd", ""));

            Assert.That(Directory.GetFiles(_cache, "escaped.txt", SearchOption.AllDirectories)
                .Where(f => !f.StartsWith(Path.Combine(_cache, "skills", "owner", "skills", "tdd") + Path.DirectorySeparatorChar)), Is.Empty);
            Assert.That(File.Exists(Path.Combine(_cache, "skills", "owner", "skills", "escaped.txt")), Is.False);
        }

        // Version 1 hashed a clone that skipped symlinks; its archive extraction is unchanged.
        [Test]
        public void Fetch_Version1Lock_ArchivedSymlink_IsExtractedAsBefore()
        {
            _github.Symlink("owner/skills", "skills/tdd/link.md", "SKILL.md");

            var folder = new GitHubSkillFetcher(_cache, _github, InstallMode.Latest).Fetch(Tdd);

            Assert.That(File.ReadAllText(Path.Combine(folder, "link.md")), Is.EqualTo("SKILL.md"));
        }

        // skills@1.8 locks a skills.sh install with the version 2 hash of the snapshot's files (computeSnapshotHash),
        // which the GitHub archive reproduces: checked on vercel-labs/agent-skills web-design-guidelines.
        [TestCase("vercel-labs/agent-skills")]
        [TestCase("zapier/connectors")]
        public void Fetch_Version2Lock_SkillsShSource_VerifiesTheHash(string source)
        {
            _github.Fixture(source, "skills/tdd", "minimal");
            var skill = SkillV2("tdd", FakeGitHub.MinimalHashV2, source);

            Assert.That(LockVerification.CanVerify(skill), Is.True);
            Assert.That(FilesUnder(Fetcher().Fetch(skill)), Is.EqualTo(new[] { "SKILL.md" }));
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
            Assert.That(LockVerification.CanVerify(skill), Is.False);
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

        // Latest installs current upstream without checking the lock, so Pinned's verification refusals do not apply.

        GitHubSkillFetcher LatestFetcher() => new GitHubSkillFetcher(_cache, _github, InstallMode.Latest);

        [TestCase("vercel-labs/agent-skills")]
        [TestCase("zapier/connectors")]
        public void Fetch_LatestMode_SourceLockedWithASkillsShServerHash_ReturnsTheUpstreamCopy(string source)
        {
            _github.Fixture(source, "skills/tdd", "minimal");

            var folder = LatestFetcher().Fetch(Skill("tdd", "server-hash-of-another-algorithm", source));

            Assert.That(SkillFolderHash.Compute(folder), Is.EqualTo(FakeGitHub.MinimalHash));
        }

        [Test]
        public void Fetch_LatestMode_NonAsciiPathAndHashDiffers_ReturnsTheUpstreamCopy()
        {
            _github
                .File("owner/skills", "skills/intl/SKILL.md", "# intl")
                .File("owner/skills", "skills/intl/résumé.md", "cv");

            var folder = LatestFetcher().Fetch(Skill("intl", FakeGitHub.MinimalHash));

            Assert.That(FilesUnder(folder), Is.EqualTo(new[] { "SKILL.md", "résumé.md" }));
        }

        // The CRLF rewrite makes a Pinned copy hash to the lock. Latest compares with upstream, so it keeps upstream's bytes.
        [Test]
        public void Fetch_LatestMode_LockHashedFromACrlfCheckout_ReturnsTheArchiveBytesUnchanged()
        {
            _github.Fixture("owner/skills", "skills/byte-exact", "byte-exact");

            var folder = LatestFetcher().Fetch(Skill("byte-exact", ByteExactCrlfCheckoutHash));

            Assert.That(File.ReadAllBytes(Path.Combine(folder, "SKILL.md")), Has.No.Member((byte)'\r'));
            Assert.That(SkillFolderHash.Compute(folder), Is.Not.EqualTo(ByteExactCrlfCheckoutHash));
        }

        [TestCase("owner/skills")]
        [TestCase("zapier/other-repo")]
        [TestCase("vercel-labs-fork/skills")]
        public void CanVerify_OtherSources_IsTrue(string source)
        {
            Assert.That(LockVerification.CanVerify(Skill("tdd", "x", source)), Is.True);
        }
    }
}
