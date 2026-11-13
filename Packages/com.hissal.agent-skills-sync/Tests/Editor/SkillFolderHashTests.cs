using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    // Expected hashes are the computedHash values `skills@1.7.0` wrote to skills-lock.json for each
    // fixture folder. Regenerate with Fixtures~/SkillFolderHash/generate-hashes.mjs.
    public class SkillFolderHashTests
    {
        static readonly string FixturesRoot =
            Path.GetFullPath("Packages/com.hissal.agent-skills-sync/Tests/Editor/Fixtures~/SkillFolderHash");

        [TestCase("minimal", "6ad83e06b125610b593c54974d6bac14a4badfbaafe421ab6976e4de0c91baf9")]
        [TestCase("nested", "c8538d846c53675d0c411fed3e626550b0a6b36ec5b3b96568530bcc37700de1")]
        [TestCase("punctuation-names", "3cc8496247a01ba07aff8544f9d0e4b06385905df56fb51722e215b068592f5b")]
        [TestCase("byte-exact", "f01c235b32bc0da8f16ef4ddf8348b133888d03b3d31f6d193b28e3157a1e368")]
        public void Compute_MatchesSkillsCliHash(string fixture, string expectedHash)
        {
            var hash = SkillFolderHash.Compute(Path.Combine(FixturesRoot, fixture));

            Assert.That(hash, Is.EqualTo(expectedHash));
        }

        // skills@1.7.0 gave the "nested" hash for this same tree (checked by hand: .git and
        // node_modules folders cannot be committed as fixtures).
        [Test]
        public void Compute_IgnoresGitAndNodeModulesFoldersAndEmptyFolders()
        {
            var skill = Path.Combine(Path.GetTempPath(), "SkillFolderHashTests-" + Path.GetRandomFileName());
            try
            {
                CopyDirectory(Path.Combine(FixturesRoot, "nested"), skill);
                Write(Path.Combine(skill, ".git", "HEAD"), "ref: refs/heads/main\n");
                Write(Path.Combine(skill, "node_modules", "pkg", "index.js"), "module.exports = 1;\n");
                Write(Path.Combine(skill, "scripts", "node_modules", "y.js"), "y\n");
                Directory.CreateDirectory(Path.Combine(skill, "empty"));

                var hash = SkillFolderHash.Compute(skill);

                Assert.That(hash, Is.EqualTo("c8538d846c53675d0c411fed3e626550b0a6b36ec5b3b96568530bcc37700de1"));
            }
            finally
            {
                if (Directory.Exists(skill)) Directory.Delete(skill, recursive: true);
            }
        }

        // Version 2 (lock version 2): the computedHash values `skills@1.8.1` wrote, from generate-hashes.mjs 1.8.1.
        [TestCase("minimal", FakeGitHub.MinimalHashV2)]
        [TestCase("nested", FakeGitHub.NestedHashV2)]
        [TestCase("punctuation-names", "96212a05eba62c761d47d18a8cdb379515db1acc5ffa828978d833345d4150c7")]
        [TestCase("byte-exact", "250215d6b2a52dd7b1d069ac0f50a758e9136e0b2891397f03d8490615dd185c")]
        public void ComputeVersion2_MatchesSkillsCliHash(string fixture, string expectedHash)
        {
            var hash = SkillFolderHash.Compute(Path.Combine(FixturesRoot, fixture), SkillHashAlgorithm.Version2);

            Assert.That(hash, Is.EqualTo(expectedHash));
        }

        [Test]
        public void ComputeVersion1_IsCompute()
        {
            var folder = Path.Combine(FixturesRoot, "byte-exact");

            Assert.That(SkillFolderHash.Compute(folder, SkillHashAlgorithm.Version1), Is.EqualTo(SkillFolderHash.Compute(folder)));
        }

        // The trees of generated-trees.json, hashed by `skills@1.8.1` (generate-hashes.mjs 1.8.1) after building them
        // the same way. excluded-names leaves out exactly what it adds, so it hashes like nested.
        [TestCase("crlf-mixed", "8c8dbdde4bd5aa96f4857acb43c2b2c8a4ab7afa08a524d22635993e10f4fb1e")]
        [TestCase("excluded-names", FakeGitHub.NestedHashV2)]
        [TestCase("node-modules", "3d68a88269389b91db62400268f097950df54381119b208fb9f67c06c40547e9")]
        [TestCase("symlinks", SymlinksTreeHashV2)]
        [TestCase("non-ascii-names", "bd67a1749bf95d0762d69259e9a1b40b624be3fa0026b265b41524ddc5d3d43a")]
        public void ComputeVersion2_GeneratedTree_MatchesSkillsCliHash(string tree, string expectedHash)
        {
            var parent = Path.Combine(Path.GetTempPath(), "SkillFolderHashTests-" + Path.GetRandomFileName());
            try
            {
                var skill = BuildGeneratedTree(tree, parent);

                Assert.That(SkillFolderHash.Compute(skill, SkillHashAlgorithm.Version2), Is.EqualTo(expectedHash));
            }
            finally
            {
                TempDirectory.Delete(parent);
            }
        }

        // hashSkillFiles turns "\\" into "/" before hashing; vector from `skills@1.8.1 add` over minimal plus "a\\b.md"
        // ("back\n"), which only a Unix file system can hold.
        [Test]
        public void ComputeVersion2_BackslashInAFileName_HashesAsAPathSeparator()
        {
            if (Path.DirectorySeparatorChar == '\\') Assert.Ignore("Windows file names can't hold a backslash.");
            var skill = Path.Combine(Path.GetTempPath(), "SkillFolderHashTests-" + Path.GetRandomFileName());
            try
            {
                CopyDirectory(Path.Combine(FixturesRoot, "minimal"), skill);
                File.WriteAllText(Path.Combine(skill, "a\\b.md"), "back\n");

                Assert.That(SkillFolderHash.Compute(skill, SkillHashAlgorithm.Version2),
                    Is.EqualTo("2a3930064debcd7bfadb346c22a64e4f5f37f0112e1e4deeeeae49a079ef74b4"));
            }
            finally
            {
                if (Directory.Exists(skill)) Directory.Delete(skill, recursive: true);
            }
        }

        // Version 2 normalises CRLF, so a CRLF checkout of a skill hashes like the LF original.
        [Test]
        public void ComputeVersion2_CrlfCheckout_HashesLikeTheOriginal()
        {
            var skill = Path.Combine(Path.GetTempPath(), "SkillFolderHashTests-" + Path.GetRandomFileName());
            try
            {
                CopyDirectory(Path.Combine(FixturesRoot, "nested"), skill);
                SkillFolderHash.ConvertToCrlfCheckout(skill);

                Assert.That(SkillFolderHash.Compute(skill, SkillHashAlgorithm.Version2), Is.EqualTo(FakeGitHub.NestedHashV2));
            }
            finally
            {
                if (Directory.Exists(skill)) Directory.Delete(skill, recursive: true);
            }
        }

        /// <summary>The CLI's version 2 hash of generated-trees.json's "symlinks" tree.</summary>
        internal const string SymlinksTreeHashV2 = "fa7a5562ef1c54c988560e9d66d94b5ade9b9ecf48a65272ec7c41146eefc9b0";

        /// <summary>generated-trees.json's tree <paramref name="name"/>: its base fixture, files, dirs, links and outside files.</summary>
        internal static List<KeyValuePair<string, object>> GeneratedTree(string name)
        {
            var spec = (List<KeyValuePair<string, object>>)JsonReader.Read(File.ReadAllText(Path.Combine(FixturesRoot, "generated-trees.json")));
            return Member(Member(spec, "trees"), name);
        }

        /// <summary>Builds generated-trees.json's <paramref name="name"/> as generate-hashes.mjs does; returns the skill folder.</summary>
        static string BuildGeneratedTree(string name, string parent)
        {
            var tree = GeneratedTree(name);
            var skill = Path.Combine(parent, name);
            CopyDirectory(Path.Combine(FixturesRoot, (string)Value(tree, "base")), skill);
            WriteAll(skill, Value(tree, "files"));
            WriteAll(parent, Value(tree, "outside"));
            if (Value(tree, "dirs") is List<object> dirs)
                foreach (var dir in dirs.Cast<string>()) Directory.CreateDirectory(Path.Combine(skill, dir));
            if (Value(tree, "links") is List<KeyValuePair<string, object>> links)
                foreach (var link in links)
                    try
                    {
                        CreateSymlink(Path.Combine(skill, link.Key), (string)link.Value);
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is PlatformNotSupportedException)
                    {
                        Assert.Ignore($"Can't create symlinks here: {e.Message}");
                    }
            return skill;
        }

        static void WriteAll(string root, object files)
        {
            if (!(files is List<KeyValuePair<string, object>> members)) return;
            foreach (var file in members)
            {
                var path = Path.Combine(root, file.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, Encoding.UTF8.GetBytes((string)file.Value));
            }
        }

        // A symlink written as is (relative target), like Node's fs.symlinkSync; a folder link when the target is one.
        static void CreateSymlink(string path, string target)
        {
            var resolved = Path.Combine(Path.GetDirectoryName(path), target);
            var ok = Path.DirectorySeparatorChar == '\\'
                ? CreateSymbolicLinkW(path, target.Replace('/', '\\'), (Directory.Exists(resolved) ? 1 : 0) | 2)
                : symlink(target, path) == 0;
            if (!ok) throw new IOException($"Could not create a symlink at {path}.");
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern bool CreateSymbolicLinkW(string link, string target, int flags);

        [System.Runtime.InteropServices.DllImport("libc")]
        static extern int symlink(string target, string link);

        internal static List<KeyValuePair<string, object>> Member(List<KeyValuePair<string, object>> obj, string key) =>
            (List<KeyValuePair<string, object>>)Value(obj, key);

        internal static object Value(List<KeyValuePair<string, object>> obj, string key) => obj.FirstOrDefault(m => m.Key == key).Value;

        static void Write(string path, string contents)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, contents);
        }

        static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            foreach (var directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
