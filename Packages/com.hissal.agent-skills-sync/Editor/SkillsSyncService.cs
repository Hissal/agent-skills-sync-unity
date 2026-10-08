using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Hissal.AgentSkillsSync.Editor
{
    /// <summary>
    /// Loads one sync's lock, mode and machine choices. Both entry points check consent against this
    /// snapshot and run it unchanged. Construction and planning are offline and never save state.
    /// </summary>
    internal sealed class SkillsSyncService
    {
        readonly string _unityRoot;
        readonly UserEnvironment _environment;
        readonly Func<InstallMode, ISkillFetcher> _fetcher;

        internal static string UnityProjectRoot => Path.GetDirectoryName(Application.dataPath);
        internal static string SkillsRoot => Lockfile.FindRoot(UnityProjectRoot) ?? UnityProjectRoot;

        internal SkillsSyncService(string unityRoot, UserEnvironment environment = null,
            Func<InstallMode, ISkillFetcher> fetcher = null, InstallMode? modeOverride = null)
        {
            _unityRoot = unityRoot;
            _environment = environment ?? UserEnvironment.Current;
            _fetcher = fetcher ?? (mode => new GitHubSkillFetcher(mode: mode));
            Root = Lockfile.FindRoot(unityRoot);
            if (Root == null)
                throw new LockfileException(
                    $"No {Lockfile.FileName} found in the Unity project folder ({unityRoot}) or the folder above it " +
                    $"({Path.GetDirectoryName(unityRoot)}).");
            Lock = Lockfile.Load(Root);
            // Validate settings even when the invocation overrides the mode.
            var projectMode = ProjectSyncSettings.Load(unityRoot).InstallMode;
            Mode = modeOverride ?? projectMode;
            var prefs = LocalPrefs.Load(unityRoot);
            Choices = MachineChoices.Read(Root, prefs, _environment);
            NewSources = SourceConsent.NewSources(Lock, prefs);
        }

        internal string Root { get; }
        internal Lockfile Lock { get; }
        internal InstallMode Mode { get; }
        internal MachineChoices Choices { get; }
        internal IReadOnlyList<string> NewSources { get; }

        SkillSync Sync(ISkillFetcher fetcher) =>
            new SkillSync(Root, fetcher, Choices, mode: Mode, prefsRoot: _unityRoot);

        internal InstallPlan Plan() => Sync(null).Plan(Lock);
        internal IReadOnlyList<string> InstalledDiffersFromLock() => Sync(null).InstalledDiffersFromLock(Lock);

        /// <summary>
        /// Syncs only after every new source is confirmed. The window also passes the mode it showed,
        /// so a changed project setting requires another review. Records consent only on success.
        /// </summary>
        internal SyncSummary Run(IEnumerable<string> confirmedSources, InstallMode? expectedMode = null)
        {
            var unconfirmed = SourceConsent.Unconfirmed(Lock, LocalPrefs.Load(_unityRoot), confirmedSources);
            if (unconfirmed.Count > 0) throw new SourceConsentException(unconfirmed);
            if (expectedMode.HasValue && expectedMode.Value != Mode)
                throw new InstallModeChangedException(Mode);

            var summary = Sync(_fetcher(Mode)).Run(Lock);
            try
            {
                // Reload because the executor has just recorded ownership in these prefs.
                var prefs = LocalPrefs.Load(_unityRoot);
                FolderSelection.Save(prefs, Choices.Layout, Choices.Selected, _environment);
                StartupCheck.RecordSynced(prefs, SyncStatus.Read(Root, Choices, _unityRoot));
                SourceConsent.RecordSynced(prefs, Lock);
                prefs.Save();
            }
            catch (Exception e) when (e is LockfileException || e is IOException || e is UnauthorizedAccessException)
            {
                throw new SyncStateSaveException(summary, e);
            }
            return summary;
        }
    }

    internal sealed class SourceConsentException : Exception
    {
        internal SourceConsentException(IReadOnlyList<string> sources)
            : base("Confirm these new sources before syncing: " + string.Join(", ", sources)) => Sources = sources;

        internal IReadOnlyList<string> Sources { get; }
    }

    internal sealed class InstallModeChangedException : Exception
    {
        internal InstallModeChangedException(InstallMode mode)
            : base($"The install mode changed to {mode} since the window listed the skills. Review it and sync again.") { }
    }

    /// <summary>The skills were synced, but consent and notification state could not be saved.</summary>
    internal sealed class SyncStateSaveException : Exception
    {
        internal SyncStateSaveException(SyncSummary summary, Exception cause)
            : base("The sync finished, but saving the out-of-sync notification state failed: " + cause.Message, cause)
            => Summary = summary;

        internal SyncSummary Summary { get; }
    }
}
