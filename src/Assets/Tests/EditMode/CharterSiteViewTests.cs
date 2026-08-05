using System.Collections.Generic;
using NUnit.Framework;
using TianZhang.Content;
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
    /// <summary>
    /// Direct EditMode coverage of the charter site panel on the built SettlementScene: the old
    /// water station entry only opens at the formal guanzhong_city with a resolvable site and
    /// static catalog; the buttons submit the five 01A actions in fixed order and the three
    /// evaluations, every display refreshes from real progress / declared catalog data / the
    /// session long-term state, failures keep stable reasons without opening or advancing, and
    /// closing the panel discards temporary progress without writing the session.
    /// </summary>
    public sealed class CharterSiteViewTests
    {
        private const string SettlementId = "guanzhong_city";
        private const string SiteId = "charter_site_old_water_station";
        private const string CapabilityId = "capability_kaihe_jiuzhang_v1";
        private const string OperatorId = "operator_old_water_station";
        private const string TargetId = "gate_old_water_station_pump";
        private const string ManagerId = "manager_old_water_station";
        private const string BeneficiaryId = "beneficiary_water_basin";
        private const string RuleEntryId = "charter_entry_suifu_diji";
        private const string AuthorityRelicId = "relic_world_charter";
        private const string CharterNode = "node_old_water_station_charter";
        private const string WaterworksNode = "node_old_water_station_waterworks";
        private const string RiverWetlandNode = "node_old_water_station_river_wetland";
        private const string SupplyRain = "supply_suifu_registered_seasonal_rain";
        private const string SupplyBalance = "supply_suifu_connected_water_balance";
        private const string SupplyLand = "supply_suifu_wetland_land_capacity";

        private readonly List<UnityEngine.Object> temporaryAssets = new List<UnityEngine.Object>();
        private GameObject sessionGo;

        [TearDown]
        public void TearDown()
        {
            if (sessionGo != null)
                UnityEngine.Object.DestroyImmediate(sessionGo);
            sessionGo = null;
            DestroyExistingSceneFlowAndSession();
            foreach (UnityEngine.Object asset in temporaryAssets)
                UnityEngine.Object.DestroyImmediate(asset);
        }

        [Test]
        public void EntryOpensTheOnlySitePanelAtGuanzhongAndDisplaysRealStableIds()
        {
            OpenBuiltSettlementScene(out CharterSiteView panel, out _);
            try
            {
                Assert.IsFalse(panel.IsOpen);
                var controller = UnityEngine.Object.FindFirstObjectByType<SettlementSceneController>();

                GameObject.Find("SettlementCharterSiteEntry").GetComponent<Button>().onClick.Invoke();

                Assert.IsTrue(panel.IsOpen);
                Assert.AreEqual(
                    SettlementSceneController.CharterSiteEntryOpenedReason + ":" + SiteId,
                    controller.LastCharterSiteReason);
                StringAssert.Contains(SiteId, PanelText(panel, "siteText").text);
                StringAssert.Contains(SettlementId, PanelText(panel, "siteText").text);
                StringAssert.Contains(OperatorId, PanelText(panel, "identityText").text);
                StringAssert.Contains(TargetId, PanelText(panel, "identityText").text);
                StringAssert.Contains(ManagerId, PanelText(panel, "identityText").text);
                StringAssert.Contains(BeneficiaryId, PanelText(panel, "identityText").text);
                StringAssert.Contains(RuleEntryId, PanelText(panel, "identityText").text);
                StringAssert.Contains(AuthorityRelicId, PanelText(panel, "identityText").text);
                StringAssert.Contains(CharterNode, PanelText(panel, "nodeText").text);
                StringAssert.Contains(WaterworksNode, PanelText(panel, "nodeText").text);
                StringAssert.Contains(RiverWetlandNode, PanelText(panel, "nodeText").text);
                StringAssert.Contains(SupplyRain, PanelText(panel, "supplyText").text);
                StringAssert.Contains(SupplyBalance, PanelText(panel, "supplyText").text);
                StringAssert.Contains(SupplyLand, PanelText(panel, "supplyText").text);
                StringAssert.Contains("charter_step_passage", PanelText(panel, "stepText").text);
                Assert.IsNull(GameSession.Instance.CharterRuntimeState);
            }
            finally
            {
                DestroyImmediateSceneFlowAndSession();
            }
        }

        [Test]
        public void FiveStepsSubmitInFixedOrderAndRefreshEachDisplay()
        {
            OpenBuiltSettlementScene(out CharterSiteView panel, out _);
            try
            {
                GameObject.Find("SettlementCharterSiteEntry").GetComponent<Button>().onClick.Invoke();
                var controller = UnityEngine.Object.FindFirstObjectByType<CharterSiteController>(
                    FindObjectsInactive.Include);

                ClickPanelButton(panel, "passageButton");
                Assert.IsTrue(controller.Progress.PassageVerified);
                Assert.AreEqual(OperatorId, controller.Progress.PassageOperatorId);
                Assert.AreEqual(TargetId, controller.Progress.PassageTargetId);
                StringAssert.Contains("charter_step_management", PanelText(panel, "stepText").text);

                ClickPanelButton(panel, "managementButton");
                Assert.IsTrue(controller.Progress.ManagementVerified);
                StringAssert.Contains("charter_step_nodes", PanelText(panel, "stepText").text);

                ClickPanelButton(panel, "nodeButton");
                CollectionAssert.AreEqual(
                    new[] { CharterNode, WaterworksNode, RiverWetlandNode },
                    controller.Progress.ConnectedNodeIds);
                StringAssert.Contains("charter_step_registration", PanelText(panel, "stepText").text);
                StringAssert.Contains(CharterNode, PanelText(panel, "nodeText").text);

                ClickPanelButton(panel, "registrationButton");
                Assert.IsTrue(controller.Progress.RuleEntryRegistrationVerified);
                StringAssert.Contains("charter_step_supplies", PanelText(panel, "stepText").text);
                StringAssert.Contains("已确认: 是", PanelText(panel, "authorizationText").text);

                ClickPanelButton(panel, "supplyButton");
                CollectionAssert.AreEqual(
                    new[] { SupplyRain, SupplyBalance, SupplyLand },
                    controller.Progress.RegisteredRealitySupplyIds);
                StringAssert.Contains("charter_step_evaluation", PanelText(panel, "stepText").text);
                StringAssert.Contains(SupplyRain, PanelText(panel, "supplyText").text);

                Assert.IsNull(GameSession.Instance.CharterRuntimeState);
                Assert.AreEqual(0, GameSession.Instance.CharterDefinitionCatalogVersion);
            }
            finally
            {
                DestroyImmediateSceneFlowAndSession();
            }
        }

        [Test]
        public void JindanAndYuanyingShowRealConflictDecisionsWithoutSessionWrites()
        {
            OpenBuiltSettlementScene(out CharterSiteView panel, out _);
            try
            {
                OpenPanelAndCompleteSteps(panel);

                ClickPanelButton(panel, "jindanButton");
                StringAssert.Contains(CharterRuleRuntimeReasons.ConflictNotWon, PanelText(panel, "resultText").text);
                StringAssert.Contains("LeftWins", PanelText(panel, "resultText").text);
                StringAssert.Contains("jindan_left", PanelText(panel, "resultText").text);
                Assert.IsNull(GameSession.Instance.CharterRuntimeState);

                ClickPanelButton(panel, "yuanyingButton");
                StringAssert.Contains("TZ_CHARTER_CONFLICT_YUANYING_ANCHORED", PanelText(panel, "resultText").text);
                StringAssert.Contains("Anchored", PanelText(panel, "resultText").text);
                Assert.IsNull(GameSession.Instance.CharterRuntimeState);
                Assert.AreEqual(0, GameSession.Instance.CharterDefinitionCatalogVersion);
            }
            finally
            {
                DestroyImmediateSceneFlowAndSession();
            }
        }

        [Test]
        public void FormalCallCommitsAtomicallyAndRebuildsDisplayFromLongTermState()
        {
            OpenBuiltSettlementScene(out CharterSiteView panel, out _);
            try
            {
                OpenPanelAndCompleteSteps(panel);
                GameSession session = GameSession.Instance;

                ClickPanelButton(panel, "formalButton");
                Assert.IsNotNull(session.CharterRuntimeState);
                Assert.AreEqual(1, session.CharterDefinitionCatalogVersion);
                StringAssert.Contains(CharterSiteController.FormalCommittedReason, PanelText(panel, "resultText").text);
                StringAssert.Contains("charter_step_committed", PanelText(panel, "stepText").text);
                StringAssert.Contains("charter_entry_suifu_diji", PanelText(panel, "resultText").text);
                StringAssert.Contains("env_guanzhong_wild", PanelText(panel, "environmentText").text);
                StringAssert.Contains("长期状态", PanelText(panel, "resultText").text);
                CollectionAssert.Contains(session.CharterRuntimeState.registeredRuleEntryIds, RuleEntryId);

                CharterRuntimeStateData committedState = session.CharterRuntimeState;
                ClickPanelButton(panel, "formalButton");
                Assert.AreSame(committedState, session.CharterRuntimeState, "已有长期状态必须保持原实例内容不变。");
                StringAssert.Contains(CharterRuleRuntimeReasons.RealitySupplyUnavailable, PanelText(panel, "resultText").text);
            }
            finally
            {
                DestroyImmediateSceneFlowAndSession();
            }
        }

        [Test]
        public void OutOfOrderAndIncompleteActionsFailClosedWithStableReasons()
        {
            OpenBuiltSettlementScene(out CharterSiteView panel, out _);
            try
            {
                GameObject.Find("SettlementCharterSiteEntry").GetComponent<Button>().onClick.Invoke();
                var controller = UnityEngine.Object.FindFirstObjectByType<CharterSiteController>(
                    FindObjectsInactive.Include);

                ClickPanelButton(panel, "managementButton");
                Assert.IsFalse(controller.Progress.ManagementVerified);
                StringAssert.Contains(
                    CharterSiteInteractionReasons.ActionOutOfOrder,
                    PanelText(panel, "resultText").text);

                ClickPanelButton(panel, "jindanButton");
                Assert.IsNull(GameSession.Instance.CharterRuntimeState);
                StringAssert.Contains(
                    CharterSiteInteractionReasons.PreparationIncomplete,
                    PanelText(panel, "resultText").text);

                ClickPanelButton(panel, "formalButton");
                Assert.IsNull(GameSession.Instance.CharterRuntimeState);
                StringAssert.Contains(
                    CharterSiteInteractionReasons.PreparationIncomplete,
                    PanelText(panel, "resultText").text);
            }
            finally
            {
                DestroyImmediateSceneFlowAndSession();
            }
        }

        [Test]
        public void UnknownSiteOrWrongSettlementOrMissingCatalogDoesNotOpenPanel()
        {
            OpenBuiltSettlementScene(out CharterSiteView panel, out ContentCatalogData catalog);
            var controller = UnityEngine.Object.FindFirstObjectByType<SettlementSceneController>();
            var view = UnityEngine.Object.FindFirstObjectByType<SettlementSceneView>();
            var dispatcher = UnityEngine.Object.FindFirstObjectByType<SettlementFeatureDispatcher>();
            try
            {
                // 目录存在但没有任何册界站点：按站点 ID 精确查询失败，稳定原因且不打开面板。
                ContentCatalogData noSiteCatalog = CreateCatalog(SettlementId, null, null);
                controller.Configure(noSiteCatalog, view, dispatcher, SiteId);
                InvokeStart(controller);
                Assert.AreEqual(SettlementId, controller.CurrentSettlement.settlementId);
                ClickEntryButton();
                Assert.IsFalse(panel.IsOpen);
                Assert.AreEqual(
                    SettlementSceneController.CharterSiteMissingReason + ":" + SiteId,
                    controller.LastCharterSiteReason);
                StringAssert.Contains(
                    SettlementSceneController.CharterSiteMissingReason,
                    GameObject.Find("SettlementCharterSiteEntryStatus").GetComponent<Text>().text);

                // 站点存在但不属于当前据点：拒绝打开。
                ContentCatalogData wrongSettlementCatalog = CreateCatalog(SettlementId, SiteId, "other_city");
                controller.Configure(wrongSettlementCatalog, view, dispatcher, SiteId);
                InvokeStart(controller);
                ClickEntryButton();
                Assert.IsFalse(panel.IsOpen);
                Assert.AreEqual(
                    SettlementSceneController.CharterSiteNotCurrentReason + ":other_city",
                    controller.LastCharterSiteReason);

                // 站点与据点合法但静态目录缺失：拒绝打开。
                ContentCatalogData noStaticCatalog = CreateCatalog(SettlementId, SiteId, SettlementId);
                controller.Configure(noStaticCatalog, view, dispatcher, SiteId);
                InvokeStart(controller);
                ClickEntryButton();
                Assert.IsFalse(panel.IsOpen);
                StringAssert.Contains(
                    SettlementSceneController.CharterSiteStaticCatalogReason,
                    controller.LastCharterSiteReason);
            }
            finally
            {
                DestroyImmediateSceneFlowAndSession();
            }
        }

        [Test]
        public void CloseDiscardsTemporaryProgressAndKeepsSessionUntouched()
        {
            OpenBuiltSettlementScene(out CharterSiteView panel, out _);
            try
            {
                GameObject.Find("SettlementCharterSiteEntry").GetComponent<Button>().onClick.Invoke();
                var controller = UnityEngine.Object.FindFirstObjectByType<CharterSiteController>(
                    FindObjectsInactive.Include);
                ClickPanelButton(panel, "passageButton");
                ClickPanelButton(panel, "managementButton");
                Assert.IsTrue(controller.Progress.ManagementVerified);

                ClickPanelButton(panel, "closeButton");
                Assert.IsFalse(panel.IsOpen);
                Assert.IsNull(GameSession.Instance.CharterRuntimeState);

                GameObject.Find("SettlementCharterSiteEntry").GetComponent<Button>().onClick.Invoke();
                var reopened = UnityEngine.Object.FindFirstObjectByType<CharterSiteController>(
                    FindObjectsInactive.Include);
                Assert.IsTrue(panel.IsOpen);
                Assert.IsFalse(reopened.Progress.PassageVerified, "重新打开必须是全新交互，临时 progress 已丢弃。");
                StringAssert.Contains("charter_step_passage", PanelText(panel, "stepText").text);
            }
            finally
            {
                DestroyImmediateSceneFlowAndSession();
            }
        }

        private void OpenBuiltSettlementScene(out CharterSiteView panel, out ContentCatalogData catalog)
        {
            DestroyExistingSceneFlowAndSession();
            SceneBuilder.BuildSettlementScene();
            EditorSceneManager.OpenScene("Assets/Scenes/SettlementScene.unity", OpenSceneMode.Single);

            sessionGo = new GameObject("CharterSiteViewSession");
            GameSession session = sessionGo.AddComponent<GameSession>();
            session.SetWorldNode("guanzhong_hub");
            session.SetSettlementId(SettlementId);

            var controller = UnityEngine.Object.FindFirstObjectByType<SettlementSceneController>();
            InvokeStart(controller);

            panel = UnityEngine.Object.FindFirstObjectByType<CharterSiteView>(FindObjectsInactive.Include);
            catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                "Assets/Data/ContentCatalog/ContentCatalog.asset");
        }

        private void OpenPanelAndCompleteSteps(CharterSiteView panel)
        {
            GameObject.Find("SettlementCharterSiteEntry").GetComponent<Button>().onClick.Invoke();
            ClickPanelButton(panel, "passageButton");
            ClickPanelButton(panel, "managementButton");
            ClickPanelButton(panel, "nodeButton");
            ClickPanelButton(panel, "registrationButton");
            ClickPanelButton(panel, "supplyButton");
        }

        private ContentCatalogData CreateCatalog(string settlementId, string siteId, string siteSettlementId)
        {
            ContentCatalogData catalog = Track(ScriptableObject.CreateInstance<ContentCatalogData>());
            var settlement = Track(ScriptableObject.CreateInstance<SettlementData>());
            settlement.settlementId = settlementId;
            settlement.contentScope = SettlementSceneController.ProductionContentScope;
            catalog.ReplaceEntries(new[] { settlement }, null, null, null);
            if (siteId != null)
            {
                CharterSiteData site = Track(ScriptableObject.CreateInstance<CharterSiteData>());
                site.siteId = siteId;
                site.settlementId = siteSettlementId;
                catalog.SetCharterSites(new[] { site });
            }
            return catalog;
        }

        private void ClickEntryButton()
        {
            GameObject.Find("SettlementCharterSiteEntry").GetComponent<Button>().onClick.Invoke();
        }

        private static void ClickPanelButton(CharterSiteView panel, string propertyName)
        {
            var serialized = new SerializedObject(panel);
            var button = serialized.FindProperty(propertyName).objectReferenceValue as Button;
            Assert.IsNotNull(button, propertyName);
            button.onClick.Invoke();
        }

        private static Text PanelText(CharterSiteView panel, string propertyName)
        {
            var serialized = new SerializedObject(panel);
            var text = serialized.FindProperty(propertyName).objectReferenceValue as Text;
            Assert.IsNotNull(text, propertyName);
            return text;
        }

        private T Track<T>(T value)
            where T : UnityEngine.Object
        {
            temporaryAssets.Add(value);
            return value;
        }

        private static void InvokeStart(MonoBehaviour controller)
        {
            controller.GetType()
                .GetMethod("Start", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(controller, null);
        }

        private void DestroyImmediateSceneFlowAndSession()
        {
            if (sessionGo != null)
                UnityEngine.Object.DestroyImmediate(sessionGo);
            sessionGo = null;
            DestroyExistingSceneFlowAndSession();
        }

        private static void DestroyExistingSceneFlowAndSession()
        {
            if (SceneFlowManager.Instance != null)
                UnityEngine.Object.DestroyImmediate(SceneFlowManager.Instance.gameObject);
            if (GameSession.Instance != null)
                UnityEngine.Object.DestroyImmediate(GameSession.Instance.gameObject);
        }
    }
}
