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
    }
}
