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
                    // Version 2 hashes a symlink as the content it points to, as the CLI's installer writes it, and leaves
                    // out what that installer never writes (metadata.json, Python caches), so nothing installed goes unhashed.
                    var links = skill.HashAlgorithm == SkillHashAlgorithm.Version2 ? new Dictionary<string, string>() : null;

                    foreach (var (entry, path) in entries)
                    {
                        if (!path.StartsWith(prefix, StringComparison.Ordinal) || path.EndsWith("/", StringComparison.Ordinal)) continue;
                        var relative = path.Substring(prefix.Length);
                        var target = Path.GetFullPath(Path.Combine(destination, relative));
                        if (!target.StartsWith(fullDestination, StringComparison.Ordinal)) continue; // zip-slip guard
                        found = true;
                        if (links != null && SkillFolderHash.IsExcludedPathVersion2(relative)) continue;
                        if (links != null && IsSymlink(entry))
                        {
                            using (var reader = new StreamReader(entry.Open())) links[relative] = reader.ReadToEnd();
                            continue;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        entry.ExtractToFile(target);
                    }

                    if (links != null) WriteLinkedContent(destination, links);

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
        /// Writes, at each archived symlink's path (folder-relative, <c>/</c>-separated, to its target), the file or
        /// folder the link points to, as the <c>skills</c> CLI 1.8 installs and hashes it (docs/skills-cli-findings.md
        /// §1a). A link that leaves the skill folder, dangles, or points at a folder holding it (a cycle) is left out,
        /// as the CLI leaves it out of the hash.
        /// </summary>
        static void WriteLinkedContent(string destination, Dictionary<string, string> links)
        {
            // Resolves a folder-relative path through any links on it; null when it leaves the folder or loops.
            string Resolve(string path)
            {
                for (var hops = 0; hops <= links.Count; hops++)
                {
                    var parts = path.Split('/');
                    string linked = null;
                    var at = 0;
                    for (; at < parts.Length; at++)
                    {
                        var head = string.Join("/", parts, 0, at + 1);
                        if (links.TryGetValue(head, out var target))
                        {
                            var parent = at == 0 ? "" : string.Join("/", parts, 0, at);
                            linked = Normalize(parent.Length == 0 ? target : parent + "/" + target);
                            break;
                        }
                    }
                    if (linked == null) return path;
                    if (linked == Outside) return null;
                    var rest = string.Join("/", parts, at + 1, parts.Length - at - 1);
                    path = rest.Length == 0 ? linked : linked.Length == 0 ? rest : linked + "/" + rest;
                }
                return null;
            }

            var resolved = links.Keys.Select(link => (Link: link, Target: Resolve(link))).Where(l => l.Target != null).ToList();

            // Files first, so a linked folder is copied with the files its own links point to.
            foreach (var (link, target) in resolved)
            {
                var source = Path.Combine(destination, target);
                if (!File.Exists(source)) continue;
                var copy = Path.Combine(destination, link);
                Directory.CreateDirectory(Path.GetDirectoryName(copy));
                File.Copy(source, copy);
            }

            // Then each folder link, once every folder link inside its target is written, so the copy holds them too.
            // A link inside its own target is a cycle the CLI leaves out too. Folder links into each other's targets are
            // left out as well, where the CLI keeps one level of each: such a rare source then fails Pinned verification
            // rather than installing unverified.
            var pending = resolved
                .Where(l => Directory.Exists(Path.Combine(destination, l.Target)) && !IsWithin(l.Link, l.Target))
                .ToList();
            while (true)
            {
                var ready = pending.FirstOrDefault(l => !pending.Any(other => other.Link != l.Link && IsWithin(other.Link, l.Target)));
                if (ready.Link == null) break;
                pending.Remove(ready);
                Paths.CopyDirectory(Path.Combine(destination, ready.Target), Path.Combine(destination, ready.Link));
            }
        }

        const string Outside = "\0outside";

        // Folds "." and ".." in a folder-relative path; Outside when it climbs out or is absolute.
        static string Normalize(string path)
        {
            if (path.StartsWith("/", StringComparison.Ordinal) || path.Contains("\\") || path.Contains(":")) return Outside;
            var parts = new List<string>();
            foreach (var part in path.Split('/'))
            {
                if (part.Length == 0 || part == ".") continue;
                if (part == "..")
                {
                    if (parts.Count == 0) return Outside;
                    parts.RemoveAt(parts.Count - 1);
                }
                else parts.Add(part);
            }
            return string.Join("/", parts);
        }

        // Whether folder-relative `path` is `folder` or inside it ("" is the skill folder itself).
        static bool IsWithin(string path, string folder) =>
            folder.Length == 0 || path == folder || path.StartsWith(folder + "/", StringComparison.Ordinal);

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
