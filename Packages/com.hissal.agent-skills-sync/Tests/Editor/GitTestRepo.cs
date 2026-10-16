using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>A real temporary Git repository with linked worktrees, driven through the <c>git</c> command.</summary>
    sealed class GitTestRepo
    {
        GitTestRepo(string root) => Root = root;

        /// <summary>The main checkout.</summary>
        public string Root { get; }

        /// <summary>
        /// Creates a repository at <paramref name="root"/> whose first commit holds <paramref name="files"/>
        /// (relative path to text). Ignores the test when <c>git</c> is not installed.
        /// </summary>
        public static GitTestRepo Create(string root, params (string Path, string Text)[] files) =>
            Create(root, null, files);

        /// <summary>
        /// Like <see cref="Create(string, (string, string)[])"/>, with the repository folder at
        /// <paramref name="separateGitDir"/> (<c>git init --separate-git-dir</c>) unless it is null.
        /// </summary>
        public static GitTestRepo Create(string root, string separateGitDir, params (string Path, string Text)[] files)
        {
            Directory.CreateDirectory(root);
            if (!TryRun(root, out _, "--version")) Assert.Ignore("git is not installed.");
            if (separateGitDir == null) Run(root, "init", "-q");
            else Run(root, "init", "-q", "--separate-git-dir", separateGitDir);
            var repo = new GitTestRepo(root);
            repo.Commit("initial", files);
            return repo;
        }

        /// <summary>Writes <paramref name="files"/> and commits them on the current branch.</summary>
        public void Commit(string message, params (string Path, string Text)[] files)
        {
            foreach (var (path, text) in files)
            {
                var full = Path.Combine(Root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, text);
            }
            Run(Root, "add", "-A");
            Run(Root, "-c", "user.name=test", "-c", "user.email=test@example.com", "commit", "-q", "--allow-empty", "-m", message);
        }

        /// <summary>Adds a linked worktree at <paramref name="path"/>, detached at the current commit.</summary>
        public string AddWorktree(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Run(Root, "worktree", "add", "-q", "--detach", path);
            return path;
        }

        /// <summary>Removes a linked worktree, as a contributor would when done with it.</summary>
        public void RemoveWorktree(string path)
        {
            TempDirectory.Delete(path);
            Run(Root, "worktree", "prune");
        }

        static void Run(string directory, params string[] args)
        {
            if (!TryRun(directory, out var output, args))
                throw new InvalidOperationException($"git {string.Join(" ", args)} failed: {output}");
        }

        static bool TryRun(string directory, out string output, params string[] args)
        {
            var start = new ProcessStartInfo("git", string.Join(" ", args.Select(Quote)))
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            try
            {
                using (var process = Process.Start(start))
                {
                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(120000))
                    {
                        process.Kill();
                        throw new TimeoutException($"git {string.Join(" ", args)} did not finish.");
                    }
                    output = stdout.Result + stderr;
                    return process.ExitCode == 0;
                }
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                output = e.Message;
                return false;
            }
        }

        static string Quote(string arg) => arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '"', '\t' }) < 0
            ? arg
            : "\"" + arg.Replace("\"", "\\\"") + "\"";
    }
}
