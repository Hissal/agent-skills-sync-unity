using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class LockfileTests
    {
        [Test]
        public void ManagedState_UnsafeRecordedName_IsIgnored()
        {
            var root = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            try
            {
                ManagedStateFile.Write(root, new[] { "tdd" });
                var path = Path.Combine(root, ManagedStateFile.FileName);
                File.WriteAllText(path, File.ReadAllText(path).Replace("/tdd", "/../../Assets\n/bad:name\n/tdd"));

                Assert.That(ManagedStateFile.Read(root), Is.EqualTo(new[] { "tdd" }));
            }
            finally
            {
                TempDirectory.Delete(root);
            }
        }

        [TestCase("../../Assets")]
        [TestCase("C:\\Assets")]
        public void PlanAction_UnsafeName_RejectsEveryStringFactory(string name)
        {
            var folder = FolderLayout.Default.Canonical;
            Assert.That(() => PlanAction.Link(name, folder, folder), Throws.TypeOf<LockfileException>());
            Assert.That(() => PlanAction.Remove(name, folder), Throws.TypeOf<LockfileException>());
            Assert.That(() => PlanAction.Unlink(name, folder), Throws.TypeOf<LockfileException>());
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(".")]
        [TestCase("..")]
        [TestCase("../../Assets")]
        [TestCase("/absolute")]
        [TestCase("C:\\Assets")]
        [TestCase("bad\\name")]
        [TestCase("bad:name")]
        [TestCase("bad*name")]
        [TestCase("bad?name")]
        [TestCase("bad\"name")]
        [TestCase("bad<name")]
        [TestCase("bad>name")]
        [TestCase("bad|name")]
        [TestCase("bad\u001fname")]
        [TestCase("trailing.")]
        [TestCase("trailing ")]
        public void Construct_UnsafeName_RejectsBothSkillTypes(string name)
        {
            Assert.That(() => new LockedSkill(name, "owner/repo", "github", null, null),
                Throws.TypeOf<LockfileException>().With.Message.Contains("safe skill folder name"));
            Assert.That(() => new UnsupportedSkill(name, "unity-package"),
                Throws.TypeOf<LockfileException>().With.Message.Contains("safe skill folder name"));
        }

        [TestCase("code-review")]
        [TestCase("a..b")]
        [TestCase(".hidden")]
        [TestCase("two words")]
        public void Construct_SafeName_PreservesBothSkillNames(string name)
        {
            Assert.That(new LockedSkill(name, "owner/repo", "github", null, null).Name, Is.EqualTo(name));
            Assert.That(new UnsupportedSkill(name, "unity-package").Name, Is.EqualTo(name));
        }

        const string ValidLock = @"{
  ""version"": 1,
  ""skills"": {
    ""code-review"": {
      ""source"": ""mattpocock/skills"",
      ""sourceType"": ""github"",
      ""skillPath"": ""skills/engineering/code-review/SKILL.md"",
      ""computedHash"": ""caa9a086baaf9e0f7cd71f64edfa83da6821c05e826b083221f3d02e3d6a1905""
    },
    ""localization"": {
      ""source"": ""Unity-Technologies/skills"",
      ""sourceType"": ""github"",
      ""skillPath"": ""skills/localization/SKILL.md"",
      ""computedHash"": ""7a4c4a94c7ef859a96b322b261b7e406467ebd7f23f5e3bfb190c240a7948f3a"",
      ""installedHash"": ""221d480e61a774a659f04672fd238914ce4fe8e2704f08ff3f601c6b863393ea""
    }
  }
}";

        [Test]
        public void Parse_ValidLock_ReturnsEveryLockedSkill()
        {
            var lockfile = Lockfile.Parse(ValidLock);

            Assert.That(lockfile.Skills, Has.Count.EqualTo(2));
            var codeReview = lockfile.Skills[0];
            Assert.That(codeReview.Name, Is.EqualTo("code-review"));
            Assert.That(codeReview.Source, Is.EqualTo("mattpocock/skills"));
            Assert.That(codeReview.SourceType, Is.EqualTo("github"));
            Assert.That(codeReview.SkillPath, Is.EqualTo("skills/engineering/code-review/SKILL.md"));
            Assert.That(codeReview.ComputedHash, Is.EqualTo("caa9a086baaf9e0f7cd71f64edfa83da6821c05e826b083221f3d02e3d6a1905"));
            Assert.That(lockfile.Skills[1].Name, Is.EqualTo("localization"));
            Assert.That(lockfile.Skills[1].Source, Is.EqualTo("Unity-Technologies/skills"));
        }

        [Test]
        public void Parse_UnknownVersion_RejectsNamingTheVersion()
        {
            const string json = @"{ ""version"": 2, ""skills"": {} }";

            var error = Assert.Throws<LockfileException>(() => Lockfile.Parse(json));

            Assert.That(error.Message, Does.Contain("version 2"));
        }

        [Test]
        public void Parse_MissingVersion_Rejects()
        {
            Assert.Throws<LockfileException>(() => Lockfile.Parse(@"{ ""skills"": {} }"));
        }

        [Test]
        public void Parse_NonGitHubSourceType_ListsItAsUnsupportedAndKeepsTheRest()
        {
            const string json = @"{
  ""version"": 1,
  ""skills"": {
    ""tdd"": { ""source"": ""owner/skills"", ""sourceType"": ""github"", ""computedHash"": ""abc"" },
    ""unity-pipeline"": { ""source"": ""com.unity.pipeline"", ""sourceType"": ""unity-package"", ""packageVersion"": ""0.7.0-exp.1"", ""skillPath"": "".claude/skills/unity-pipeline/SKILL.md"" },
    ""local-thing"": { ""source"": ""./skills/local-thing"", ""sourceType"": ""local"" }
  }
}";

            var lockfile = Lockfile.Parse(json);

            Assert.That(lockfile.Skills.Select(s => s.Name), Is.EqualTo(new[] { "tdd" }));
            Assert.That(lockfile.Unsupported.Select(s => $"{s.Name}:{s.SourceType}"),
                Is.EqualTo(new[] { "unity-pipeline:unity-package", "local-thing:local" }));
        }

        [TestCase(@"", TestName = "Parse_MissingSourceType_Rejects")]
        [TestCase(@", ""sourceType"": """"", TestName = "Parse_EmptySourceType_Rejects")]
        [TestCase(@", ""sourceType"": 3", TestName = "Parse_NonStringSourceType_Rejects")]
        public void Parse_NoUsableSourceType_RejectsNamingTheSkill(string jsonSourceTypeMember)
        {
            var json = @"{ ""version"": 1, ""skills"": { ""a"": { ""source"": ""owner/repo""" + jsonSourceTypeMember + @" } } }";

            var error = Assert.Throws<LockfileException>(() => Lockfile.Parse(json));

            Assert.That(error.Message, Does.Contain("\"a\"").And.Contain("source type"));
        }

        [TestCase(@"{ ""version"": 1, ""skills"": { ")]
        [TestCase(@"{ ""version"": 1 ""skills"": {} }")]
        [TestCase("not json")]
        [TestCase("")]
        public void Parse_MalformedJson_RejectsAsInvalidJson(string json)
        {
            var error = Assert.Throws<LockfileException>(() => Lockfile.Parse(json));

            Assert.That(error.Message, Does.Contain("not valid JSON"));
        }

        static string LockWithSkillNamed(string jsonEscapedName) =>
            @"{ ""version"": 1, ""skills"": { """ + jsonEscapedName + @""": { ""source"": ""owner/repo"", ""sourceType"": ""github"", ""computedHash"": ""abc"" } } }";

        [TestCase("../../Assets", TestName = "Parse_NameWithParentTraversal_Rejects")]
        [TestCase("..", TestName = "Parse_NameDotDot_Rejects")]
        [TestCase(".", TestName = "Parse_NameDot_Rejects")]
        [TestCase("", TestName = "Parse_EmptyName_Rejects")]
        [TestCase("nested/skill", TestName = "Parse_NameWithForwardSlash_Rejects")]
        [TestCase(@"nested\\skill", TestName = "Parse_NameWithBackslash_Rejects")]
        [TestCase("/etc", TestName = "Parse_RootedUnixName_Rejects")]
        [TestCase(@"C:\\Users", TestName = "Parse_RootedWindowsName_Rejects")]
        [TestCase("C:skill", TestName = "Parse_DriveRelativeName_Rejects")]
        [TestCase("a*b", TestName = "Parse_NameWithWildcard_Rejects")]
        [TestCase("a|b", TestName = "Parse_NameWithPipe_Rejects")]
        [TestCase(@"a\u0000b", TestName = "Parse_NameWithNulChar_Rejects")]
        [TestCase("skill.", TestName = "Parse_NameWithTrailingDot_Rejects")]
        [TestCase("skill ", TestName = "Parse_NameWithTrailingSpace_Rejects")]
        public void Parse_NameThatIsNotASinglePathComponent_RejectsAsUnsafeName(string jsonEscapedName)
        {
            var error = Assert.Throws<LockfileException>(() => Lockfile.Parse(LockWithSkillNamed(jsonEscapedName)));

            Assert.That(error.Message, Does.Contain("not a safe skill folder name"));
        }

        [Test]
        public void Parse_UnsupportedEntryWithUnsafeName_RejectsAsUnsafeName()
        {
            const string json = @"{ ""version"": 1, ""skills"": { ""safe\n!Assets"": { ""source"": ""com.unity.pipeline"", ""sourceType"": ""unity-package"" } } }";

            var error = Assert.Throws<LockfileException>(() => Lockfile.Parse(json));

            Assert.That(error.Message, Does.Contain("not a safe skill folder name"));
        }

        [TestCase("code-review")]
        [TestCase(".hidden-skill")]
        [TestCase("skill.v2")]
        [TestCase("a b")]
        public void Parse_PlainFolderName_Accepts(string name)
        {
            var lockfile = Lockfile.Parse(LockWithSkillNamed(name));

            Assert.That(lockfile.Skills[0].Name, Is.EqualTo(name));
        }

        static string LockWithRef(string jsonRefMember) =>
            @"{ ""version"": 1, ""skills"": { ""a"": { ""source"": ""owner/repo"", ""sourceType"": ""github"", ""computedHash"": ""abc""" +
            jsonRefMember + @" } } }";

        [TestCase("v1.2.0")]
        [TestCase("main")]
        [TestCase("feature/new-skill")]
        [TestCase("0123abcd")]
        public void Parse_Ref_KeepsIt(string reference)
        {
            var lockfile = Lockfile.Parse(LockWithRef(@", ""ref"": """ + reference + @""""));

            Assert.That(lockfile.Skills[0].Ref, Is.EqualTo(reference));
        }

        [TestCase("", TestName = "Parse_NoRef_RefIsNull")]
        [TestCase(@", ""ref"": """"", TestName = "Parse_EmptyRef_RefIsNull")]
        [TestCase(@", ""ref"": null", TestName = "Parse_NullRef_RefIsNull")]
        public void Parse_NoUsableRef_RefIsNull(string jsonRefMember)
        {
            var lockfile = Lockfile.Parse(LockWithRef(jsonRefMember));

            Assert.That(lockfile.Skills[0].Ref, Is.Null);
        }

        [TestCase("../main", TestName = "Parse_RefWithParentTraversal_Rejects")]
        [TestCase("a..b", TestName = "Parse_RefWithDoubleDot_Rejects")]
        [TestCase("/main", TestName = "Parse_RefWithLeadingSlash_Rejects")]
        [TestCase("main/", TestName = "Parse_RefWithTrailingSlash_Rejects")]
        [TestCase("a//b", TestName = "Parse_RefWithEmptyComponent_Rejects")]
        [TestCase("a b", TestName = "Parse_RefWithSpace_Rejects")]
        [TestCase(@"a\\b",TestName = "Parse_RefWithBackslash_Rejects")]
        [TestCase("a?b", TestName = "Parse_RefWithQuestionMark_Rejects")]
        [TestCase("a#b", TestName = "Parse_RefWithHash_Rejects")]
        [TestCase("a:b", TestName = "Parse_RefWithColon_Rejects")]
        [TestCase("main.lock", TestName = "Parse_RefEndingInLock_Rejects")]
        public void Parse_UnsafeRef_RejectsNamingTheSkill(string reference)
        {
            var error = Assert.Throws<LockfileException>(() => Lockfile.Parse(LockWithRef(@", ""ref"": """ + reference + @"""")));

            Assert.That(error.Message, Does.Contain("\"a\"").And.Contain("ref"));
        }

        [Test]
        public void Parse_NonStringRef_Rejects()
        {
            Assert.Throws<LockfileException>(() => Lockfile.Parse(LockWithRef(@", ""ref"": 3")));
        }

        [Test]
        public void FindRoot_LockInUnityProject_ReturnsTheUnityProject()
        {
            WithRepo((repo, unityProject) =>
            {
                File.WriteAllText(Path.Combine(unityProject, Lockfile.FileName), ValidLock);
                File.WriteAllText(Path.Combine(repo, Lockfile.FileName), ValidLock);

                Assert.That(Lockfile.FindRoot(unityProject), Is.EqualTo(unityProject));
            });
        }

        [Test]
        public void FindRoot_LockOnlyInTheFolderAbove_ReturnsThatFolder()
        {
            WithRepo((repo, unityProject) =>
            {
                File.WriteAllText(Path.Combine(repo, Lockfile.FileName), ValidLock);

                Assert.That(Lockfile.FindRoot(unityProject), Is.EqualTo(repo));
                Assert.That(Lockfile.FindRoot(unityProject + Path.DirectorySeparatorChar), Is.EqualTo(repo));
            });
        }

        [Test]
        public void FindRoot_LockTwoFoldersAbove_ReturnsNull()
        {
            WithRepo((repo, unityProject) =>
            {
                var nested = Path.Combine(unityProject, "Nested");
                Directory.CreateDirectory(nested);
                File.WriteAllText(Path.Combine(repo, Lockfile.FileName), ValidLock);

                Assert.That(Lockfile.FindRoot(nested), Is.Null);
            });
        }

        [Test]
        public void FindRoot_NoLock_ReturnsNull()
        {
            WithRepo((_, unityProject) => Assert.That(Lockfile.FindRoot(unityProject), Is.Null));
        }

        /// <summary>Runs <paramref name="test"/> on a temp repo folder with a Unity project folder inside it.</summary>
        static void WithRepo(Action<string, string> test)
        {
            var repo = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            var unityProject = Path.Combine(repo, "UnityProject");
            Directory.CreateDirectory(unityProject);
            try
            {
                test(repo, unityProject);
            }
            finally
            {
                TempDirectory.Delete(repo);
            }
        }
    }
}
