using System;

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
            return last != '.' && last != ' ' && !IsWindowsDeviceName(name);
        }

        // Device names remain reserved when followed by an extension, including COM/LPT superscript digits.
        // https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file
        static bool IsWindowsDeviceName(string name)
        {
            var dot = name.IndexOf('.');
            var basename = dot < 0 ? name : name.Substring(0, dot);
            if (basename.Equals("CON", StringComparison.OrdinalIgnoreCase)
                || basename.Equals("PRN", StringComparison.OrdinalIgnoreCase)
                || basename.Equals("AUX", StringComparison.OrdinalIgnoreCase)
                || basename.Equals("NUL", StringComparison.OrdinalIgnoreCase)) return true;
            if (basename.Length != 4
                || !(basename.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || basename.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))) return false;
            var digit = basename[3];
            return (digit >= '1' && digit <= '9') || digit == '\u00b9' || digit == '\u00b2' || digit == '\u00b3';
        }

        /// <exception cref="LockfileException">The name cannot safely be used as a skill folder name.</exception>
        public static void Validate(string name)
        {
            if (IsSafe(name)) return;
            throw new LockfileException(
                $"Skill name {(name == null ? "(missing)" : $"\"{name}\"")} is not a safe skill folder name. " +
                "A skill name must be a single folder name: no path separators, no \".\" or \"..\", no drive or root, " +
                "no characters that are invalid in file names, no trailing dot or space, and no reserved Windows device names.");
        }
    }
}
