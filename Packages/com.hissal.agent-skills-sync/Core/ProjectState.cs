using System;
using System.Collections.Generic;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>What one skills folder currently holds.</summary>
    public sealed class FolderState
    {
        /// <param name="installedHashes">Content hash of each managed skill copy in the folder (see <see cref="InstalledHash"/>).</param>
        /// <param name="files">Names in <paramref name="entries"/> that are plain files rather than folders or folder links.</param>
        /// <param name="withoutSkillFile">Folders in <paramref name="entries"/> with no <c>SKILL.md</c>, which are not skills.</param>
        /// <param name="ignored">Names the folder's <c>.gitignore</c> block lists now (see <see cref="Ignored"/>).</param>
        /// <param name="staleLinks">Managed link-folder entries that no longer show the canonical entry (see <see cref="IsStaleLink"/>).</param>
        public FolderState(SkillsFolder folder, IEnumerable<string> entries, IEnumerable<string> managed,
            IReadOnlyDictionary<string, string> installedHashes = null, IEnumerable<string> files = null,
            IEnumerable<string> staleLinks = null, IEnumerable<string> withoutSkillFile = null,
            IEnumerable<string> ignored = null)
        {
            Folder = folder;
            Entries = new HashSet<string>(entries ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            Managed = new HashSet<string>(managed ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            Ignored = new HashSet<string>(ignored ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            _installedHashes = installedHashes ?? new Dictionary<string, string>();
            _files = new HashSet<string>(files ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            _staleLinks = new HashSet<string>(staleLinks ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            _withoutSkillFile = new HashSet<string>(withoutSkillFile ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        }

        readonly IReadOnlyDictionary<string, string> _installedHashes;
        readonly HashSet<string> _files;
        readonly HashSet<string> _staleLinks;
        readonly HashSet<string> _withoutSkillFile;

        public SkillsFolder Folder { get; }

        /// <summary>Names of every entry (folder, link or file) in the folder, managed or not.</summary>
        public IReadOnlyCollection<string> Entries { get; }

        /// <summary>
        /// Names of the entries this machine's syncs made in the folder and still manage (from
        /// <see cref="LocalPrefs.ManagedSkills"/>; before the first recorded sync, the names in the folder's
        /// <c>.gitignore</c> block).
        /// </summary>
        public IReadOnlyCollection<string> Managed { get; }

        /// <summary>
        /// Names the tool's block in the folder's <c>.gitignore</c> lists now. The block is committed and the same on
        /// every machine (see <see cref="InstallPlan.IgnoredNames"/>), so it is no record of what this machine manages.
        /// </summary>
        public IReadOnlyCollection<string> Ignored { get; }

        public bool Has(string name) => ((HashSet<string>)Entries).Contains(name);

        public bool Manages(string name) => ((HashSet<string>)Managed).Contains(name);

        /// <summary>
        /// The <see cref="SkillFolderHash"/> of the managed copy named <paramref name="name"/>, or null when it was not
        /// hashed (not managed, not a folder, or a folder the scanner does not hash, such as a link folder).
        /// </summary>
        public string InstalledHash(string name) => _installedHashes.TryGetValue(name, out var hash) ? hash : null;

        /// <summary>True when the entry is a plain file (e.g. a README), which can never be a skill.</summary>
        public bool IsFile(string name) => _files.Contains(name);

        /// <summary>True when the entry is a skill: a folder (or folder link) holding a <c>SKILL.md</c>.</summary>
        public bool IsSkillFolder(string name) => Has(name) && !_files.Contains(name) && !_withoutSkillFile.Contains(name);

        /// <summary>
        /// True when the managed link-folder entry no longer shows the canonical entry, so it must be re-linked: a
        /// symlink or junction that is broken or resolves somewhere else, or a copy made by the Copy fallback whose
        /// content differs from the canonical folder's.
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
