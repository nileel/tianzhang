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
