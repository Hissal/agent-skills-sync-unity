using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Compression;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Fetches skills from GitHub repo archives into a per-user cache and, in <see cref="InstallMode.Pinned"/> mode,
    /// verifies each against its locked <c>computedHash</c>. Each repo is downloaded at most once per fetcher instance
    /// (one sync), and a failed download is not retried within it; skill folders are extracted fresh on every fetch.
    /// </summary>
    public sealed class GitHubSkillFetcher : ISkillFetcher
    {
        const string SkillFile = "SKILL.md";

        readonly string _cacheRoot;
        readonly IArchiveDownloader _downloader;
        readonly InstallMode _mode;
        readonly Dictionary<string, string> _archives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Exception> _failedDownloads = new Dictionary<string, Exception>(StringComparer.OrdinalIgnoreCase);

        /// <param name="cacheRoot">Cache folder; defaults to <see cref="DefaultCacheRoot"/>.</param>
        /// <param name="downloader">Network access; defaults to <see cref="HttpArchiveDownloader"/>.</param>
        /// <param name="mode">
        /// <see cref="InstallMode.Pinned"/> (the default) refuses a skill whose content no longer matches its locked hash;
        /// <see cref="InstallMode.Latest"/> returns the current upstream copy without checking.
        /// </param>
        public GitHubSkillFetcher(string cacheRoot = null, IArchiveDownloader downloader = null, InstallMode mode = InstallMode.Pinned)
        {
            _mode = mode;
            _cacheRoot = cacheRoot ?? DefaultCacheRoot;
            _downloader = downloader ?? new HttpArchiveDownloader();
        }

        /// <summary>Per-user cache: local application data, else the temp folder.</summary>
        public static string DefaultCacheRoot
        {
            get
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(string.IsNullOrEmpty(local) ? Path.GetTempPath() : local, "AgentSkillsSync", "Cache");
            }
        }

        public string Fetch(LockedSkill skill)
        {
            var repo = ParseRepo(skill);
            // Latest installs current upstream without checking the lock, so an unverifiable lock hash is no reason to refuse.
            if (_mode == InstallMode.Pinned && !LockVerification.CanVerify(skill))
                throw Unverifiable(skill,
                    $"the lock holds a skills.sh server hash for {skill.Source}, which this tool cannot reproduce");
            if (_mode == InstallMode.Pinned && LockVerification.LacksMigratedHash(skill)) throw LockVerification.NoLockedHash(skill);
            var archive = DownloadOnce(repo, skill);
            var destination = Path.Combine(_cacheRoot, "skills", repo.Owner, repo.Name, skill.Name);
            Extract(archive, SkillFolderInRepo(skill), destination, skill);
            if (_mode == InstallMode.Pinned) Verify(destination, skill);
            return destination;
        }

        static void Verify(string folder, LockedSkill skill)
        {
            var actual = LockVerification.Hash(skill, folder);
            if (LockVerification.MatchesLock(skill, actual)) return;
            // Version 2 hashes normalise line endings and order paths exactly: a mismatch is a changed source.
            var version1 = skill.HashAlgorithm == SkillHashAlgorithm.Version1;
            // Version 1, locked from a checkout with core.autocrlf=true: the CLI hashed CRLF text files, the archive has LF.
            // Hand out that checkout's bytes, so the installed copy hashes to the lock and is not seen as outdated.
            if (version1 && LockVerification.MatchesLock(skill, SkillFolderHash.ComputeAsCrlfCheckout(folder)))
            {
                SkillFolderHash.ConvertToCrlfCheckout(folder);
                return;
            }
            var nonAsciiPaths = version1 && SkillFolderHash.HasNonAsciiPath(folder);

            try
            {
                Directory.Delete(folder, recursive: true); // never leave unverified content where it could be picked up
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            if (nonAsciiPaths)
                throw Unverifiable(skill,
                    "it has non-ASCII file names, and the CLI's hash orders those in a way this tool does not reproduce, " +
                    "so a mismatch does not show whether the source changed");

            throw LockVerification.SourceChangedSinceLocked(skill, actual);
        }

        static SkillFetchException Unverifiable(LockedSkill skill, string reason) =>
            new SkillFetchException(skill.Name, SkillFetchFailure.Unverifiable,
                $"Skill \"{skill.Name}\": can't verify this source's lock hash ({reason}). " +
                "It was not installed, because an unverified copy could differ from what was locked. " +
                "Switch the install mode to Latest to install the current upstream copy without this check.");

        string DownloadOnce((string Owner, string Name) repo, LockedSkill skill)
        {
            // The lock's ref (branch, tag or commit; Lockfile accepts only plain git ref names), else the default branch.
            var reference = string.IsNullOrEmpty(skill.Ref) ? null : skill.Ref;
            var key = repo.Owner + "/" + repo.Name + (reference == null ? "" : "@" + reference);
            if (_archives.TryGetValue(key, out var cached)) return cached;
            if (_failedDownloads.TryGetValue(key, out var failure)) throw DownloadFailed(key, skill, failure);

            var archiveName = repo.Name + (reference == null ? "" : "@" + Uri.EscapeDataString(reference)) + ".zip";
            var archive = Path.Combine(_cacheRoot, "archives", repo.Owner, archiveName);
            var partial = archive + ".part";
            var urlRef = reference == null ? "HEAD" : string.Join("/", Array.ConvertAll(reference.Split('/'), Uri.EscapeDataString));
            var url = $"https://github.com/{repo.Owner}/{repo.Name}/archive/{urlRef}.zip";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(archive));
                if (File.Exists(partial)) File.Delete(partial);
                _downloader.Download(url, partial);
                if (File.Exists(archive)) File.Delete(archive);
                File.Move(partial, archive);
            }
            catch (Exception e)
            {
                _failedDownloads[key] = e;
                throw DownloadFailed(key, skill, e);
            }

            _archives[key] = archive;
            return archive;
        }

        static SkillFetchException DownloadFailed(string repo, LockedSkill skill, Exception cause) =>
            new SkillFetchException(skill.Name, SkillFetchFailure.Download,
                $"Skill \"{skill.Name}\": could not download {repo}. Check your network connection; " +
                $"if you are online, the repo may have been deleted or made private. ({cause.Message})", cause);

        /// <summary>The skill's folder inside the repo, from <c>skillPath</c>; "" for the repo root, null when the lock omits it.</summary>
        static string SkillFolderInRepo(LockedSkill skill)
        {
            if (string.IsNullOrEmpty(skill.SkillPath)) return null;
            var path = skill.SkillPath.Replace('\\', '/').Trim('/');
            if (path == SkillFile) return "";
            if (path.EndsWith("/" + SkillFile, StringComparison.Ordinal)) return path.Substring(0, path.Length - SkillFile.Length - 1);
            return path;
        }

        static void Extract(string archivePath, string folderInRepo, string destination, LockedSkill skill)
        {
            try
            {
                if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);

                using (var zip = ZipFile.OpenRead(archivePath))
                {
                    var entries = new List<(ZipArchiveEntry Entry, string Path)>();
                    foreach (var entry in zip.Entries)
                    {
                        // GitHub archives wrap the repo in one "<repo>-<ref>/" folder.
                        var slash = entry.FullName.IndexOf('/');
                        if (slash < 0) continue;
                        entries.Add((entry, entry.FullName.Substring(slash + 1)));
                    }

                    if (folderInRepo == null) folderInRepo = FindFolderByName(entries, skill.Name);
                    var prefix = folderInRepo.Length == 0 ? "" : folderInRepo + "/";
                    var fullDestination = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
                    var found = false;
                    // Version 2 hashes the files the CLI's installer writes, so it is extracted the way that installer
                    // copies: from a tree of the archive's entries, with links followed (InstallerTree).
                    var tree = skill.HashAlgorithm == SkillHashAlgorithm.Version2 ? new InstallerTree() : null;

                    foreach (var (entry, path) in entries)
                    {
                        if (!path.StartsWith(prefix, StringComparison.Ordinal) || path.EndsWith("/", StringComparison.Ordinal)) continue;
                        var relative = path.Substring(prefix.Length);
                        var target = Path.GetFullPath(Path.Combine(destination, relative));
                        if (!target.StartsWith(fullDestination, StringComparison.Ordinal)) continue; // zip-slip guard
                        found = true;
                        if (tree != null)
                        {
                            tree.Add(relative, entry);
                            continue;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        entry.ExtractToFile(target);
                    }

                    tree?.CopyTo(destination);

                    if (!found)
                        throw new SkillFetchException(skill.Name, SkillFetchFailure.SourceUnusable,
                            $"Skill \"{skill.Name}\" was not found in {skill.Source} at \"{folderInRepo}\". " +
                            "The source may have moved it since it was locked.");
                }
            }
            catch (SkillFetchException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new SkillFetchException(skill.Name, SkillFetchFailure.SourceUnusable, $"Could not extract skill \"{skill.Name}\" from {skill.Source}: {e.Message}", e);
            }
        }

        // Git archives keep a symlink's Unix mode in the entry's high external-attribute bits; its content is the target.
        static bool IsSymlink(ZipArchiveEntry entry) => ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

        /// <summary>
        /// A skill folder as a GitHub archive holds it (files, and symlinks as entries holding their target), copied as
        /// the <c>skills</c> CLI 1.8's installer copies a checkout: <c>copyDirectory</c> in
        /// <a href="https://github.com/vercel-labs/skills/blob/v1.8.1/src/installer.ts">src/installer.ts</a>, whose
        /// output the version 2 hash covers. Excluded names are left out at every level; a link is replaced by the
        /// content it points to, unless it leaves the skill folder or dangles; a folder already on the current branch
        /// (a link cycle) adds nothing.
        /// </summary>
        sealed class InstallerTree
        {
            const int MaxLinkHops = 40;

            readonly Dictionary<string, ZipArchiveEntry> _files = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            readonly Dictionary<string, string> _links = new Dictionary<string, string>(StringComparer.Ordinal);
            readonly HashSet<string> _folders = new HashSet<string>(StringComparer.Ordinal) { "" };

            /// <summary>Adds an entry by its path in the skill folder (<c>/</c>-separated).</summary>
            public void Add(string path, ZipArchiveEntry entry)
            {
                if (IsSymlink(entry))
                    using (var reader = new StreamReader(entry.Open())) _links[path] = reader.ReadToEnd();
                else _files[path] = entry;
                for (var slash = path.LastIndexOf('/'); slash > 0; slash = path.LastIndexOf('/', slash - 1))
                    _folders.Add(path.Substring(0, slash));
            }

            string _root;

            public void CopyTo(string destination)
            {
                _root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                Copy("", destination, new HashSet<string>(StringComparer.Ordinal));
            }

            // The zip-slip guard again, on each path written: a linked folder's copy joins the link's path with names
            // from under its target, and Windows trims trailing dots and spaces, so ".. " can climb out on its own.
            bool Inside(string path) => (Path.GetFullPath(path) + Path.DirectorySeparatorChar).StartsWith(_root, StringComparison.Ordinal);

            // copyDirectory: realSource is the folder's real path, so a folder reached again on this branch stops here.
            void Copy(string realSource, string destination, HashSet<string> ancestors)
            {
                if (ancestors.Contains(realSource)) return;
                var branch = new HashSet<string>(ancestors, StringComparer.Ordinal) { realSource };
                Directory.CreateDirectory(destination);
                foreach (var name in Children(realSource))
                {
                    var path = Join(realSource, name);
                    var isLink = _links.ContainsKey(path);
                    var isFolder = !isLink && _folders.Contains(path);
                    if (SkillFolderHash.IsExcludedVersion2(name, isFolder)) continue;
                    var real = path;
                    if (isLink)
                    {
                        real = RealPath(path);
                        if (real == null) continue; // dangling, looping, or outside the skill folder
                        isFolder = _folders.Contains(real);
                        if (isFolder && SkillFolderHash.IsExcludedVersion2(name, true)) continue;
                    }
                    var target = Path.Combine(destination, name);
                    if (!Inside(target)) continue;
                    if (isFolder)
                    {
                        Copy(real, target, branch);
                    }
                    else
                    {
                        using (var input = _files[real].Open())
                        using (var output = File.Create(target))
                            input.CopyTo(output);
                    }
                }
            }

            IEnumerable<string> Children(string folder)
            {
                var prefix = folder.Length == 0 ? "" : folder + "/";
                return _files.Keys.Concat(_links.Keys).Concat(_folders)
                    .Where(p => p.Length > prefix.Length && p.StartsWith(prefix, StringComparison.Ordinal) && p.IndexOf('/', prefix.Length) < 0)
                    .Select(p => p.Substring(prefix.Length))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }

            // realpath within the skill folder, one component at a time: a link is replaced by its target, read from
            // the link's folder, and ".." leaves the folder resolved so far, as POSIX resolves it. Null when the path
            // leaves the skill folder (an absolute target included), names nothing, or loops.
            string RealPath(string path)
            {
                var hops = 0;
                var real = new List<string>();
                var parts = new LinkedList<string>(path.Split('/'));
                while (parts.Count > 0)
                {
                    var part = parts.First.Value;
                    parts.RemoveFirst();
                    if (part.Length == 0 || part == ".") continue;
                    if (part == "..")
                    {
                        if (real.Count == 0) return null;
                        real.RemoveAt(real.Count - 1);
                        continue;
                    }
                    var next = Join(string.Join("/", real), part);
                    if (_links.TryGetValue(next, out var target))
                    {
                        if (++hops > MaxLinkHops) return null;
                        if (target.StartsWith("/", StringComparison.Ordinal) || target.Contains("\\") || target.Contains(":"))
                            return null;
                        var targetParts = target.Split('/');
                        for (var i = targetParts.Length - 1; i >= 0; i--) parts.AddFirst(targetParts[i]);
                        continue;
                    }
                    if (!_files.ContainsKey(next) && !_folders.Contains(next)) return null;
                    real.Add(part);
                }
                return string.Join("/", real);
            }

            static string Join(string folder, string name) => folder.Length == 0 ? name : folder + "/" + name;
        }

        static string FindFolderByName(List<(ZipArchiveEntry Entry, string Path)> entries, string name)
        {
            foreach (var (_, path) in entries)
                if (path == name + "/" + SkillFile || path.EndsWith("/" + name + "/" + SkillFile, StringComparison.Ordinal))
                    return path.Substring(0, path.Length - SkillFile.Length - 1);
            return name;
        }

        /// <summary>The skill's GitHub <c>owner/repo</c> source.</summary>
        /// <exception cref="SkillFetchException">The source is not an owner/repo GitHub source.</exception>
        internal static (string Owner, string Name) ParseRepo(LockedSkill skill)
        {
            var source = skill.Source.Trim();
            foreach (var prefix in new[] { "https://github.com/", "http://github.com/", "github.com/" })
                if (source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) source = source.Substring(prefix.Length);
            if (source.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) source = source.Substring(0, source.Length - 4);

            var parts = source.Trim('/').Split('/');
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0 || parts[1] == ".." || parts[0] == "..")
                throw new SkillFetchException(skill.Name, SkillFetchFailure.SourceUnusable, $"Skill \"{skill.Name}\" has source \"{skill.Source}\", which is not an owner/repo GitHub source.");
            return (parts[0], parts[1]);
        }
    }
}
