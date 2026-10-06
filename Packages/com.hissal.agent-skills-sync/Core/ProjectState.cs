using System;
using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>What one skills folder currently holds.</summary>
    public sealed class FolderState
    {
        /// <param name="installedHashes">Content hash of each managed skill copy in the folder (see <see cref="InstalledHash"/>).</param>
        /// <param name="staleLinks">Managed link-folder entries that are symlinks or junctions not resolving to the canonical entry (see <see cref="IsStaleLink"/>).</param>
        public FolderState(SkillsFolder folder, IEnumerable<string> entries, IEnumerable<string> managed,
            IReadOnlyDictionary<string, string> installedHashes = null, IEnumerable<string> staleLinks = null)
        {
            Folder = folder;
            Entries = new HashSet<string>(entries ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            Managed = new HashSet<string>(managed ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            _installedHashes = installedHashes ?? new Dictionary<string, string>();
            _staleLinks = new HashSet<string>(staleLinks ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        }

        readonly IReadOnlyDictionary<string, string> _installedHashes;
        readonly HashSet<string> _staleLinks;

        public SkillsFolder Folder { get; }

        /// <summary>Names of every entry (folder, link or file) in the folder, managed or not.</summary>
        public IReadOnlyCollection<string> Entries { get; }

        /// <summary>Names the folder's managed-state file says the tool manages.</summary>
        public IReadOnlyCollection<string> Managed { get; }

        public bool Has(string name) => ((HashSet<string>)Entries).Contains(name);

        public bool Manages(string name) => ((HashSet<string>)Managed).Contains(name);

        /// <summary>
        /// The <see cref="SkillFolderHash"/> of the managed copy named <paramref name="name"/>, or null when it was not
        /// hashed (not managed, not a folder, or a folder the scanner does not hash, such as a link folder).
        /// </summary>
        public string InstalledHash(string name) => _installedHashes.TryGetValue(name, out var hash) ? hash : null;

        /// <summary>
        /// True when the managed link-folder entry is a symlink or junction that is broken or resolves somewhere other
        /// than this project's canonical entry, so it must be re-linked.
        /// </summary>
        public bool IsStaleLink(string name) => _staleLinks.Contains(name);
    }

    /// <summary>Snapshot of the project's skills folders, as read by <see cref="ProjectScanner"/>.</summary>
    public sealed class ProjectState
    {
        readonly Dictionary<string, FolderState> _folders;

        public ProjectState(IEnumerable<FolderState> folders) =>
            _folders = folders.ToDictionary(f => f.Folder.RelativePath, StringComparer.Ordinal);

        /// <summary>A project with no skills folders at all.</summary>
        public static ProjectState Empty { get; } = new ProjectState(Enumerable.Empty<FolderState>());

        /// <summary>The folder's state; an empty state when the folder does not exist.</summary>
        public FolderState For(SkillsFolder folder) =>
            _folders.TryGetValue(folder.RelativePath, out var state) ? state : new FolderState(folder, null, null);
    }
}
