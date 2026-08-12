using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TianZhang.Adventure;
using TianZhang.Bootstrap;
using TianZhang.Character;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Cultivation;
using TianZhang.Entity;
using TianZhang.Game;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.Persistence;
using TianZhang.Map;
using TianZhang.Settlement;
using TianZhang.Tactical;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace TianZhang.Tests
{
    /// <summary>
    /// 关中正式接取至存档端到端闸门：同一 GameRuntime 经正式入口完成唯一生产链
    /// （guanzhong_city 接取 -> guanzhong_wild 击败石甲兽 -> 普通掉落 -> 据点返回 ->
    /// 领取悬赏 -> 保存并读取）。正向链路经既有场景流与悬赏面板所有者驱动：
    /// <see cref="SceneFlowManager.PrepareSettlementEntry"/>、
    /// <see cref="SceneFlowManager.PrepareAdventureEntry"/>、
    /// <see cref="BountyBoardView.Show"/>／<see cref="BountyBoardView.SubmitAccept"/>／
    /// <see cref="BountyBoardView.SubmitClaim"/>，正式遭遇配置经唯一生产 Adventure 场景的
    /// 控制器 Awake 生产链路完成。同时证明非生产或缺失的敌人、掉落、目录、悬赏引用与
    /// 非法保存输入失败关闭，不伪造胜利、掉落、进度、领取或恢复结果。
    /// </summary>
    public sealed class GuanzhongFormalEndToEndTests
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
            foreach (UnityEngine.Object asset in temporaryAssets)
                UnityEngine.Object.DestroyImmediate(asset);
            DestroyExistingSceneFlowAndSession();
        }

        [Test]
        public void FormalGuanzhongProductionChainAcceptsDefeatsDropsReturnsClaimsSavesAndRestoresOnce()
        {
            DestroyExistingSceneFlowAndSession();
            // 唯一生产 Adventure 场景：控制器序列化绑定生产目录与 env_guanzhong_wild 环境档案。
            EditorSceneManager.OpenScene("Assets/Scenes/AdventureScene.unity", OpenSceneMode.Single);
            GameObject flowGo = null;
            GameObject boardGo = null;
            var profile = ScriptableObject.CreateInstance<CharacterData>();
            try
            {
                profile.charName = "端到端闸门";
                GameRuntime runtime = GameBootstrap.RequireRuntime();
                runtime.BeginNewGame(
                    CharacterRuntimeProfile.FromDefinition("player", profile),
                    CultivationState.CreateEmpty(),
                    "guanzhong_hub");
                ContentCatalogData catalog = LoadProductionCatalog();

                // 1. 进入关中城：由 SceneFlowManager.PrepareSettlementEntry 持久化据点与返回上下文。
                flowGo = new GameObject("FormalE2ESceneFlow");
                SceneFlowManager flow = flowGo.AddComponent<SceneFlowManager>();
                Assert.AreEqual("SettlementScene", flow.PrepareSettlementEntry(SettlementId));
                Assert.AreEqual(SettlementId, runtime.Navigation.SettlementId);
                Assert.AreEqual(GameplaySceneNames.World, runtime.Navigation.ReturnTarget.SceneName);
                Assert.AreEqual("guanzhong_hub", runtime.Navigation.ReturnTarget.WorldNodeId);

                // 2. 接取：BountyBoardView.Show 打开正式据点悬赏，SubmitAccept 提交唯一生产悬赏。
                boardGo = new GameObject("FormalE2EBountyBoard");
                BountyBoardView board = boardGo.AddComponent<BountyBoardView>();
                board.Show(catalog, SettlementId, runtime.Bounties);
                Assert.AreEqual(1, board.ListedBountyCount);
                Assert.AreEqual(BountyId, board.CurrentBountyId);
                Assert.AreEqual(BountyStatus.Available, runtime.Bounties.GetState(BountyId).Status);
                board.SubmitAccept(BountyId);
                BountyState accepted = runtime.Bounties.GetState(BountyId);
                Assert.AreEqual(BountyStatus.Accepted, accepted.Status);
                Assert.AreEqual(0, accepted.Progress);
                Assert.IsNull(board.LastResultReason, "成功不得伪造结果字面量，只显示刷新后的实际状态");
                // 重复接取失败关闭，不改变状态。
                board.SubmitAccept(BountyId);
                Assert.AreEqual(BountyUseCaseReasons.RepeatedAccept, board.LastResultReason);
                Assert.AreEqual(BountyStatus.Accepted, runtime.Bounties.GetState(BountyId).Status);

                // 3. 进入正式冒险：由 SceneFlowManager.PrepareAdventureEntry 持久化副本与据点返回上下文。
                Assert.AreEqual(
                    "AdventureScene",
                    flow.PrepareAdventureEntry(
                        FormalEncounterRules.GuanzhongWildAdventureId,
                        SceneReturnTarget.Settlement(SettlementId)));
                Assert.AreEqual(AdventureId, runtime.Navigation.AdventureId);
                Assert.AreEqual(GameplaySceneNames.Settlement, runtime.Navigation.ReturnTarget.SceneName);
                Assert.AreEqual(SettlementId, runtime.Navigation.ReturnTarget.SettlementId);

                // 4. 正式遭遇配置：唯一生产场景的控制器经生产 Awake 链路绑定正式石甲兽；
                //    序列化绑定必须是生产目录与 env_guanzhong_wild，不依赖测试注入。
                AdventureSceneController controller =
                    UnityEngine.Object.FindFirstObjectByType<AdventureSceneController>();
                Assert.IsNotNull(controller, "The formal AdventureScene must contain AdventureSceneController.");
                var serializedController = new SerializedObject(controller);
                Assert.AreEqual(
                    catalog,
                    serializedController.FindProperty("contentCatalog").objectReferenceValue,
                    "The formal AdventureScene must bind the single production content catalog.");
                Assert.AreEqual(
                    LoadProductionEnvironmentProfile(),
                    serializedController.FindProperty("guanzhongWildEnvironmentProfile").objectReferenceValue,
                    "The formal AdventureScene must bind env_guanzhong_wild.");
                controller.SetEncounterRandomSource(new SequenceRandomSource(99, 49));
                InvokeAwake(controller);
                ExplorationController boundExploration =
                    UnityEngine.Object.FindFirstObjectByType<ExplorationController>();
                Assert.IsNotNull(boundExploration, "The formal AdventureScene must contain ExplorationController.");
                Assert.IsTrue(boundExploration.enabled);
                Assert.AreEqual(1, boundExploration.enemyCount);

                // 5. 正式胜利结算：普通掉落与悬赏进度各只提交一次；返回链路经
                //    SceneFlowManager.ReturnToPreviousScene 的 PrepareReturnToPreviousScene 提交
                //    据点还原、副本清空与返回目标清空。场景加载不在 EditMode 范围，与既有
                //    AdventureSceneControllerTests 约定一致（SceneManager.LoadScene 在 EditMode 抛出
                //    InvalidOperationException，会话事实已在加载尝试前提交）。
                Assert.IsTrue(catalog.TryGetEnemy(FormalEncounterRules.ShijiahouEnemyId, out EnemyData enemy));
                Assert.Throws<InvalidOperationException>(
                    () => controller.ResolveEncounterAndReturn(CombatSessionOutcome.Victory, enemy));

                Assert.AreEqual(CombatSessionOutcome.Victory, controller.LastEncounterOutcome);
                Assert.IsNotNull(controller.LastFormalEncounterResult);
                Assert.AreEqual(EnemyId, controller.LastFormalEncounterResult.EnemyId);
                Assert.AreEqual(AdventureId, controller.LastFormalEncounterResult.AdventureId);
                BountyState completed = runtime.Bounties.GetState(BountyId);
                Assert.AreEqual(BountyStatus.ObjectiveCompleted, completed.Status);
                Assert.AreEqual(1, completed.Progress);
                Assert.AreEqual(1, InventoryQuantity(runtime, "item_shijia_piece"));
                Assert.AreEqual(1, InventoryQuantity(runtime, "item_lingshi_low"));
                Assert.AreEqual(SettlementId, runtime.Navigation.SettlementId);
                Assert.IsNull(runtime.Navigation.AdventureId);
                Assert.IsTrue(string.IsNullOrEmpty(runtime.Navigation.ReturnTarget.SceneName));

                // 6. 重复结算失败关闭：同一控制器经流重新进入后二次消费仍稳定拒绝，不重复掉落或进度。
                Assert.AreEqual(
                    "AdventureScene",
                    flow.PrepareAdventureEntry(
                        FormalEncounterRules.GuanzhongWildAdventureId,
                        SceneReturnTarget.Settlement(SettlementId)));
                LogAssert.Expect(LogType.Error, new Regex(FormalEncounterRules.AlreadyConsumedReason));
                Assert.Throws<InvalidOperationException>(
                    () => controller.ResolveEncounterAndReturn(CombatSessionOutcome.Victory, enemy));
                Assert.AreEqual(
                    FormalEncounterRules.AlreadyConsumedReason,
                    controller.EncounterResolutionFailureReason);
                Assert.AreEqual(1, InventoryQuantity(runtime, "item_shijia_piece"));
                Assert.AreEqual(1, InventoryQuantity(runtime, "item_lingshi_low"));
                Assert.AreEqual(BountyStatus.ObjectiveCompleted, runtime.Bounties.GetState(BountyId).Status);
                Assert.AreEqual(SettlementId, runtime.Navigation.SettlementId);
                Assert.IsNull(runtime.Navigation.AdventureId);

                // 7. 回到关中城后经悬赏面板领奖：普通掉落与悬赏奖励各只授予一次。
                board.Show(catalog, SettlementId, runtime.Bounties);
                board.SubmitClaim(BountyId);
                BountyState claimed = runtime.Bounties.GetState(BountyId);
                Assert.AreEqual(BountyStatus.Claimed, claimed.Status);
                Assert.AreEqual(1, claimed.Progress);
                Assert.AreEqual(1, InventoryQuantity(runtime, "item_shijia_piece"));
                Assert.AreEqual(4, InventoryQuantity(runtime, "item_lingshi_low"));
                Assert.IsNull(board.LastResultReason, "成功不得伪造结果字面量，只显示刷新后的实际状态");
                // 重复领奖失败关闭。
                board.SubmitClaim(BountyId);
                Assert.AreEqual(BountyUseCaseReasons.RepeatedClaim, board.LastResultReason);
                Assert.AreEqual(BountyStatus.Claimed, runtime.Bounties.GetState(BountyId).Status);

                // 8. 保存并以全新会话读取：Claimed、库存与据点／返回事实无损保留。
                GameSaveEnvelope saved = runtime.CaptureSave();
                Assert.AreEqual(GameSaveSerializer.SchemaVersion, saved.schemaVersion);
                string savedJson = runtime.CaptureSaveJson();
                var loaded = new GameRuntime();
                loaded.RestoreSaveJson(savedJson, catalog);

                BountyState restored = loaded.Bounties.GetState(BountyId);
                Assert.AreEqual(BountyStatus.Claimed, restored.Status);
                Assert.AreEqual(1, restored.Progress);
                Assert.AreEqual(1, InventoryQuantity(loaded, "item_shijia_piece"));
                Assert.AreEqual(4, InventoryQuantity(loaded, "item_lingshi_low"));
                Assert.AreEqual("guanzhong_hub", loaded.Navigation.WorldNodeId);
                Assert.AreEqual(SettlementId, loaded.Navigation.SettlementId);
                Assert.IsNull(loaded.Navigation.AdventureId);
                Assert.IsTrue(string.IsNullOrEmpty(loaded.Navigation.ReturnTarget.SceneName));
            }
            finally
            {
                if (flowGo != null)
                    UnityEngine.Object.DestroyImmediate(flowGo);
                if (boardGo != null)
                    UnityEngine.Object.DestroyImmediate(boardGo);
                UnityEngine.Object.DestroyImmediate(profile);
                DestroyExistingSceneFlowAndSession();
                // 生产 Adventure 场景的控制器不参与本卡其他用例：恢复干净场景避免残留对象串扰。
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [Test]
        public void FormalGuanzhongChainFailsClosedOnNonProductionMissingOrIllegalInputs()
        {
            DestroyExistingSceneFlowAndSession();
            try
            {
                GameRuntime runtime = GameBootstrap.RequireRuntime();
                runtime.EnterWorld("guanzhong_hub");
                runtime.EnterSettlement(SettlementId);
                ContentCatalogData catalog = LoadProductionCatalog();
                AssertSucceeded(runtime.Bounties.Accept(catalog, BountyId, SettlementId));

                // 1. 缺失目录：遭遇配置被阻止，胜利结算也不伪造掉落或进度。
                var missingCatalogExplorationGo = new GameObject("FormalE2EFailureClosedExploration");
                var missingCatalogControllerGo = new GameObject("FormalE2EFailureClosedController");
                try
                {
                    missingCatalogExplorationGo.AddComponent<ExplorationController>();
                    AdventureSceneController missingCatalog =
                        missingCatalogControllerGo.AddComponent<AdventureSceneController>();
                    missingCatalog.SetContentCatalog(null);
                    missingCatalog.SetGuanzhongWildEnvironmentProfile(LoadProductionEnvironmentProfile());
                    missingCatalog.SetEncounterRandomSource(new SequenceRandomSource(0, 0));

                    runtime.EnterAdventure(
                        FormalEncounterRules.GuanzhongWildAdventureId,
                        SceneReturnTarget.Settlement(SettlementId));
                    try
                    {
                        LogAssert.Expect(LogType.Error, new Regex(FormalEncounterRules.CatalogMissingReason));
                        InvokePrivate(missingCatalog, "ConfigureCurrentAdventureEncounter");
                        Assert.AreEqual(AdventureSceneState.Loading, missingCatalog.CurrentState);
                        Assert.IsFalse(GetPrivateField<ExplorationController>(
                            missingCatalog, "explorationController").enabled);

                        Assert.IsTrue(catalog.TryGetEnemy(
                            FormalEncounterRules.ShijiahouEnemyId, out EnemyData productionEnemy));
                        LogAssert.Expect(LogType.Error, new Regex(FormalEncounterRules.CatalogMissingReason));
                        missingCatalog.ResolveEncounterAndReturn(
                            CombatSessionOutcome.Victory, productionEnemy);
                        Assert.AreEqual(
                            FormalEncounterRules.CatalogMissingReason,
                            missingCatalog.EncounterResolutionFailureReason);
                        Assert.AreEqual(0, runtime.CaptureSave().inventory.Length);
                        BountyState state = runtime.Bounties.GetState(BountyId);
                        Assert.AreEqual(BountyStatus.Accepted, state.Status);
                        Assert.AreEqual(0, state.Progress);
                    }
                    finally
                    {
                        if (runtime.Navigation.AdventureId != null)
                            runtime.ReturnToPreviousScene();
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(missingCatalogExplorationGo);
                    UnityEngine.Object.DestroyImmediate(missingCatalogControllerGo);
                }

                // 2. 非生产敌人身份：目录外敌人不伪造胜利、掉落或进度。
                var mismatchExplorationGo = new GameObject("FormalE2EFailureClosedExploration");
                var mismatchControllerGo = new GameObject("FormalE2EFailureClosedController");
                try
                {
                    mismatchExplorationGo.AddComponent<ExplorationController>();
                    AdventureSceneController mismatch =
                        mismatchControllerGo.AddComponent<AdventureSceneController>();
                    mismatch.SetContentCatalog(catalog);
                    mismatch.SetGuanzhongWildEnvironmentProfile(LoadProductionEnvironmentProfile());
                    mismatch.SetEncounterRandomSource(new SequenceRandomSource(0, 0));

                    runtime.EnterAdventure(
                        FormalEncounterRules.GuanzhongWildAdventureId,
                        SceneReturnTarget.Settlement(SettlementId));
                    try
                    {
                        InvokePrivate(mismatch, "ConfigureCurrentAdventureEncounter");
                        var draftEnemy = Track(ScriptableObject.CreateInstance<EnemyData>());
                        draftEnemy.enemyId = "enemy_shijiahou_draft";
                        draftEnemy.contentScope = "content_scope_draft";

                        LogAssert.Expect(LogType.Error, new Regex(FormalEncounterRules.EnemyIdentityMismatchReason));
                        mismatch.ResolveEncounterAndReturn(CombatSessionOutcome.Victory, draftEnemy);
                        Assert.AreEqual(
                            FormalEncounterRules.EnemyIdentityMismatchReason,
                            mismatch.EncounterResolutionFailureReason);
                        Assert.AreEqual(0, runtime.CaptureSave().inventory.Length);
                        BountyState state = runtime.Bounties.GetState(BountyId);
                        Assert.AreEqual(BountyStatus.Accepted, state.Status);
                        Assert.AreEqual(0, state.Progress);
                    }
                    finally
                    {
                        if (runtime.Navigation.AdventureId != null)
                            runtime.ReturnToPreviousScene();
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(mismatchExplorationGo);
                    UnityEngine.Object.DestroyImmediate(mismatchControllerGo);
                }

                // 3. 缺失敌人：目录无法解析正式石甲兽时遭遇被阻止。
                AssertEncounterBlocked(
                    runtime,
                    CreateEmptyCatalog(),
                    FormalEncounterRules.EnemyMissingReason);

                // 4. 非生产范围敌人：目录中的草稿范围敌人不进入正式遭遇。
                AssertEncounterBlocked(
                    runtime,
                    CreateDraftScopeEnemyCatalog(),
                    FormalEncounterRules.EnemyScopeInvalidReason);

                // 5. 缺失掉落：正式敌人没有掉落条目时遭遇被阻止。
                AssertEncounterBlocked(
                    runtime,
                    CreateDroplessEnemyCatalog(),
                    FormalEncounterRules.DropsMissingReason);

                // 6. 悬赏引用失败关闭：接取与领取都不伪造进度或库存。
                ContentCatalogData fixtureCatalog = CreateBountyReferenceCatalog();
                ContentCatalogData emptyCatalog = CreateEmptyCatalog();
                Assert.AreEqual(
                    BountyUseCaseReasons.BountyNotProduction,
                    runtime.Bounties.Accept(fixtureCatalog, DraftBountyId, SettlementId).FailureReason);
                Assert.AreEqual(BountyStatus.Available, runtime.Bounties.GetState(DraftBountyId).Status);
                Assert.AreEqual(
                    BountyUseCaseReasons.WrongSettlement,
                    runtime.Bounties.Accept(fixtureCatalog, OtherSettlementBountyId, SettlementId).FailureReason);
                Assert.AreEqual(BountyStatus.Available, runtime.Bounties.GetState(OtherSettlementBountyId).Status);
                Assert.AreEqual(
                    BountyUseCaseReasons.BountyMissing,
                    runtime.Bounties.Accept(fixtureCatalog, "bounty_unknown", SettlementId).FailureReason);
                Assert.AreEqual(BountyStatus.Available, runtime.Bounties.GetState("bounty_unknown").Status);
                Assert.AreEqual(
                    BountyUseCaseReasons.NotCompleted,
                    runtime.Bounties.Claim(fixtureCatalog, "bounty_unknown").FailureReason);
                Assert.AreEqual(0, runtime.CaptureSave().inventory.Length);

                AssertSucceeded(runtime.Bounties.RecordDefeat(catalog, AdventureId, EnemyId));
                BountyState completed = runtime.Bounties.GetState(BountyId);
                Assert.AreEqual(BountyStatus.ObjectiveCompleted, completed.Status);
                Assert.AreEqual(1, completed.Progress);
                // 领取时目录缺少该悬赏引用：拒绝且库存不变。
                Assert.AreEqual(
                    BountyUseCaseReasons.BountyMissing,
                    runtime.Bounties.Claim(emptyCatalog, BountyId).FailureReason);
                Assert.AreEqual(0, runtime.CaptureSave().inventory.Length);
                Assert.AreEqual(BountyStatus.ObjectiveCompleted, runtime.Bounties.GetState(BountyId).Status);

                // 7. 非法保存输入：Claimed 进度与目标不符、未知悬赏引用均原子拒绝，会话不变。
                string baselineJson = runtime.CaptureSaveJson();
                GameSaveEnvelope tampered = GameSaveSerializer.Deserialize(baselineJson);
                tampered.bounties[0].status = (int)BountyStatus.Claimed;
                tampered.bounties[0].progress = 0;
                Assert.Throws<ArgumentException>(() => runtime.RestoreSave(tampered, catalog));
                Assert.AreEqual(baselineJson, runtime.CaptureSaveJson());

                tampered = GameSaveSerializer.Deserialize(baselineJson);
                tampered.bounties[0].bountyId = "bounty_unknown";
                Assert.Throws<ArgumentException>(() => runtime.RestoreSave(tampered, catalog));
                Assert.AreEqual(baselineJson, runtime.CaptureSaveJson());
            }
            finally
            {
                DestroyExistingSceneFlowAndSession();
            }
        }

        /// <summary>
        /// 遭遇配置在正式副本上下文下必须被阻止：状态保持 Loading、探索控制器被禁用，
        /// 且日志出现稳定失败原因。
        /// </summary>
        private static void AssertEncounterBlocked(
            GameRuntime runtime,
            ContentCatalogData catalog,
            string expectedBlockReason)
        {
            // 控制器在副本上下文未设置时创建：Awake 不提前触发正式配置。
            var explorationGo = new GameObject("FormalE2EFailureClosedExploration");
            var controllerGo = new GameObject("FormalE2EFailureClosedController");
            try
            {
                explorationGo.AddComponent<ExplorationController>();
                AdventureSceneController controller = controllerGo.AddComponent<AdventureSceneController>();
                controller.SetContentCatalog(catalog);
                controller.SetGuanzhongWildEnvironmentProfile(LoadProductionEnvironmentProfile());
                controller.SetEncounterRandomSource(new SequenceRandomSource(0, 0));

                runtime.EnterAdventure(
                    FormalEncounterRules.GuanzhongWildAdventureId,
                    SceneReturnTarget.Settlement(SettlementId));
                try
                {
                    LogAssert.Expect(LogType.Error, new Regex(expectedBlockReason));
                    InvokePrivate(controller, "ConfigureCurrentAdventureEncounter");

                    Assert.AreEqual(AdventureSceneState.Loading, controller.CurrentState);
                    Assert.IsFalse(GetPrivateField<ExplorationController>(
                        controller, "explorationController").enabled);
                }
                finally
                {
                    if (runtime.Navigation.AdventureId != null)
                        runtime.ReturnToPreviousScene();
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(explorationGo);
                UnityEngine.Object.DestroyImmediate(controllerGo);
            }
        }

        private ContentCatalogData CreateEmptyCatalog()
        {
            return Track(ScriptableObject.CreateInstance<ContentCatalogData>());
        }

        private ContentCatalogData CreateDraftScopeEnemyCatalog()
        {
            ContentCatalogData catalog = Track(ScriptableObject.CreateInstance<ContentCatalogData>());
            var enemy = Track(ScriptableObject.CreateInstance<EnemyData>());
            enemy.enemyId = EnemyId;
            enemy.contentScope = "content_scope_draft";
            catalog.ReplaceEntries(null, new[] { enemy }, null, null);
            return catalog;
        }

        private ContentCatalogData CreateDroplessEnemyCatalog()
        {
            ContentCatalogData catalog = Track(ScriptableObject.CreateInstance<ContentCatalogData>());
            var enemy = Track(ScriptableObject.CreateInstance<EnemyData>());
            enemy.enemyId = EnemyId;
            enemy.contentScope = FormalEncounterRules.GuanzhongContentScope;
            enemy.aiProfileId = EnemyAIProfileResolver.MeleeProfileId;
            enemy.combatTemplate = Track(ScriptableObject.CreateInstance<CharacterData>());
            enemy.dropEntries = null;
            catalog.ReplaceEntries(null, new[] { enemy }, null, null);
            return catalog;
        }

        private ContentCatalogData CreateBountyReferenceCatalog()
        {
            ContentCatalogData catalog = Track(ScriptableObject.CreateInstance<ContentCatalogData>());
            var settlement = Track(ScriptableObject.CreateInstance<SettlementData>());
            settlement.settlementId = SettlementId;
            var reward = Track(ScriptableObject.CreateInstance<ItemData>());
            reward.itemId = "item_lingshi_low";
            reward.contentScope = InventoryGrantUseCase.ProductionContentScope;
            reward.maxStack = 99;

            BountyData draft = Track(CreateBounty(DraftBountyId, "content_scope_draft", SettlementId));
            BountyData otherSettlement = Track(CreateBounty(
                OtherSettlementBountyId,
                InventoryGrantUseCase.ProductionContentScope,
                "other_city"));

            catalog.ReplaceEntries(
                new[] { settlement },
                null,
                new[] { reward },
                new[] { draft, otherSettlement });
            return catalog;
        }

        private static BountyData CreateBounty(string bountyId, string scope, string issuerSettlementId)
        {
            var bounty = ScriptableObject.CreateInstance<BountyData>();
            bounty.bountyId = bountyId;
            bounty.contentScope = scope;
            bounty.issuerSettlementId = issuerSettlementId;
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

        private static ContentCatalogData LoadProductionCatalog()
        {
            ContentCatalogData catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                "Assets/Data/ContentCatalog/ContentCatalog.asset");
            Assert.IsNotNull(catalog, "The single production content catalog asset is missing.");
            return catalog;
        }

        private static EnvironmentProfileAsset LoadProductionEnvironmentProfile()
        {
            EnvironmentProfileAsset profile = AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>(
                "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset");
            Assert.IsNotNull(profile, "The formal env_guanzhong_wild environment profile is missing.");
            return profile;
        }

        private static int InventoryQuantity(GameRuntime runtime, string itemId)
        {
            foreach (InventoryRecord record in runtime.CaptureSave().inventory)
            {
                if (record.itemId == itemId)
                    return record.quantity;
            }
            return 0;
        }

        private static void AssertSucceeded(BountyActionResult result)
        {
            Assert.IsTrue(result.Succeeded, result.FailureReason);
        }

        private static void InvokePrivate(MonoBehaviour target, string methodName)
        {
            var method = target.GetType().GetMethod(
                methodName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(method, "Missing private method " + methodName);
            method.Invoke(target, null);
        }

        private static void InvokeAwake(MonoBehaviour target)
        {
            var method = target.GetType().GetMethod(
                "Awake",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(method, "Missing private Awake method");
            method.Invoke(target, null);
        }

        private static T GetPrivateField<T>(MonoBehaviour target, string fieldName)
        {
            var field = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing private field " + fieldName);
            return (T)field.GetValue(target);
        }

        private static void DestroyExistingSceneFlowAndSession()
        {
            if (SceneFlowManager.Instance != null)
                UnityEngine.Object.DestroyImmediate(SceneFlowManager.Instance.gameObject);
            GameBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            if (bootstrap != null)
                UnityEngine.Object.DestroyImmediate(bootstrap.gameObject);
        }

        private sealed class SequenceRandomSource : IFormalEncounterRandomSource
        {
            private readonly Queue<int> values;

            public SequenceRandomSource(params int[] values)
            {
                this.values = new Queue<int>(values);
            }

            public int NextPercent()
            {
                return values.Count == 0 ? 0 : values.Dequeue();
            }
        }
    }
}
