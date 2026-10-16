using System;
using System.IO;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class ManagedStateFileTests
    {
        string _root;
        string _path;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _path = Path.Combine(_root, ManagedStateFile.FileName);
        }

        [TearDown]
        public void TearDown()
        {
            TempDirectory.Delete(_root);
        }

        /// <summary>Rewrites the tool-written file with CRLF line endings, as a Windows checkout of it would.</summary>
        void ConvertToCrlf()
        {
            File.WriteAllText(_path, File.ReadAllText(_path).Replace("\n", "\r\n"));
        }

        [Test]
        public void Write_UnchangedBlockInCrlfFile_LeavesTheFileUntouched()
        {
            ManagedStateFile.Write(_root, new[] { "code-review", "tdd" });
            ConvertToCrlf();
            var bytes = File.ReadAllBytes(_path);
            var lastWrite = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(_path, lastWrite);

            ManagedStateFile.Write(_root, new[] { "code-review", "tdd" });

            Assert.That(File.ReadAllBytes(_path), Is.EqualTo(bytes));
            Assert.That(File.GetLastWriteTimeUtc(_path), Is.EqualTo(lastWrite));
        }

        [Test]
        public void Write_ChangedBlockInCrlfFile_RewritesWithCrlfKeepingUserLines()
        {
            File.WriteAllText(_path, "# my rules\n");
            ManagedStateFile.Write(_root, new[] { "old-skill" });
            File.AppendAllText(_path, "keep-me\n");
            ConvertToCrlf();

            ManagedStateFile.Write(_root, new[] { "new-skill" });

            var text = File.ReadAllText(_path);
            Assert.That(text.Replace("\r\n", ""), Does.Not.Contain("\n"), "expected CRLF throughout");
            Assert.That(text, Does.StartWith("# my rules\r\n"));
            Assert.That(text, Does.EndWith("keep-me\r\n"));
            Assert.That(text, Does.Contain("/new-skill\r\n"));
            Assert.That(text, Does.Not.Contain("old-skill"));
        }

        [Test]
        public void Write_ChangedBlockInLfFile_StaysLf()
        {
            ManagedStateFile.Write(_root, new[] { "old-skill" });

            ManagedStateFile.Write(_root, new[] { "new-skill" });

            var text = File.ReadAllText(_path);
            Assert.That(text, Does.Not.Contain("\r"));
            Assert.That(text, Does.Contain("/new-skill\n"));
        }

        [Test]
        public void Write_NewFile_UsesLf()
        {
            ManagedStateFile.Write(_root, new[] { "tdd" });

            var text = File.ReadAllText(_path);
            Assert.That(text, Does.Not.Contain("\r"));
            Assert.That(text, Does.EndWith("\n"));
        }

        [Test]
        public void Read_CrlfFile_ReturnsTheNames()
        {
            ManagedStateFile.Write(_root, new[] { "tdd", "code-review" });
            ConvertToCrlf();

            Assert.That(ManagedStateFile.Read(_root), Is.EqualTo(new[] { "code-review", "tdd" }));
        }
    }
}
