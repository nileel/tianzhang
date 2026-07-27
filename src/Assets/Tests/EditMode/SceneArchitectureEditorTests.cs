using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TianZhang.Adventure;
using TianZhang.Combat;
using TianZhang.Cultivation;
using TianZhang.Editor;
using TianZhang.Game;
using TianZhang.Settlement;
using TianZhang.Tactical;
using TianZhang.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
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
        public void BatchValidationDoesNotRewriteFormalSceneFiles()
        {
            var originalContents = ScenePaths.ToDictionary(path => path, File.ReadAllBytes);

            SceneBuilder.ValidateSceneArchitectureShellsForBatchMode();

            foreach (var scenePath in ScenePaths)
                CollectionAssert.AreEqual(originalContents[scenePath], File.ReadAllBytes(scenePath), scenePath);
        }

        [Test]
        public void AdventureSceneShellIncludesTheFormalSingleEncounterOwners()
        {
            SceneBuilder.BuildAdventureScene();
            EditorSceneManager.OpenScene(ScenePaths[3], OpenSceneMode.Single);

            var exploration = Object.FindFirstObjectByType<TianZhang.Map.ExplorationController>();
            Assert.IsNotNull(Object.FindFirstObjectByType<AdventureSceneController>());
            Assert.IsNotNull(Object.FindFirstObjectByType<TianZhang.HexTile.HexTilemapManager>());
            Assert.IsNotNull(exploration);
            Assert.IsNotNull(Object.FindFirstObjectByType<BattleUIManager>());
            Assert.AreEqual(1, exploration.enemyCount);
        }

        [Test]
        public void FormalAdventureSceneGroundTileHasRenderableSprite()
        {
            EditorSceneManager.OpenScene(ScenePaths[3], OpenSceneMode.Single);

            var tilemapManager = Object.FindFirstObjectByType<TianZhang.HexTile.HexTilemapManager>();
            Assert.IsNotNull(tilemapManager);
            Assert.IsNotNull(tilemapManager.groundTile);

            var groundTile = tilemapManager.groundTile as UnityEngine.Tilemaps.Tile;
            Assert.IsNotNull(groundTile);
            Assert.IsNotNull(groundTile.sprite, "The formal adventure ground tile must render a sprite.");
        }

        [Test]
        public void FormalAndRebuiltAdventureScenesBindTheSameFormalShijiahou()
        {
            EditorSceneManager.OpenScene(ScenePaths[3], OpenSceneMode.Single);
            var formalEnemyGuid = GetFormalAdventureEnemyGuid();

            SceneBuilder.BuildAdventureScene();
            EditorSceneManager.OpenScene(ScenePaths[3], OpenSceneMode.Single);

            Assert.AreEqual(formalEnemyGuid, GetFormalAdventureEnemyGuid());
        }

        [Test]
        public void FormalAndRebuiltAdventureScenesBindTheProductionEnvironmentProfile()
        {
            EditorSceneManager.OpenScene(ScenePaths[3], OpenSceneMode.Single);
            var formalEnvironmentGuid = GetFormalAdventureEnvironmentGuid();

            SceneBuilder.BuildAdventureScene();
            EditorSceneManager.OpenScene(ScenePaths[3], OpenSceneMode.Single);

            Assert.AreEqual("a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6", formalEnvironmentGuid);
            Assert.AreEqual(formalEnvironmentGuid, GetFormalAdventureEnvironmentGuid());
        }

        [Test]
        public void LegacyExplorationSceneGeneratorIsNotExposed()
        {
            var legacyGenerator = typeof(SceneBuilder).GetMethod(
                "BuildExplorationScene",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            Assert.IsNull(legacyGenerator);
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
            Assert.IsNotNull(sectSelection.innateBudgetText);
            Assert.IsNotNull(sectSelection.visibleRootText);
            Assert.IsNotNull(sectSelection.hiddenRootSeedText);
            Assert.IsNotNull(sectSelection.creationBudgetText);
            Assert.IsNotNull(sectSelection.craftSkillText);
            Assert.IsNotNull(GameObject.Find("CharacterCreationSummary"));
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
                Assert.AreEqual("taiyi_sect", jiangzuo.settlementId);
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

        [Test]
        public void SettlementSceneControllerBuildsCurrentSettlementUiFromSession()
        {
            DestroyExistingSceneFlowAndSession();
            var sessionGo = new GameObject("GameSessionTest");
            var controllerGo = new GameObject("SettlementSceneControllerTest");
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                session.SetWorldNode("guanzhong_hub");
                session.SetSettlementId("guanzhong_city");
                var controller = controllerGo.AddComponent<SettlementSceneController>();

                InvokeStart(controller);

                Assert.AreEqual("guanzhong_city", controller.CurrentSettlement.id);
                Assert.IsTrue(controller.TryGetSettlement("taiyi_sect", out var sect));
                Assert.AreEqual(SettlementType.Sect, sect.settlementType);
                Assert.IsTrue(controller.TryGetSettlement("guanzhong_city", out var city));
                Assert.AreEqual(SettlementType.City, city.settlementType);
                Assert.AreEqual("关中城", GameObject.Find("SettlementNameText")?.GetComponent<Text>()?.text);
                StringAssert.Contains("城池", GameObject.Find("SettlementTypeText")?.GetComponent<Text>()?.text);
                StringAssert.Contains("guanzhong_hub", GameObject.Find("SettlementReturnContextText")?.GetComponent<Text>()?.text);
                Assert.AreEqual(4, CountButtonsWithPrefix("SettlementService_"));
                Assert.IsTrue(GameObject.Find("SettlementAdventure_guanzhong_wild").GetComponent<Button>().interactable);
                Assert.IsNotNull(GameObject.Find("ReturnToWorldButton"));
            }
            finally
            {
                DestroySettlementUi();
                Object.DestroyImmediate(controllerGo);
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void SettlementSceneControllerFallsBackToDefaultSettlementWhenSessionIdIsUnknown()
        {
            DestroyExistingSceneFlowAndSession();
            var sessionGo = new GameObject("GameSessionTest");
            var controllerGo = new GameObject("SettlementSceneControllerTest");
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                session.SetSettlementId("missing_settlement");
                var controller = controllerGo.AddComponent<SettlementSceneController>();

                InvokeStart(controller);

                Assert.AreEqual("taiyi_sect", controller.CurrentSettlement.id);
                Assert.AreEqual("太一道庭", GameObject.Find("SettlementNameText")?.GetComponent<Text>()?.text);
                StringAssert.Contains("宗门", GameObject.Find("SettlementTypeText")?.GetComponent<Text>()?.text);
                Assert.IsNotNull(GameObject.Find("SettlementAdventure_taiyi_trial"));
            }
            finally
            {
                DestroySettlementUi();
                Object.DestroyImmediate(controllerGo);
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void SceneFlowManagerPreparesAdventureAndReturnContextsWithoutSceneLoad()
        {
            DestroyExistingSceneFlowAndSession();
            var flowGo = new GameObject("SceneFlowManagerTest");
            var sessionGo = new GameObject("GameSessionTest");
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                var flow = flowGo.AddComponent<SceneFlowManager>();
                session.SetWorldNode("longxi_hub");

                Assert.AreEqual(
                    "AdventureScene",
                    flow.PrepareAdventureEntry("longxi_trial", SceneReturnTarget.World("longxi_hub")));
                Assert.AreEqual("longxi_trial", session.CurrentAdventureId);
                Assert.AreEqual("WorldScene", session.LastReturnTarget.SceneName);
                Assert.AreEqual("longxi_hub", session.LastReturnTarget.WorldNodeId);

                Assert.AreEqual("WorldScene", flow.PrepareReturnToPreviousScene());
                Assert.AreEqual("longxi_hub", session.CurrentWorldNodeId);
                Assert.IsNull(session.CurrentAdventureId);
                Assert.IsNull(session.LastReturnTarget.SceneName);

                session.SetSettlementId("taiyi_sect");
                Assert.AreEqual(
                    "AdventureScene",
                    flow.PrepareAdventureEntry("taiyi_trial", SceneReturnTarget.Settlement("taiyi_sect")));
                Assert.AreEqual("taiyi_trial", session.CurrentAdventureId);
                Assert.AreEqual("SettlementScene", session.LastReturnTarget.SceneName);
                Assert.AreEqual("taiyi_sect", session.LastReturnTarget.SettlementId);

                Assert.AreEqual("SettlementScene", flow.PrepareReturnToPreviousScene());
                Assert.AreEqual("taiyi_sect", session.CurrentSettlementId);
                Assert.IsNull(session.CurrentAdventureId);
                Assert.IsNull(session.LastReturnTarget.SceneName);

                session.SetAdventureId("old_trial");
                session.SetReturnTarget(SceneReturnTarget.Settlement("taiyi_sect"));
                session.BeginNewGame(null, "jiangzuo_hub");
                Assert.IsNull(session.CurrentAdventureId);
                Assert.IsNull(session.LastReturnTarget.SceneName);
            }
            finally
            {
                Object.DestroyImmediate(flowGo);
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void GameSessionOwnsWorldTimeAcrossNewGameSceneChangesAndBattleReturn()
        {
            DestroyExistingSceneFlowAndSession();
            var flowGo = new GameObject("SceneFlowManagerTest");
            var sessionGo = new GameObject("GameSessionTest");
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                var flow = flowGo.AddComponent<SceneFlowManager>();

                flow.PrepareNewGame(null);

                Assert.AreEqual(387, session.WorldYear);
                Assert.AreEqual("autumn", session.WorldSeasonId);
                Assert.AreEqual(1, session.WorldDay);
                Assert.AreEqual("dawn", session.WorldTimeOfDayId);

                session.AdvanceWorldDay();
                Assert.AreEqual(2, session.WorldDay);
                Assert.AreEqual("dawn", session.WorldTimeOfDayId);

                Assert.AreEqual("AdventureScene", flow.PrepareAdventureEntry(
                    "time_test_adventure", SceneReturnTarget.World("jiangzuo_hub")));
                Assert.AreEqual(2, session.WorldDay);
                Assert.AreEqual("dawn", session.WorldTimeOfDayId);

                Assert.AreEqual("WorldScene", flow.PrepareReturnToPreviousScene());
                Assert.AreEqual(2, session.WorldDay);
                Assert.AreEqual("dawn", session.WorldTimeOfDayId);
            }
            finally
            {
                Object.DestroyImmediate(flowGo);
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void SceneFlowManagerPreparesNewGameStartNodeFromOriginAndRecordsUnknownOriginFallback()
        {
            DestroyExistingSceneFlowAndSession();
            var flowGo = new GameObject("SceneFlowManagerTest");
            var sessionGo = new GameObject("GameSessionTest");
            var looseProfile = ScriptableObject.CreateInstance<TianZhang.Entity.CharacterData>();
            var clanProfile = ScriptableObject.CreateInstance<TianZhang.Entity.CharacterData>();
            var legacyProfile = ScriptableObject.CreateInstance<TianZhang.Entity.CharacterData>();
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                var flow = flowGo.AddComponent<SceneFlowManager>();
                looseProfile.originId = "origin_loose";
                clanProfile.originId = "origin_minor_clan";
                legacyProfile.originId = "legacy_removed_origin";

                Assert.AreEqual("WorldScene", flow.PrepareNewGame(looseProfile));
                Assert.AreEqual("jiangzuo_hub", session.CurrentWorldNodeId);

                Assert.AreEqual("WorldScene", flow.PrepareNewGame(clanProfile));
                Assert.AreEqual("guanzhong_hub", session.CurrentWorldNodeId);

                LogAssert.Expect(LogType.Warning, "[SceneFlow] Unknown or legacy origin 'legacy_removed_origin'; using fallback start node 'jiangzuo_hub' without changing the profile.");
                Assert.AreEqual("WorldScene", flow.PrepareNewGame(legacyProfile));
                Assert.AreEqual("jiangzuo_hub", session.CurrentWorldNodeId);
                Assert.AreEqual("legacy_removed_origin", legacyProfile.originId);
            }
            finally
            {
                Object.DestroyImmediate(looseProfile);
                Object.DestroyImmediate(clanProfile);
                Object.DestroyImmediate(legacyProfile);
                Object.DestroyImmediate(flowGo);
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void FormalBuildPathPreservesProfileOriginStartAndSettlementReturnContext()
        {
            DestroyExistingSceneFlowAndSession();
            var flowGo = new GameObject("SceneFlowManagerTest");
            var sessionGo = new GameObject("GameSessionTest");
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                var flow = flowGo.AddComponent<SceneFlowManager>();
                var draft = TianZhang.Game.CharacterCreation.CharacterCreationCatalog.CreateDefaultDraft();
                draft.OriginId = "origin_minor_clan";

                var profile = TianZhang.Game.CharacterCreation.CharacterCreationManager.BeginNewGame(draft, session);
                Assert.AreSame(profile, session.PlayerProfile);
                Assert.AreEqual("origin_minor_clan", session.PlayerProfile.originId);
                Assert.AreEqual("guanzhong_hub", session.CurrentWorldNodeId);
                Assert.AreEqual("WorldScene", flow.PrepareWorldEntry(session.CurrentWorldNodeId));

                Assert.AreEqual("SettlementScene", flow.PrepareSettlementEntry("guanzhong_city"));
                Assert.AreEqual("guanzhong_city", session.CurrentSettlementId);
                Assert.AreEqual("guanzhong_hub", session.LastReturnTarget.WorldNodeId);

                Assert.AreEqual(
                    "AdventureScene",
                    flow.PrepareAdventureEntry("guanzhong_wild", SceneReturnTarget.Settlement("guanzhong_city")));
                Assert.AreEqual("guanzhong_wild", session.CurrentAdventureId);
                Assert.AreEqual("SettlementScene", session.LastReturnTarget.SceneName);
                Assert.AreEqual("guanzhong_city", session.LastReturnTarget.SettlementId);

                Assert.AreEqual("SettlementScene", flow.PrepareReturnToPreviousScene());
                Assert.AreEqual("guanzhong_city", session.CurrentSettlementId);
                Assert.IsNull(session.CurrentAdventureId);
                Assert.IsNull(session.LastReturnTarget.SceneName);
            }
            finally
            {
                Object.DestroyImmediate(flowGo);
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void SettlementSceneControllerBuildsClickableAdventureEntrances()
        {
            DestroyExistingSceneFlowAndSession();
            var sessionGo = new GameObject("GameSessionTest");
            var controllerGo = new GameObject("SettlementSceneControllerTest");
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                session.SetSettlementId("taiyi_sect");
                var controller = controllerGo.AddComponent<SettlementSceneController>();

                InvokeStart(controller);

                var target = controller.BuildAdventureReturnTarget();
                Assert.AreEqual("SettlementScene", target.SceneName);
                Assert.AreEqual("taiyi_sect", target.SettlementId);

                var entranceButton = GameObject.Find("SettlementAdventure_taiyi_trial")?.GetComponent<Button>();
                Assert.IsNotNull(entranceButton);
                Assert.IsTrue(entranceButton.interactable);
                StringAssert.Contains("taiyi_trial", entranceButton.GetComponentInChildren<Text>().text);
            }
            finally
            {
                DestroySettlementUi();
                Object.DestroyImmediate(controllerGo);
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void SettlementSceneControllerBuildsClickableServicePlaceholders()
        {
            DestroyExistingSceneFlowAndSession();
            var sessionGo = new GameObject("GameSessionTest");
            var controllerGo = new GameObject("SettlementSceneControllerTest");
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                session.SetSettlementId("taiyi_sect");
                var controller = controllerGo.AddComponent<SettlementSceneController>();

                InvokeStart(controller);

                AssertServiceButtonLogsPlaceholder("修炼", "taiyi_sect");
                AssertServiceButtonLogsPlaceholder("功法", "taiyi_sect");
                AssertServiceButtonLogsPlaceholder("任务", "taiyi_sect");

                Assert.IsTrue(controller.SelectSettlement("guanzhong_city"));
                AssertServiceButtonLogsPlaceholder("坊市", "guanzhong_city");
            }
            finally
            {
                DestroySettlementUi();
                Object.DestroyImmediate(controllerGo);
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void AdventureSceneControllerDisplaysCurrentAdventureAndSource()
        {
            DestroyExistingSceneFlowAndSession();
            var sessionGo = new GameObject("GameSessionTest");
            var controllerGo = new GameObject("AdventureSceneControllerTest");
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                session.SetAdventureId("taiyi_trial");
                session.SetReturnTarget(SceneReturnTarget.Settlement("taiyi_sect"));
                var controller = controllerGo.AddComponent<AdventureSceneController>();

                InvokeStart(controller);

                Assert.AreEqual("taiyi_trial", controller.CurrentAdventureId);
                StringAssert.Contains("taiyi_sect", controller.BuildSourceDescription());
                StringAssert.Contains("taiyi_trial", GameObject.Find("AdventureIdText")?.GetComponent<Text>()?.text);
                StringAssert.Contains("taiyi_sect", GameObject.Find("AdventureSourceText")?.GetComponent<Text>()?.text);
                Assert.IsNotNull(GameObject.Find("ReturnToSourceButton"));
            }
            finally
            {
                DestroyAdventureUi();
                Object.DestroyImmediate(controllerGo);
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void AdventureSceneControllerDisplaysOnlyRenderedEnvironmentFeedback()
        {
            DestroyExistingSceneFlowAndSession();
            var controllerGo = new GameObject("AdventureEnvironmentFeedbackTest");
            try
            {
                var profile = AssetDatabase.LoadAssetAtPath<EnvironmentProfileData>(
                    "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset");
                Assert.IsNotNull(profile);

                var model = new TacticalGridModel();
                foreach (var edge in profile.directedEdges)
                {
                    model.SetTile(new TacticalTileData(new TianZhang.Core.HexCoord(edge.fromQ, edge.fromR)));
                    model.SetTile(new TacticalTileData(new TianZhang.Core.HexCoord(edge.toQ, edge.toR)));
                }
                Assert.IsTrue(model.TryConfigureEnvironmentProfile(profile, out var reason), reason);

                var controller = controllerGo.AddComponent<AdventureSceneController>();
                controller.SetEnvironmentPresentation(EnvironmentPresentationSnapshot.Create(model));
                InvokeStart(controller);

                string feedback = GameObject.Find("EnvironmentFeedbackText")?.GetComponent<Text>()?.text;
                StringAssert.Contains("surface_grassland", feedback);
                StringAssert.Contains("气流", feedback);
                StringAssert.Contains("格边", feedback);
                StringAssert.Contains("移动允许", feedback);
                StringAssert.DoesNotContain("surface_default", feedback);
            }
            finally
            {
                DestroyAdventureUi();
                Object.DestroyImmediate(controllerGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void AdventurePanelDoesNotOverlapPlayerHud()
        {
            DestroyExistingSceneFlowAndSession();
            var battleUiGo = new GameObject("BattleUIManagerTest");
            var controllerGo = new GameObject("AdventureSceneControllerTest");
            try
            {
                var battleUi = battleUiGo.AddComponent<BattleUIManager>();
                typeof(BattleUIManager)
                    .GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(battleUi, null);

                var controller = controllerGo.AddComponent<AdventureSceneController>();
                InvokeStart(controller);
                Canvas.ForceUpdateCanvases();

                var canvas = GameObject.Find("UICanvas")?.transform;
                var playerPanel = GameObject.Find("PlayerPanel")?.GetComponent<RectTransform>();
                var adventurePanel = GameObject.Find("AdventurePanel")?.GetComponent<RectTransform>();
                Assert.IsNotNull(canvas);
                Assert.IsNotNull(playerPanel);
                Assert.IsNotNull(adventurePanel);

                var playerBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(canvas, playerPanel);
                var adventureBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(canvas, adventurePanel);
                bool overlaps = playerBounds.min.x < adventureBounds.max.x
                    && playerBounds.max.x > adventureBounds.min.x
                    && playerBounds.min.y < adventureBounds.max.y
                    && playerBounds.max.y > adventureBounds.min.y;

                Assert.IsFalse(
                    overlaps,
                    $"PlayerPanel {playerBounds.min}..{playerBounds.max} overlaps AdventurePanel {adventureBounds.min}..{adventureBounds.max}.");
            }
            finally
            {
                DestroyAdventureUi();
                Object.DestroyImmediate(controllerGo);
                Object.DestroyImmediate(battleUiGo);
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

        private static string GetFormalAdventureEnemyGuid()
        {
            var controller = Object.FindFirstObjectByType<AdventureSceneController>();
            Assert.IsNotNull(controller);
            var serializedController = new SerializedObject(controller);
            var guanzhongEnemyTemplates = serializedController.FindProperty("guanzhongWildEnemyTemplates");
            Assert.IsNotNull(guanzhongEnemyTemplates);
            Assert.AreEqual(1, guanzhongEnemyTemplates.arraySize);

            var enemy = guanzhongEnemyTemplates.GetArrayElementAtIndex(0).objectReferenceValue as TianZhang.Entity.CharacterData;
            Assert.IsNotNull(enemy);
            Assert.AreEqual("石甲兽", enemy.charName);

            var assetPath = AssetDatabase.GetAssetPath(enemy);
            Assert.AreEqual("Assets/Data/Characters/Char_Enemy_enemy_shijiahou.asset", assetPath);
            return AssetDatabase.AssetPathToGUID(assetPath);
        }

        private static string GetFormalAdventureEnvironmentGuid()
        {
            var controller = Object.FindFirstObjectByType<AdventureSceneController>();
            Assert.IsNotNull(controller);
            var serializedController = new SerializedObject(controller);
            var environmentProperty = serializedController.FindProperty("guanzhongWildEnvironmentProfile");
            Assert.IsNotNull(environmentProperty);

            var environment = environmentProperty.objectReferenceValue as TianZhang.Tactical.EnvironmentProfileData;
            Assert.IsNotNull(environment);
            Assert.AreEqual("env_guanzhong_wild", environment.profileId);

            var assetPath = AssetDatabase.GetAssetPath(environment);
            Assert.AreEqual(
                "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset",
                assetPath);
            return AssetDatabase.AssetPathToGUID(assetPath);
        }

        private static void DestroyExistingSceneFlowAndSession()
        {
            if (SceneFlowManager.Instance != null)
                Object.DestroyImmediate(SceneFlowManager.Instance.gameObject);
            if (GameSession.Instance != null)
                Object.DestroyImmediate(GameSession.Instance.gameObject);
        }

        private static void InvokeStart(MonoBehaviour controller)
        {
            controller.GetType()
                .GetMethod("Start", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(controller, null);
        }

        private static int CountButtonsWithPrefix(string prefix)
        {
            return Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Count(button => button.name.StartsWith(prefix));
        }

        private static void AssertServiceButtonLogsPlaceholder(string service, string settlementId)
        {
            var button = GameObject.Find("SettlementService_" + service)?.GetComponent<Button>();
            Assert.IsNotNull(button, service);
            Assert.IsTrue(button.interactable, service);

            LogAssert.Expect(
                LogType.Log,
                new Regex("\\[SettlementScene\\].*service=" + Regex.Escape(service) + ".*settlement=" + Regex.Escape(settlementId)));
            button.onClick.Invoke();
        }

        private static void DestroySettlementUi()
        {
            var canvas = GameObject.Find("UICanvas");
            if (canvas != null)
                Object.DestroyImmediate(canvas);
        }

        private static void DestroyAdventureUi()
        {
            var canvas = GameObject.Find("UICanvas");
            if (canvas != null)
                Object.DestroyImmediate(canvas);
        }
    }

    public class DataConfigImporterContentScopeTests
    {
        [Test]
        public void RuntimeDataObjectsKeepPlayerContentScopeDefault()
        {
            var gongFa = ScriptableObject.CreateInstance<GongFaGrowthData>();
            var spell = ScriptableObject.CreateInstance<SpellData>();
            var skill = ScriptableObject.CreateInstance<DivineSkillData>();
            try
            {
                Assert.AreEqual("player", gongFa.contentScope);
                Assert.AreEqual("player", spell.contentScope);
                Assert.AreEqual("player", skill.contentScope);
            }
            finally
            {
                Object.DestroyImmediate(gongFa);
                Object.DestroyImmediate(spell);
                Object.DestroyImmediate(skill);
            }
        }

        [Test]
        public void ContentScopeLookupUsesHeaderAndDefaultsMissingOrEmptyValues()
        {
            Assert.AreEqual(
                "reserved",
                DataConfigImporter.GetColumnValueOrDefault(
                    new[] { "name", "contentScope" },
                    new[] { "spell_test", "reserved" },
                    "contentScope",
                    "player"));

            Assert.AreEqual(
                "reserved",
                DataConfigImporter.GetColumnValueOrDefault(
                    new[] { "contentScope", "name" },
                    new[] { "reserved", "spell_test" },
                    "contentScope",
                    "player"));

            Assert.AreEqual(
                "player",
                DataConfigImporter.GetColumnValueOrDefault(
                    new[] { "name", "contentScope" },
                    new[] { "spell_test", "" },
                    "contentScope",
                    "player"));

            Assert.AreEqual(
                "player",
                DataConfigImporter.GetColumnValueOrDefault(
                    new[] { "name" },
                    new[] { "spell_test" },
                    "contentScope",
                    "player"));
        }

        [Test]
        public void ContentScopeImportRejectsMissingAndUnknownValues()
        {
            var getRequiredContentScope = typeof(DataConfigImporter).GetMethod(
                "GetRequiredContentScope",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);

            Assert.IsNotNull(getRequiredContentScope, "Importer must expose a required contentScope reader.");
            Assert.AreEqual(
                "reserved",
                getRequiredContentScope.Invoke(null, new object[]
                {
                    new[] { "name", "contentScope" },
                    new[] { "spell_test", "reserved" },
                    "Spells.csv"
                }));
            Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                getRequiredContentScope.Invoke(null, new object[]
                {
                    new[] { "name" },
                    new[] { "spell_test" },
                    "Spells.csv"
                }));
            Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                getRequiredContentScope.Invoke(null, new object[]
                {
                    new[] { "name", "contentScope" },
                    new[] { "spell_test", "legacy" },
                    "Spells.csv"
                }));
        }

        [Test]
        public void PlayerContentScopePolicyOnlyAdmitsExplicitPlayerAssets()
        {
            var policyType = typeof(GongFaGrowthData).Assembly.GetType(
                "TianZhang.Cultivation.ContentScopePolicy");
            Assert.IsNotNull(policyType, "Runtime contentScope policy must be shared outside editor-only code.");

            var isPlayerAvailable = policyType.GetMethod(
                "IsPlayerAvailable",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            Assert.IsNotNull(isPlayerAvailable);
            Assert.IsTrue((bool)isPlayerAvailable.Invoke(null, new object[] { "player" }));
            Assert.IsFalse((bool)isPlayerAvailable.Invoke(null, new object[] { "reserved" }));
            Assert.IsFalse((bool)isPlayerAvailable.Invoke(null, new object[] { "" }));
            Assert.IsFalse((bool)isPlayerAvailable.Invoke(null, new object[] { "legacy" }));
        }

        [Test]
        public void LianShenReservedAbilitiesRemainExcludedAtPlayerLoad()
        {
            AssertCsvRowKeepsReservedLianShenScope("Spells.csv", "spell_wanliuguizong");
            AssertCsvRowKeepsReservedLianShenScope("Spells.csv", "spell_tianshuyunzhuan");
            AssertCsvRowKeepsReservedLianShenScope("Skills.csv", "skill_fayu_wanxiangxuanmen");
            AssertCsvRowKeepsReservedLianShenScope("Skills.csv", "skill_lingyu_wanxiangguiyuan");

            var wanLiuGuiZong = AssetDatabase.LoadAssetAtPath<SpellData>(
                "Assets/Data/Spells/Spell_spell_wanliuguizong.asset");
            var tianShuYunZhuan = AssetDatabase.LoadAssetAtPath<SpellData>(
                "Assets/Data/Spells/Spell_spell_tianshuyunzhuan.asset");
            var faYuWanXiangXuanMen = AssetDatabase.LoadAssetAtPath<DivineSkillData>(
                "Assets/Data/Skills/Skill_skill_fayu_wanxiangxuanmen.asset");
            var lingYuWanXiangGuiYuan = AssetDatabase.LoadAssetAtPath<DivineSkillData>(
                "Assets/Data/Skills/Skill_skill_lingyu_wanxiangguiyuan.asset");

            AssertReservedLianShenAsset(wanLiuGuiZong, "spell_wanliuguizong");
            AssertReservedLianShenAsset(tianShuYunZhuan, "spell_tianshuyunzhuan");
            AssertReservedLianShenAsset(faYuWanXiangXuanMen, "skill_fayu_wanxiangxuanmen");
            AssertReservedLianShenAsset(lingYuWanXiangGuiYuan, "skill_lingyu_wanxiangguiyuan");

            var managerObject = new GameObject("LianShenReservedAbilityLoader");
            var controllerObject = new GameObject("LianShenReservedAbilityController");
            var characterData = ScriptableObject.CreateInstance<TianZhang.Entity.CharacterData>();
            try
            {
                var manager = managerObject.AddComponent<SectSelectionManager>();
                var controller = controllerObject.AddComponent<TianZhang.Map.ExplorationController>();
                var player = new TianZhang.Entity.Character
                {
                    RealmMultiplier = 24f,
                    VisibleRootElement = "水"
                };
                characterData.equippedSpells = new[] { "wanliuguizong", "tianshuyunzhuan" };
                characterData.equippedSkills = new[]
                {
                    "skill_fayu_wanxiangxuanmen",
                    "skill_lingyu_wanxiangguiyuan"
                };

                var loadAbilities = typeof(SectSelectionManager).GetMethod(
                    "UpdateExplorationAbilities",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(loadAbilities);

                LogAssert.Expect(LogType.Warning, new Regex("Excluded non-player spell: wanliuguizong.*scope=reserved"));
                LogAssert.Expect(LogType.Warning, new Regex("Excluded non-player spell: tianshuyunzhuan.*scope=reserved"));
                LogAssert.Expect(LogType.Warning, new Regex("Excluded non-player skill: skill_fayu_wanxiangxuanmen.*scope=reserved"));
                LogAssert.Expect(LogType.Warning, new Regex("Excluded non-player skill: skill_lingyu_wanxiangguiyuan.*scope=reserved"));

                loadAbilities.Invoke(manager, new object[] { controller, characterData, player });

                Assert.IsEmpty(controller.playerSpells);
                Assert.IsEmpty(controller.playerSkills);
            }
            finally
            {
                Object.DestroyImmediate(characterData);
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(managerObject);
            }
        }

        private static void AssertCsvRowKeepsReservedLianShenScope(string csvFileName, string contentId)
        {
            string[] lines = File.ReadAllLines(Path.Combine(Application.dataPath, "DataConfig", csvFileName));
            string header = lines.First(line => !line.StartsWith("#", System.StringComparison.Ordinal));
            string row = lines.First(line => line.StartsWith(contentId + ",", System.StringComparison.Ordinal));
            string[] headers = header.Split(',');
            string[] values = row.Split(',');

            int realmIndex = System.Array.IndexOf(headers, "realmReq");
            int scopeIndex = System.Array.IndexOf(headers, "contentScope");
            Assert.GreaterOrEqual(realmIndex, 0, csvFileName + " must contain realmReq.");
            Assert.GreaterOrEqual(scopeIndex, 0, csvFileName + " must contain contentScope.");
            Assert.AreEqual("realm_lianshen", values[realmIndex], contentId);
            Assert.AreEqual("reserved", values[scopeIndex], contentId);
        }

        private static void AssertReservedLianShenAsset(SpellData asset, string contentId)
        {
            Assert.IsNotNull(asset, contentId);
            Assert.AreEqual("reserved", asset.contentScope, contentId);
            Assert.AreEqual("realm_lianshen", asset.realmRequirement, contentId);
        }

        private static void AssertReservedLianShenAsset(DivineSkillData asset, string contentId)
        {
            Assert.IsNotNull(asset, contentId);
            Assert.AreEqual("reserved", asset.contentScope, contentId);
            Assert.AreEqual("realm_lianshen", asset.realmRequirement, contentId);
        }

        [Test]
        public void ElementLookupRequiresIndependentElementColumnAndUsesHeaderOrder()
        {
            Assert.AreEqual(
                "water",
                DataConfigImporter.GetRequiredColumnValue(
                    new[] { "name", "elementReq", "element" },
                    new[] { "spell_test", "fire_req", "water" },
                    "element",
                    "Spells.csv"));

            Assert.AreEqual(
                "water",
                DataConfigImporter.GetRequiredColumnValue(
                    new[] { "name", "element", "elementReq" },
                    new[] { "spell_test", "water", "fire_req" },
                    "element",
                    "Spells.csv"));

            Assert.Throws<InvalidDataException>(() =>
                DataConfigImporter.GetRequiredColumnValue(
                    new[] { "name", "elementReq" },
                    new[] { "spell_test", "fire_req" },
                    "element",
                    "Spells.csv"));
        }

        [Test]
        public void ColumnReorderDoesNotMisdirectReads()
        {
            // Spells header: name, type, minRange
            // Reorder to: minRange, name, type
            var headers = new[] { "minRange", "name", "type" };
            var cols = new[] { "3", "spell_test", "1" };

            Assert.AreEqual("spell_test",
                DataConfigImporter.GetRequiredColumnValue(headers, cols, "name", "Spells.csv"));
            Assert.AreEqual("1",
                DataConfigImporter.GetRequiredColumnValue(headers, cols, "type", "Spells.csv"));
            Assert.AreEqual("3",
                DataConfigImporter.GetRequiredColumnValue(headers, cols, "minRange", "Spells.csv"));
        }

        [Test]
        public void FindHeaderDiscoversReorderedRealHeaderWithoutNameInFirstColumn()
        {
            var findHeader = typeof(DataConfigImporter).GetMethod(
                "FindHeader",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var lines = new[]
            {
                "# Characters.csv",
                "realmMultiplier,name,rootBone,equippedSpells,equippedSkills",
                "1.5,char_test,10,,"
            };

            var headers = (string[])findHeader.Invoke(null, new object[] { lines });
            var findHeaderIndex = typeof(DataConfigImporter).GetMethod(
                "FindHeaderIndex",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            CollectionAssert.AreEqual(
                new[] { "realmMultiplier", "name", "rootBone", "equippedSpells", "equippedSkills" },
                headers);
            Assert.AreEqual(1, (int)findHeaderIndex.Invoke(null, new object[] { lines }));
        }

        [Test]
        public void ImportCharactersReadsReorderedHeaderAndKeepsEmptyEquipment()
        {
            const string sourceAssetPath = "Assets/DataConfig/Characters.csv";
            const string importedAssetPath = "Assets/Data/Characters/Char_tq043_reordered.asset";
            string sourceFilePath = Path.Combine(Application.dataPath, "DataConfig/Characters.csv");
            byte[] originalContents = File.ReadAllBytes(sourceFilePath);

            try
            {
                AssetDatabase.DeleteAsset(importedAssetPath);
                File.WriteAllText(sourceFilePath,
                    "# TQ-043 temporary test fixture\n" +
                    "realmMultiplier,name,rootBone,physique,spirit,mind,reaction,talent,blockRate,blockReduction,soulShieldRate,soulShieldReduction,dodgeRate,critRate,critDamage,hitRateBonus,gongFaName,equippedSpells,equippedSkills\n" +
                    "1.5,tq043_reordered,10,10,16,14,12,14,5,0,13,0,0,5,15,3,gongfa_baoyuanshouyi,,,\n");
                AssetDatabase.ImportAsset(sourceAssetPath, ImportAssetOptions.ForceSynchronousImport);

                var importCharacters = typeof(DataConfigImporter).GetMethod(
                    "ImportCharacters",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                importCharacters.Invoke(null, null);

                var asset = AssetDatabase.LoadAssetAtPath<TianZhang.Entity.CharacterData>(importedAssetPath);
                Assert.IsNotNull(asset);
                Assert.AreEqual("tq043_reordered", asset.charName);
                Assert.AreEqual(1.5f, asset.realmMultiplier);
                Assert.IsEmpty(asset.equippedSpells);
                Assert.IsEmpty(asset.equippedSkills);
            }
            finally
            {
                AssetDatabase.DeleteAsset(importedAssetPath);
                File.WriteAllBytes(sourceFilePath, originalContents);
                AssetDatabase.ImportAsset(sourceAssetPath, ImportAssetOptions.ForceSynchronousImport);
            }
        }

        [Test]
        public void TrailingColumnDoesNotShiftReads()
        {
            // The row has more columns than the header, simulating trailing column addition
            var headers = new[] { "name", "type", "minRange" };
            var cols = new[] { "spell_test", "1", "3", "extra_value" };

            Assert.AreEqual("spell_test",
                DataConfigImporter.GetRequiredColumnValue(headers, cols, "name", "Spells.csv"));
            Assert.AreEqual("1",
                DataConfigImporter.GetRequiredColumnValue(headers, cols, "type", "Spells.csv"));
            Assert.AreEqual("3",
                DataConfigImporter.GetRequiredColumnValue(headers, cols, "minRange", "Spells.csv"));
        }

        [Test]
        public void MissingRequiredColumnThrowsInvalidDataException()
        {
            var headers = new[] { "name", "type" };
            var cols = new[] { "spell_test", "1" };

            Assert.Throws<InvalidDataException>(() =>
                DataConfigImporter.GetRequiredColumnValue(headers, cols, "minRange", "Spells.csv"));
        }

        [Test]
        public void HeaderMissingFromRowThrowsInvalidDataException()
        {
            var headers = new[] { "name", "type", "minRange" };
            var cols = new[] { "spell_test", "1" }; // row too short

            Assert.Throws<InvalidDataException>(() =>
                DataConfigImporter.GetRequiredColumnValue(headers, cols, "minRange", "Spells.csv"));
        }

        [Test]
        public void OptionalColumnsDefaultWhileRequiredColumnsRejectMissingHeaders()
        {
            var headers = new[] { "name", "type" };

            Assert.AreEqual(
                "player",
                DataConfigImporter.GetColumnValueOrDefault(
                    headers,
                    new[] { "spell_test" },
                    "contentScope",
                    "player"));

            var requireColumns = typeof(DataConfigImporter).GetMethod(
                "RequireColumns",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                requireColumns.Invoke(null, new object[]
                {
                    headers,
                    "Test.csv",
                    new[] { "name", "minRange" }
                }));
            Assert.IsInstanceOf<InvalidDataException>(exception.InnerException);
        }
    }

    public static class DataConfigImporterBatchRunner
    {
        public static void RunReorderedHeaderRegression()
        {
            try
            {
                new DataConfigImporterContentScopeTests()
                    .ImportCharactersReadsReorderedHeaderAndKeepsEmptyEquipment();
                new DataConfigImporterContentScopeTests()
                    .FindHeaderDiscoversReorderedRealHeaderWithoutNameInFirstColumn();
                new DataConfigImporterContentScopeTests()
                    .OptionalColumnsDefaultWhileRequiredColumnsRejectMissingHeaders();
                Debug.Log("DataConfigImporterBatchRunner.RunReorderedHeaderRegression passed.");
                EditorApplication.Exit(0);
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }
    }
}
