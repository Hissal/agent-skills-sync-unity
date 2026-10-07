namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// An entry of <c>skills-lock.json</c> whose source type this tool does not install, e.g. a <c>unity-package</c> skill
    /// that a Unity package installs itself. The tool leaves its folders alone.
    /// </summary>
    public sealed class UnsupportedSkill
    {
        public UnsupportedSkill(string name, string sourceType)
        {
            Name = name;
            SourceType = sourceType;
        }

        /// <summary>The skill's name as the lock lists it.</summary>
        public string Name { get; }

        /// <summary>The lock's <c>sourceType</c>, e.g. <c>unity-package</c>.</summary>
        public string SourceType { get; }
    }
}
