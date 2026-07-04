using System.Linq;
using NUnit.Framework;
using TianZhang.Adventure;
using TianZhang.Editor;
using TianZhang.Game;
using TianZhang.Settlement;
using TianZhang.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

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

        [Test]
        public void WorldSceneControllerExposesPrototypeNodesAndSelectsCurrentNode()
        {
            DestroyExistingSceneFlowAndSession();
            var sessionGo = new GameObject("GameSessionTest");
            var controllerGo = new GameObject("WorldSceneControllerTest");
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                session.SetWorldNode("jiangzuo_hub");
                var controller = controllerGo.AddComponent<WorldSceneController>();

                Assert.AreEqual(4, controller.Nodes.Count);
                Assert.IsTrue(controller.TryGetNode("jiangzuo_hub", out var jiangzuo));
                Assert.AreEqual("太一道庭", jiangzuo.settlementId);
                CollectionAssert.Contains(jiangzuo.connectedNodeIds, "guanzhong_hub");
                Assert.IsTrue(controller.TryGetNode("longxi_hub", out var longxi));
                CollectionAssert.AreEqual(new[] { "longxi_trial" }, longxi.adventureIds);

                Assert.IsTrue(controller.SelectNode("longxi_hub"));

                Assert.AreEqual("longxi_hub", controller.SelectedNodeId);
                Assert.AreEqual("longxi_hub", session.CurrentWorldNodeId);
            }
            finally
            {
                Object.DestroyImmediate(controllerGo);
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void WorldSceneControllerBuildsNodeButtonsOnStart()
        {
            DestroyExistingSceneFlowAndSession();
            var sessionGo = new GameObject("GameSessionTest");
            var controllerGo = new GameObject("WorldSceneControllerTest");
            try
            {
                sessionGo.AddComponent<GameSession>();
                var controller = controllerGo.AddComponent<WorldSceneController>();

                typeof(WorldSceneController)
                    .GetMethod("Start", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(controller, null);

                var nodeButtons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(button => button.name.StartsWith("WorldNode_"))
                    .ToArray();

                Assert.AreEqual(4, nodeButtons.Length);
                Assert.IsNotNull(GameObject.Find("WorldNodePanel"));
                Assert.IsNotNull(GameObject.Find("EnterLocationButton"));
            }
            finally
            {
                var canvas = GameObject.Find("UICanvas");
                if (canvas != null)
                    Object.DestroyImmediate(canvas);
                Object.DestroyImmediate(controllerGo);
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
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

        private static void DestroyExistingSceneFlowAndSession()
        {
            if (SceneFlowManager.Instance != null)
                Object.DestroyImmediate(SceneFlowManager.Instance.gameObject);
            if (GameSession.Instance != null)
                Object.DestroyImmediate(GameSession.Instance.gameObject);
        }
    }
}
