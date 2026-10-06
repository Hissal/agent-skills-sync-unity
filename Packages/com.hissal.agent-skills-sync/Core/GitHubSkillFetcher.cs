using System;
using System.Collections.Generic;
using System.IO;
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

        /// <summary>
        /// Owners (and owner/repo sources) the <c>skills</c> CLI installs from skills.sh snapshots, locking a server hash
        /// of another algorithm. See docs/skills-cli-findings.md §1 "Blob-installed sources".
        /// </summary>
        static readonly string[] SkillsShHashedSources = { "vercel", "vercel-labs", "heygen-com", "remotion-dev", "zapier/connectors" };

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

        /// <summary>
        /// Whether the skill's <c>computedHash</c> can be checked. False for sources the CLI locks with a skills.sh
        /// server hash, which <see cref="SkillFolderHash"/> does not reproduce; <see cref="Fetch"/> refuses those.
        /// </summary>
        public static bool CanVerify(LockedSkill skill)
        {
            string owner, name;
            try
            {
                (owner, name) = ParseRepo(skill);
            }
            catch (SkillFetchException)
            {
                return true; // Fetch refuses it anyway.
            }

            foreach (var source in SkillsShHashedSources)
                if (string.Equals(source, owner, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(source, owner + "/" + name, StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }

        public string Fetch(LockedSkill skill)
        {
            var repo = ParseRepo(skill);
            // Latest installs current upstream without checking the lock, so an unverifiable lock hash is no reason to refuse.
            if (_mode == InstallMode.Pinned && !CanVerify(skill))
                throw Unverifiable(skill,
                    $"the lock holds a skills.sh server hash for {skill.Source}, which this tool cannot reproduce");
            var archive = DownloadOnce(repo, skill);
            var destination = Path.Combine(_cacheRoot, "skills", repo.Owner, repo.Name, skill.Name);
            Extract(archive, SkillFolderInRepo(skill), destination, skill);
            if (_mode == InstallMode.Pinned) Verify(destination, skill);
            return destination;
        }

        static void Verify(string folder, LockedSkill skill)
        {
            var actual = SkillFolderHash.Compute(folder);
            if (MatchesLock(skill, actual)) return;
            // Locked from a checkout with core.autocrlf=true: the CLI hashed CRLF text files, the archive has LF.
            // Hand out that checkout's bytes, so the installed copy hashes to the lock and is not seen as outdated.
            if (MatchesLock(skill, SkillFolderHash.ComputeAsCrlfCheckout(folder)))
            {
                SkillFolderHash.ConvertToCrlfCheckout(folder);
                return;
            }
            var nonAsciiPaths = SkillFolderHash.HasNonAsciiPath(folder);

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

            throw SourceChangedSinceLocked(skill, actual);
        }

        /// <summary>Whether <paramref name="hash"/> (a <see cref="SkillFolderHash"/>) is the skill's locked <c>computedHash</c>.</summary>
        internal static bool MatchesLock(LockedSkill skill, string hash) =>
            hash != null && string.Equals(hash, skill.ComputedHash?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);

        /// <summary>The Pinned-mode refusal of a skill whose upstream content hashes to <paramref name="actual"/>, not the lock.</summary>
        internal static SkillFetchException SourceChangedSinceLocked(LockedSkill skill, string actual)
        {
            var locked = skill.ComputedHash?.Trim() ?? "";
            var lockedText = locked.Length == 0 ? "no hash" : Short(locked);
            return new SkillFetchException(skill.Name, SkillFetchFailure.HashMismatch,
                $"Skill \"{skill.Name}\": source {skill.Source} changed since it was locked (locked {lockedText}, now {Short(actual)}). " +
                $"Run `npx skills update` and commit {Lockfile.FileName}, or switch the install mode to Latest.");
        }

        /// <summary>
        /// Whether <paramref name="folder"/> (a copy of the skill) verifiably differs from the lock: false when it hashes
        /// to the lock as it is or as a CRLF checkout, and when a mismatch can't tell (no locked hash, skills.sh hash,
        /// non-ASCII paths).
        /// </summary>
        internal static bool DiffersFromLock(LockedSkill skill, string folder) =>
            !string.IsNullOrWhiteSpace(skill.ComputedHash)
            && CanVerify(skill)
            && !MatchesLock(skill, SkillFolderHash.Compute(folder))
            && !MatchesLock(skill, SkillFolderHash.ComputeAsCrlfCheckout(folder))
            && !SkillFolderHash.HasNonAsciiPath(folder);

        static SkillFetchException Unverifiable(LockedSkill skill, string reason) =>
            new SkillFetchException(skill.Name, SkillFetchFailure.Unverifiable,
                $"Skill \"{skill.Name}\": can't verify this source's lock hash ({reason}). " +
                "It was not installed, because an unverified copy could differ from what was locked. " +
                "Switch the install mode to Latest to install the current upstream copy without this check.");

        static string Short(string hash) => hash.Length > 12 ? hash.Substring(0, 12) : hash;

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

                    foreach (var (entry, path) in entries)
                    {
                        if (!path.StartsWith(prefix, StringComparison.Ordinal) || path.EndsWith("/", StringComparison.Ordinal)) continue;
                        var target = Path.GetFullPath(Path.Combine(destination, path.Substring(prefix.Length)));
                        if (!target.StartsWith(fullDestination, StringComparison.Ordinal)) continue; // zip-slip guard
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        entry.ExtractToFile(target);
                        found = true;
                    }

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

        static string FindFolderByName(List<(ZipArchiveEntry Entry, string Path)> entries, string name)
        {
            foreach (var (_, path) in entries)
                if (path == name + "/" + SkillFile || path.EndsWith("/" + name + "/" + SkillFile, StringComparison.Ordinal))
                    return path.Substring(0, path.Length - SkillFile.Length - 1);
            return name;
        }

        static (string Owner, string Name) ParseRepo(LockedSkill skill)
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
