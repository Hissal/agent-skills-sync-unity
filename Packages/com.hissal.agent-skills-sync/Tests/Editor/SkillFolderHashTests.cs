using System.IO;
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
