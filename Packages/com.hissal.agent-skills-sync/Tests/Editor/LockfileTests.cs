using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class LockfileTests
    {
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
        public void Parse_NonGitHubSourceType_RejectsNamingTheSkillAndType()
        {
            const string json = @"{
  ""version"": 1,
  ""skills"": {
    ""local-thing"": { ""source"": ""./skills/local-thing"", ""sourceType"": ""local"", ""computedHash"": ""abc"" }
  }
}";

            var error = Assert.Throws<LockfileException>(() => Lockfile.Parse(json));

            Assert.That(error.Message, Does.Contain("local-thing").And.Contain("\"local\""));
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

        [TestCase("code-review")]
        [TestCase(".hidden-skill")]
        [TestCase("skill.v2")]
        [TestCase("a b")]
        public void Parse_PlainFolderName_Accepts(string name)
        {
            var lockfile = Lockfile.Parse(LockWithSkillNamed(name));

            Assert.That(lockfile.Skills[0].Name, Is.EqualTo(name));
        }
    }
}
