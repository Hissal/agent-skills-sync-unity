namespace Hissal.AgentSkillsSync
{
    /// <summary>The shared rule for skill names used as folder names on every supported OS.</summary>
    public static class SkillName
    {
        // Apply Windows' rules everywhere so the same names work on every machine.
        static readonly char[] UnsafeChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

        public static bool IsSafe(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "." || name == "..") return false;
            if (name.IndexOfAny(UnsafeChars) >= 0) return false;
            foreach (var c in name)
                if (c < 0x20) return false;
            var last = name[name.Length - 1];
            return last != '.' && last != ' ';
        }

        /// <exception cref="LockfileException">The name cannot safely be used as a skill folder name.</exception>
        public static void Validate(string name)
        {
            if (IsSafe(name)) return;
            throw new LockfileException(
                $"Skill name {(name == null ? "(missing)" : $"\"{name}\"")} is not a safe skill folder name. " +
                "A skill name must be a single folder name: no path separators, no \".\" or \"..\", no drive or root, " +
                "no characters that are invalid in file names, and no trailing dot or space.");
        }
    }
}
