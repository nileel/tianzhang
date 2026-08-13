using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Editor;
using TianZhang.Features.Adventure;
using TianZhang.Features.CharacterCreation;
using TianZhang.Features.Settlement;
using TianZhang.Features.WorldMap;
using TianZhang.Game.CharacterCreation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TianZhang.Tests.EditMode
{
    public sealed class SceneArchitectureEditorTests
    {
        [Test]
        public void BuildSettingsContainExactlyFourFormalScenes()
        {
            CollectionAssert.AreEqual(
                new[]
                {
                    SceneBuildSupport.StartMenuScenePath,
                    SceneBuildSupport.WorldScenePath,
                    SceneBuildSupport.SettlementScenePath,
                    SceneBuildSupport.AdventureScenePath,
                },
                EditorBuildSettings.scenes.Where(item => item.enabled).Select(item => item.path).ToArray());
        }

        [TestCase(SceneBuildSupport.StartMenuScenePath, typeof(StartMenuSceneInstaller), typeof(StartMenuController), true)]
        [TestCase(SceneBuildSupport.WorldScenePath, typeof(WorldSceneInstaller), typeof(WorldMapController), false)]
        [TestCase(SceneBuildSupport.SettlementScenePath, typeof(SettlementSceneInstaller), typeof(SettlementController), false)]
        [TestCase(SceneBuildSupport.AdventureScenePath, typeof(AdventureSceneInstaller), typeof(AdventureController), false)]
        public void FormalSceneHasOneInstallerAndExpectedBootstrap(
            string path,
            System.Type installerType,
            System.Type controllerType,
            bool expectsBootstrap)
        {
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Assert.IsNotNull(Object.FindFirstObjectByType(installerType));
            Assert.IsNotNull(Object.FindFirstObjectByType(controllerType));
            Assert.AreEqual(
                expectsBootstrap ? 1 : 0,
                Object.FindObjectsByType<GameBootstrap>(FindObjectsSortMode.None).Length);
        }

        [Test]
        public void PointBuyBindingIsIdempotentAndUsesOnlyProductionAsset()
        {
            StartMenuSceneBuilder.BindPointBuyConfig();
            StartMenuSceneBuilder.BindPointBuyConfig();
            StartMenuSceneInstaller installer = Object.FindFirstObjectByType<StartMenuSceneInstaller>();
            var serializedInstaller = new SerializedObject(installer);
            SerializedProperty configProperty = serializedInstaller.FindProperty("pointBuyConfig");
            CharacterCreationPointBuyConfig expected =
                AssetDatabase.LoadAssetAtPath<CharacterCreationPointBuyConfig>(
                    "Assets/Resources/Data/CharacterCreation/CharacterCreationPointBuyConfig.asset");

            Assert.IsNotNull(expected);
            Assert.IsNotNull(configProperty);
            Assert.AreSame(expected, configProperty.objectReferenceValue);
            Assert.AreEqual(
                1,
                Object.FindObjectsByType<StartMenuSceneInstaller>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(
                1,
                AssetDatabase.FindAssets(
                    "t:CharacterCreationPointBuyConfig",
                    new[] { "Assets/Resources/Data/CharacterCreation" }).Length);
        }

        [Test]
        public void LegacyFormalControllersAreAbsentFromSource()
        {
            Assert.IsFalse(System.IO.File.Exists("Assets/Scripts/Map/ExplorationController.cs"));
            Assert.IsFalse(System.IO.File.Exists("Assets/Scripts/Game/BattleUIManager.cs"));
            Assert.IsFalse(System.IO.File.Exists("Assets/Scripts/Game/SceneFlowManager.cs"));
            Assert.IsFalse(System.IO.File.Exists("Assets/Scripts/Game/SectSelectionManager.cs"));
        }

        [Test]
        public void LegacyPrototypesNamesAndSerializedGuidsAreAbsent()
        {
            foreach (string relativePath in new[]
            {
                "Assets/Scenes/CharacterPresentationPrototype.unity",
                "Assets/Scenes/HybridTacticalPrototype.unity",
                "Assets/Scripts/Game",
                "Assets/Scripts/Adventure",
                "Assets/Scripts/World",
                "Assets/Scripts/Settlement",
                "Assets/Scripts/Grid",
                "Assets/Scripts/Tilemap",
                "Assets/Data/CharacterPresentation",
            })
            {
                Assert.That(File.Exists(relativePath) || Directory.Exists(relativePath), Is.False, relativePath);
            }

            string runtimeSource = string.Join("\n", Directory
                .GetFiles(Path.Combine(Application.dataPath, "Scripts"), "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
            foreach (string forbiddenName in new[]
            {
                "ExplorationController", "TacticalCombatController", "BattleUIManager",
                "CharacterPresentationDefinition", "CharacterPresentationPrototypeBootstrap",
                "CharacterPresentationView", "HybridTacticalRenderer",
                "HybridTacticalPrototypeController", "TilemapTacticalRenderer",
                "HexTilemapManager", "SettlementDefinition",
            })
            {
                StringAssert.DoesNotContain(forbiddenName, runtimeSource, forbiddenName);
            }
            StringAssert.DoesNotContain("Resources.Load", runtimeSource);

            string[] serializedPaths = Directory.GetFiles(Application.dataPath, "*.*", SearchOption.AllDirectories)
                .Where(path => new[] { ".unity", ".prefab", ".asset", ".controller", ".anim" }
                    .Contains(Path.GetExtension(path)))
                .ToArray();
            string serializedText = string.Join("\n", serializedPaths.Select(File.ReadAllText));
            foreach (string forbiddenGuid in new[]
            {
                "94c894b71a044b1a90481056883d2e79", "1d89dbb770d441e1b5ed4dca483b5958",
                "86c802f5138b468fa610ad6fe1484156", "a1f682eb19af4a1dbfacf2d7db8ec465",
                "9b3ebc52ac2b4f8a8d810c94bf77440b", "6d19730dc3a24d4dbb2c1ec2b88352de",
                "f2d2660b8be74bb1a0d4aea69bc97803", "94e07f99acd54ceab27e0e53bbd3268e",
                "a46f750bfb654de418640b6e8ef0b6c5", "627c6a97d3b3f7c4d8c24a4d6ab8574a",
                "c55c0fc3249cf1b45bc8573edda736ad", "60defa32f87f4c7db6cb77d8c94d8d7e",
                "e14cb87aa02a42c39d408b91ede8792a", "7fd737d9074f4f60a5325dde7fc3e857",
                "b9e07c9dc74d47ca819ece49b414534f", "5e94c574d72a1ef44939707c77015b8b",
                "746f2703a0362914dbd184bb2572cfd5", "c7a729d02f2210c45a43b4f0fb9f3eb6",
                "9e44f567e8af5c1479a2d1cbe78bc011",
            })
            {
                StringAssert.DoesNotContain(forbiddenGuid, serializedText, forbiddenGuid);
            }
        }
    }
}
