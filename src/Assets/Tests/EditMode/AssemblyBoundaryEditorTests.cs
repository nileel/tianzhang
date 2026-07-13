using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace TianZhang.Tests
{
    public class AssemblyBoundaryEditorTests
    {
        [Test]
        public void ProjectAssembliesFollowTheRuntimeDependencyDirection()
        {
            AssertAssembly("Assets/Scripts/Core/TianZhang.Foundation.asmdef", "TianZhang.Foundation");
            AssertAssembly("Assets/Scripts/Entity/TianZhang.Domain.asmdef", "TianZhang.Domain", "TianZhang.Foundation");
            AssertAssembly("Assets/Scripts/Combat/TianZhang.Combat.asmdef", "TianZhang.Combat", "TianZhang.Foundation", "TianZhang.Domain");
            AssertAssembly(
                "Assets/Scripts/Game/TianZhang.Gameplay.asmdef",
                "TianZhang.Gameplay",
                "TianZhang.Foundation",
                "TianZhang.Domain",
                "TianZhang.Combat",
                "Unity.InputSystem",
                "Unity.InputSystem.ForUI",
                "UnityEngine.UI");

            AssertAssemblyReference("Assets/Scripts/Cultivation/TianZhang.Domain.asmref", "TianZhang.Domain");
            AssertAssemblyReference("Assets/Scripts/Adventure/TianZhang.Gameplay.asmref", "TianZhang.Gameplay");
            AssertAssemblyReference("Assets/Scripts/World/TianZhang.Gameplay.asmref", "TianZhang.Gameplay");
            AssertAssemblyReference("Assets/Scripts/Settlement/TianZhang.Gameplay.asmref", "TianZhang.Gameplay");
            AssertAssemblyReference("Assets/Scripts/Map/TianZhang.Gameplay.asmref", "TianZhang.Gameplay");
            AssertAssemblyReference("Assets/Scripts/Grid/TianZhang.Gameplay.asmref", "TianZhang.Gameplay");
            AssertAssemblyReference("Assets/Scripts/Tilemap/TianZhang.Gameplay.asmref", "TianZhang.Gameplay");
        }

        private static void AssertAssembly(string relativePath, string expectedName, params string[] expectedReferences)
        {
            string fullPath = Path.Combine(Application.dataPath, "..", relativePath);
            Assert.That(File.Exists(fullPath), Is.True, "Missing assembly definition: " + relativePath);

            var definition = JsonUtility.FromJson<AssemblyDefinition>(File.ReadAllText(fullPath));
            Assert.That(definition.name, Is.EqualTo(expectedName), relativePath);
            CollectionAssert.AreEquivalent(expectedReferences, definition.references, relativePath);
        }

        private static void AssertAssemblyReference(string relativePath, string expectedReference)
        {
            string fullPath = Path.Combine(Application.dataPath, "..", relativePath);
            Assert.That(File.Exists(fullPath), Is.True, "Missing assembly reference: " + relativePath);

            var reference = JsonUtility.FromJson<AssemblyReference>(File.ReadAllText(fullPath));
            Assert.That(reference.reference, Is.EqualTo(expectedReference), relativePath);
        }

        [System.Serializable]
        private sealed class AssemblyDefinition
        {
            public string name = string.Empty;
            public string[] references = new string[0];
        }

        [System.Serializable]
        private sealed class AssemblyReference
        {
            public string reference = string.Empty;
        }
    }
}
