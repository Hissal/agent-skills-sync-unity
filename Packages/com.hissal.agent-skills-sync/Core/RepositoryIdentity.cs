using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Which repository and Unity project a checkout belongs to, the same in every worktree of one repository: Git's
    /// common directory (shared by the main checkout and its linked worktrees) and the Unity project's path inside the
    /// checkout. Checkout folder names, the origin URL and where the main checkout sits play no part, so two clones are
    /// two repositories and two Unity projects in one repository are two identities.
    /// </summary>
    /// <remarks>
    /// Read from the files Git keeps rather than by running Git: a checkout's <c>.git</c> is the repository folder (the
    /// main checkout) or a file naming the worktree's own folder (<c>gitdir: …</c>), whose <c>commondir</c> file names
    /// the common directory; each linked worktree's <c>worktrees/&lt;id&gt;/gitdir</c> names its checkout's <c>.git</c>
    /// file. Either path may be relative (<c>worktree.useRelativePaths</c>). See
    /// https://git-scm.com/docs/gitrepository-layout and https://git-scm.com/docs/git-worktree#_details.
    /// Paths are compared as given, made absolute: a checkout reached through a symlink or an 8.3 short name reads as
    /// another identity, which shares nothing rather than the wrong thing.
    /// </remarks>
    public sealed class RepositoryIdentity
    {
        RepositoryIdentity(string commonDirectory, string checkout, string projectPath)
        {
            CommonDirectory = commonDirectory;
            Checkout = checkout;
            ProjectPath = projectPath;
            Key = Hash(Normalize(commonDirectory) + "\n" + projectPath);
        }

        /// <summary>Git's common directory: the main checkout's <c>.git</c> folder, or a bare repository.</summary>
        public string CommonDirectory { get; }

        /// <summary>The checkout (main or linked worktree) holding the project.</summary>
        public string Checkout { get; }

        /// <summary>The Unity project's path inside its checkout, with forward slashes; <c>.</c> for the checkout itself.</summary>
        public string ProjectPath { get; }

        /// <summary>A file-name-safe key for this repository and project, the same in every checkout.</summary>
        public string Key { get; }

        /// <summary>The identity of the Unity project at <paramref name="projectRoot"/>; null when it is in no Git checkout.</summary>
        public static RepositoryIdentity Find(string projectRoot)
        {
            var project = TrimEnd(Path.GetFullPath(projectRoot));
            for (var folder = project; folder != null; folder = Path.GetDirectoryName(folder))
            {
                var dotGit = Path.Combine(folder, ".git");
                string gitDirectory;
                if (Directory.Exists(dotGit)) gitDirectory = dotGit;
                else if (File.Exists(dotGit)) gitDirectory = ReadGitDirFile(dotGit, folder);
                else continue;
                if (gitDirectory == null) return null;

                var relative = Paths.Relative(folder, project).Replace(Path.DirectorySeparatorChar, '/');
                return new RepositoryIdentity(CommonDirectoryOf(gitDirectory), folder, relative.Length == 0 ? "." : relative);
            }
            return null;
        }

        /// <summary>
        /// Every checkout of the repository Git records that still exists: the main checkout first, then the linked
        /// worktrees, and this identity's own <see cref="Checkout"/>. Git records a main checkout only as the folder
        /// holding the <c>.git</c> folder, so the main checkout of a repository made with
        /// <c>git init --separate-git-dir</c> is missing unless it is this one (<c>git worktree list</c> shows the
        /// repository folder there instead); <see cref="SharedState.ProjectFolders"/> adds it once it has synced.
        /// </summary>
        public IReadOnlyList<string> Checkouts()
        {
            var checkouts = new List<string>();
            if (Path.GetFileName(CommonDirectory) == ".git") checkouts.Add(Path.GetDirectoryName(CommonDirectory));
            checkouts.Add(Checkout);
            var worktrees = Path.Combine(CommonDirectory, "worktrees");
            if (Directory.Exists(worktrees))
                foreach (var admin in Directory.GetDirectories(worktrees).OrderBy(d => d, StringComparer.Ordinal))
                {
                    var dotGit = ReadPathFile(Path.Combine(admin, "gitdir"), admin);
                    var checkout = dotGit == null ? null : Path.GetDirectoryName(dotGit);
                    if (checkout != null && Directory.Exists(checkout)) checkouts.Add(checkout);
                }
            return checkouts.Where(Directory.Exists).Distinct(Paths.Comparer).ToList();
        }

        /// <summary>This Unity project's folder in <paramref name="checkout"/>.</summary>
        public string ProjectIn(string checkout) => TrimEnd(Paths.InProject(checkout, ProjectPath));

        static string CommonDirectoryOf(string gitDirectory)
        {
            var common = ReadPathFile(Path.Combine(gitDirectory, "commondir"), gitDirectory);
            return TrimEnd(common ?? gitDirectory);
        }

        /// <summary>The folder a checkout's <c>.git</c> file names (<c>gitdir: &lt;path&gt;</c>); null when it names none.</summary>
        static string ReadGitDirFile(string file, string checkout)
        {
            const string prefix = "gitdir:";
            var text = ReadText(file);
            if (text == null || !text.StartsWith(prefix, StringComparison.Ordinal)) return null;
            return Resolve(text.Substring(prefix.Length).Trim(), checkout);
        }

        /// <summary>The path a one-line Git file holds, absolute; null when the file is missing or empty.</summary>
        static string ReadPathFile(string file, string relativeTo)
        {
            var text = ReadText(file);
            return string.IsNullOrEmpty(text) ? null : Resolve(text, relativeTo);
        }

        static string ReadText(string file)
        {
            try
            {
                return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        static string Resolve(string path, string relativeTo) =>
            TrimEnd(Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(relativeTo, path)));

        static string TrimEnd(string path)
        {
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmed.Length == 0 || trimmed.EndsWith(":", StringComparison.Ordinal) ? path : trimmed;
        }

        static string Normalize(string path)
        {
            var full = TrimEnd(Path.GetFullPath(path)).Replace('\\', '/');
            // Equal paths must give one key, so fold case where the file system ignores it.
            return Paths.Comparison == StringComparison.OrdinalIgnoreCase ? full.ToLowerInvariant() : full;
        }

        static string Hash(string text)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var hex = new StringBuilder();
                for (var i = 0; i < 16; i++) hex.Append(bytes[i].ToString("x2"));
                return hex.ToString();
            }
        }

        public override string ToString() => CommonDirectory + " (" + ProjectPath + ")";
    }
}
