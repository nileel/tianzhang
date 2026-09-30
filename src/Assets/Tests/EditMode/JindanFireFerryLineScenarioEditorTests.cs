using System.Linq;
using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Features.Adventure;
using TianZhang.Features.CombatPresentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TianZhang.Tests.EditMode
{
    public sealed class JindanFireFerryLineScenarioEditorTests
    {
        private static readonly string[] FormalScenePaths =
        {
            SceneBuildSupport.StartMenuScenePath,
            SceneBuildSupport.WorldScenePath,
            SceneBuildSupport.SettlementScenePath,
            SceneBuildSupport.AdventureScenePath,
        };

        [Test]
        public void BuilderIsIdempotentAndPersistsOnlyTheIsolatedFixture()
        {
            string[] buildSettingsBefore = EnabledBuildScenes();
            JindanFireFerryLineScenarioSceneBuilder.Build();
            JindanFireFerryLineScenarioSceneBuilder.Build();

            Scene scene = EditorSceneManager.OpenScene(
                JindanFireFerryLineScenarioSceneBuilder.ScenePath,
                OpenSceneMode.Single);
            Assert.IsTrue(scene.isLoaded);
            JindanFireFerryLineScenarioController controller =
                Object.FindFirstObjectByType<JindanFireFerryLineScenarioController>();
            Assert.IsNotNull(controller);
            Assert.AreEqual(JindanFireFerryLineScenarioController.FixtureIdValue, controller.FixtureId);
            Assert.AreEqual(3, controller.RouteNodeCount);
            Assert.AreEqual(1, Object.FindObjectsByType<JindanFireFerryLineScenarioController>(
                FindObjectsSortMode.None).Length);
            Assert.AreEqual(1, Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length);
            var serialized = new SerializedObject(controller);
            Assert.AreSame(FindSceneObjectIncludingInactive("FormationEye_0_1"),
                serialized.FindProperty("formationEyeMarker").objectReferenceValue);
            Assert.AreSame(FindSceneObjectIncludingInactive("SourceFireMarker"),
                serialized.FindProperty("sourceMarker").objectReferenceValue);
            SerializedProperty ferryMarkers = serialized.FindProperty("ferryLineMarkers");
            Assert.AreEqual(3, ferryMarkers.arraySize);
            for (int index = 0; index < ferryMarkers.arraySize; index++)
                Assert.IsNotNull(ferryMarkers.GetArrayElementAtIndex(index).objectReferenceValue);
            Assert.AreSame(FindSceneObjectIncludingInactive("FixtureStatus").GetComponent<UnityEngine.UI.Text>(),
                serialized.FindProperty("stateText").objectReferenceValue);
            Assert.IsNotNull(GameObject.Find("SourceCell_0_0"));
            Assert.IsNotNull(GameObject.Find("FormationEye_0_1"));
            Assert.AreEqual(3, Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Count(item => item.name.StartsWith("FerryFireMarker_")));
            Assert.IsNotNull(GameObject.Find("FixtureStatus"));
            Assert.IsNull(Object.FindFirstObjectByType<TianZhang.Bootstrap.GameBootstrap>());
            Assert.IsNull(Object.FindFirstObjectByType<TianZhang.Bootstrap.AdventureSceneInstaller>());
            Assert.IsNull(Object.FindFirstObjectByType<Static3DCombatUnitPresentationProvider>());
            CollectionAssert.AreEqual(buildSettingsBefore, EnabledBuildScenes());
            CollectionAssert.AreEqual(FormalScenePaths, EnabledBuildScenes());
            Assert.IsFalse(EditorBuildSettings.scenes.Any(item =>
                item.path == JindanFireFerryLineScenarioSceneBuilder.ScenePath));
        }

        private static string[] EnabledBuildScenes() =>
            EditorBuildSettings.scenes.Where(item => item.enabled).Select(item => item.path).ToArray();

        private static GameObject FindSceneObjectIncludingInactive(string name)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (Transform value in root.GetComponentsInChildren<Transform>(true))
                if (value.name == name) return value.gameObject;
            return null;
        }
    }
}
