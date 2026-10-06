using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Hissal.AgentSkillsSync
{
    /// <summary>A cheap fingerprint of <c>skills-lock.json</c>, compared with the one recorded at the last sync.</summary>
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

            var text = File.ReadAllText(path).Replace("\r\n", "\n");
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
