using System.IO;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>Test temp-folder cleanup that is safe on trees holding symlinks or junctions.</summary>
    static class TempDirectory
    {
        /// <summary>
        /// Deletes <paramref name="root"/> and everything in it. Links are removed first with
        /// <see cref="DirectoryLink.Remove"/>: Mono's recursive delete fails on a junction, and must never follow a link.
        /// </summary>
        public static void Delete(string root)
        {
            if (!Directory.Exists(root)) return;
            RemoveLinks(root);
            Directory.Delete(root, recursive: true);
        }

        static void RemoveLinks(string folder)
        {
            foreach (var child in Directory.GetDirectories(folder))
            {
                if (File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint)) DirectoryLink.Remove(child);
                else RemoveLinks(child);
            }
        }
    }
}
