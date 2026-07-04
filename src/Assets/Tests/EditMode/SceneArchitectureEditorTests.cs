using System.Linq;
using NUnit.Framework;
using TianZhang.Adventure;
using TianZhang.Editor;
using TianZhang.Settlement;
using TianZhang.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TianZhang.Tests
{
    public class SceneArchitectureEditorTests
    {
        private static readonly string[] ScenePaths =
        {
            "Assets/Scenes/StartMenuScene.unity",
            "Assets/Scenes/WorldScene.unity",
            "Assets/Scenes/SettlementScene.unity",
            "Assets/Scenes/AdventureScene.unity",
        };

        [Test]
        public void SceneArchitectureShellsAreRegisteredAndLoadWithExpectedControllers()
        {
            EditorBuildSettings.scenes = new EditorBuildSettingsScene[0];

            SceneBuilder.BuildSceneArchitectureShells();

            CollectionAssert.AreEqual(
                ScenePaths,
                EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray());

            AssertSceneHasObjects(ScenePaths[0], "StartMenuRoot", expectedControllerType: null);
            AssertSceneHasObjects(ScenePaths[1], "WorldRoot", typeof(WorldSceneController));
            AssertSceneHasObjects(ScenePaths[2], "SettlementRoot", typeof(SettlementSceneController));
            AssertSceneHasObjects(ScenePaths[3], "AdventureRoot", typeof(AdventureSceneController));
        }

        [Test]
        public void StartMenuSceneContainsSectSelectionFlow()
        {
            SceneBuilder.BuildStartMenuScene();

            EditorSceneManager.OpenScene(ScenePaths[0], OpenSceneMode.Single);

            var uiCanvas = GameObject.Find("UICanvas");
            var sectSelection = Object.FindFirstObjectByType<TianZhang.Game.SectSelectionManager>();

            Assert.IsNotNull(uiCanvas);
            Assert.IsNotNull(sectSelection);
            Assert.IsNotNull(sectSelection.selectionPanel);
            Assert.IsNotNull(sectSelection.buttonContainer);
            Assert.IsNotNull(sectSelection.startButton);
            Assert.AreSame(GameObject.Find("GameManager").GetComponent<TianZhang.Game.GameManager>(), sectSelection.gameManager);
        }

        private static void AssertSceneHasObjects(string scenePath, string rootName, System.Type expectedControllerType)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            Assert.IsTrue(scene.IsValid(), scenePath);
            Assert.IsTrue(scene.isLoaded, scenePath);
            Assert.IsNotNull(GameObject.Find(rootName), rootName);
            Assert.IsNotNull(GameObject.Find("Main Camera"), scenePath);
            Assert.IsNotNull(GameObject.Find("EventSystem"), scenePath);
            Assert.IsNotNull(GameObject.Find("GameManager"), scenePath);

            var sceneFlowManager = GameObject.Find("GameManager").GetComponent<TianZhang.Game.SceneFlowManager>();
            Assert.IsNotNull(sceneFlowManager, scenePath);

            var controller = GameObject.Find("SceneController");
            if (expectedControllerType == null)
            {
                Assert.IsNull(controller, scenePath);
                return;
            }

            Assert.IsNotNull(controller, scenePath);
            Assert.IsNotNull(controller.GetComponent(expectedControllerType), expectedControllerType.Name);
        }
    }
}
