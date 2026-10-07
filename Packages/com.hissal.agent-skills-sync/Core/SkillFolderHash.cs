using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Computes a skill folder's content hash exactly as the <c>skills</c> CLI (lockfile version 1)
    /// writes it to <c>computedHash</c> in <c>skills-lock.json</c>.
    /// See docs/skills-cli-findings.md for the algorithm and its limits.
    /// </summary>
    public static class SkillFolderHash
    {
        /// <summary>
        /// Returns the lowercase hex SHA-256 the CLI would record for the skill folder at
        /// <paramref name="skillDirectory"/> (the folder holding SKILL.md).
        /// </summary>
        /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
        public static string Compute(string skillDirectory) => Compute(skillDirectory, crlfCheckout: false);

        /// <summary>
        /// The hash the CLI records when it hashed a git checkout made with <c>core.autocrlf=true</c>: every text
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

        /// <summary>Whether any file path in the folder has a non-ASCII character, where the path order (and so the hash) is not exact.</summary>
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

        // Mirrors the CLI's collectFiles: recurse into real folders except .git and node_modules,
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
