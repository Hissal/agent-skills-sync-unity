using System;
using System.IO;
using System.Threading;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// One repository's state on this machine, shared by all its checkouts (<see cref="RepositoryIdentity"/>), kept in
    /// <see cref="UserEnvironment.StateDirectory"/> outside every checkout: the shared choices
    /// (<see cref="SharedChoices"/>) and the recorded snapshot resolutions (<see cref="SnapshotResolutions"/>).
    /// Read-modify-write changes take <see cref="Lock"/>, so parallel syncs and restores never lose each other's writes.
    /// </summary>
    internal sealed class SharedState
    {
        static readonly TimeSpan LockTimeout = TimeSpan.FromMinutes(2);

        public SharedState(RepositoryIdentity identity, UserEnvironment environment)
        {
            Identity = identity;
            Directory = Path.Combine(environment.StateDirectory, "repos", identity.Key);
            Snapshots = new SnapshotStore(environment);
        }

        public RepositoryIdentity Identity { get; }

        /// <summary>This repository's folder in the state directory.</summary>
        public string Directory { get; }

        /// <summary>The content-addressed skill snapshots, shared by every repository.</summary>
        public SnapshotStore Snapshots { get; }

        string ChoicesPath => Path.Combine(Directory, "choices.json");

        string ResolutionsPath => Path.Combine(Directory, "resolutions.json");

        /// <summary>
        /// The shared choices and synced sources, in the shape of <see cref="LocalPrefs"/>; empty until a checkout
        /// first shares them. Only the choice members and <see cref="LocalPrefs.SyncedSources"/> are used.
        /// </summary>
        public LocalPrefs LoadChoices() => LocalPrefs.LoadFile(ChoicesPath);

        public SnapshotResolutions LoadResolutions() => SnapshotResolutions.Load(ResolutionsPath);

        /// <summary>
        /// Waits for, then holds, this repository's lock until disposed. The lock is an open file no other process can
        /// open; the operating system releases it when the holder exits, so a crash never leaves it held.
        /// </summary>
        /// <exception cref="IOException">Another process held the lock for longer than the timeout.</exception>
        public IDisposable Lock()
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = Path.Combine(Directory, ".lock");
            var deadline = DateTime.UtcNow + LockTimeout;
            while (true)
            {
                try
                {
                    return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}
