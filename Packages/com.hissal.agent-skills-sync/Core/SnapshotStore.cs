using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Immutable skill snapshots on this machine, each a skill folder stored under its <see cref="SkillFolderHash"/>, so
    /// identical content is kept once whichever repository or checkout recorded it. A snapshot is published whole by
    /// renaming a finished, verified copy into place: a reader never sees a partial one, and two checkouts adding the
    /// same content at once both end with the one copy. Checkouts install copies of a snapshot, never links to it.
    /// </summary>
    internal sealed class SnapshotStore
    {
        static readonly Regex HashPattern = new Regex("^[0-9a-f]{64}$");

        public SnapshotStore(UserEnvironment environment) => Root = Path.Combine(environment.StateDirectory, "snapshots");

        public string Root { get; }

        /// <summary>Whether <paramref name="hash"/> has the form of a <see cref="SkillFolderHash"/>, so it is safe as a folder name.</summary>
        public static bool IsHash(string hash) => hash != null && HashPattern.IsMatch(hash);

        /// <summary>The folder of the snapshot hashing to <paramref name="hash"/>, or null when there is none.</summary>
        public string Find(string hash)
        {
            if (!IsHash(hash?.ToLowerInvariant())) return null;
            var path = Path.Combine(Root, hash.ToLowerInvariant());
            return Directory.Exists(path) ? path : null;
        }

        /// <summary>Stores a copy of <paramref name="folder"/> unless its content is already stored, and returns its hash.</summary>
        /// <exception cref="IOException">The copy could not be made, or changed while it was made; nothing was stored.</exception>
        public string Add(string folder)
        {
            var hash = SkillFolderHash.Compute(folder);
            if (Find(hash) != null) return hash;

            Directory.CreateDirectory(Root);
            var staging = Path.Combine(Root, ".staging-" + Guid.NewGuid().ToString("N"));
            try
            {
                Paths.CopyDirectory(folder, staging);
                if (SkillFolderHash.Compute(staging) != hash)
                    throw new IOException($"{folder} changed while it was being stored; nothing was stored.");
                try
                {
                    Directory.Move(staging, Path.Combine(Root, hash));
                }
                catch (IOException) when (Find(hash) != null)
                {
                    // Another checkout stored the same content first.
                }
            }
            finally
            {
                if (Directory.Exists(staging)) TempDelete(staging);
            }
            return hash;
        }

        static void TempDelete(string path)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }
    }
}
