using System.Collections.Generic;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Content;
using TianZhang.Editor;
using TianZhang.Game;
using TianZhang.Features.Settlement;
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
        private const string YuanyingAnchoredReason = "TZ_CHARTER_CONFLICT_YUANYING_ANCHORED";
        private const string EnvironmentProfileId = "env_guanzhong_wild";
        private const string CharterNode = "node_old_water_station_charter";
        private const string WaterworksNode = "node_old_water_station_waterworks";
        private const string RiverWetlandNode = "node_old_water_station_river_wetland";
        private const string SupplyRain = "supply_suifu_registered_seasonal_rain";
        private const string SupplyBalance = "supply_suifu_connected_water_balance";
        private const string SupplyLand = "supply_suifu_wetland_land_capacity";

        private readonly List<UnityEngine.Object> temporaryAssets = new List<UnityEngine.Object>();
        [TearDown]
        public void TearDown()
        {
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
                var controller = UnityEngine.Object.FindFirstObjectByType<SettlementController>();

                GameObject.Find("SettlementCharterSiteEntry").GetComponent<Button>().onClick.Invoke();

                Assert.IsTrue(panel.IsOpen);
                // 稳定原因与站点 ID 保留在控制器字段；玩家面板只显示中文名与可理解状态。
                Assert.AreEqual(
                    SettlementController.CharterSiteEntryOpenedReason + ":" + SiteId,
                    controller.LastCharterSiteReason);
                StringAssert.Contains("旧水驿", PanelText(panel, "siteText").text);
                StringAssert.Contains("关中城", PanelText(panel, "siteText").text);
                StringAssert.Contains("通行: 未确认", PanelText(panel, "identityText").text);
                StringAssert.Contains("管理: 未确认", PanelText(panel, "identityText").text);
                StringAssert.Contains("水府地纪", PanelText(panel, "identityText").text);
                StringAssert.Contains("已声明", PanelText(panel, "identityText").text);
                StringAssert.Contains("声明节点: 3 个", PanelText(panel, "nodeText").text);
                StringAssert.Contains("声明供给: 3 项", PanelText(panel, "supplyText").text);
                StringAssert.Contains("通行确认", PanelText(panel, "stepText").text);
                // 玩家显示不再直接暴露稳定 ID / 协议标识。
                StringAssert.DoesNotContain(SiteId, PanelText(panel, "siteText").text);
                StringAssert.DoesNotContain(OperatorId, PanelText(panel, "identityText").text);
                StringAssert.DoesNotContain(TargetId, PanelText(panel, "identityText").text);
                StringAssert.DoesNotContain(AuthorityRelicId, PanelText(panel, "identityText").text);
                StringAssert.DoesNotContain(CharterNode, PanelText(panel, "nodeText").text);
                StringAssert.DoesNotContain(SupplyRain, PanelText(panel, "supplyText").text);
                Assert.IsNull(GameBootstrap.RequireRuntime().Charters.CurrentState);
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
                StringAssert.Contains("管理确认", PanelText(panel, "stepText").text);

                ClickPanelButton(panel, "managementButton");
                Assert.IsTrue(controller.Progress.ManagementVerified);
                StringAssert.Contains("接通节点", PanelText(panel, "stepText").text);

                ClickPanelButton(panel, "nodeButton");
                CollectionAssert.AreEqual(
                    new[] { CharterNode, WaterworksNode, RiverWetlandNode },
                    controller.Progress.ConnectedNodeIds);
                StringAssert.Contains("条目登记", PanelText(panel, "stepText").text);
                StringAssert.Contains("已接通: 3 个", PanelText(panel, "nodeText").text);

                ClickPanelButton(panel, "registrationButton");
                Assert.IsTrue(controller.Progress.RuleEntryRegistrationVerified);
                StringAssert.Contains("准备供给", PanelText(panel, "stepText").text);
                StringAssert.Contains("登记确认: 已完成", PanelText(panel, "authorizationText").text);

                ClickPanelButton(panel, "supplyButton");
                CollectionAssert.AreEqual(
                    new[] { SupplyRain, SupplyBalance, SupplyLand },
                    controller.Progress.RegisteredRealitySupplyIds);
                StringAssert.Contains("评估推演", PanelText(panel, "stepText").text);
                StringAssert.Contains("已准备: 3 项", PanelText(panel, "supplyText").text);

                Assert.IsNull(GameBootstrap.RequireRuntime().Charters.CurrentState);
                Assert.AreEqual(0, GameBootstrap.RequireRuntime().Charters.DefinitionCatalogVersion);
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
                var controller = UnityEngine.Object.FindFirstObjectByType<CharterSiteController>(
                    FindObjectsInactive.Include);

                ClickPanelButton(panel, "jindanButton");
                // 玩家文本与内部稳定原因分层：显示中文结果，LastReason 保留原始原因。
                Assert.AreEqual(CharterRuleRuntimeReasons.ConflictNotWon, controller.LastReason);
                StringAssert.Contains("册界候选未获胜", PanelText(panel, "resultText").text);
                StringAssert.Contains("左侧候选获胜", PanelText(panel, "resultText").text);
                StringAssert.DoesNotContain("jindan_left", PanelText(panel, "resultText").text);
                Assert.IsNull(GameBootstrap.RequireRuntime().Charters.CurrentState);

                ClickPanelButton(panel, "yuanyingButton");
                Assert.AreEqual(YuanyingAnchoredReason, controller.LastReason);
                StringAssert.Contains("元婴受锚成功", PanelText(panel, "resultText").text);
                StringAssert.Contains("已受锚", PanelText(panel, "resultText").text);
                Assert.IsNull(GameBootstrap.RequireRuntime().Charters.CurrentState);
                Assert.AreEqual(0, GameBootstrap.RequireRuntime().Charters.DefinitionCatalogVersion);
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
                GameRuntime runtime = GameBootstrap.RequireRuntime();
                var controller = UnityEngine.Object.FindFirstObjectByType<CharterSiteController>(
                    FindObjectsInactive.Include);

                ClickPanelButton(panel, "formalButton");
                Assert.IsNotNull(runtime.Charters.CurrentState);
                Assert.AreEqual(1, runtime.Charters.DefinitionCatalogVersion);
                Assert.AreEqual(CharterSiteController.FormalCommittedReason, controller.LastReason);
                StringAssert.Contains("正式提交成功", PanelText(panel, "resultText").text);
                StringAssert.Contains("已正式提交", PanelText(panel, "stepText").text);
                StringAssert.Contains("登记条目 1", PanelText(panel, "resultText").text);
                StringAssert.DoesNotContain(RuleEntryId, PanelText(panel, "resultText").text);
                StringAssert.Contains("已生效", PanelText(panel, "environmentText").text);
                StringAssert.DoesNotContain(EnvironmentProfileId, PanelText(panel, "environmentText").text);
                StringAssert.Contains("长期状态", PanelText(panel, "resultText").text);
                CollectionAssert.Contains(runtime.Charters.CurrentState.registeredRuleEntryIds, RuleEntryId);

                CharterRuntimeStateData committedState = runtime.Charters.CurrentState;
                ClickPanelButton(panel, "formalButton");
                CollectionAssert.AreEqual(
                    committedState.registeredRuleEntryIds,
                    runtime.Charters.CurrentState.registeredRuleEntryIds,
                    "已有长期状态必须保持内容不变。");
                Assert.AreEqual(CharterRuleRuntimeReasons.RealitySupplyUnavailable, controller.LastReason);
                StringAssert.Contains("现实供给不可用", PanelText(panel, "resultText").text);
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
                Assert.AreEqual(CharterSiteInteractionReasons.ActionOutOfOrder, controller.LastReason);
                StringAssert.Contains("步骤顺序不正确", PanelText(panel, "resultText").text);

                ClickPanelButton(panel, "jindanButton");
                Assert.IsNull(GameBootstrap.RequireRuntime().Charters.CurrentState);
                Assert.AreEqual(CharterSiteInteractionReasons.PreparationIncomplete, controller.LastReason);
                StringAssert.Contains("前置步骤尚未完成", PanelText(panel, "resultText").text);

                ClickPanelButton(panel, "formalButton");
                Assert.IsNull(GameBootstrap.RequireRuntime().Charters.CurrentState);
                Assert.AreEqual(CharterSiteInteractionReasons.PreparationIncomplete, controller.LastReason);
                StringAssert.Contains("前置步骤尚未完成", PanelText(panel, "resultText").text);
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
            var controller = UnityEngine.Object.FindFirstObjectByType<SettlementController>();
            var view = UnityEngine.Object.FindFirstObjectByType<SettlementView>();
            var dispatcher = UnityEngine.Object.FindFirstObjectByType<SettlementFeatureDispatcher>();
            try
            {
                // 目录存在但没有任何册界站点：按站点 ID 精确查询失败，稳定原因且不打开面板。
                ContentCatalogData noSiteCatalog = CreateCatalog(SettlementId, null, null);
                ConfigureController(controller, noSiteCatalog, view, dispatcher);
                InvokeStart(controller);
                Assert.AreEqual(SettlementId, controller.CurrentSettlement.settlementId);
                ClickEntryButton();
                Assert.IsFalse(panel.IsOpen);
                Assert.AreEqual(
                    SettlementController.CharterSiteMissingReason + ":" + SiteId,
                    controller.LastCharterSiteReason);
                // 玩家入口文本只显示可理解失败；稳定原因保留在控制器字段。
                StringAssert.Contains(
                    "单据点入口不可用",
                    GameObject.Find("SettlementCharterSiteEntryStatus").GetComponent<Text>().text);

                // 站点存在但不属于当前据点：拒绝打开。
                ContentCatalogData wrongSettlementCatalog = CreateCatalog(SettlementId, SiteId, "other_city");
                ConfigureController(controller, wrongSettlementCatalog, view, dispatcher);
                InvokeStart(controller);
                ClickEntryButton();
                Assert.IsFalse(panel.IsOpen);
                Assert.AreEqual(
                    SettlementController.CharterSiteNotCurrentReason + ":other_city",
                    controller.LastCharterSiteReason);

                // 站点与据点合法但静态目录缺失：拒绝打开。
                ContentCatalogData noStaticCatalog = CreateCatalog(SettlementId, SiteId, SettlementId);
                ConfigureController(controller, noStaticCatalog, view, dispatcher);
                InvokeStart(controller);
                ClickEntryButton();
                Assert.IsFalse(panel.IsOpen);
                StringAssert.Contains(
                    SettlementController.CharterSiteStaticCatalogReason,
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
                Assert.IsNull(GameBootstrap.RequireRuntime().Charters.CurrentState);

                GameObject.Find("SettlementCharterSiteEntry").GetComponent<Button>().onClick.Invoke();
                var reopened = UnityEngine.Object.FindFirstObjectByType<CharterSiteController>(
                    FindObjectsInactive.Include);
                Assert.IsTrue(panel.IsOpen);
                Assert.IsFalse(reopened.Progress.PassageVerified, "重新打开必须是全新交互，临时 progress 已丢弃。");
                StringAssert.Contains("通行确认", PanelText(panel, "stepText").text);
            }
            finally
            {
                DestroyImmediateSceneFlowAndSession();
            }
        }

        private void OpenBuiltSettlementScene(out CharterSiteView panel, out ContentCatalogData catalog)
        {
            DestroyExistingSceneFlowAndSession();
            SettlementSceneBuilder.Build();
            EditorSceneManager.OpenScene("Assets/Scenes/SettlementScene.unity", OpenSceneMode.Single);

            new GameObject("GameBootstrapTest").AddComponent<GameBootstrap>();
            GameRuntime runtime = GameBootstrap.RequireRuntime();
            runtime.EnterWorld("guanzhong_hub");
            runtime.EnterSettlement(SettlementId);

            InvokePrivate(UnityEngine.Object.FindFirstObjectByType<SettlementSceneInstaller>(), "Awake");
            var controller = UnityEngine.Object.FindFirstObjectByType<SettlementController>();
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
            settlement.contentScope = SettlementController.ProductionContentScope;
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

        private static void ConfigureController(
            SettlementController controller,
            ContentCatalogData catalog,
            SettlementView view,
            SettlementFeatureDispatcher dispatcher)
        {
            GameRuntime runtime = GameBootstrap.RequireRuntime();
            controller.Configure(
                catalog,
                view,
                dispatcher,
                SiteId,
                runtime,
                runtime.Bounties,
                runtime.Charters,
                SettlementId,
                "guanzhong_hub",
                _ => { },
                () => null);
        }

        private static void InvokePrivate(MonoBehaviour target, string methodName)
        {
            Assert.IsNotNull(target, methodName + " target must exist in the built scene.");
            var method = target.GetType().GetMethod(
                methodName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(method, methodName);
            method.Invoke(target, null);
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
            DestroyExistingSceneFlowAndSession();
        }

        private static void DestroyExistingSceneFlowAndSession()
        {
            GameBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            if (bootstrap != null)
                UnityEngine.Object.DestroyImmediate(bootstrap.gameObject);
        }
    }
}
