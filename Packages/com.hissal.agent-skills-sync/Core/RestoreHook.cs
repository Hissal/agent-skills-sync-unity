using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Hissal.AgentSkillsSync
{
    public enum RestoreHookStatus { NotInstalled, Installed, Outdated, Blocked }

    /// <summary>The hook's observed state and an explanation suitable for the CLI and window.</summary>
    public sealed class RestoreHookReport
    {
        internal RestoreHookReport(RestoreHookStatus status, string path, string message)
        { Status = status; Path = path; Message = message; }
        public RestoreHookStatus Status { get; }
        public string Path { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Installs a repository-wide post-checkout block, retaining foreign shell code byte for byte. Each project is
    /// registered relative to the checkout; the command DLL lives outside it. No Git configuration is changed.
    /// Git's hook cwd, null old HEAD, executable bit and hooksPath rules are documented at
    /// https://git-scm.com/docs/githooks#_post_checkout and https://git-scm.com/docs/git-config#Documentation/git-config.txt-corehooksPath.
    /// Relative hooksPath is resolved from the hook cwd (the checkout); paths inside any checkout are refused.
    /// </summary>
    public sealed class RestoreHook
    {
        const string Begin = "# >>> agent-skills-sync restore >>>\n";
        const string End = "# <<< agent-skills-sync restore <<<\n";
        const string Header = "#!/bin/sh\n";
        readonly RepositoryIdentity _identity;
        readonly string _command;
        readonly string _version;

        public RestoreHook(string projectRoot, string command, string version)
        {
            _identity = RepositoryIdentity.Find(projectRoot) ?? throw new RepositoryException("The project is not in a Git checkout.");
            _command = Path.GetFullPath(command);
            _version = version;
        }

        public RestoreHookReport Status() => Inspect(out _, out _, out _, out _);

        /// <summary>Adds this project, or updates the command for all registered projects. Blocked hooks are untouched.</summary>
        public RestoreHookReport Install()
        {
            using (MutationLock()) return InstallLocked();
        }

        RestoreHookReport InstallLocked()
        {
            var report = Inspect(out var text, out var start, out var length, out var config);
            if (report.Status == RestoreHookStatus.Blocked) return report;
            if (_identity.Checkouts().Any(c => Within(_command, c)))
                return Report(RestoreHookStatus.Blocked, report.Path, "The command must live outside every checkout. Build it from the sync window or copy the complete build to a stable external folder.");
            if (!File.Exists(_command)) return Report(RestoreHookStatus.Blocked, report.Path, "Build the command before installing the hook.");
            config = config ?? new Configuration { CreatedHeader = text.Length == 0 };
            config.Command = _command;
            config.Version = _version;
            config.Repository = _identity.CommonDirectory;
            if (!config.Projects.Contains(_identity.ProjectPath, StringComparer.Ordinal)) config.Projects.Add(_identity.ProjectPath);
            if (text.Length == 0) text = Header;
            if (start < 0)
            {
                var newline = text.IndexOf('\n');
                config.JoinHeader = newline < 0;
                start = newline < 0 ? text.Length : newline + 1;
                length = 0;
            }
            var updated = text.Remove(start, length).Insert(start, Block(config));
            Write(report.Path, updated);
            return Status();
        }

        /// <summary>Removes this project's registration, retaining other projects and all foreign hook text.</summary>
        public RestoreHookReport Uninstall()
        {
            using (MutationLock()) return UninstallLocked();
        }

        RestoreHookReport UninstallLocked()
        {
            var report = Inspect(out var text, out var start, out var length, out var config);
            if (report.Status == RestoreHookStatus.Blocked || start < 0) return report;
            config.Projects.Remove(_identity.ProjectPath);
            var updated = text.Remove(start, length);
            if (config.Projects.Count > 0) updated = updated.Insert(start, Block(config));
            if (config.CreatedHeader && updated == Header) File.Delete(report.Path);
            else Write(report.Path, updated);
            return Status();
        }

        // One lock for the whole repository, since project registrations share one block.
        IDisposable MutationLock() => new FileStream(Path.Combine(_identity.CommonDirectory, "agent-skills-sync-hook.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        RestoreHookReport Inspect(out string text, out int start, out int length, out Configuration config)
        {
            text = ""; start = -1; length = 0; config = null;
            var configured = Git("config --path --get core.hooksPath", allowMissing: true).TrimEnd('\r', '\n');
            var hooks = configured.Length == 0 ? Path.Combine(_identity.CommonDirectory, "hooks") :
                Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(_identity.Checkout, configured));
            var path = Path.Combine(hooks, "post-checkout");
            if (configured.Length > 0 && _identity.Checkouts().Any(c => Within(hooks, c)))
                return Report(RestoreHookStatus.Blocked, path,
                    "core.hooksPath is inside a checkout. Add the restore command to that hook yourself, guarded by a null previous HEAD; see the README's New worktrees section.");
            if (configured.Length > 0 && !Path.IsPathRooted(configured))
                return Report(RestoreHookStatus.Blocked, path,
                    "A relative external core.hooksPath changes location between worktrees. Set it to an absolute external path, then install again.");
            for (var directory = hooks; directory != null; directory = Path.GetDirectoryName(directory))
                if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    return Report(RestoreHookStatus.Blocked, path, "The hooks directory uses a symbolic link; manage this hook manually.");
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                return Report(RestoreHookStatus.Blocked, path, "post-checkout is a symbolic link; manage it manually.");
            try { text = File.Exists(path) ? new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path)) : ""; }
            catch (DecoderFallbackException)
            {
                return Report(RestoreHookStatus.Blocked, path, "post-checkout is not UTF-8; manage it manually to preserve its encoding.");
            }
            if (text.Length > 0 && !ShellHook(text))
                return Report(RestoreHookStatus.Blocked, path, "post-checkout is not a supported shell hook (sh, bash or dash). Manage it manually.");
            start = text.IndexOf(Begin, StringComparison.Ordinal);
            var end = text.IndexOf(End, StringComparison.Ordinal);
            if (start < 0 && end < 0) return Report(RestoreHookStatus.NotInstalled, path, "Not installed for this project.");
            if (start < 0 || end < start || (start > 0 && text[start - 1] != '\n') || (end > 0 && text[end - 1] != '\n') || text.IndexOf(Begin, start + Begin.Length, StringComparison.Ordinal) >= 0 ||
                text.IndexOf(End, end + End.Length, StringComparison.Ordinal) >= 0)
                return Report(RestoreHookStatus.Blocked, path, "The managed block is malformed; repair or remove it manually.");
            length = end + End.Length - start;
            try { config = Configuration.Read(text.Substring(start + Begin.Length, end - start - Begin.Length)); }
            catch (FormatException e) { return Report(RestoreHookStatus.Blocked, path, "Invalid hook configuration: " + e.Message); }
            if (config.JoinHeader)
            {
                if (start == 0 || text[start - 1] != '\n')
                    return Report(RestoreHookStatus.Blocked, path, "The managed block's header separator is missing.");
                start--; length++;
            }
            if (!Paths.Comparer.Equals(config.Repository, _identity.CommonDirectory))
                return Report(RestoreHookStatus.Blocked, path, "This hook belongs to another repository; manage shared hooks manually.");
            if (!config.Projects.Contains(_identity.ProjectPath, StringComparer.Ordinal))
                return Report(RestoreHookStatus.NotInstalled, path, "Not installed for this project (other projects are registered).");
            if (!string.Equals(text.Substring(start, length), Block(config), StringComparison.Ordinal))
                return Report(RestoreHookStatus.Outdated, path, "The managed hook block differs from this package. Update the hook.");
            if (!File.Exists(config.Command) || config.Version != _version || !Paths.Comparer.Equals(config.Command, _command))
                return Report(RestoreHookStatus.Outdated, path, "The command is missing or differs from this package. Update the hook.");
            return Report(RestoreHookStatus.Installed, path, "Installed: new worktrees restore skills automatically.");
        }

        static bool Within(string path, string directory) => Paths.Comparer.Equals(path, directory) ||
            path.StartsWith(directory.TrimEnd('/', '\\') + Path.DirectorySeparatorChar, Paths.Comparison);

        static bool ShellHook(string text)
        {
            var first = text.Split('\n')[0].TrimEnd('\r');
            return new[] { "#!/bin/sh", "#!/bin/bash", "#!/bin/dash", "#!/usr/bin/env sh", "#!/usr/bin/env bash" }.Contains(first);
        }

        static RestoreHookReport Report(RestoreHookStatus status, string path, string message) => new RestoreHookReport(status, path, message);

        static string Block(Configuration config)
        {
            var result = new StringBuilder(config.JoinHeader ? "\n" + Begin : Begin);
            result.Append("# command ").AppendLine(Encode(config.Command));
            result.Append("# createdHeader ").AppendLine(Encode(config.CreatedHeader ? "yes" : "no"));
            result.Append("# headerSeparator ").AppendLine(Encode(config.JoinHeader ? "yes" : "no"));
            result.Append("# version ").AppendLine(Encode(config.Version));
            result.Append("# repository ").AppendLine(Encode(config.Repository));
            foreach (var project in config.Projects) result.Append("# project ").AppendLine(Encode(project));
            // Subshell isolates variables/exit from a foreign hook, and the OR prevents a foreign `set -e` from aborting.
            result.Append("(\ncase \"$1\" in ''|*[!0]*) exit 0 ;; esac\n[ \"$3\" = 1 ] || exit 0\n");
            // An absolute external hooksPath can be shared by unrelated repositories.
            result.Append("ass_common=$(git rev-parse --path-format=absolute --git-common-dir) || exit 0\n");
            result.Append("[ \"$ass_common\" = ").Append(Quote(config.Repository.Replace('\\', '/'))).Append(" ] || exit 0\n");
            foreach (var project in config.Projects)
            {
                result.Append("if [ -d ").Append(Quote(project)).Append(" ]; then\n");
                result.Append("  ass_output=$(dotnet ").Append(Quote(config.Command.Replace('\\', '/')))
                    .Append(" restore --project ").Append(Quote(project)).Append(" --hook-output 2>&1)\n");
                result.Append("  case \"$ass_output\" in\n    agent-skills-sync:*) printf '%s\\n' \"$ass_output\" ;;\n    *) printf '%s\\n' ")
                    .Append(Quote("agent-skills-sync: " + project + ": command unavailable or failed; update the hook from Window > Agent Skills Sync, or run hook install with a working command."))
                    .Append(" ;;\n  esac\nfi\n");
            }
            result.Append("exit 0\n) || :\n").Append(End);
            return result.ToString().Replace("\r\n", "\n");
        }

        internal static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
        static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

        void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + "." + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                if (Path.DirectorySeparatorChar != '\\')
                    RunProcess("chmod", "+x " + ProcessQuote(temp), _identity.Checkout);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        string Git(string args, bool allowMissing = false) => RunProcess("git", args, _identity.Checkout, allowMissing);

        internal static string ProcessQuote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

        internal static string RunProcess(string file, string args, string directory, bool allowMissing = false)
        {
            var info = new ProcessStartInfo(file, args) { WorkingDirectory = directory, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            using (var process = Process.Start(info))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(300000)) { process.Kill(); throw new IOException(file + " timed out."); }
                if (process.ExitCode != 0 && !(allowMissing && process.ExitCode == 1))
                    throw new IOException(file + " failed: " + stderr.Result);
                return stdout.Result;
            }
        }

        sealed class Configuration
        {
            public bool JoinHeader;
            public bool CreatedHeader;
            public string Command;
            public string Version;
            public string Repository;
            public readonly List<string> Projects = new List<string>();

            public static Configuration Read(string text)
            {
                var config = new Configuration();
                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var line in text.Split('\n'))
                {
                    if (!line.StartsWith("# ", StringComparison.Ordinal)) continue;
                    var parts = line.Substring(2).Split(' ');
                    if (parts.Length != 2) throw new FormatException("Invalid metadata line.");
                    var value = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
                    if (value.Length == 0 || value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0) throw new FormatException("Invalid metadata value.");
                    if (parts[0] == "project")
                    {
                        if (Path.IsPathRooted(value) || value.Split('/', '\\').Contains("..") || value.Contains("\\") || value.Contains(":"))
                            throw new FormatException("Project path must stay inside the checkout.");
                        if (config.Projects.Contains(value)) throw new FormatException("Duplicate project.");
                        config.Projects.Add(value);
                    }
                    else if (new[] { "command", "version", "repository", "headerSeparator", "createdHeader" }.Contains(parts[0]) && !fields.ContainsKey(parts[0])) fields.Add(parts[0], value);
                    else throw new FormatException("Unknown or duplicate metadata.");
                }
                if (!new[] { "command", "version", "repository", "headerSeparator" }.All(fields.ContainsKey) ||
                    config.Projects.Count == 0 || !Path.IsPathRooted(fields["command"]) || !Path.IsPathRooted(fields["repository"]))
                    throw new FormatException("Missing or invalid metadata.");
                if (fields["headerSeparator"] != "yes" && fields["headerSeparator"] != "no") throw new FormatException("Invalid header separator.");
                // Older blocks did not record ownership. Keep their header rather than risk deleting a foreign one.
                if (fields.TryGetValue("createdHeader", out var createdHeader))
                {
                    if (createdHeader != "yes" && createdHeader != "no") throw new FormatException("Invalid header ownership.");
                    config.CreatedHeader = createdHeader == "yes";
                }
                config.JoinHeader = fields["headerSeparator"] == "yes";
                config.Command = fields["command"]; config.Version = fields["version"]; config.Repository = fields["repository"];
                return config;
            }
        }
    }
}
