using System;
using System.Collections.Generic;
using System.IO;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Per-machine choices for one project, kept in <c>UserSettings/AgentSkillsSync.json</c>
    /// (Unity's per-user folder, never committed). A missing or unreadable file reads as empty prefs.
    /// </summary>
    /// <remarks>
    /// The file is one JSON object. Each setting is a property backed by one top-level member, read
    /// and written through the <c>Get*</c>/<c>Set</c> helpers; setting a value to null removes its
    /// member. Members this version does not know are kept on save, so an older package version
    /// never drops a newer one's settings. To add a setting, add a property with a new member name.
    /// </remarks>
    public sealed class LocalPrefs
    {
        public const string FolderName = "UserSettings";
        public const string FileName = "AgentSkillsSync.json";
        public const int FormatVersion = 1;

        const string VersionKey = "version";
        const string LastSyncedLockHashKey = "lastSyncedLockHash";
        const string DeclinedStateKey = "declinedState";

        readonly string _path;
        readonly List<KeyValuePair<string, object>> _members;

        LocalPrefs(string path, List<KeyValuePair<string, object>> members)
        {
            _path = path;
            _members = members;
        }

        /// <summary>The prefs file of the project at <paramref name="projectRoot"/>.</summary>
        public static string PathFor(string projectRoot) => Path.Combine(projectRoot, FolderName, FileName);

        /// <summary>Reads the project's prefs; empty prefs when the file is missing or not a JSON object.</summary>
        public static LocalPrefs Load(string projectRoot)
        {
            var path = PathFor(projectRoot);
            return new LocalPrefs(path, ReadMembers(path) ?? new List<KeyValuePair<string, object>>());
        }

        /// <summary>Writes the prefs, creating <c>UserSettings/</c> if needed.</summary>
        public void Save()
        {
            if (Get(VersionKey) == null) _members.Insert(0, new KeyValuePair<string, object>(VersionKey, (double)FormatVersion));
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonWriter.Write(_members));
            if (File.Exists(_path)) File.Delete(_path);
            File.Move(temp, _path);
        }

        /// <summary>Hash of the lockfile as it was at the last successful sync (see <see cref="LockfileHash"/>).</summary>
        public string LastSyncedLockHash
        {
            get => GetString(LastSyncedLockHashKey);
            set => Set(LastSyncedLockHashKey, value);
        }

        /// <summary>
        /// The <see cref="SyncStatus.Fingerprint"/> the contributor last declined to sync;
        /// the startup check stays quiet while the status still matches it.
        /// </summary>
        public string DeclinedState
        {
            get => GetString(DeclinedStateKey);
            set => Set(DeclinedStateKey, value);
        }

        string GetString(string key) => Get(key) as string;

        object Get(string key)
        {
            foreach (var member in _members)
                if (member.Key == key) return member.Value;
            return null;
        }

        /// <summary>Sets a member, keeping its position; null removes it.</summary>
        void Set(string key, object value)
        {
            var index = _members.FindIndex(m => m.Key == key);
            if (value == null)
            {
                if (index >= 0) _members.RemoveAt(index);
            }
            else if (index >= 0) _members[index] = new KeyValuePair<string, object>(key, value);
            else _members.Add(new KeyValuePair<string, object>(key, value));
        }

        static List<KeyValuePair<string, object>> ReadMembers(string path)
        {
            try
            {
                return File.Exists(path) ? JsonReader.Read(File.ReadAllText(path)) as List<KeyValuePair<string, object>> : null;
            }
            catch (Exception e) when (e is FormatException || e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
