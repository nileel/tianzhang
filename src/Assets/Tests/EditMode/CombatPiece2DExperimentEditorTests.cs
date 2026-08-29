using System.Linq;
using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Features.CombatPresentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TianZhang.Tests.EditMode
{
    public sealed class CombatPiece2DExperimentEditorTests
    {
        private static readonly string[] FormalScenePaths =
        {
            SceneBuildSupport.StartMenuScenePath,
            SceneBuildSupport.WorldScenePath,
            SceneBuildSupport.SettlementScenePath,
            SceneBuildSupport.AdventureScenePath,
        };

        [Test]
        public void BuilderIsIdempotentAndPersistsOnlyTheExperimentComposition()
        {
            string[] buildSettingsBefore = EnabledBuildScenes();
            CombatPiece2DExperimentSceneBuilder.Build();
            CombatPiece2DExperimentSceneBuilder.Build();

            Scene scene = EditorSceneManager.OpenScene(CombatPiece2DExperimentSceneBuilder.ScenePath, OpenSceneMode.Single);
            Assert.IsTrue(scene.isLoaded);
            BattleAnimationSpriteCombatUnitPresentationAdapter adapter =
                Object.FindFirstObjectByType<BattleAnimationSpriteCombatUnitPresentationAdapter>();
            Assert.IsNotNull(adapter);
            Assert.AreEqual(1, Object.FindObjectsByType<BattleAnimationSpriteCombatUnitPresentationAdapter>(
                FindObjectsSortMode.None).Length);
            Assert.AreEqual(1, Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(0, Object.FindObjectsByType<TianZhang.Bootstrap.GameBootstrap>(
                FindObjectsSortMode.None).Length);
            Assert.IsNull(Object.FindFirstObjectByType<TianZhang.Bootstrap.AdventureSceneInstaller>());

            SerializedProperty prefab = new SerializedObject(adapter).FindProperty("battleAnimationSpritePrefab");
            Assert.IsNotNull(prefab);
            Assert.AreEqual(VisualBaselineBuilder.BattleAnimationSpritePrefabPath,
                AssetDatabase.GetAssetPath(prefab.objectReferenceValue));
            CollectionAssert.AreEqual(buildSettingsBefore, EnabledBuildScenes());
            CollectionAssert.AreEqual(FormalScenePaths, EnabledBuildScenes());
            Assert.IsFalse(EditorBuildSettings.scenes.Any(item =>
                item.path == CombatPiece2DExperimentSceneBuilder.ScenePath));
        }

        private static string[] EnabledBuildScenes() =>
            EditorBuildSettings.scenes.Where(item => item.enabled).Select(item => item.path).ToArray();
    }
}
