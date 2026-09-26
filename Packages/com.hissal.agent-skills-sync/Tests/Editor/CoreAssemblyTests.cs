using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class CoreAssemblyTests
    {
        [Test]
        public void Core_DoesNotReferenceUnity()
        {
            var unityReferences = Assembly.Load("Hissal.AgentSkillsSync.Core")
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(name => name.StartsWith("UnityEngine") || name.StartsWith("UnityEditor"));

            Assert.That(unityReferences, Is.Empty);
        }
    }
}
