using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>
    /// Stands in for GitHub: serves zips shaped like repo archives (one top-level "repo-ref/" folder) instead of
    /// downloading. Repos are keyed "owner/repo"; a repo that is not served, or every repo when offline, fails.
    /// </summary>
    sealed class FakeGitHub : IArchiveDownloader
    {
        /// <summary>The CLI's hash of the <c>minimal</c> fixture (see SkillFolderHashTests).</summary>
        public const string MinimalHash = "6ad83e06b125610b593c54974d6bac14a4badfbaafe421ab6976e4de0c91baf9";

        /// <summary>The CLI's hash of the <c>nested</c> fixture.</summary>
        public const string NestedHash = "c8538d846c53675d0c411fed3e626550b0a6b36ec5b3b96568530bcc37700de1";

        static readonly string FixturesRoot =
            Path.GetFullPath("Packages/com.hissal.agent-skills-sync/Tests/Editor/Fixtures~/SkillFolderHash");

        readonly Dictionary<string, Dictionary<string, byte[]>> _repos =
            new Dictionary<string, Dictionary<string, byte[]>>(StringComparer.OrdinalIgnoreCase);

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
            const string suffix = "/archive/HEAD.zip";
            var repo = url.Substring(prefix.Length, url.Length - prefix.Length - suffix.Length);
            if (!_repos.TryGetValue(repo, out var files)) throw new IOException($"{url} returned HTTP 404 Not Found.");

            var top = repo.Substring(repo.IndexOf('/') + 1) + "-main/";
            using (var zip = ZipFile.Open(destinationPath, ZipArchiveMode.Create))
            {
                foreach (var file in files)
                    using (var stream = zip.CreateEntry(top + file.Key).Open())
                        stream.Write(file.Value, 0, file.Value.Length);
            }
        }
    }
}
