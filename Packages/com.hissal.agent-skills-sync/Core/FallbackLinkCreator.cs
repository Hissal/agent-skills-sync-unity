using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>Tries each link method in order and keeps the first that works.</summary>
    public sealed class FallbackLinkCreator : ILinkCreator
    {
        readonly IReadOnlyList<ILinkCreator> _methods;

        public FallbackLinkCreator(params ILinkCreator[] methods) => _methods = methods;

        /// <summary>Symlink, then directory junction, then a plain copy.</summary>
        public static FallbackLinkCreator Default =>
            new FallbackLinkCreator(new SymlinkCreator(), new JunctionCreator(), new CopyLinkCreator());

        public LinkMethod CreateDirectoryLink(string linkPath, string targetPath)
        {
            var failures = new List<string>();
            foreach (var method in _methods)
            {
                try
                {
                    return method.CreateDirectoryLink(linkPath, targetPath);
                }
                catch (IOException e)
                {
                    failures.Add(e.Message);
                }
            }

            throw new IOException($"Could not link {linkPath} -> {targetPath}: {string.Join(" | ", failures.DefaultIfEmpty("no link methods"))}");
        }
    }

    /// <summary>Last resort: copies the target. The copy does not follow later edits to the canonical copy.</summary>
    public sealed class CopyLinkCreator : ILinkCreator
    {
        public LinkMethod CreateDirectoryLink(string linkPath, string targetPath)
        {
            if (Directory.Exists(linkPath) || File.Exists(linkPath))
                throw new IOException($"Could not copy to {linkPath}: it already exists.");
            Paths.CopyDirectory(targetPath, linkPath);
            return LinkMethod.Copy;
        }
    }
}
