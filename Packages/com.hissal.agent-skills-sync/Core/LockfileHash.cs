using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Cheap fingerprints of <c>skills-lock.json</c>, compared with the ones recorded at the last sync: of the whole
    /// file, and of only what this machine installs from it.
    /// </summary>
    public static class LockfileHash
    {
        /// <summary>
        /// SHA-256 (lowercase hex) of the lockfile's text with line endings normalized to LF, so a
        /// checkout that converts line endings does not count as a change; null when there is no lockfile.
        /// </summary>
        public static string Compute(string projectRoot)
        {
            var path = Path.Combine(projectRoot, Lockfile.FileName);
            if (!File.Exists(path)) return null;

            return Sha256(File.ReadAllText(path).Replace("\r\n", "\n"));
        }

        /// <summary>
        /// SHA-256 (lowercase hex) of the lock entries a sync on this machine reads: every field of each skill in
        /// <paramref name="selection"/> and the name and source type of each unsupported entry, in name order. Locking,
        /// changing or removing a skill this machine does not install leaves it unchanged, as does reformatting the file.
        /// </summary>
        public static string OfSelected(Lockfile lockfile, SkillSelection selection)
        {
            var text = new StringBuilder();
            foreach (var skill in selection.SelectedIn(lockfile).OrderBy(s => s.Name, StringComparer.Ordinal))
                foreach (var field in new[] { skill.Name, skill.Source, skill.SourceType, skill.SkillPath, skill.ComputedHash, skill.Ref })
                    text.Append(field).Append(field == null ? '\u0001' : '\0');
            text.Append('\n');
            foreach (var skill in lockfile.Unsupported.OrderBy(s => s.Name, StringComparer.Ordinal))
                text.Append(skill.Name).Append('\0').Append(skill.SourceType).Append('\0');
            return Sha256(text.ToString());
        }

        static string Sha256(string text)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var hex = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) hex.Append(b.ToString("x2"));
                return hex.ToString();
            }
        }
    }
}
