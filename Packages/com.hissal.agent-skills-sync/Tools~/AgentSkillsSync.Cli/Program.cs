using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hissal.AgentSkillsSync.Cli
{
    /// <summary>
    /// The standalone command: the package's Core run without Unity, for worktree setup before an agent starts.
    /// Writes one JSON object to standard output and exits 0 (done or ready), 1 (failed), 2 (invalid usage or input)
    /// or 3 (needs the caller: confirm sources, sync or import snapshots, or name a baseline).
    /// </summary>
    static class Program
    {
        const int Ok = 0;
        const int Failed = 1;
        const int Invalid = 2;
        const int ActionRequired = 3;

        const string Usage =
            "Usage: agent-skills-sync <command> [--project <unity-project>] [options]\n" +
            "  restore [--dry-run] [--confirm-source <owner/repo>]... [--baseline <checkout>]\n" +
            "      Install the selected skills from recorded snapshots, offline, before an agent starts.\n" +
            "  sync [--confirm-source <owner/repo>]... [--install-mode latest|pinned]\n" +
            "      Fetch from upstream, install, and record the snapshots later restores install.\n" +
            "  import\n" +
            "      Record the skills an earlier version installed as snapshots, as found on disk.\n" +
            "  scope --shared | --worktree\n" +
            "      Follow the repository's shared choices, or keep this checkout's own.\n" +
            "--project defaults to the current directory. Output is one JSON object.";

        static int Main(string[] args)
        {
            var output = new List<KeyValuePair<string, object>>();
            var code = Run(args, output);
            Console.Out.Write(JsonWriter.Write(output));
            return code;
        }

        static int Run(string[] args, List<KeyValuePair<string, object>> output)
        {
            Options options;
            try
            {
                options = Options.Parse(args);
            }
            catch (ArgumentException e)
            {
                Add(output, "status", "invalid");
                Add(output, "error", e.Message);
                Add(output, "usage", Usage);
                return Invalid;
            }
            Add(output, "command", options.Command);
            Add(output, "project", options.Project);

            try
            {
                switch (options.Command)
                {
                    case "restore": return Restore(options, output);
                    case "sync": return Sync(options, output);
                    case "import": return Import(options, output);
                    case "scope": return Scope(options, output);
                    default:
                        Add(output, "status", "ok");
                        Add(output, "usage", Usage);
                        return Ok;
                }
            }
            catch (Exception e) when (e is LockfileException || e is ProjectSyncSettingsException || e is RepositoryException)
            {
                Add(output, "status", "invalid");
                Add(output, "error", e.Message);
                return Invalid;
            }
            catch (SyncAbortedException e)
            {
                Add(output, "status", "failed");
                Add(output, "error", e.Message);
                Add(output, "failures", e.Failures.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => (object)Object(
                    ("skill", f.Key), ("failure", Camel(f.Value.Failure.ToString())), ("message", f.Value.Message))).ToList());
                return Failed;
            }
            catch (Exception e)
            {
                Add(output, "status", "failed");
                Add(output, "error", e.Message);
                return Failed;
            }
        }

        static int Restore(Options options, List<KeyValuePair<string, object>> output)
        {
            var restore = new WorktreeRestore(options.Project, UserEnvironment.Current, options.Baseline);
            var report = options.DryRun ? restore.Plan() : restore.Run(options.ConfirmedSources);

            Add(output, "status", Camel(report.Status.ToString()));
            Add(output, "dryRun", report.DryRun);
            Add(output, "repository", Object(
                ("commonDirectory", report.Identity.CommonDirectory), ("projectPath", report.Identity.ProjectPath)));
            if (report.Mode.HasValue) Add(output, "installMode", Camel(report.Mode.Value.ToString()));
            // Undecided until a baseline is named.
            var scope = report.Status == RestoreStatus.BaselineRequired ? null : Camel(report.Scope.ToString());
            Add(output, "choices", Object(("scope", scope), ("baselineFrom", report.BaselineFrom)));
            Add(output, "baselineCandidates", Strings(report.BaselineCandidates));
            Add(output, "unconfirmedSources", Strings(report.UnconfirmedSources));
            Add(output, "missingSnapshots", report.MissingSnapshots
                .Select(m => (object)Object(("skill", m.Skill), ("reason", m.Reason))).ToList());
            if (report.Plan != null) Add(output, "actions", report.Plan.Actions.Select(Action).ToList());
            if (report.Summary != null) Add(output, "summary", Summary(report.Summary));
            return report.Succeeded ? Ok : ActionRequired;
        }

        static int Sync(Options options, List<KeyValuePair<string, object>> output)
        {
            var service = new SkillsSyncService(options.Project, UserEnvironment.Current, modeOverride: options.InstallMode);
            Add(output, "installMode", Camel(service.Mode.ToString()));
            var unconfirmed = SourceConsent.Unconfirmed(service.Lock, SharedChoices.Load(options.Project, UserEnvironment.Current),
                options.ConfirmedSources, service.Choices.Skills);
            if (unconfirmed.Count > 0)
            {
                Add(output, "status", "sourcesNeedConfirmation");
                Add(output, "unconfirmedSources", Strings(unconfirmed));
                return ActionRequired;
            }
            var summary = service.Run(options.ConfirmedSources);
            Add(output, "status", "synced");
            Add(output, "summary", Summary(summary));
            return Ok;
        }

        static int Import(Options options, List<KeyValuePair<string, object>> output)
        {
            var imported = SnapshotRecorder.Import(options.Project, UserEnvironment.Current);
            Add(output, "status", "imported");
            Add(output, "imported", Strings(imported));
            return Ok;
        }

        static int Scope(Options options, List<KeyValuePair<string, object>> output)
        {
            if (!options.Scope.HasValue) throw new RepositoryException("scope needs --shared or --worktree.");
            SharedChoices.SetScope(options.Project, options.Scope.Value);
            Add(output, "status", "ok");
            Add(output, "scope", Camel(options.Scope.Value.ToString()));
            return Ok;
        }

        static List<KeyValuePair<string, object>> Summary(SyncSummary summary) => Object(
            ("installed", Strings(summary.Installed)),
            ("updated", Strings(summary.Updated)),
            ("removed", Strings(summary.Removed)),
            ("noLongerSelectedByDefault", Strings(summary.NoLongerSelectedByDefault)),
            ("skipped", Strings(summary.Skipped)),
            ("skippedForUserScope", Strings(summary.SkippedForUserScope)),
            ("linked", Strings(summary.Linked)),
            ("unlinked", Strings(summary.Unlinked)),
            ("differsFromLock", Strings(summary.DiffersFromLock)),
            ("nothingChanged", summary.NothingChanged));

        static object Action(PlanAction action) => Object(
            ("kind", Camel(action.Kind.ToString())),
            ("skill", action.SkillName),
            ("folder", action.Folder.RelativePath),
            ("linkTarget", action.LinkTarget?.RelativePath));

        static List<object> Strings(IEnumerable<string> values) => values.Cast<object>().ToList();

        static List<KeyValuePair<string, object>> Object(params (string Key, object Value)[] members) =>
            members.Select(m => new KeyValuePair<string, object>(m.Key, m.Value)).ToList();

        static void Add(List<KeyValuePair<string, object>> output, string key, object value) =>
            output.Add(new KeyValuePair<string, object>(key, value));

        static string Camel(string name) => char.ToLowerInvariant(name[0]) + name.Substring(1);

        sealed class Options
        {
            public string Command;
            public string Project = Directory.GetCurrentDirectory();
            public bool DryRun;
            public string Baseline;
            public InstallMode? InstallMode;
            public ChoiceScope? Scope;
            public readonly List<string> ConfirmedSources = new List<string>();

            /// <exception cref="ArgumentException">The arguments are not valid; the message says why.</exception>
            public static Options Parse(string[] args)
            {
                if (args.Length == 0) throw new ArgumentException("Name a command.");
                var options = new Options { Command = args[0] };
                if (!new[] { "restore", "sync", "import", "scope", "help", "--help" }.Contains(options.Command))
                    throw new ArgumentException($"Unknown command \"{options.Command}\".");

                for (var i = 1; i < args.Length; i++)
                {
                    string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
                    switch (args[i])
                    {
                        case "--project": options.Project = Path.GetFullPath(Value()); break;
                        case "--dry-run" when options.Command == "restore": options.DryRun = true; break;
                        case "--baseline" when options.Command == "restore": options.Baseline = Path.GetFullPath(Value()); break;
                        case "--confirm-source" when options.Command == "restore" || options.Command == "sync":
                            options.ConfirmedSources.Add(Value());
                            break;
                        case "--install-mode" when options.Command == "sync":
                            var mode = Value();
                            if (string.Equals(mode, "latest", StringComparison.OrdinalIgnoreCase)) options.InstallMode = AgentSkillsSync.InstallMode.Latest;
                            else if (string.Equals(mode, "pinned", StringComparison.OrdinalIgnoreCase)) options.InstallMode = AgentSkillsSync.InstallMode.Pinned;
                            else throw new ArgumentException("--install-mode must be latest or pinned.");
                            break;
                        case "--shared" when options.Command == "scope": options.Scope = ChoiceScope.Shared; break;
                        case "--worktree" when options.Command == "scope": options.Scope = ChoiceScope.Worktree; break;
                        default: throw new ArgumentException($"Unknown option \"{args[i]}\" for {options.Command}.");
                    }
                }
                if (options.Command == "scope" && !options.Scope.HasValue)
                    throw new ArgumentException("scope needs --shared or --worktree.");
                return options;
            }
        }
    }
}
