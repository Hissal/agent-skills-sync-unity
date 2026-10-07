using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
        const string SyncedSourcesKey = "syncedSources";
        const string SelectedFoldersKey = "selectedFolders";
        const string DeclinedFoldersKey = "declinedFolders";
        const string ManagedSkillsKey = "managedSkills";
        const string SkippedSkillsKey = "skippedSkills";

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

        /// <summary>
        /// Source repos (<c>owner/repo</c>) of every skill synced so far; a source missing here is new
        /// and needs the contributor's explicit confirmation (see <see cref="SourceConsent"/>). Never null.
        /// </summary>
        public IReadOnlyList<string> SyncedSources
        {
            get => GetStringList(SyncedSourcesKey);
            set => SetStringList(SyncedSourcesKey, value);
        }

        /// <summary>
        /// <see cref="SkillsFolder.RelativePath"/>s of the skills folders the contributor chose to install into; null
        /// while they have never chosen (the window then pre-selects by autofill), empty when they chose none.
        /// Use <see cref="FolderSelection"/> rather than this directly.
        /// </summary>
        public IReadOnlyList<string> SelectedFolders
        {
            get => Get(SelectedFoldersKey) is List<object> ? GetStringList(SelectedFoldersKey) : null;
            set => SetStringList(SelectedFoldersKey, value);
        }

        /// <summary>
        /// <see cref="SkillsFolder.RelativePath"/>s of folders the contributor declined to add when their user-scope
        /// home appeared, so the startup check does not offer them again. Never null.
        /// </summary>
        public IReadOnlyList<string> DeclinedFolders
        {
            get => GetStringList(DeclinedFoldersKey);
            set => SetStringList(DeclinedFoldersKey, value);
        }

        /// <summary>
        /// Per skills folder (<see cref="SkillsFolder.RelativePath"/>), the sorted names of locked skills the contributor
        /// chose to skip there in favour of their user-scope copy. Never null; folders with no skips are left out.
        /// Use <see cref="SkipChoices"/> rather than this directly.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> SkippedSkills
        {
            get
            {
                var result = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                if (Get(SkippedSkillsKey) is List<KeyValuePair<string, object>> folders)
                    foreach (var folder in folders)
                    {
                        if (!(folder.Value is List<object> items)) continue;
                        var names = new SortedSet<string>(StringComparer.Ordinal);
                        foreach (var item in items)
                            if (item is string s) names.Add(s);
                        if (names.Count > 0) result[folder.Key] = new List<string>(names);
                    }
                return result;
            }
            set
            {
                var members = new List<KeyValuePair<string, object>>();
                if (value != null)
                {
                    var keys = new List<string>(value.Keys);
                    keys.Sort(StringComparer.Ordinal);
                    foreach (var key in keys)
                    {
                        var names = new SortedSet<string>(value[key] ?? new string[0], StringComparer.Ordinal);
                        if (names.Count > 0) members.Add(new KeyValuePair<string, object>(key, new List<object>(names)));
                    }
                }
                Set(SkippedSkillsKey, members.Count == 0 ? null : members);
            }
        }

        /// <summary>
        /// Per skills folder (<see cref="SkillsFolder.RelativePath"/>), the names of the entries this machine's syncs
        /// created there and still manage; null until the first sync records it (the scanner then falls back to the
        /// names in each folder's <c>.gitignore</c> block). Written by <see cref="PlanExecutor"/>.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> ManagedSkills
        {
            get
            {
                if (!(Get(ManagedSkillsKey) is List<KeyValuePair<string, object>> folders)) return null;
                var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                foreach (var folder in folders)
                {
                    var names = new List<string>();
                    if (folder.Value is List<object> items)
                        foreach (var item in items)
                            if (item is string s) names.Add(s);
                    result[folder.Key] = names;
                }
                return result;
            }
            set
            {
                if (value == null)
                {
                    Set(ManagedSkillsKey, null);
                    return;
                }
                var folders = new List<KeyValuePair<string, object>>();
                foreach (var folder in value.OrderBy(f => f.Key, StringComparer.Ordinal))
                    if (folder.Value.Count > 0)
                        folders.Add(new KeyValuePair<string, object>(folder.Key,
                            new List<object>(folder.Value.OrderBy(n => n, StringComparer.Ordinal))));
                Set(ManagedSkillsKey, folders);
            }
        }

        string GetString(string key) => Get(key) as string;

        /// <summary>The string items of an array member; empty when the member is missing or not an array.</summary>
        IReadOnlyList<string> GetStringList(string key)
        {
            var list = new List<string>();
            if (Get(key) is List<object> items)
                foreach (var item in items)
                    if (item is string s) list.Add(s);
            return list;
        }

        /// <summary>Stores the strings as an array member; null removes it.</summary>
        void SetStringList(string key, IEnumerable<string> values) =>
            Set(key, values == null ? null : new List<object>(values));

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
