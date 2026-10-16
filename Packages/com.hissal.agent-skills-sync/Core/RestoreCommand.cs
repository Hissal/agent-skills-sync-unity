using System;
using System.IO;
using System.Linq;

namespace Hissal.AgentSkillsSync
{
    /// <summary>Prepares the package's standalone command outside the package and every checkout, keyed by package version.</summary>
    public sealed class RestoreCommand
    {
        readonly string _source;
        readonly string _directory;

        public RestoreCommand(string packageDirectory, string version, UserEnvironment environment)
        {
            if (string.IsNullOrWhiteSpace(version) || version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || version.Contains(".."))
                throw new ArgumentException("Invalid package version.", nameof(version));
            _source = Path.Combine(packageDirectory, "Tools~", "AgentSkillsSync.Cli", "AgentSkillsSync.Cli.csproj");
            _directory = Path.Combine(environment.StateDirectory, "commands", version);
        }

        public string PathForCommand => Path.Combine(_directory, "agent-skills-sync.dll");

        /// <summary>Requires a .NET SDK supporting the command's net8.0 target; a runtime alone cannot build it.</summary>
        public bool HasSdk()
        {
            try
            {
                return RestoreHook.RunProcess("dotnet", "--list-sdks", Path.GetDirectoryName(_source)).Split('\n')
                    .Any(line => int.TryParse(line.Split('.')[0], out var major) && major >= 8);
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is IOException) { return false; }
        }

        /// <summary>Builds into a temporary folder, publishing only a successful build. Existing version builds are reused.</summary>
        public string Build()
        {
            if (File.Exists(PathForCommand)) return PathForCommand;
            var parent = Path.GetDirectoryName(_directory);
            Directory.CreateDirectory(parent);
            var temp = Path.Combine(parent, ".build-" + Guid.NewGuid().ToString("N"));
            var backup = temp + "-previous";
            Directory.CreateDirectory(temp);
            var published = false;
            try
            {
                RestoreHook.RunProcess("dotnet", "build " + RestoreHook.ProcessQuote(_source) + " -nologo -v q --artifacts-path " +
                    RestoreHook.ProcessQuote(Path.Combine(temp, "artifacts")) + " -o " + RestoreHook.ProcessQuote(Path.Combine(temp, "command")),
                    Path.GetDirectoryName(_source));
                if (Directory.Exists(_directory)) Directory.Move(_directory, backup);
                try { Directory.Move(Path.Combine(temp, "command"), _directory); }
                catch { if (Directory.Exists(backup)) Directory.Move(backup, _directory); throw; }
                published = true;
                return PathForCommand;
            }
            finally
            {
                Cleanup(temp);
                if (published) Cleanup(backup);
            }
        }
        static void Cleanup(string directory)
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }
    }
}
