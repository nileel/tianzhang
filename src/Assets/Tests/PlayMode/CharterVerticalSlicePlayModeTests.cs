using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Adventure;
using TianZhang.Content;
using TianZhang.Game;
using TianZhang.Settlement;
using TianZhang.World;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TianZhang.Tests
{
    /// <summary>
    /// 单据点端到端闸门（U-TZ-CHARTER-SLICE-01D）：只在正式 SettlementScene／AdventureScene 与
    /// 公开生产入口上驱动批准的十步流程，断言各层真实结果。用例从空
    /// <see cref="GameSession.CharterRuntimeState"/> 开始，逐步完成五类临时证明、完整 candidate、
    /// 三份独立请求（同一 preparation 派生）、金丹未获胜、元婴受锚、首次正式原子提交、正式
    /// Adventure 的 `env_guanzhong_wild` 反馈、schema 4 保存／读取与重复消费失败。测试不复制
    /// candidate、请求、冲突、环境解析或存档实现，不调用旧 BuildValidState、不手工赋值会话长期
    /// 状态、不伪造站点／环境，也不创建第二场景、Canvas、会话或存档所有者。
    /// </summary>
    public sealed class CharterVerticalSlicePlayModeTests
    {
        private const string SettlementId = "guanzhong_city";
        private const string AdventureId = "guanzhong_wild";
        private const string SiteId = "charter_site_old_water_station";
        private const string RuleEntryId = "charter_entry_suifu_diji";
        private const string WaterRedistributionEventId = "event_suifu_water_redistribution";
        private const string EnvironmentProfileId = "env_guanzhong_wild";
        private const string YuanyingAnchoredReason = "TZ_CHARTER_CONFLICT_YUANYING_ANCHORED";

        private GameObject flowGo;

        [UnityTearDown]
        public void TearDown()
        {
            if (flowGo != null)
                Object.DestroyImmediate(flowGo);
            if (SceneFlowManager.Instance != null)
                Object.DestroyImmediate(SceneFlowManager.Instance.gameObject);
            if (GameSession.Instance != null)
                Object.DestroyImmediate(GameSession.Instance.gameObject);
            flowGo = null;
        }

        /// <summary>
        /// 正式场景完整纵向流程：通行、管理、节点、登记、供给五类临时证明；越序失败节点；
        /// 金丹未获胜与元婴受锚（无 NextState／事件输出、不写会话）；首次正式调用原子提交；
        /// 同一面板重复消费稳定失败；正式 Adventure 显示条目事件与 `env_guanzhong_wild` 档案；
        /// schema 4 保存／读取后长期状态与目录版本一致、环境引用仍生效；读档后再次正式调用
        /// 稳定失败且状态不变。
        /// </summary>
        [UnityTest]
        public IEnumerator FormalScenesCompleteTheApprovedTenStepFlow()
        {
            flowGo = new GameObject("CharterVerticalSliceE2EFlow");
            var flow = flowGo.AddComponent<SceneFlowManager>();
            Assert.IsNotNull(GameSession.Instance, "SceneFlowManager must ensure the single session.");
            Assert.IsNull(GameSession.Instance.CharterRuntimeState, "The E2E must start from an empty charter long-term state.");
            Assert.AreEqual(0, GameSession.Instance.CharterDefinitionCatalogVersion);

            // 1. 进入正式 SettlementScene（生产入口：持久化据点 ID 后加载场景）。
            flow.EnterSettlement(SettlementId);
            yield return null;

            var settlementController = Object.FindFirstObjectByType<SettlementSceneController>();
            Assert.IsNotNull(settlementController, "The formal SettlementScene must bind its settlement controller.");
            Assert.AreEqual(SettlementId, GameSession.Instance.CurrentSettlementId);

            // 2. 打开旧水驿入口：生产 UI 按钮只经正式控制器打开唯一站点面板。
            ClickByName("SettlementCharterSiteEntry");
            var panel = Object.FindFirstObjectByType<CharterSiteView>(FindObjectsInactive.Include);
            var charterController = Object.FindFirstObjectByType<CharterSiteController>(FindObjectsInactive.Include);
            Assert.IsNotNull(panel, "The formal SettlementScene must keep the charter site panel.");
            Assert.IsNotNull(charterController);
            Assert.IsTrue(panel.IsOpen);
            // 玩家显示分层：面板显示中文名与步骤状态，稳定 ID 保留在控制器字段。
            StringAssert.Contains("旧水驿", PanelText("CharterSiteSiteText").text);
            StringAssert.DoesNotContain(SiteId, PanelText("CharterSiteSiteText").text);
            StringAssert.Contains("通行确认", PanelText("CharterSiteStepText").text);

            // 3. 越序失败节点：未完成五类证明时任何评估都不构造请求、不推进、不写会话。
            ClickByName("CharterSiteYuanyingButton");
            Assert.AreEqual(CharterSiteInteractionReasons.PreparationIncomplete, charterController.LastReason);
            StringAssert.Contains("通行确认", PanelText("CharterSiteStepText").text);
            Assert.IsNull(GameSession.Instance.CharterRuntimeState);

            // 4. 五类临时证明按固定顺序推进，每步只推进临时 progress。
            ClickByName("CharterSiteManagementButton");
            Assert.AreEqual(CharterSiteInteractionReasons.ActionOutOfOrder, charterController.LastReason);
            Assert.IsFalse(charterController.Progress.ManagementVerified);

            ClickByName("CharterSitePassageButton");
            Assert.IsTrue(charterController.Progress.PassageVerified);
            StringAssert.Contains("管理确认", PanelText("CharterSiteStepText").text);

            ClickByName("CharterSiteManagementButton");
            Assert.IsTrue(charterController.Progress.ManagementVerified);
            StringAssert.Contains("接通节点", PanelText("CharterSiteStepText").text);

            ClickByName("CharterSiteNodeButton");
            Assert.AreEqual(3, charterController.Progress.ConnectedNodeIds.Length);
            StringAssert.Contains("条目登记", PanelText("CharterSiteStepText").text);

            ClickByName("CharterSiteRegistrationButton");
            Assert.IsTrue(charterController.Progress.RuleEntryRegistrationVerified);
            StringAssert.Contains("准备供给", PanelText("CharterSiteStepText").text);

            ClickByName("CharterSiteSupplyButton");
            Assert.AreEqual(3, charterController.Progress.RegisteredRealitySupplyIds.Length);
            Assert.IsTrue(charterController.Progress.IsComplete);
            StringAssert.Contains("评估推演", PanelText("CharterSiteStepText").text);
            Assert.IsNull(GameSession.Instance.CharterRuntimeState);
            Assert.AreEqual(0, GameSession.Instance.CharterDefinitionCatalogVersion);

            // 5. 金丹评估：同一 preparation 派生独立请求，稳定返回册界侧未获胜；
            //    不产生 NextState／事件输出，长期状态与目录版本保持不变。
            ClickByName("CharterSiteJindanButton");
            CharterRuleInvocationResult jindan = charterController.LastEvaluation;
            Assert.IsNotNull(jindan);
            Assert.AreEqual(CharterRuleRuntimeReasons.ConflictNotWon, jindan.Reason);
            AssertNoNextStateOrEvents(jindan);
            Assert.IsNull(GameSession.Instance.CharterRuntimeState);
            Assert.AreEqual(0, GameSession.Instance.CharterDefinitionCatalogVersion);

            // 6. 元婴评估：稳定返回受锚，不降格金丹冲突；不产生 NextState／事件输出。
            ClickByName("CharterSiteYuanyingButton");
            CharterRuleInvocationResult yuanying = charterController.LastEvaluation;
            Assert.IsNotNull(yuanying);
            Assert.AreEqual(YuanyingAnchoredReason, yuanying.Reason);
            AssertNoNextStateOrEvents(yuanying);
            Assert.IsNull(GameSession.Instance.CharterRuntimeState);
            Assert.AreEqual(0, GameSession.Instance.CharterDefinitionCatalogVersion);

            // 7. 首次正式调用：candidate 经 GameSession 唯一提交入口原子写入，
            //    目录版本随长期状态一次替换；面板从长期状态重建显示环境引用。
            int catalogVersion = charterController.CatalogVersion;
            Assert.Greater(catalogVersion, 0, "The single static catalog must declare its production version.");
            ClickByName("CharterSiteFormalButton");
            Assert.AreEqual(CharterSiteController.FormalCommittedReason, charterController.LastReason);
            Assert.IsNotNull(GameSession.Instance.CharterRuntimeState);
            Assert.AreEqual(catalogVersion, GameSession.Instance.CharterDefinitionCatalogVersion);
            CollectionAssert.Contains(GameSession.Instance.CharterRuntimeState.registeredRuleEntryIds, RuleEntryId);
            CollectionAssert.Contains(GameSession.Instance.CharterRuntimeState.currentRegionRuleEntryIds, RuleEntryId);
            Assert.AreEqual(CharterSiteController.FormalCommittedReason, charterController.LastReason);
            StringAssert.Contains("已正式提交", PanelText("CharterSiteStepText").text);
            StringAssert.Contains("已生效", PanelText("CharterSiteEnvironmentText").text);
            StringAssert.DoesNotContain(EnvironmentProfileId, PanelText("CharterSiteEnvironmentText").text);

            // 8. 重复消费失败节点：同一面板再次正式调用稳定失败，长期状态保留独立前态副本并
            //    逐项比较完整内容不变（不只看同一引用或数组长度）。
            CharterRuntimeStateData committedState = GameSession.Instance.CharterRuntimeState;
            CharterRuntimeStateData committedStateBeforeRepeat = committedState.CreateCopy();
            ClickByName("CharterSiteFormalButton");
            Assert.AreEqual(CharterRuleRuntimeReasons.RealitySupplyUnavailable, charterController.LastReason);
            AssertCharterStateEquivalent(committedStateBeforeRepeat, GameSession.Instance.CharterRuntimeState);
            Assert.AreEqual(catalogVersion, GameSession.Instance.CharterDefinitionCatalogVersion);

            // 9. 进入正式 AdventureScene：生产入口持久化 adventureId；环境投影从已提交长期状态
            //    解析条目事件并精确匹配已序列化 env_guanzhong_wild 档案，遭遇启动不被投影阻断。
            ContentCatalogData catalog = ReadSerializedCatalog(settlementController);
            flow.EnterAdventure(AdventureId, SceneReturnTarget.Settlement(SettlementId));
            yield return null;

            var adventure = Object.FindFirstObjectByType<AdventureSceneController>();
            Assert.IsNotNull(adventure, "The formal AdventureScene must bind its adventure controller.");
            Assert.AreEqual(AdventureSceneState.Exploration, adventure.CurrentState);
            string feedback = GameObject.Find("EnvironmentFeedbackText").GetComponent<Text>().text;
            // 玩家显示只呈现已批准中文名与可理解状态；原始条目/事件/档案 ID 保留在投影对象中。
            StringAssert.Contains("册界环境引用: 已生效", feedback);
            StringAssert.Contains("水府地纪", feedback);
            StringAssert.DoesNotContain(RuleEntryId, feedback);
            StringAssert.DoesNotContain(EnvironmentProfileId, feedback);
            CharterEnvironmentProjectionResult projection = ReadProjection(adventure);
            CollectionAssert.Contains(projection.RuleEntryIds, RuleEntryId);
            CollectionAssert.Contains(projection.EventIds, WaterRedistributionEventId);
            Assert.AreEqual(EnvironmentProfileId, projection.EnvironmentProfileId);

            // 10. schema 4 保存／读取：长期状态与目录版本原子恢复，水府地纪仍登记且生效，
            //     同一生产目录与序列化档案 ID 仍可解析。
            GameSessionSaveData saveData = GameSession.Instance.CaptureSaveData();
            GameSession.Instance.RestoreSaveData(saveData, catalog);
            AssertCharterStateEquivalent(committedState, GameSession.Instance.CharterRuntimeState);
            Assert.AreEqual(catalogVersion, GameSession.Instance.CharterDefinitionCatalogVersion);
            Assert.IsTrue(CharterEnvironmentProjection.TryResolve(
                    GameSession.Instance.CharterRuntimeState,
                    catalog,
                    EnvironmentProfileId,
                    out CharterEnvironmentProjectionResult restoredProjection),
                restoredProjection.Reason);
            Assert.AreEqual(EnvironmentProfileId, restoredProjection.EnvironmentProfileId);

            // 11. 读档后再次进入正式 AdventureScene：环境反馈仍显示条目事件与档案 ID。
            flow.EnterAdventure(AdventureId, SceneReturnTarget.Settlement(SettlementId));
            yield return null;

            var adventureAfterRestore = Object.FindFirstObjectByType<AdventureSceneController>();
            Assert.AreEqual(AdventureSceneState.Exploration, adventureAfterRestore.CurrentState);
            string feedbackAfterRestore = GameObject.Find("EnvironmentFeedbackText").GetComponent<Text>().text;
            StringAssert.Contains("册界环境引用: 已生效", feedbackAfterRestore);
            StringAssert.DoesNotContain(RuleEntryId, feedbackAfterRestore);
            Assert.AreEqual(EnvironmentProfileId, ReadProjection(adventureAfterRestore).EnvironmentProfileId);

            // 12. 读档后重复消费失败节点：重新进入 Settlement、完成五类证明后再次正式调用
            //     稳定失败，读档恢复的长期状态保留独立前态副本并逐项比较完整内容不变。
            flow.EnterSettlement(SettlementId);
            yield return null;

            ClickByName("SettlementCharterSiteEntry");
            var restoredPanel = Object.FindFirstObjectByType<CharterSiteView>(FindObjectsInactive.Include);
            var restoredController = Object.FindFirstObjectByType<CharterSiteController>(FindObjectsInactive.Include);
            Assert.IsNotNull(restoredPanel);
            Assert.IsTrue(restoredPanel.IsOpen);
            ClickByName("CharterSitePassageButton");
            ClickByName("CharterSiteManagementButton");
            ClickByName("CharterSiteNodeButton");
            ClickByName("CharterSiteRegistrationButton");
            ClickByName("CharterSiteSupplyButton");
            Assert.IsTrue(restoredController.Progress.IsComplete);

            CharterRuntimeStateData restoredStateBeforeRepeat =
                GameSession.Instance.CharterRuntimeState.CreateCopy();
            ClickByName("CharterSiteFormalButton");
            Assert.AreEqual(CharterRuleRuntimeReasons.RealitySupplyUnavailable, restoredController.LastReason);
            AssertCharterStateEquivalent(restoredStateBeforeRepeat, GameSession.Instance.CharterRuntimeState);
            Assert.AreEqual(catalogVersion, GameSession.Instance.CharterDefinitionCatalogVersion);
        }

        /// <summary>
        /// 失败节点：尚未正式调用（长期状态为空）时进入正式 AdventureScene，环境反馈只显示稳定
        /// 原因且既有遭遇启动不被投影阻断；会话仍未被写入。
        /// </summary>
        [UnityTest]
        public IEnumerator FormalAdventureShowsStableReasonWithoutCommittedCharterState()
        {
            flowGo = new GameObject("CharterVerticalSliceNoStateFlow");
            var flow = flowGo.AddComponent<SceneFlowManager>();
            Assert.IsNull(GameSession.Instance.CharterRuntimeState);

            flow.EnterAdventure(AdventureId, SceneReturnTarget.Settlement(SettlementId));
            yield return null;

            var adventure = Object.FindFirstObjectByType<AdventureSceneController>();
            Assert.IsNotNull(adventure, "The formal AdventureScene must bind its adventure controller.");
            Assert.AreEqual(AdventureSceneState.Exploration, adventure.CurrentState,
                "The projection must not block the existing encounter startup chain.");
            string feedback = GameObject.Find("EnvironmentFeedbackText").GetComponent<Text>().text;
            // 未提交长期状态时显示可理解失败；原始稳定原因保留在投影对象中。
            StringAssert.Contains("册界环境引用未生效", feedback);
            StringAssert.DoesNotContain(CharterEnvironmentProjectionReasons.NoLongTermState, feedback);
            Assert.AreEqual(
                CharterEnvironmentProjectionReasons.NoLongTermState,
                ReadProjection(adventure).Reason);
            Assert.IsNull(GameSession.Instance.CharterRuntimeState);
            Assert.AreEqual(0, GameSession.Instance.CharterDefinitionCatalogVersion);
        }

        /// <summary>读取 Adventure 控制器的只读投影对象（显示边界；原始条目/事件/档案 ID 仍保留）。</summary>
        private static CharterEnvironmentProjectionResult ReadProjection(AdventureSceneController adventure)
        {
            var field = typeof(AdventureSceneController).GetField(
                "charterEnvironmentProjection",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "The formal adventure controller must keep its environment projection.");
            var projection = field.GetValue(adventure) as CharterEnvironmentProjectionResult;
            Assert.IsNotNull(projection, "The formal adventure controller must resolve its environment projection.");
            return projection;
        }

        private static void ClickByName(string gameObjectName)
        {
            var button = GameObject.Find(gameObjectName)?.GetComponent<Button>();
            Assert.IsNotNull(button, "Production button was not found: " + gameObjectName);
            button.onClick.Invoke();
        }

        private static Text PanelText(string gameObjectName)
        {
            var text = GameObject.Find(gameObjectName)?.GetComponent<Text>();
            Assert.IsNotNull(text, "Panel text was not found: " + gameObjectName);
            return text;
        }

        private static ContentCatalogData ReadSerializedCatalog(SettlementSceneController controller)
        {
            var field = typeof(SettlementSceneController).GetField(
                "contentCatalog",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "The formal settlement controller must serialize its catalog reference.");
            var catalog = field.GetValue(controller) as ContentCatalogData;
            Assert.IsNotNull(catalog, "The formal SettlementScene must bind the production catalog asset.");
            return catalog;
        }

        private static void AssertNoNextStateOrEvents(CharterRuleInvocationResult evaluation)
        {
            Assert.IsNull(evaluation.NextState, "Conflict evaluations must never produce a NextState.");
            Assert.IsTrue(
                evaluation.EmittedEvents == null || evaluation.EmittedEvents.Length == 0,
                "Conflict evaluations must never emit world events.");
        }

        /// <summary>
        /// 逐项比较 <see cref="CharterRuntimeStateData"/> 的全部标量、字符串集合与记录内容：
        /// 读档往返和失败节点前后都必须保持完整内容相等，不允许只比较数组长度或同一对象
        /// 引用（否则等长或原地修改的错误状态会通过闸门）。
        /// </summary>
        private static void AssertCharterStateEquivalent(
            CharterRuntimeStateData expected,
            CharterRuntimeStateData actual)
        {
            Assert.AreEqual(expected.stateId, actual.stateId, "stateId");
            Assert.AreEqual(expected.charterRelicState, actual.charterRelicState, "charterRelicState");
            Assert.AreEqual(expected.worldSealState, actual.worldSealState, "worldSealState");
            AssertStringArrayEquivalent(expected.registeredRuleEntryIds, actual.registeredRuleEntryIds, "registeredRuleEntryIds");
            AssertStringArrayEquivalent(expected.currentRegionRuleEntryIds, actual.currentRegionRuleEntryIds, "currentRegionRuleEntryIds");
            AssertStringArrayEquivalent(expected.currentCoverageSet, actual.currentCoverageSet, "currentCoverageSet");
            AssertNodeStatesEquivalent(expected.nodeStates, actual.nodeStates);
            AssertAuthorizationVersionsEquivalent(
                expected.organizationAuthorizationVersions,
                actual.organizationAuthorizationVersions);
            AssertOccupancyRecordsEquivalent(
                "ruleEntryOccupancies",
                expected.ruleEntryOccupancies,
                actual.ruleEntryOccupancies);
            AssertOccupancyRecordsEquivalent(
                "nodeOccupancies",
                expected.nodeOccupancies,
                actual.nodeOccupancies);
            AssertRealitySupplyStatesEquivalent(expected.realitySupplyStates, actual.realitySupplyStates);
            AssertCommitResultsEquivalent(
                "positiveCommitResults",
                expected.positiveCommitResults,
                actual.positiveCommitResults);
            AssertCommitResultsEquivalent(
                "negativeCommitResults",
                expected.negativeCommitResults,
                actual.negativeCommitResults);
        }

        private static void AssertStringArrayEquivalent(string[] expected, string[] actual, string label)
        {
            if (expected == null || actual == null)
            {
                Assert.AreEqual(expected, actual, label + " presence");
                return;
            }
            Assert.AreEqual(expected.Length, actual.Length, label + " length");
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], actual[i], label + "[" + i + "]");
            }
        }

        /// <summary>
        /// 按序逐项比较记录数组：先比较存在性与长度，再逐条比较两个字符串字段。
        /// </summary>
        private static void AssertRecordArrayEquivalent<T>(
            T[] expected,
            T[] actual,
            string label,
            System.Action<int, T, T> compareRecord)
        {
            if (expected == null || actual == null)
            {
                Assert.AreEqual(expected, actual, label + " presence");
                return;
            }
            Assert.AreEqual(expected.Length, actual.Length, label + " length");
            for (int i = 0; i < expected.Length; i++)
            {
                compareRecord(i, expected[i], actual[i]);
            }
        }

        private static void AssertNodeStatesEquivalent(
            CharterNodeRuntimeStateData[] expected,
            CharterNodeRuntimeStateData[] actual)
        {
            AssertRecordArrayEquivalent(expected, actual, "nodeStates", (i, left, right) =>
            {
                if (left == null || right == null)
                {
                    Assert.AreEqual(left, right, "nodeStates[" + i + "] record presence");
                    return;
                }
                Assert.AreEqual(left.nodeId, right.nodeId, "nodeStates[" + i + "].nodeId");
                Assert.AreEqual(left.state, right.state, "nodeStates[" + i + "].state");
            });
        }

        private static void AssertAuthorizationVersionsEquivalent(
            CharterAuthorizationVersionStateData[] expected,
            CharterAuthorizationVersionStateData[] actual)
        {
            AssertRecordArrayEquivalent(
                expected,
                actual,
                "organizationAuthorizationVersions",
                (i, left, right) =>
                {
                    if (left == null || right == null)
                    {
                        Assert.AreEqual(left, right, "organizationAuthorizationVersions[" + i + "] record presence");
                        return;
                    }
                    Assert.AreEqual(
                        left.authorizationVersionId,
                        right.authorizationVersionId,
                        "organizationAuthorizationVersions[" + i + "].authorizationVersionId");
                    Assert.AreEqual(
                        left.state,
                        right.state,
                        "organizationAuthorizationVersions[" + i + "].state");
                });
        }

        private static void AssertOccupancyRecordsEquivalent(
            string label,
            CharterOccupancyStateData[] expected,
            CharterOccupancyStateData[] actual)
        {
            AssertRecordArrayEquivalent(expected, actual, label, (i, left, right) =>
            {
                if (left == null || right == null)
                {
                    Assert.AreEqual(left, right, label + "[" + i + "] record presence");
                    return;
                }
                Assert.AreEqual(left.resourceId, right.resourceId, label + "[" + i + "].resourceId");
                Assert.AreEqual(left.occupancyId, right.occupancyId, label + "[" + i + "].occupancyId");
            });
        }

        private static void AssertRealitySupplyStatesEquivalent(
            CharterRealitySupplyStateData[] expected,
            CharterRealitySupplyStateData[] actual)
        {
            AssertRecordArrayEquivalent(expected, actual, "realitySupplyStates", (i, left, right) =>
            {
                if (left == null || right == null)
                {
                    Assert.AreEqual(left, right, "realitySupplyStates[" + i + "] record presence");
                    return;
                }
                Assert.AreEqual(
                    left.realitySupplyId,
                    right.realitySupplyId,
                    "realitySupplyStates[" + i + "].realitySupplyId");
                Assert.AreEqual(left.state, right.state, "realitySupplyStates[" + i + "].state");
            });
        }

        private static void AssertCommitResultsEquivalent(
            string label,
            CharterCommitResultStateData[] expected,
            CharterCommitResultStateData[] actual)
        {
            AssertRecordArrayEquivalent(expected, actual, label, (i, left, right) =>
            {
                if (left == null || right == null)
                {
                    Assert.AreEqual(left, right, label + "[" + i + "] record presence");
                    return;
                }
                Assert.AreEqual(left.commitId, right.commitId, label + "[" + i + "].commitId");
                Assert.AreEqual(left.resultState, right.resultState, label + "[" + i + "].resultState");
            });
        }
    }
}
