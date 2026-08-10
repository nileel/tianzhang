using System.IO;
using System.Linq;
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
            AssertAssembly("Assets/Scripts/Entity/TianZhang.Domain.asmdef", "TianZhang.Domain", "TianZhang.Foundation", "TianZhang.Spatial");
            AssertAssembly("Assets/Scripts/Combat/TianZhang.Combat.asmdef", "TianZhang.Combat", "TianZhang.Foundation", "TianZhang.Domain", "TianZhang.Spatial");
            AssertAssembly(
                "Assets/Scripts/Game/TianZhang.Gameplay.asmdef",
                "TianZhang.Gameplay",
                "TianZhang.Foundation",
                "TianZhang.Domain",
                "TianZhang.Combat",
                "TianZhang.Spatial",
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

        [Test]
        public void TargetSkeletonUsesTheApprovedOneWayDependencies()
        {
            AssertAssembly("Assets/Scripts/Modules/Spatial/TianZhang.Spatial.asmdef",
                "TianZhang.Spatial", "TianZhang.Foundation");
            AssertAssembly("Assets/Scripts/Modules/Content/TianZhang.Content.asmdef",
                "TianZhang.Content", "TianZhang.Foundation");
            AssertAssembly("Assets/Scripts/Modules/Character/TianZhang.Character.asmdef",
                "TianZhang.Character", "TianZhang.Foundation", "TianZhang.Content", "TianZhang.Spatial");
            AssertAssembly("Assets/Scripts/Modules/Cultivation/TianZhang.Cultivation.asmdef",
                "TianZhang.Cultivation", "TianZhang.Foundation", "TianZhang.Content", "TianZhang.Character");
            AssertAssembly("Assets/Scripts/Modules/World/TianZhang.World.asmdef",
                "TianZhang.World", "TianZhang.Foundation", "TianZhang.Content");
            AssertAssembly("Assets/Scripts/Modules/GameplayContracts/TianZhang.Gameplay.Contracts.asmdef",
                "TianZhang.Gameplay.Contracts", "TianZhang.Foundation");

            AssertAssembly("Assets/Scripts/Modules/Features/CharacterCreation/TianZhang.Features.CharacterCreation.asmdef",
                "TianZhang.Features.CharacterCreation", "TianZhang.Foundation", "TianZhang.Content",
                "TianZhang.Character", "TianZhang.Cultivation", "TianZhang.Gameplay.Contracts");
            AssertAssembly("Assets/Scripts/Modules/Features/WorldMap/TianZhang.Features.WorldMap.asmdef",
                "TianZhang.Features.WorldMap", "TianZhang.Foundation", "TianZhang.Content",
                "TianZhang.World", "TianZhang.Gameplay.Contracts");
            AssertAssembly("Assets/Scripts/Modules/Features/Settlement/TianZhang.Features.Settlement.asmdef",
                "TianZhang.Features.Settlement", "TianZhang.Foundation", "TianZhang.Content",
                "TianZhang.Character", "TianZhang.World", "TianZhang.Gameplay.Contracts");
            AssertAssembly("Assets/Scripts/Modules/Features/Adventure/TianZhang.Features.Adventure.asmdef",
                "TianZhang.Features.Adventure", "TianZhang.Foundation", "TianZhang.Content",
                "TianZhang.Character", "TianZhang.World", "TianZhang.Combat", "TianZhang.Gameplay.Contracts");
            AssertAssembly("Assets/Scripts/Modules/Features/CombatPresentation/TianZhang.Features.CombatPresentation.asmdef",
                "TianZhang.Features.CombatPresentation", "TianZhang.Foundation", "TianZhang.Combat",
                "TianZhang.Gameplay.Contracts");

            AssertAssembly("Assets/Scripts/Modules/Infrastructure/Persistence/TianZhang.Infrastructure.Persistence.asmdef",
                "TianZhang.Infrastructure.Persistence", "TianZhang.Foundation", "TianZhang.Content",
                "TianZhang.Character", "TianZhang.Cultivation", "TianZhang.World", "TianZhang.Gameplay.Contracts");
            AssertAssembly("Assets/Scripts/Modules/Infrastructure/UnityContent/TianZhang.Infrastructure.UnityContent.asmdef",
                "TianZhang.Infrastructure.UnityContent", "TianZhang.Foundation", "TianZhang.Content");
            AssertAssembly("Assets/Scripts/Modules/Bootstrap/TianZhang.Bootstrap.asmdef",
                "TianZhang.Bootstrap", "TianZhang.Gameplay.Contracts",
                "TianZhang.Features.CharacterCreation", "TianZhang.Features.WorldMap",
                "TianZhang.Features.Settlement", "TianZhang.Features.Adventure",
                "TianZhang.Features.CombatPresentation", "TianZhang.Infrastructure.Persistence",
                "TianZhang.Infrastructure.UnityContent");

            string scriptsRoot = Path.Combine(Application.dataPath, "Scripts");
            string[] bootstrapDefinitions = Directory.GetFiles(scriptsRoot, "*.asmdef", SearchOption.AllDirectories)
                .Where(path => JsonUtility.FromJson<AssemblyDefinition>(File.ReadAllText(path)).name ==
                               "TianZhang.Bootstrap")
                .ToArray();
            Assert.That(bootstrapDefinitions, Has.Length.EqualTo(1));
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
