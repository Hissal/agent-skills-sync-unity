using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>
    /// Stands in for GitHub: serves zips shaped like repo archives (one top-level "repo-ref/" folder) instead of
    /// downloading. Repos are keyed "owner/repo" (its default branch, <c>archive/HEAD.zip</c>) or "owner/repo@ref"
    /// (<c>archive/&lt;ref&gt;.zip</c>); a repo or ref that is not served, or every repo when offline, fails.
    /// </summary>
    sealed class FakeGitHub : IArchiveDownloader
    {
        /// <summary>The CLI's hash of the <c>minimal</c> fixture (see SkillFolderHashTests).</summary>
        public const string MinimalHash = "6ad83e06b125610b593c54974d6bac14a4badfbaafe421ab6976e4de0c91baf9";

        /// <summary>The CLI's hash of the <c>nested</c> fixture.</summary>
        public const string NestedHash = "c8538d846c53675d0c411fed3e626550b0a6b36ec5b3b96568530bcc37700de1";

        /// <summary>The CLI's version 2 hash (lock version 2) of the <c>minimal</c> fixture.</summary>
        public const string MinimalHashV2 = "470ad5e6b6adb3ab8c8144542d9900a17eefcf1a46e3cc08cc298a61340da90f";

        /// <summary>The CLI's version 2 hash of the <c>nested</c> fixture.</summary>
        public const string NestedHashV2 = "e1e3b7e2753077215f2b2de8d04714f647e2370e692953279db2b85888626dca";

        static readonly string FixturesRoot =
            Path.GetFullPath("Packages/com.hissal.agent-skills-sync/Tests/Editor/Fixtures~/SkillFolderHash");

        readonly Dictionary<string, Dictionary<string, byte[]>> _repos =
            new Dictionary<string, Dictionary<string, byte[]>>(StringComparer.OrdinalIgnoreCase);

        readonly HashSet<string> _symlinks = new HashSet<string>(StringComparer.Ordinal);

        public readonly List<string> Requested = new List<string>();
        public bool Offline;

        /// <summary>Puts a file into a repo's archive.</summary>
        public FakeGitHub File(string repo, string path, string text)
        {
            Repo(repo)[path] = System.Text.Encoding.UTF8.GetBytes(text);
            return this;
        }

        /// <summary>Puts a hash fixture's files into a repo's archive under <paramref name="folder"/>.</summary>
        public FakeGitHub Fixture(string repo, string folder, string fixture)
        {
            var source = Path.Combine(FixturesRoot, fixture);
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(source.Length + 1).Replace('\\', '/');
                Repo(repo)[folder + "/" + relative] = System.IO.File.ReadAllBytes(file);
            }
            return this;
        }

        /// <summary>Puts a symlink into a repo's archive, as git archives one: a link-mode entry holding its target.</summary>
        public FakeGitHub Symlink(string repo, string path, string target)
        {
            File(repo, path, target);
            _symlinks.Add(repo + "|" + path);
            return this;
        }

        Dictionary<string, byte[]> Repo(string repo)
        {
            if (!_repos.TryGetValue(repo, out var files)) _repos[repo] = files = new Dictionary<string, byte[]>();
            return files;
        }

        public void Download(string url, string destinationPath)
        {
            Requested.Add(url);
            if (Offline) throw new IOException("No such host is known.");

            const string prefix = "https://github.com/";
            const string archive = "/archive/";
            var path = url.Substring(prefix.Length, url.Length - prefix.Length - ".zip".Length);
            var repo = path.Substring(0, path.IndexOf(archive, StringComparison.Ordinal));
            var reference = Uri.UnescapeDataString(path.Substring(repo.Length + archive.Length));
            var key = reference == "HEAD" ? repo : repo + "@" + reference;
            if (!_repos.TryGetValue(key, out var files)) throw new IOException($"{url} returned HTTP 404 Not Found.");

            var top = repo.Substring(repo.IndexOf('/') + 1) + "-main/";
            using (var zip = ZipFile.Open(destinationPath, ZipArchiveMode.Create))
            {
                foreach (var file in files)
                {
                    var entry = zip.CreateEntry(top + file.Key);
                    if (_symlinks.Contains(repo + "|" + file.Key) || _symlinks.Contains(key + "|" + file.Key))
                        entry.ExternalAttributes = unchecked((int)0xA1FF0000); // S_IFLNK | 0777
                    using (var stream = entry.Open())
                        stream.Write(file.Value, 0, file.Value.Length);
                }
            }
        }
    }
}
