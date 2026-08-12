using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TianZhang.Tests
{
    public sealed class BountyBoardViewTests
    {
        private const string BountyId = "bounty_guanzhong_shijiahou";
        private const string DraftBountyId = "bounty_guanzhong_draft";
        private const string OtherSettlementBountyId = "bounty_other_city";
        private const string SettlementId = "guanzhong_city";
        private const string AdventureId = "guanzhong_wild";
        private const string EnemyId = "enemy_shijiahou";

        private readonly List<UnityEngine.Object> temporaryAssets = new List<UnityEngine.Object>();
        [TearDown]
        public void TearDown()
        {
            DestroyExistingSceneFlowAndSession();
            foreach (UnityEngine.Object asset in temporaryAssets)
                UnityEngine.Object.DestroyImmediate(asset);
        }

        [Test]
        public void ProductionGuanzhongBountyOpensBoardAndAcceptsFromBoard()
        {
            GameRuntime runtime = OpenBuiltSettlementScene(out BountyBoardView board, out _);
            try
            {
                Assert.IsFalse(board.IsOpen);
                var featureButton = GameObject.Find("SettlementFeature_bounty_board").GetComponent<Button>();

                featureButton.onClick.Invoke();

                Assert.IsTrue(board.IsOpen);
                Assert.AreEqual(BountyId, board.CurrentBountyId);
                Assert.AreEqual(1, board.ListedBountyCount);
                // 玩家显示只呈现已批准悬赏标题与中文状态；稳定 ID 保留在 board 与会话对象中。
                StringAssert.Contains("悬赏板已打开", BoardStatusText().text);
                StringAssert.Contains("石甲兽悬赏 · 一次性除害令 | 可接取 | 进度 0/1", BoardEntriesText(board).text);
                Assert.AreEqual(BountyStatus.Available, runtime.Bounties.GetState(BountyId).Status);

                GetBoardButton(board, "acceptButton").onClick.Invoke();

                Assert.AreEqual(BountyStatus.Accepted, runtime.Bounties.GetState(BountyId).Status);
                StringAssert.Contains("石甲兽悬赏 · 一次性除害令 | 已接取 | 进度 0/1", BoardEntriesText(board).text);
                Assert.IsNull(board.LastResultReason, "成功不得伪造结果字面量，只显示刷新后的实际状态");

                GetBoardButton(board, "closeButton").onClick.Invoke();
                Assert.IsFalse(board.IsOpen);
            }
            finally
            {
                DestroyImmediateSceneFlowAndSession();
            }
        }

        [Test]
        public void ObjectiveCompletedBountyCanBeClaimedFromBoard()
        {
            GameRuntime runtime = OpenBuiltSettlementScene(out BountyBoardView board, out ContentCatalogData catalog);
            try
            {
                GameObject.Find("SettlementFeature_bounty_board").GetComponent<Button>().onClick.Invoke();
                GetBoardButton(board, "acceptButton").onClick.Invoke();
                AssertSucceeded(runtime.Bounties.RecordDefeat(catalog, AdventureId, EnemyId));

                GameObject.Find("SettlementFeature_bounty_board").GetComponent<Button>().onClick.Invoke();
                StringAssert.Contains("石甲兽悬赏 · 一次性除害令 | 目标已完成 | 进度 1/1", BoardEntriesText(board).text);

                GetBoardButton(board, "claimButton").onClick.Invoke();

                Assert.AreEqual(BountyStatus.Claimed, runtime.Bounties.GetState(BountyId).Status);
                StringAssert.Contains("石甲兽悬赏 · 一次性除害令 | 已领取 | 进度 1/1", BoardEntriesText(board).text);
                Assert.IsNull(board.LastResultReason, "成功不得伪造结果字面量，只显示刷新后的实际状态");
                Assert.AreEqual(3, runtime.CaptureSave().inventory[0].quantity);
            }
            finally
            {
                DestroyImmediateSceneFlowAndSession();
            }
        }

        [Test]
        public void ClaimedBountyCannotBeClaimedAgainFromBoard()
        {
            GameRuntime runtime = OpenBuiltSettlementScene(out BountyBoardView board, out ContentCatalogData catalog);
            try
            {
                GameObject.Find("SettlementFeature_bounty_board").GetComponent<Button>().onClick.Invoke();
                GetBoardButton(board, "acceptButton").onClick.Invoke();
                AssertSucceeded(runtime.Bounties.RecordDefeat(catalog, AdventureId, EnemyId));
                GetBoardButton(board, "claimButton").onClick.Invoke();
                Assert.AreEqual(BountyStatus.Claimed, runtime.Bounties.GetState(BountyId).Status);

                GetBoardButton(board, "claimButton").onClick.Invoke();

                Assert.AreEqual(BountyUseCaseReasons.RepeatedClaim, board.LastResultReason);
                Assert.AreEqual(BountyStatus.Claimed, runtime.Bounties.GetState(BountyId).Status);
                Assert.AreEqual(3, runtime.CaptureSave().inventory[0].quantity);
            }
            finally
            {
                DestroyImmediateSceneFlowAndSession();
            }
        }

        [Test]
        public void IllegalNonProductionAndWrongSettlementRequestsDoNotFabricateDisplayOrState()
        {
            DestroyExistingSceneFlowAndSession();
            SettlementSceneBuilder.Build();
            EditorSceneManager.OpenScene("Assets/Scenes/SettlementScene.unity", OpenSceneMode.Single);
            GameRuntime runtime = CreateRuntime();
            BountyBoardView board = FindBoardFromBuiltScene();
            ContentCatalogData catalog = CreateCatalogWithOutOfScopeBounties();
            try
            {
                board.Show(catalog, SettlementId, runtime.Bounties);

                StringAssert.Contains("石甲兽悬赏 · 一次性除害令 | 可接取 | 进度 0/1", BoardEntriesText(board).text);
                // 非正式（无批准标题）悬赏：显示回退为稳定 ID 本身，不伪造占位文本。
                StringAssert.Contains(DraftBountyId + " | 可接取 | 进度 0/1", BoardEntriesText(board).text);
                Assert.IsFalse(BoardEntriesText(board).text.Contains(OtherSettlementBountyId));

                board.SubmitAccept(DraftBountyId);
                Assert.AreEqual(BountyUseCaseReasons.BountyNotProduction, board.LastResultReason);
                Assert.AreEqual(BountyStatus.Available, runtime.Bounties.GetState(DraftBountyId).Status);

                board.SubmitAccept(OtherSettlementBountyId);
                Assert.AreEqual(BountyUseCaseReasons.WrongSettlement, board.LastResultReason);
                Assert.AreEqual(BountyStatus.Available, runtime.Bounties.GetState(OtherSettlementBountyId).Status);

                board.SubmitAccept("bounty_unknown");
                Assert.AreEqual(BountyUseCaseReasons.BountyMissing, board.LastResultReason);
                Assert.AreEqual(BountyStatus.Available, runtime.Bounties.GetState("bounty_unknown").Status);

                StringAssert.Contains("石甲兽悬赏 · 一次性除害令 | 可接取 | 进度 0/1", BoardEntriesText(board).text);
                StringAssert.Contains(DraftBountyId + " | 可接取 | 进度 0/1", BoardEntriesText(board).text);
            }
            finally
            {
                DestroyImmediateSceneFlowAndSession();
            }
        }

        [Test]
        public void UnknownAndDisabledFeaturesDoNotOpenBoard()
        {
            OpenBuiltSettlementScene(out BountyBoardView board, out _);
            try
            {
                var controller = UnityEngine.Object.FindFirstObjectByType<SettlementController>();
                var dispatchFeature = typeof(SettlementController).GetMethod(
                    "DispatchFeature",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                dispatchFeature.Invoke(controller, new object[]
                {
                    new SettlementFeatureData
                    {
                        featureId = SettlementFeatureDispatcher.BountyBoardFeatureId,
                        availability = "disabled",
                        disabledReasonKey = "settlement_feature_disabled",
                    },
                });
                Assert.IsFalse(board.IsOpen);
                // 玩家只看到可理解的禁用反馈；稳定原因保留在控制器结果中。
                Assert.AreEqual("功能未开放", BoardStatusText().text);

                dispatchFeature.Invoke(controller, new object[]
                {
                    new SettlementFeatureData
                    {
                        featureId = "market",
                        availability = "enabled",
                    },
                });
                Assert.IsFalse(board.IsOpen);
                Assert.AreEqual("功能不存在", BoardStatusText().text);
            }
            finally
            {
                DestroyImmediateSceneFlowAndSession();
            }
        }

        [Test]
        public void HandlerExceptionFailsClosedWithoutOpeningBoard()
        {
            OpenBuiltSettlementScene(out BountyBoardView board, out _);
            try
            {
                var dispatcher = UnityEngine.Object.FindFirstObjectByType<SettlementFeatureDispatcher>();
                var handlersField = typeof(SettlementFeatureDispatcher).GetField(
                    "handlers",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var handlers = (Dictionary<string, Func<SettlementFeatureData, string>>)handlersField.GetValue(dispatcher);
                handlers[SettlementFeatureDispatcher.BountyBoardFeatureId] = _ =>
                    throw new InvalidOperationException("bounty_board_handler_boom");

                LogAssert.Expect(
                    LogType.Error,
                    new Regex("SettlementFeatureDispatcher.*bounty_board.*bounty_board_handler_boom"));
                GameObject.Find("SettlementFeature_bounty_board").GetComponent<Button>().onClick.Invoke();

                Assert.IsFalse(board.IsOpen);
                Assert.AreEqual("功能操作失败", BoardStatusText().text);
            }
            finally
            {
                DestroyImmediateSceneFlowAndSession();
            }
        }

        private GameRuntime OpenBuiltSettlementScene(
            out BountyBoardView board,
            out ContentCatalogData catalog)
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
            InvokePrivate(controller, "Start");

            board = FindBoardFromBuiltScene();
            catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                "Assets/Data/ContentCatalog/ContentCatalog.asset");
            return runtime;
        }

        private static BountyBoardView FindBoardFromBuiltScene()
        {
            return UnityEngine.Object.FindFirstObjectByType<BountyBoardView>(FindObjectsInactive.Include);
        }

        private static GameRuntime CreateRuntime()
        {
            if (UnityEngine.Object.FindFirstObjectByType<GameBootstrap>() == null)
                new GameObject("GameBootstrapTest").AddComponent<GameBootstrap>();
            GameRuntime runtime = GameBootstrap.RequireRuntime();
            runtime.EnterSettlement(SettlementId);
            return runtime;
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

        private ContentCatalogData CreateCatalogWithOutOfScopeBounties()
        {
            ContentCatalogData catalog = Track(ScriptableObject.CreateInstance<ContentCatalogData>());
            var settlement = Track(ScriptableObject.CreateInstance<SettlementData>());
            settlement.settlementId = SettlementId;
            var enemy = Track(ScriptableObject.CreateInstance<EnemyData>());
            enemy.enemyId = EnemyId;
            var reward = Track(ScriptableObject.CreateInstance<ItemData>());
            reward.itemId = "item_lingshi_low";
            reward.contentScope = InventoryGrantUseCase.ProductionContentScope;
            reward.maxStack = 99;

            BountyData production = Track(CreateBounty(BountyId, scope: InventoryGrantUseCase.ProductionContentScope));
            BountyData draft = Track(CreateBounty(DraftBountyId, scope: "content_scope_draft"));
            BountyData otherSettlement = Track(CreateBounty(OtherSettlementBountyId, scope: InventoryGrantUseCase.ProductionContentScope));
            otherSettlement.issuerSettlementId = "other_city";

            catalog.ReplaceEntries(
                new[] { settlement },
                new[] { enemy },
                new[] { reward },
                new[] { production, draft, otherSettlement });
            return catalog;
        }

        private BountyData CreateBounty(string bountyId, string scope)
        {
            BountyData bounty = Track(ScriptableObject.CreateInstance<BountyData>());
            bounty.bountyId = bountyId;
            bounty.titleKey = bountyId + "_title";
            bounty.contentScope = scope;
            bounty.issuerSettlementId = SettlementId;
            bounty.objectiveType = "defeat_enemy";
            bounty.targetEnemyId = EnemyId;
            bounty.requiredCount = 1;
            bounty.allowedAdventureId = AdventureId;
            bounty.rewardEntries = new[]
            {
                new BountyRewardEntry { itemId = "item_lingshi_low", quantity = 3 },
            };
            bounty.repeatPolicy = "one_time";
            return bounty;
        }

        private T Track<T>(T value)
            where T : UnityEngine.Object
        {
            temporaryAssets.Add(value);
            return value;
        }

        private static Button GetBoardButton(BountyBoardView board, string propertyName)
        {
            var serialized = new SerializedObject(board);
            return serialized.FindProperty(propertyName).objectReferenceValue as Button;
        }

        private static Text BoardEntriesText(BountyBoardView board)
        {
            var serialized = new SerializedObject(board);
            return serialized.FindProperty("entriesText").objectReferenceValue as Text;
        }

        private static Text BoardStatusText()
        {
            return GameObject.Find("SettlementStatusText")?.GetComponent<Text>();
        }

        private static void AssertSucceeded(BountyActionResult result)
        {
            Assert.IsTrue(result.Succeeded, result.FailureReason);
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
