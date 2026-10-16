using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>How the bytes of a recorded snapshot were obtained (<see cref="SnapshotResolution.Provenance"/>).</summary>
    public enum SnapshotProvenance
    {
        /// <summary>Installed by a sync, which fetched them (Latest) or verified them against the lock (Pinned).</summary>
        Sync,

        /// <summary>Read from an existing installation by an explicit import; the bytes are as found on disk.</summary>
        Import,
    }

    /// <summary>Which snapshot a complete lock entry resolved to when it was last installed, and how that is known.</summary>
    public sealed class SnapshotResolution
    {
        public SnapshotResolution(string snapshot, SnapshotProvenance provenance, InstallMode mode, bool lockVerified, string recordedAt)
        {
            Snapshot = snapshot;
            Provenance = provenance;
            Mode = mode;
            LockVerified = lockVerified;
            RecordedAt = recordedAt;
        }

        /// <summary>The snapshot's <see cref="SkillFolderHash"/> (see <see cref="SnapshotStore"/>).</summary>
        public string Snapshot { get; }

        public SnapshotProvenance Provenance { get; }

        /// <summary>The install mode of the sync or import that recorded it.</summary>
        public InstallMode Mode { get; }

        /// <summary>
        /// Whether the snapshot hashes to the entry's locked <c>computedHash</c>. Latest-mode or imported bytes are
        /// lock-verified only when they really match; a mismatch is recorded as false, never assumed.
        /// </summary>
        public bool LockVerified { get; }

        /// <summary>When it was recorded, as an ISO 8601 UTC time.</summary>
        public string RecordedAt { get; }
    }

    /// <summary>
    /// One repository's recorded resolutions (<see cref="SharedState"/>): for each complete lock entry (name, source,
    /// source type, skill path, locked hash and ref), the snapshot its last explicit sync or import installed. A
    /// restore installs that snapshot instead of fetching upstream again, so an unchanged lock entry gets the same
    /// bytes in every worktree; a changed entry is another key, with no resolution until it is synced.
    /// Kept in a JSON file; entries that are not well formed are ignored, and an unreadable file reads as empty.
    /// </summary>
    public sealed class SnapshotResolutions
    {
        const int FormatVersion = 1;

        readonly string _path;
        readonly Dictionary<string, (LockedSkill Skill, SnapshotResolution Resolution)> _entries;

        SnapshotResolutions(string path, Dictionary<string, (LockedSkill, SnapshotResolution)> entries)
        {
            _path = path;
            _entries = entries;
        }

        /// <summary>Reads the file at <paramref name="path"/>; empty when it is missing or unusable.</summary>
        internal static SnapshotResolutions Load(string path)
        {
            var entries = new Dictionary<string, (LockedSkill, SnapshotResolution)>(StringComparer.Ordinal);
            foreach (var item in ReadItems(path))
            {
                var parsed = Parse(item);
                if (parsed.HasValue) entries[KeyOf(parsed.Value.Skill)] = parsed.Value;
            }
            return new SnapshotResolutions(path, entries);
        }

        /// <summary>The resolution recorded for exactly this lock entry, or null.</summary>
        public SnapshotResolution Find(LockedSkill skill) =>
            _entries.TryGetValue(KeyOf(skill), out var entry) ? entry.Resolution : null;

        /// <summary>Records that <paramref name="skill"/>, as locked now, resolved to <paramref name="resolution"/>. Call <see cref="Save"/> after.</summary>
        internal void Record(LockedSkill skill, SnapshotResolution resolution) => _entries[KeyOf(skill)] = (skill, resolution);

        /// <summary>The current time in the form <see cref="SnapshotResolution.RecordedAt"/> holds.</summary>
        internal static string Now() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        /// <summary>Writes the file beside the old one and swaps it in, so a failed write keeps the old records.</summary>
        internal void Save()
        {
            var items = _entries.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => (object)Write(e.Value.Skill, e.Value.Resolution)).ToList();
            var root = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("version", (double)FormatVersion),
                new KeyValuePair<string, object>("resolutions", items),
            };
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonWriter.Write(root));
            if (File.Exists(_path)) File.Replace(temp, _path, null);
            else File.Move(temp, _path);
        }

        static string KeyOf(LockedSkill skill) => string.Join("\n",
            skill.Name, skill.Source, skill.SourceType, skill.SkillPath ?? "", skill.ComputedHash ?? "", skill.Ref ?? "");

        static IEnumerable<List<KeyValuePair<string, object>>> ReadItems(string path)
        {
            object root;
            try
            {
                if (!File.Exists(path)) yield break;
                root = JsonReader.Read(File.ReadAllText(path));
            }
            catch (Exception e) when (e is FormatException || e is IOException || e is UnauthorizedAccessException)
            {
                yield break;
            }
            if (!(root is List<KeyValuePair<string, object>> members)) yield break;
            if (!(Get(members, "version") is double version) || version != FormatVersion) yield break;
            if (!(Get(members, "resolutions") is List<object> items)) yield break;
            foreach (var item in items)
                if (item is List<KeyValuePair<string, object>> entry) yield return entry;
        }

        static (LockedSkill Skill, SnapshotResolution Resolution)? Parse(List<KeyValuePair<string, object>> entry)
        {
            var name = Get(entry, "skill") as string;
            var source = Get(entry, "source") as string;
            var snapshot = Get(entry, "snapshot") as string;
            if (!SkillName.IsSafe(name) || string.IsNullOrEmpty(source) || !SnapshotStore.IsHash(snapshot)) return null;
            if (!Enum.TryParse(Get(entry, "provenance") as string, true, out SnapshotProvenance provenance)) return null;
            if (!Enum.TryParse(Get(entry, "installMode") as string, true, out InstallMode mode)) return null;
            if (!(Get(entry, "lockVerified") is bool verified)) return null;

            var skill = new LockedSkill(name, source, Get(entry, "sourceType") as string ?? Lockfile.GitHubSourceType,
                Get(entry, "skillPath") as string, Get(entry, "computedHash") as string, Get(entry, "ref") as string);
            return (skill, new SnapshotResolution(snapshot, provenance, mode, verified, Get(entry, "recordedAt") as string));
        }

        static List<KeyValuePair<string, object>> Write(LockedSkill skill, SnapshotResolution resolution)
        {
            var members = new List<KeyValuePair<string, object>>();
            void Add(string key, object value)
            {
                if (value != null) members.Add(new KeyValuePair<string, object>(key, value));
            }
            Add("skill", skill.Name);
            Add("source", skill.Source);
            Add("sourceType", skill.SourceType);
            Add("skillPath", skill.SkillPath);
            Add("computedHash", skill.ComputedHash);
            Add("ref", skill.Ref);
            Add("snapshot", resolution.Snapshot);
            Add("provenance", resolution.Provenance.ToString().ToLowerInvariant());
            Add("installMode", resolution.Mode.ToString().ToLowerInvariant());
            Add("lockVerified", resolution.LockVerified);
            Add("recordedAt", resolution.RecordedAt);
            return members;
        }

        static object Get(List<KeyValuePair<string, object>> members, string key)
        {
            foreach (var member in members)
                if (member.Key == key) return member.Value;
            return null;
        }
    }
}
