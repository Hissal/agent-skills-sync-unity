using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Which skills folders of the folder table this machine installs into.
    /// </summary>
    public static class FolderSelection
    {
        /// <summary>
        /// Shallow autofill: the folders, in table order, for which any user-scope location's home exists on this
        /// machine (e.g. <c>~/.codex</c> pre-selects <c>.agents/skills</c>). No per-agent install detection.
        /// </summary>
        public static IReadOnlyList<SkillsFolder> Autofill(FolderLayout table, UserEnvironment environment) =>
            table.Folders.Where(folder => HasHome(folder, environment)).ToList();

        /// <summary>Whether any of the folder's user-scope homes exists on this machine.</summary>
        public static bool HasHome(SkillsFolder folder, UserEnvironment environment) =>
            folder.UserScopeHomes(environment).Any(Directory.Exists);
    }
}
