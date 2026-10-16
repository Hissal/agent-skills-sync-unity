using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class RestoreHookTests
    {
        string _root;
        GitTestRepo _repo;
        string _command;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "RestoreHookTests", Guid.NewGuid().ToString("N"));
            _repo = GitTestRepo.Create(Path.Combine(_root, "repo"), ("readme", "test"));
            _command = Path.Combine(_root, "command.dll");
            File.WriteAllText(_command, "probe");
        }

        [TearDown]
        public void TearDown() => TempDirectory.Delete(_root);

        [TestCase("\n")]
        [TestCase("\r\n")]
        public void Install_ReinstallAndUninstall_KeepForeignHookAndRunItInNewWorktrees(string newline)
        {
            var path = Path.Combine(_repo.Root, ".git", "hooks", "post-checkout");
            var foreign = "#!/bin/sh\nprintf foreign > foreign-ran\nexit 0\n".Replace("\n", newline);
            File.WriteAllText(path, foreign);
            var hook = new RestoreHook(_repo.Root, _command, "1.0.0");

            Assert.That(hook.Status().Status, Is.EqualTo(RestoreHookStatus.NotInstalled));
            Assert.That(hook.Install().Status, Is.EqualTo(RestoreHookStatus.Installed));
            hook.Install();
            var worktree = _repo.AddWorktree(Path.Combine(_root, "wt"));
            Assert.That(File.ReadAllText(Path.Combine(worktree, "foreign-ran")), Is.EqualTo("foreign"));
            Assert.That(new RestoreHook(worktree, _command, "1.0.0").Status().Status, Is.EqualTo(RestoreHookStatus.Installed));
            hook.Uninstall();
            Assert.That(File.ReadAllText(path), Is.EqualTo(foreign));
            Assert.That(hook.Status().Status, Is.EqualTo(RestoreHookStatus.NotInstalled));
        }

        [Test]
        public void Install_TrackedHooksPath_RefusesWithoutWriting()
        {
            GitTestRepo.Run(_repo.Root, "config", "core.hooksPath", ".githooks");
            var hook = new RestoreHook(_repo.Root, _command, "1.0.0");
            Assert.That(hook.Status().Status, Is.EqualTo(RestoreHookStatus.Blocked));
            Assert.That(hook.Install().Status, Is.EqualTo(RestoreHookStatus.Blocked));
            Assert.That(hook.Uninstall().Status, Is.EqualTo(RestoreHookStatus.Blocked));
            Assert.That(Directory.Exists(Path.Combine(_repo.Root, ".githooks")), Is.False);
        }

        [Test]
        public void Install_ExternalHooksPath_UsesItAndRegistersSeveralProjects()
        {
            var external = Path.Combine(_root, "external hooks");
            GitTestRepo.Run(_repo.Root, "config", "core.hooksPath", external);
            var project = Path.Combine(_repo.Root, "game");
            Directory.CreateDirectory(project);
            var root = new RestoreHook(_repo.Root, _command, "1.0.0");
            var subfolder = new RestoreHook(project, _command, "1.0.0");
            root.Install();
            subfolder.Install();
            Assert.That(File.Exists(Path.Combine(external, "post-checkout")), Is.True);
            root.Uninstall();
            Assert.That(root.Status().Status, Is.EqualTo(RestoreHookStatus.NotInstalled));
            Assert.That(subfolder.Status().Status, Is.EqualTo(RestoreHookStatus.Installed));
            subfolder.Uninstall();
            Assert.That(File.Exists(Path.Combine(external, "post-checkout")), Is.False);
        }

        [Test]
        public void Status_MissingCommandOrNewVersion_IsOutdatedAndCanBeUpdated()
        {
            new RestoreHook(_repo.Root, _command, "1.0.0").Install();
            var newer = new RestoreHook(_repo.Root, _command, "2.0.0");
            Assert.That(newer.Status().Status, Is.EqualTo(RestoreHookStatus.Outdated));
            Assert.That(newer.Install().Status, Is.EqualTo(RestoreHookStatus.Installed));
            File.Delete(_command);
            Assert.That(newer.Status().Status, Is.EqualTo(RestoreHookStatus.Outdated));
            Assert.That(newer.Uninstall().Status, Is.EqualTo(RestoreHookStatus.NotInstalled));
        }

        [TestCase("#!/usr/bin/python3\nprint('foreign')\n")]
        [TestCase("#!/bin/sh\n# >>> agent-skills-sync restore >>>\n")]
        [TestCase("#!/bin/sh\n# >>> agent-skills-sync restore >>>\n# command invalid!\n# <<< agent-skills-sync restore <<<\n")]
        public void Operations_UnsupportedOrMalformedHook_LeaveItUntouched(string foreign)
        {
            var path = Path.Combine(_repo.Root, ".git", "hooks", "post-checkout");
            File.WriteAllText(path, foreign);
            var hook = new RestoreHook(_repo.Root, _command, "1.0.0");
            Assert.That(hook.Status().Status, Is.EqualTo(RestoreHookStatus.Blocked));
            Assert.That(hook.Install().Status, Is.EqualTo(RestoreHookStatus.Blocked));
            Assert.That(hook.Uninstall().Status, Is.EqualTo(RestoreHookStatus.Blocked));
            Assert.That(File.ReadAllText(path), Is.EqualTo(foreign));
        }

        [TestCase("#!/bin/sh")]
        [TestCase("#!/bin/sh\n")]
        [TestCase("#!/bin/sh\r\n")]
        public void Install_ShebangOnlyHook_PreservesItAcrossReinstallAndUninstall(string foreign)
        {
            var path = Path.Combine(_repo.Root, ".git", "hooks", "post-checkout");
            File.WriteAllText(path, foreign);
            var hook = new RestoreHook(_repo.Root, _command, "1.0.0");
            Assert.That(hook.Install().Status, Is.EqualTo(RestoreHookStatus.Installed));
            Assert.That(hook.Install().Status, Is.EqualTo(RestoreHookStatus.Installed));
            _repo.AddWorktree(Path.Combine(_root, "wt"));
            hook.Uninstall();
            Assert.That(File.ReadAllText(path), Is.EqualTo(foreign));
        }

        [Test]
        public void Install_CommandInsideCheckout_RefusesWithoutCreatingHook()
        {
            var command = Path.Combine(_repo.Root, "command.dll");
            File.WriteAllText(command, "probe");
            var hook = new RestoreHook(_repo.Root, command, "1.0.0");
            Assert.That(hook.Install().Status, Is.EqualTo(RestoreHookStatus.Blocked));
            Assert.That(File.Exists(Path.Combine(_repo.Root, ".git", "hooks", "post-checkout")), Is.False);
        }

        [Test]
        public void Install_RelativeExternalHooksPath_ExplainsNeedForAbsolutePath()
        {
            GitTestRepo.Run(_repo.Root, "config", "core.hooksPath", "../hooks");
            var hook = new RestoreHook(_repo.Root, _command, "1.0.0");
            Assert.That(hook.Install().Status, Is.EqualTo(RestoreHookStatus.Blocked));
            Assert.That(hook.Status().Message, Does.Contain("absolute external path"));
            Assert.That(Directory.Exists(Path.Combine(_root, "hooks")), Is.False);
        }

        [Test]
        public void Operations_NonUtf8Hook_PreserveItsBytes()
        {
            var path = Path.Combine(_repo.Root, ".git", "hooks", "post-checkout");
            var bytes = new byte[] { 35, 33, 47, 98, 105, 110, 47, 115, 104, 10, 35, 32, 255, 10 };
            File.WriteAllBytes(path, bytes);
            var hook = new RestoreHook(_repo.Root, _command, "1.0.0");
            Assert.That(hook.Status().Status, Is.EqualTo(RestoreHookStatus.Blocked));
            Assert.That(hook.Install().Status, Is.EqualTo(RestoreHookStatus.Blocked));
            Assert.That(hook.Uninstall().Status, Is.EqualTo(RestoreHookStatus.Blocked));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
        }

        [Test]
        public void Status_ExecutableBlockRemoved_ReportsOutdatedAndInstallRepairsIt()
        {
            var hook = new RestoreHook(_repo.Root, _command, "1.0.0");
            hook.Install();
            var path = Path.Combine(_repo.Root, ".git", "hooks", "post-checkout");
            // Keep the markers and metadata but remove all executable lines, as an external edit might.
            File.WriteAllText(path, string.Join("\n", File.ReadAllLines(path).Where(line => line.StartsWith("#", StringComparison.Ordinal))) + "\n");

            Assert.That(hook.Status().Status, Is.EqualTo(RestoreHookStatus.Outdated));
            Assert.That(hook.Install().Status, Is.EqualTo(RestoreHookStatus.Installed));
            var output = GitTestRepo.Run(_repo.Root, "worktree", "add", "-q", "--detach", Path.Combine(_root, "repaired"));
            Assert.That(output, Does.Contain("agent-skills-sync:"), "The repaired executable block must run during checkout.");
        }

        [Test]
        public void Install_LegacyBlockWithoutHeaderOwnership_UpdatesAndPreservesHeader()
        {
            var hook = new RestoreHook(_repo.Root, _command, "1.0.0");
            hook.Install();
            var path = Path.Combine(_repo.Root, ".git", "hooks", "post-checkout");
            File.WriteAllText(path, string.Join("\n", File.ReadAllLines(path)
                .Where(line => !line.StartsWith("# createdHeader ", StringComparison.Ordinal))) + "\n");

            Assert.That(hook.Status().Status, Is.EqualTo(RestoreHookStatus.Outdated));
            Assert.That(hook.Install().Status, Is.EqualTo(RestoreHookStatus.Installed));
            hook.Uninstall();
            Assert.That(File.ReadAllText(path), Is.EqualTo("#!/bin/sh\n"), "Unknown ownership must preserve the header.");
        }

        [Test]
        public void Operations_InvalidHeaderOwnership_LeaveHookUntouched()
        {
            var hook = new RestoreHook(_repo.Root, _command, "1.0.0");
            hook.Install();
            var path = Path.Combine(_repo.Root, ".git", "hooks", "post-checkout");
            var invalid = string.Join("\n", File.ReadAllLines(path).Select(line =>
                line.StartsWith("# createdHeader ", StringComparison.Ordinal) ? "# createdHeader aW52YWxpZA==" : line)) + "\n";
            File.WriteAllText(path, invalid);

            Assert.That(hook.Status().Status, Is.EqualTo(RestoreHookStatus.Blocked));
            Assert.That(hook.Install().Status, Is.EqualTo(RestoreHookStatus.Blocked));
            Assert.That(hook.Uninstall().Status, Is.EqualTo(RestoreHookStatus.Blocked));
            Assert.That(File.ReadAllText(path), Is.EqualTo(invalid));
        }
    }
}
