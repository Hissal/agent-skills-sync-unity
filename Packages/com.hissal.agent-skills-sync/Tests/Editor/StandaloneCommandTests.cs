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
            var environment = new UserEnvironment(_build, _ => null, _build);
            var builder = new RestoreCommand(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Project), "../..")), "test", environment);
            if (!builder.HasSdk()) Assert.Ignore("The .NET SDK (dotnet 8 or newer) is not installed.");
            _command = builder.Build();
            Assert.That(File.Exists(_command), Is.True);
            Assert.That(builder.Build(), Is.EqualTo(_command), "A completed version build is reused.");
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

        [Test]
        public void Hook_InstallStatusUninstall_ReportJsonAndRestoreBeforeWorktreeAddReturns()
        {
            var (code, json) = Command("hook", "install", "--project", _repo.Root);
            Assert.That(code, Is.EqualTo(0));
            Assert.That(Member(json, "status"), Is.EqualTo("installed"));
            Assert.That(Member(Command("hook", "status", "--project", _repo.Root).Json, "status"), Is.EqualTo("installed"));
            // Git inherits the same machine state the command used.
            var previous = Environment.GetEnvironmentVariable(UserEnvironment.StateDirectoryVariable);
            Environment.SetEnvironmentVariable(UserEnvironment.StateDirectoryVariable, _environment.StateDirectory);
            try
            {
                var worktree = Path.Combine(_root, "automatic worktree");
                var outcome = GitTestRepo.Run(_repo.Root, "worktree", "add", "-q", "--detach", worktree);
                Assert.That(outcome.Trim(), Does.EndWith(": restored"));
                var skill = Path.Combine(worktree, ".agents", "skills", Skill, "SKILL.md");
                Assert.That(File.Exists(skill), Is.True, "The hook must restore before git returns.");
                File.WriteAllText(skill, "local edit");
                GitTestRepo.Run(worktree, "checkout", "-q", "--detach", "HEAD");
                Assert.That(File.ReadAllText(skill), Is.EqualTo("local edit"), "Branch switches must leave skills alone.");
                Directory.Delete(Path.Combine(_environment.StateDirectory, "snapshots"), true);
                var failed = Path.Combine(_root, "missing snapshots");
                var failure = GitTestRepo.Run(_repo.Root, "worktree", "add", "-q", "--detach", failed);
                Assert.That(failure, Does.Contain("snapshotsMissing").And.Contain("sync --project"));
                Assert.That(failure.Trim().Split('\n'), Has.Length.EqualTo(1));
                Assert.That(File.Exists(Path.Combine(failed, Lockfile.FileName)), Is.True, "Restore failure must not fail checkout.");
                Assert.That(File.Exists(Path.Combine(failed, ".agents", "skills", Skill, "SKILL.md")), Is.False);
            }
            finally { Environment.SetEnvironmentVariable(UserEnvironment.StateDirectoryVariable, previous); }
            Assert.That(Command("hook", "uninstall", "--project", _repo.Root).Code, Is.EqualTo(0));
            Assert.That(Member(Command("hook", "status", "--project", _repo.Root).Json, "status"), Is.EqualTo("notInstalled"));
        }

        [Test]
        public void Hook_SeveralProjectsWithSpacesAndQuotes_RestoresEachAndSkipsWhenLockIsRemoved()
        {
            // Ignore local preferences before committing the subprojects.
            var ignore = "UserSettings/\n**/UserSettings/\n";
            _repo.Commit("projects", (".gitignore", ignore), ("game one/skills-lock.json", Lock), ("game 'two/skills-lock.json", Lock));
            foreach (var name in new[] { "game one", "game 'two" })
            {
                var project = Path.Combine(_repo.Root, name);
                var prefs = LocalPrefs.Load(project);
                FolderSelection.Save(prefs, FolderLayout.Default, new[] { FolderLayout.Default.Canonical }, _environment);
                prefs.Save();
                var github = new FakeGitHub().Fixture("owner/skills", "skills/" + Skill, "minimal");
                var service = new SkillsSyncService(project, _environment,
                    m => new GitHubSkillFetcher(Path.Combine(_root, "cache"), github, m));
                service.Run(service.NewSources);
                Assert.That(Command("hook", "install", "--project", project).Code, Is.EqualTo(0));
            }
            var previous = Environment.GetEnvironmentVariable(UserEnvironment.StateDirectoryVariable);
            Environment.SetEnvironmentVariable(UserEnvironment.StateDirectoryVariable, _environment.StateDirectory);
            try
            {
                var worktree = _repo.AddWorktree(Path.Combine(_root, "multi"));
                foreach (var name in new[] { "game one", "game 'two" })
                    Assert.That(File.Exists(Path.Combine(worktree, name, ".agents", "skills", Skill, "SKILL.md")), Is.True);
                // A repository that has removed skill syncing must not gain any files from the hook.
                foreach (var file in Directory.GetFiles(_repo.Root, Lockfile.FileName, SearchOption.AllDirectories)) File.Delete(file);
                _repo.Commit("remove locks");
                var empty = _repo.AddWorktree(Path.Combine(_root, "without-locks"));
                Assert.That(File.Exists(LocalPrefs.PathFor(Path.Combine(empty, "game one"))), Is.False);
                Assert.That(File.Exists(Path.Combine(empty, "game one", ".agents", "skills", Skill, "SKILL.md")), Is.False);
            }
            finally { Environment.SetEnvironmentVariable(UserEnvironment.StateDirectoryVariable, previous); }
        }

        [Test]
        public void Hook_BlockedHooksPath_ReportsActionRequiredWithoutChangingIt()
        {
            GitTestRepo.Run(_repo.Root, "config", "core.hooksPath", ".githooks");
            foreach (var action in new[] { "install", "status", "uninstall" })
            {
                var (code, json) = Command("hook", action, "--project", _repo.Root);
                Assert.That(code, Is.EqualTo(3));
                Assert.That(Member(json, "status"), Is.EqualTo("blocked"));
                Assert.That(Member(json, "message"), Does.Contain("core.hooksPath"));
            }
            Assert.That(Directory.Exists(Path.Combine(_repo.Root, ".githooks")), Is.False);
        }

        [Test]
        public void HookOutput_NewSource_NamesSourceAndRecoveryCommandOnOneLine()
        {
            var worktree = _repo.AddWorktree(Path.Combine(_root, "new-source"));
            File.WriteAllText(Path.Combine(worktree, Lockfile.FileName), Lock.Replace("owner/skills", "new/skills"));
            var (code, stdout, stderr) = Run("dotnet", "\"" + _command + "\" restore --hook-output --project \"" + worktree + "\"", _environment.StateDirectory);
            Assert.That(code, Is.EqualTo(3), stderr);
            Assert.That(stdout, Does.Contain("sourcesNeedConfirmation").And.Contain("--confirm-source 'new/skills'"));
            Assert.That(stdout.Trim().Split('\n'), Has.Length.EqualTo(1));
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
