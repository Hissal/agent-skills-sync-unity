using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Which <c>skills</c> CLI algorithm a <c>computedHash</c> was made with. The lockfile's <c>version</c> picks it
    /// (<see cref="Lockfile.HashAlgorithm"/>), and every comparison with that lock uses only that one.
    /// </summary>
    public enum SkillHashAlgorithm
    {
        /// <summary>Lockfile version 1 (<c>skills</c> CLI before 1.8.0): raw bytes in <c>localeCompare</c> path order.</summary>
        Version1 = 1,

        /// <summary>
        /// Lockfile version 2 (<c>skills</c> CLI 1.8.0 and later): byte-ordered paths, length-prefixed CRLF-normalised
        /// content, <c>metadata.json</c> and Python cache folders left out, symlinks inside the folder followed.
        /// </summary>
        Version2 = 2,
    }

    /// <summary>
    /// Computes a skill folder's content hash exactly as the <c>skills</c> CLI writes it to <c>computedHash</c> in
    /// <c>skills-lock.json</c>, with the algorithm of either lockfile version (<see cref="SkillHashAlgorithm"/>).
    /// See docs/skills-cli-findings.md for the algorithms and their limits.
    /// </summary>
    public static class SkillFolderHash
    {
        /// <summary>
        /// Returns the lowercase hex SHA-256 the CLI would record in a version 1 lock for the skill folder at
        /// <paramref name="skillDirectory"/> (the folder holding SKILL.md).
        /// </summary>
        /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
        public static string Compute(string skillDirectory) => Compute(skillDirectory, crlfCheckout: false);

        /// <summary>The hash the CLI would record for the folder in a lock whose <c>computedHash</c> uses <paramref name="algorithm"/>.</summary>
        /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
        public static string Compute(string skillDirectory, SkillHashAlgorithm algorithm) =>
            algorithm == SkillHashAlgorithm.Version2 ? ComputeVersion2(skillDirectory) : Compute(skillDirectory);

        /// <summary>
        /// Version 1 only: the hash the CLI records when it hashed a git checkout made with <c>core.autocrlf=true</c>: every text
        /// file (no NUL and no CR byte, as git decides for autocrlf) with its LF line endings turned into CRLF.
        /// </summary>
        internal static string ComputeAsCrlfCheckout(string skillDirectory) => Compute(skillDirectory, crlfCheckout: true);

        /// <summary>
        /// Rewrites the folder's text files as a <c>core.autocrlf=true</c> checkout has them, so the folder then
        /// hashes (<see cref="Compute(string)"/>) to what <see cref="ComputeAsCrlfCheckout"/> returned before.
        /// </summary>
        internal static void ConvertToCrlfCheckout(string skillDirectory)
        {
            var files = new List<(string Path, string FullName)>();
            CollectFiles(new DirectoryInfo(skillDirectory), "", files);
            foreach (var file in files)
            {
                var content = File.ReadAllBytes(file.FullName);
                var converted = ToCrlf(content);
                if (converted != content) File.WriteAllBytes(file.FullName, converted);
            }
        }

        /// <summary>
        /// Whether any file path in the folder has a non-ASCII character, where the version 1 path order (and so the
        /// hash) is not exact.
        /// </summary>
        internal static bool HasNonAsciiPath(string skillDirectory)
        {
            var files = new List<(string Path, string FullName)>();
            CollectFiles(new DirectoryInfo(skillDirectory), "", files);
            return files.Any(f => f.Path.Any(c => c > 0x7E));
        }

        static string Compute(string skillDirectory, bool crlfCheckout)
        {
            var root = new DirectoryInfo(skillDirectory);
            if (!root.Exists) throw new DirectoryNotFoundException($"Skill folder not found: {skillDirectory}");

            var files = new List<(string Path, string FullName)>();
            CollectFiles(root, "", files);
            files.Sort((a, b) => IcuRootPathComparer.Instance.Compare(a.Path, b.Path));

            using (var sha = SHA256.Create())
            {
                foreach (var file in files)
                {
                    var pathBytes = Encoding.UTF8.GetBytes(file.Path);
                    sha.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);
                    var content = File.ReadAllBytes(file.FullName);
                    if (crlfCheckout) content = ToCrlf(content);
                    sha.TransformBlock(content, 0, content.Length, null, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return string.Concat(sha.Hash.Select(b => b.ToString("x2")));
            }
        }

        // Mirrors the CLI's (1.8.x) computeSkillFolderHash and hashSkillFiles (dist/_chunks/remove.mjs of skills@1.8.1);
        // docs/skills-cli-findings.md §1a lists every rule it follows.
        static string ComputeVersion2(string skillDirectory)
        {
            if (!Directory.Exists(skillDirectory)) throw new DirectoryNotFoundException($"Skill folder not found: {skillDirectory}");
            var root = RealPath(skillDirectory);

            var files = new List<(byte[] Path, string FullName)>();
            CollectFilesVersion2(skillDirectory, "", root, new HashSet<string>(StringComparer.Ordinal), files);
            files.Sort((a, b) => CompareBytes(a.Path, b.Path));

            using (var sha = SHA256.Create())
            {
                var separator = new byte[] { 0 };
                foreach (var file in files)
                {
                    var content = NormalizeLineEndings(File.ReadAllBytes(file.FullName));
                    var length = Encoding.ASCII.GetBytes(content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    sha.TransformBlock(file.Path, 0, file.Path.Length, null, 0);
                    sha.TransformBlock(separator, 0, 1, null, 0);
                    sha.TransformBlock(length, 0, length.Length, null, 0);
                    sha.TransformBlock(separator, 0, 1, null, 0);
                    sha.TransformBlock(content, 0, content.Length, null, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return string.Concat(sha.Hash.Select(b => b.ToString("x2")));
            }
        }

        // The CLI's collectFiles: skip excluded names, follow a symlink (or junction, which Node also reports as a
        // symlink) only when its real path stays inside the skill folder's, never enter a folder already on the
        // current branch (a link cycle), and skip anything else that is neither file nor folder.
        static void CollectFilesVersion2(string directory, string relativePrefix, string root, HashSet<string> ancestors,
            List<(byte[] Path, string FullName)> files)
        {
            var realDirectory = RealPath(directory);
            if (ancestors.Contains(realDirectory)) return;
            var branch = new HashSet<string>(ancestors, StringComparer.Ordinal) { realDirectory };

            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                var isLink = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
                var isDirectory = !isLink && entry is DirectoryInfo;
                if (IsExcludedVersion2(entry.Name, isDirectory)) continue;
                var relativePath = relativePrefix + entry.Name;
                if (isLink)
                {
                    var target = DirectoryLink.FinalPath(entry.FullName);
                    if (target == null || (target != root && !target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
                        continue;
                    isDirectory = Directory.Exists(target);
                    if (isDirectory && IsExcludedVersion2(entry.Name, true)) continue;
                }

                if (isDirectory) CollectFilesVersion2(entry.FullName, relativePath + "/", root, branch, files);
                // hashSkillFiles turns every backslash into "/", so a Unix name holding one hashes as a path.
                else files.Add((Encoding.UTF8.GetBytes(relativePath.Replace('\\', '/')), entry.FullName));
            }
        }

        /// <summary>
        /// Whether a folder-relative path (<c>/</c> or <c>\\</c> separated) is one the version 2 hash leaves out, and so the
        /// CLI's installer never writes: the CLI's <c>isExcludedPath</c>.
        /// </summary>
        internal static bool IsExcludedPathVersion2(string relativePath)
        {
            var parts = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
                if (IsExcludedVersion2(parts[i], i < parts.Length - 1)) return true;
            return false;
        }

        // A file name is excluded whether the entry is a file or a folder, as in the CLI's isExcluded.
        static bool IsExcludedVersion2(string name, bool isDirectory) =>
            name == "metadata.json" || (isDirectory && (name == ".git" || name == "__pycache__" || name == "__pypackages__"));

        static string RealPath(string path) =>
            DirectoryLink.FinalPath(path) ?? throw new IOException($"Can't resolve the real path of {path}.");

        // Buffer.compare: unsigned bytes, then length.
        static int CompareBytes(byte[] x, byte[] y)
        {
            var length = Math.Min(x.Length, y.Length);
            for (var i = 0; i < length; i++)
                if (x[i] != y[i]) return x[i].CompareTo(y[i]);
            return x.Length.CompareTo(y.Length);
        }

        // Content with no NUL byte has each CRLF turned into LF; a lone CR stays.
        static byte[] NormalizeLineEndings(byte[] content)
        {
            const byte Cr = 0x0D, Lf = 0x0A;
            if (Array.IndexOf(content, (byte)0) >= 0) return content;
            var hasCrlf = false;
            for (var i = 0; i + 1 < content.Length && !hasCrlf; i++)
                hasCrlf = content[i] == Cr && content[i + 1] == Lf;
            if (!hasCrlf) return content;
            var normalized = new List<byte>(content.Length);
            for (var i = 0; i < content.Length; i++)
                if (!(content[i] == Cr && i + 1 < content.Length && content[i + 1] == Lf)) normalized.Add(content[i]);
            return normalized.ToArray();
        }

        static byte[] ToCrlf(byte[] content)
        {
            const byte Cr = 0x0D, Lf = 0x0A;
            if (Array.IndexOf(content, (byte)0) >= 0 || Array.IndexOf(content, Cr) >= 0) return content;
            var converted = new List<byte>(content.Length + content.Length / 16);
            foreach (var b in content)
            {
                if (b == Lf) converted.Add(Cr);
                converted.Add(b);
            }
            return converted.ToArray();
        }

        // Version 1: mirrors the CLI's (1.7.x) collectFiles: recurse into real folders except .git and node_modules,
        // take regular files, and skip symlinks and junctions (Node's Dirent reports them as
        // neither file nor folder).
        static void CollectFiles(DirectoryInfo directory, string relativePrefix, List<(string Path, string FullName)> files)
        {
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                var relativePath = relativePrefix + entry.Name;
                if (entry is DirectoryInfo subdirectory)
                {
                    if (entry.Name == ".git" || entry.Name == "node_modules") continue;
                    CollectFiles(subdirectory, relativePath + "/", files);
                }
                else
                {
                    files.Add((relativePath, entry.FullName));
                }
            }
        }

        /// <summary>
        /// The order JavaScript's <c>String.prototype.localeCompare</c> gives under ICU's root
        /// collation (as in the en locale), exact for printable ASCII: primary weights over the
        /// whole string first (case-insensitive, punctuation before digits before letters), then
        /// lowercase before uppercase. Other characters fall back to code-point order after all
        /// ASCII letters, which ICU does not match.
        /// </summary>
        sealed class IcuRootPathComparer : IComparer<string>
        {
            public static readonly IcuRootPathComparer Instance = new IcuRootPathComparer();

            // Printable ASCII in ICU root primary order; a letter's uppercase shares its weight.
            const string PrimaryOrder = " _-,;:!?.'\"()[]{}@*/\\&#%`^+<=>|~$0123456789abcdefghijklmnopqrstuvwxyz";
            const int NonAsciiBase = 0x10000;

            public int Compare(string x, string y)
            {
                var primary = CompareBy(x, y, Primary);
                if (primary != 0) return primary;
                var tertiary = CompareBy(x, y, Tertiary);
                if (tertiary != 0) return tertiary;
                return string.CompareOrdinal(x, y);
            }

            static int CompareBy(string x, string y, Func<char, int> weight)
            {
                var length = Math.Min(x.Length, y.Length);
                for (var i = 0; i < length; i++)
                {
                    var difference = weight(x[i]).CompareTo(weight(y[i]));
                    if (difference != 0) return difference;
                }
                return x.Length.CompareTo(y.Length);
            }

            static int Primary(char c)
            {
                if (c > 0x7E) return NonAsciiBase + c;
                var index = PrimaryOrder.IndexOf(char.ToLowerInvariant(c));
                return index >= 0 ? index : c - 0x20; // control characters: below every printable
            }

            static int Tertiary(char c) => c >= 'A' && c <= 'Z' ? 1 : 0;
        }
    }
}
