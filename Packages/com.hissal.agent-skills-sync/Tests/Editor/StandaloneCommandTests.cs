using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>
    /// Runs the standalone command (<c>Tools~/AgentSkillsSync.Cli</c>) as its own process, outside Unity, the way a
    /// worktree launcher would. Built once with the .NET SDK; ignored where <c>dotnet</c> is not installed.
    /// </summary>
    public class StandaloneCommandTests
    {
        // A name no contributor has at user scope, since the command scans the real home.
        const string Skill = "asr-standalone-probe";

        const string Lock = @"{
  ""version"": 1,
  ""skills"": {
    """ + Skill + @""": { ""source"": ""owner/skills"", ""sourceType"": ""github"", ""skillPath"": ""skills/" + Skill + @"/SKILL.md"", ""computedHash"": """ + FakeGitHub.MinimalHash + @""" }
  }
}";

        static readonly string Project = Path.GetFullPath("Packages/com.hissal.agent-skills-sync/Tools~/AgentSkillsSync.Cli/AgentSkillsSync.Cli.csproj");

        string _build;
        string _command;
        string _root;
        GitTestRepo _repo;
        UserEnvironment _environment;

        [OneTimeSetUp]
        public void BuildCommand()
        {
            _build = Path.Combine(Path.GetTempPath(), "AgentSkillsStandaloneBuild", Guid.NewGuid().ToString("N"));
            var (code, stdout, stderr) = Run("dotnet", $"build \"{Project}\" -nologo -v q --artifacts-path \"{_build}\"", null);
            if (code == -1) Assert.Ignore("The .NET SDK (dotnet) is not installed.");
            Assert.That(code, Is.EqualTo(0), stdout + stderr);
            _command = Directory.GetFiles(Path.Combine(_build, "bin"), "agent-skills-sync.dll", SearchOption.AllDirectories).Single();
        }

        [OneTimeTearDown]
        public void DeleteBuild() => TempDirectory.Delete(_build);

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "AgentSkillsStandaloneTests", Guid.NewGuid().ToString("N"));
            var home = Path.Combine(_root, "home");
            Directory.CreateDirectory(Path.Combine(home, ".codex"));
            _environment = new UserEnvironment(home, _ => null, Path.Combine(_root, "state"));
            _repo = GitTestRepo.Create(Path.Combine(_root, "repo"), (Lockfile.FileName, Lock));

            // The main checkout chose .agents/skills only and synced, recording a snapshot.
            var prefs = LocalPrefs.Load(_repo.Root);
            FolderSelection.Save(prefs, FolderLayout.Default, new[] { FolderLayout.Default.Canonical }, _environment);
            prefs.Save();
            var github = new FakeGitHub().Fixture("owner/skills", "skills/" + Skill, "minimal");
            var service = new SkillsSyncService(_repo.Root, _environment,
                m => new GitHubSkillFetcher(Path.Combine(_root, "cache"), github, m));
            service.Run(service.NewSources);
        }

        [TearDown]
        public void TearDown() => TempDirectory.Delete(_root);

        (int Code, List<KeyValuePair<string, object>> Json) Command(params string[] args)
        {
            var (code, stdout, stderr) = Run("dotnet", "\"" + _command + "\" " + string.Join(" ", args.Select(a => "\"" + a + "\"")),
                _environment.StateDirectory);
            var json = JsonReader.Read(stdout) as List<KeyValuePair<string, object>>;
            Assert.That(json, Is.Not.Null, stdout + stderr);
            return (code, json);
        }

        static object Member(List<KeyValuePair<string, object>> json, string key) => json.Single(m => m.Key == key).Value;

        [Test]
        public void Restore_FreshWorktree_InstallsAndReportsRestoredWithExitCodeZero()
        {
            var worktree = _repo.AddWorktree(Path.Combine(_root, "elsewhere", "wt"));

            var (code, json) = Command("restore", "--project", worktree);

            Assert.That(code, Is.EqualTo(0));
            Assert.That(Member(json, "status"), Is.EqualTo("restored"));
            var summary = (List<KeyValuePair<string, object>>)Member(json, "summary");
            Assert.That(Member(summary, "installed"), Is.EqualTo(new object[] { Skill }));
            Assert.That(File.Exists(Path.Combine(worktree, ".agents", "skills", Skill, "SKILL.md")), Is.True);
        }

        [Test]
        public void RestoreDryRun_ListsActionsAndChangesNothing()
        {
            var worktree = _repo.AddWorktree(Path.Combine(_root, "elsewhere", "wt"));

            var (code, json) = Command("restore", "--project", worktree, "--dry-run");

            Assert.That(code, Is.EqualTo(0));
            Assert.That(Member(json, "status"), Is.EqualTo("ready"));
            Assert.That(Member(json, "dryRun"), Is.EqualTo(true));
            var action = (List<KeyValuePair<string, object>>)((List<object>)Member(json, "actions")).Single();
            Assert.That(Member(action, "kind"), Is.EqualTo("install"));
            Assert.That(Directory.Exists(Path.Combine(worktree, ".agents")), Is.False);
            Assert.That(File.Exists(LocalPrefs.PathFor(worktree)), Is.False);
        }

        [Test]
        public void Restore_NewSource_ListsItWithExitCodeThree()
        {
            var worktree = _repo.AddWorktree(Path.Combine(_root, "elsewhere", "wt"));
            File.WriteAllText(Path.Combine(worktree, Lockfile.FileName), Lock.Replace("owner/skills", "new/skills"));

            var (code, json) = Command("restore", "--project", worktree);

            Assert.That(code, Is.EqualTo(3));
            Assert.That(Member(json, "status"), Is.EqualTo("sourcesNeedConfirmation"));
            Assert.That(Member(json, "unconfirmedSources"), Is.EqualTo(new object[] { "new/skills" }));
        }

        [Test]
        public void Restore_ProjectInNoGitCheckout_IsInvalidWithExitCodeTwo()
        {
            var loose = Path.Combine(_root, "loose");
            Directory.CreateDirectory(loose);
            File.WriteAllText(Path.Combine(loose, Lockfile.FileName), Lock);

            var (code, json) = Command("restore", "--project", loose);

            Assert.That(code, Is.EqualTo(2));
            Assert.That(Member(json, "status"), Is.EqualTo("invalid"));
            Assert.That((string)Member(json, "error"), Does.Contain("not in a Git checkout"));
        }

        [Test]
        public void UnknownOption_IsInvalidWithExitCodeTwo()
        {
            var (code, json) = Command("restore", "--everything");

            Assert.That(code, Is.EqualTo(2));
            Assert.That(Member(json, "status"), Is.EqualTo("invalid"));
        }

        /// <summary>Runs a process to completion; code -1 when it cannot start.</summary>
        static (int Code, string Stdout, string Stderr) Run(string file, string arguments, string stateDirectory)
        {
            var start = new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            start.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            start.EnvironmentVariables["DOTNET_NOLOGO"] = "1";
            if (stateDirectory != null) start.EnvironmentVariables[UserEnvironment.StateDirectoryVariable] = stateDirectory;
            try
            {
                using (var process = Process.Start(start))
                {
                    var stderr = process.StandardError.ReadToEndAsync();
                    var stdout = process.StandardOutput.ReadToEnd();
                    if (!process.WaitForExit(300000))
                    {
                        process.Kill();
                        throw new TimeoutException($"{file} {arguments} did not finish.");
                    }
                    return (process.ExitCode, stdout, stderr.Result);
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return (-1, "", "");
            }
        }
    }
}
