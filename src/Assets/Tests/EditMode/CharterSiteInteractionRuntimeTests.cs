using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TianZhang.Content;
using TianZhang.World;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Tests
{
    /// <summary>
    /// Direct EditMode coverage of the scene-independent charter site interaction bridge on the
    /// approved old water station: bootstrap from an unaccessed Charter use-case state,
    /// the five proofs in fixed order, every out-of-order, static-identity mismatch and duplicate-ID
    /// rejection, the complete candidate mapping with empty long-term result fields, the three request
    /// shapes derived without shared mutable instances, the stable jindan loss and yuanying anchor, and
    /// the one-time formal consumption at rule level.
    /// </summary>
    public sealed class CharterSiteInteractionRuntimeTests
    {
        private const string SettlementId = "guanzhong_city";
        private const string CapabilityId = "capability_kaihe_jiuzhang_v1";
        private const string OperatorId = "operator_old_water_station";
        private const string TargetId = "gate_old_water_station_pump";
        private const string ManagerId = "manager_old_water_station";
        private const string BeneficiaryId = "beneficiary_water_basin";
        private const string RuleEntryId = "charter_entry_suifu_diji";
        private const string CharterRelicId = "relic_world_charter";
        private const string BasinAuthorization = "authorization_suifu_water_basin_v1";
        private const string SealAuthorization = "authorization_taixuan_seal_old_water_station_management_v1";
        private const string CharterNode = "node_old_water_station_charter";
        private const string WaterworksNode = "node_old_water_station_waterworks";
        private const string RiverWetlandNode = "node_old_water_station_river_wetland";
        private const string CharterCoverage = "coverage_old_water_station_charter";
        private const string WaterworksCoverage = "coverage_old_water_station_waterworks";
        private const string RiverWetlandCoverage = "coverage_old_water_station_river_wetland";
        private const string SupplyRain = "supply_suifu_registered_seasonal_rain";
        private const string SupplyBalance = "supply_suifu_connected_water_balance";
        private const string SupplyLand = "supply_suifu_wetland_land_capacity";

        private static readonly string[] AllNodes = { CharterNode, WaterworksNode, RiverWetlandNode };
        private static readonly string[] AllAuthorizations = { BasinAuthorization, SealAuthorization };
        private static readonly string[] AllSupplies = { SupplyRain, SupplyBalance, SupplyLand };
        private static readonly string[] AllCoverages = { CharterCoverage, WaterworksCoverage, RiverWetlandCoverage };

        private readonly List<UnityEngine.Object> temporaryAssets = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object asset in temporaryAssets)
                UnityEngine.Object.DestroyImmediate(asset);
        }

        [Test]
        public void BootstrapFromEmptyCharterStateBuildsCompleteValidCandidate()
        {
            var useCase = new CharterUseCase(new CharterStore());
            Assert.IsNull(useCase.CurrentState);
            Assert.AreEqual(0, useCase.DefinitionCatalogVersion);

            CharterSiteData site = LoadProductionSite();
            CharterRuleStaticCatalogData staticCatalog = LoadProductionStaticCatalog();
            Assert.That(CharterSiteInteractionRuntime.TryCreate(
                site, staticCatalog, SettlementId, out CharterSiteInteractionRuntime runtime, out string createReason),
                Is.True, createReason);

            CompleteAllSteps(runtime);
            Assert.That(runtime.Progress.IsComplete, Is.True);

            Assert.That(runtime.TryCreatePreparation(out CharterInvocationPreparation preparation, out string reason),
                Is.True, reason);
            Assert.AreSame(site, preparation.Site);
            Assert.AreEqual(staticCatalog.DefinitionCatalogVersion, preparation.CatalogVersion);
            Assert.IsNotNull(preparation.ChallengeArchive);

            CharterRuntimeStateData candidate = preparation.CandidateState;
            Assert.AreEqual("charter_runtime_charter_site_old_water_station", candidate.stateId);
            Assert.AreEqual(CharterRuleRuntime.RecognizedState, candidate.charterRelicState);
            Assert.AreEqual(CharterRuleRuntime.RecognizedState, candidate.worldSealState);
            Assert.AreEqual(AllNodes.Length, candidate.nodeStates.Length);
            Assert.IsTrue(candidate.nodeStates.All(node =>
                node.state == CharterRuleRuntime.ConnectedNodeState &&
                AllNodes.Contains(node.nodeId)));
            Assert.AreEqual(AllAuthorizations.Length, candidate.organizationAuthorizationVersions.Length);
            Assert.IsTrue(candidate.organizationAuthorizationVersions.All(authorization =>
                authorization.state == CharterRuleRuntime.RecognizedState &&
                AllAuthorizations.Contains(authorization.authorizationVersionId)));
            Assert.AreEqual(AllCoverages.Length, candidate.currentCoverageSet.Length);
            Assert.IsTrue(candidate.currentCoverageSet.All(AllCoverages.Contains));
            Assert.AreEqual(AllSupplies.Length, candidate.realitySupplyStates.Length);
            Assert.IsTrue(candidate.realitySupplyStates.All(supply =>
                supply.state == CharterRuleRuntime.RegisteredSupplyState &&
                AllSupplies.Contains(supply.realitySupplyId)));

            // 长期结果字段在调用前为空：只有成功事务才追加。
            Assert.IsNull(candidate.registeredRuleEntryIds);
            Assert.IsNull(candidate.currentRegionRuleEntryIds);
            Assert.IsNull(candidate.ruleEntryOccupancies);
            Assert.IsNull(candidate.nodeOccupancies);
            Assert.IsNull(candidate.positiveCommitResults);
            Assert.IsNull(candidate.negativeCommitResults);

            Assert.That(candidate.TryValidate(staticCatalog.Definitions, staticCatalog.ReferenceCatalog, out string stateReason),
                Is.True, stateReason);

            // candidate 不是第二套长期状态：会话仍保持未接入。
            Assert.IsNull(useCase.CurrentState);
            Assert.AreEqual(0, useCase.DefinitionCatalogVersion);
        }

        [Test]
        public void FiveStepsAdvanceOnlyTheirOwnProofInFixedOrder()
        {
            CharterSiteInteractionRuntime runtime = CreateRuntime(LoadProductionSite());

            Assert.IsFalse(runtime.Progress.PassageVerified);
            AssertOk(runtime.VerifyPassage(CapabilityId, OperatorId, TargetId));
            Assert.IsTrue(runtime.Progress.PassageVerified);
            Assert.AreEqual(OperatorId, runtime.Progress.PassageOperatorId);
            Assert.AreEqual(TargetId, runtime.Progress.PassageTargetId);
            Assert.IsFalse(runtime.Progress.ManagementVerified);

            AssertOk(runtime.VerifyManagement(ManagerId, BeneficiaryId));
            Assert.IsTrue(runtime.Progress.ManagementVerified);
            Assert.IsNull(runtime.Progress.ConnectedNodeIds);

            AssertOk(runtime.ConnectNodes(AllNodes));
            Assert.IsTrue(runtime.Progress.ConnectedNodeIds.SequenceEqual(AllNodes));
            Assert.IsFalse(runtime.Progress.RuleEntryRegistrationVerified);

            AssertOk(runtime.VerifyRuleEntryRegistration(RuleEntryId, CharterRelicId, AllAuthorizations));
            Assert.IsTrue(runtime.Progress.RuleEntryRegistrationVerified);
            Assert.IsNull(runtime.Progress.RegisteredRealitySupplyIds);

            AssertOk(runtime.PrepareRealitySupplies(AllSupplies));
            Assert.IsTrue(runtime.Progress.RegisteredRealitySupplyIds.SequenceEqual(AllSupplies));
        }

        [Test]
        public void EveryOutOfOrderActionRejectedWithoutAdvancing()
        {
            CharterSiteInteractionRuntime runtime = CreateRuntime(LoadProductionSite());

            AssertRejected(CharterSiteInteractionReasons.ActionOutOfOrder,
                runtime.VerifyManagement(ManagerId, BeneficiaryId));
            AssertRejected(CharterSiteInteractionReasons.ActionOutOfOrder,
                runtime.ConnectNodes(AllNodes));
            AssertRejected(CharterSiteInteractionReasons.ActionOutOfOrder,
                runtime.VerifyRuleEntryRegistration(RuleEntryId, CharterRelicId, AllAuthorizations));
            AssertRejected(CharterSiteInteractionReasons.ActionOutOfOrder,
                runtime.PrepareRealitySupplies(AllSupplies));
            Assert.IsFalse(runtime.Progress.IsComplete);
            Assert.IsFalse(runtime.Progress.ManagementVerified);

            AssertOk(runtime.VerifyPassage(CapabilityId, OperatorId, TargetId));
            AssertRejected(CharterSiteInteractionReasons.ActionOutOfOrder,
                runtime.ConnectNodes(AllNodes));
            AssertRejected(CharterSiteInteractionReasons.ActionOutOfOrder,
                runtime.VerifyRuleEntryRegistration(RuleEntryId, CharterRelicId, AllAuthorizations));
            AssertRejected(CharterSiteInteractionReasons.ActionOutOfOrder,
                runtime.PrepareRealitySupplies(AllSupplies));

            AssertOk(runtime.VerifyManagement(ManagerId, BeneficiaryId));
            AssertRejected(CharterSiteInteractionReasons.ActionOutOfOrder,
                runtime.VerifyRuleEntryRegistration(RuleEntryId, CharterRelicId, AllAuthorizations));
            AssertRejected(CharterSiteInteractionReasons.ActionOutOfOrder,
                runtime.PrepareRealitySupplies(AllSupplies));

            AssertOk(runtime.ConnectNodes(AllNodes));
            AssertRejected(CharterSiteInteractionReasons.ActionOutOfOrder,
                runtime.PrepareRealitySupplies(AllSupplies));

            // 任意越序后正确步骤仍只推进自身证明。
            AssertOk(runtime.VerifyRuleEntryRegistration(RuleEntryId, CharterRelicId, AllAuthorizations));
            AssertOk(runtime.PrepareRealitySupplies(AllSupplies));
            Assert.IsTrue(runtime.Progress.IsComplete);
        }

        [Test]
        public void StaticIdentityMismatchActionsRejectedWithStableReasons()
        {
            CharterSiteInteractionRuntime runtime = CreateRuntime(LoadProductionSite());

            AssertRejected(CharterSiteInteractionReasons.PassageMismatch,
                runtime.VerifyPassage("capability_other", OperatorId, TargetId));
            AssertRejected(CharterSiteInteractionReasons.PassageMismatch,
                runtime.VerifyPassage(CapabilityId, "operator_other", TargetId));
            AssertRejected(CharterSiteInteractionReasons.PassageMismatch,
                runtime.VerifyPassage(CapabilityId, OperatorId, "gate_other"));
            AssertRejected(CharterSiteInteractionReasons.InvalidInput,
                runtime.VerifyPassage(null, OperatorId, TargetId));
            Assert.IsFalse(runtime.Progress.PassageVerified);

            AssertOk(runtime.VerifyPassage(CapabilityId, OperatorId, TargetId));
            AssertRejected(CharterSiteInteractionReasons.ManagementMismatch,
                runtime.VerifyManagement("manager_other", BeneficiaryId));
            AssertRejected(CharterSiteInteractionReasons.ManagementMismatch,
                runtime.VerifyManagement(ManagerId, "beneficiary_other"));
            Assert.IsFalse(runtime.Progress.ManagementVerified);

            AssertOk(runtime.VerifyManagement(ManagerId, BeneficiaryId));
            AssertRejected(CharterSiteInteractionReasons.NodeUnknown,
                runtime.ConnectNodes(new[] { CharterNode, WaterworksNode, "node_unknown" }));
            AssertRejected(CharterSiteInteractionReasons.NodeUnknown,
                runtime.ConnectNodes(new[] { CharterNode, WaterworksNode, RiverWetlandNode, "node_extra" }));
            AssertRejected(CharterSiteInteractionReasons.NodeSetMismatch,
                runtime.ConnectNodes(new[] { CharterNode, WaterworksNode }));
            Assert.IsNull(runtime.Progress.ConnectedNodeIds);

            AssertOk(runtime.ConnectNodes(AllNodes));
            AssertRejected(CharterSiteInteractionReasons.EntryMismatch,
                runtime.VerifyRuleEntryRegistration("charter_entry_other", CharterRelicId, AllAuthorizations));
            AssertRejected(CharterSiteInteractionReasons.RelicMismatch,
                runtime.VerifyRuleEntryRegistration(RuleEntryId, "relic_other", AllAuthorizations));
            AssertRejected(CharterSiteInteractionReasons.AuthorizationMismatch,
                runtime.VerifyRuleEntryRegistration(RuleEntryId, CharterRelicId, new[] { BasinAuthorization }));
            AssertRejected(CharterSiteInteractionReasons.AuthorizationMismatch,
                runtime.VerifyRuleEntryRegistration(RuleEntryId, CharterRelicId,
                    new[] { BasinAuthorization, SealAuthorization, "authorization_unknown" }));
            Assert.IsFalse(runtime.Progress.RuleEntryRegistrationVerified);

            AssertOk(runtime.VerifyRuleEntryRegistration(RuleEntryId, CharterRelicId, AllAuthorizations));
            AssertRejected(CharterSiteInteractionReasons.SupplyUnknown,
                runtime.PrepareRealitySupplies(new[] { SupplyRain, SupplyBalance, "supply_unknown" }));
            AssertRejected(CharterSiteInteractionReasons.SupplySetMismatch,
                runtime.PrepareRealitySupplies(new[] { SupplyRain, SupplyBalance }));
            Assert.IsNull(runtime.Progress.RegisteredRealitySupplyIds);

            AssertOk(runtime.PrepareRealitySupplies(AllSupplies));
            Assert.IsTrue(runtime.Progress.IsComplete);
        }

        [Test]
        public void UnusableGateOrWrongSettlementOrMissingCatalogFailsClosed()
        {
            // 门禁声明不可操作：结构损坏 → 停在本步，不推进。
            CharterSiteData damagedGate = CreateFixtureSite(structureState: "damaged");
            temporaryAssets.Add(damagedGate);
            CharterSiteInteractionRuntime damagedRuntime = CreateRuntime(damagedGate);
            AssertRejected(CharterSiteInteractionReasons.PassageUnavailable,
                damagedRuntime.VerifyPassage(CapabilityId, OperatorId, TargetId));
            Assert.IsFalse(damagedRuntime.Progress.PassageVerified);

            // 站点不属于当前据点 → 不创建交互。
            CharterSiteData otherSettlement = CreateFixtureSite(settlementId: "other_city");
            temporaryAssets.Add(otherSettlement);
            Assert.That(CharterSiteInteractionRuntime.TryCreate(
                otherSettlement, LoadProductionStaticCatalog(), SettlementId,
                out CharterSiteInteractionRuntime ignored, out string settlementReason), Is.False);
            Assert.AreEqual(CharterSiteInteractionReasons.SiteNotCurrentSettlement, settlementReason);

            // 站点缺失或静态目录缺失 → 失败关闭。
            Assert.That(CharterSiteInteractionRuntime.TryCreate(
                null, LoadProductionStaticCatalog(), SettlementId,
                out ignored, out string siteReason), Is.False);
            Assert.AreEqual(CharterSiteInteractionReasons.SiteUnavailable, siteReason);
            Assert.That(CharterSiteInteractionRuntime.TryCreate(
                LoadProductionSite(), null, SettlementId,
                out ignored, out string catalogReason), Is.False);
            Assert.AreEqual(CharterSiteInteractionReasons.CatalogUnavailable, catalogReason);
        }

        [Test]
        public void MissingAnyProofCannotConstructPreparation()
        {
            CharterSiteInteractionRuntime runtime = CreateRuntime(LoadProductionSite());

            AssertPreparationIncomplete(runtime);

            AssertOk(runtime.VerifyPassage(CapabilityId, OperatorId, TargetId));
            AssertPreparationIncomplete(runtime);

            AssertOk(runtime.VerifyManagement(ManagerId, BeneficiaryId));
            AssertPreparationIncomplete(runtime);

            AssertOk(runtime.ConnectNodes(AllNodes));
            AssertPreparationIncomplete(runtime);

            AssertOk(runtime.VerifyRuleEntryRegistration(RuleEntryId, CharterRelicId, AllAuthorizations));
            AssertPreparationIncomplete(runtime);
        }

        [Test]
        public void ThreeEvaluationsDeriveDistinctRequestsFromTheSamePreparation()
        {
            CharterSiteInteractionRuntime runtime = CreateRuntime(LoadProductionSite());
            CompleteAllSteps(runtime);
            Assert.That(runtime.TryCreatePreparation(out CharterInvocationPreparation preparation, out string reason),
                Is.True, reason);

            // 金丹：同一 shared 决定稳定返回册界侧未获胜；NextState 与事件输出为空。
            CharterRuleInvocationResult jindan = runtime.EvaluateJindan(preparation, 100, "applied", "applied");
            Assert.IsFalse(jindan.Succeeded);
            Assert.AreEqual(CharterRuleRuntimeReasons.ConflictNotWon, jindan.Reason);
            Assert.IsNotNull(jindan.ConflictDecision);
            Assert.AreEqual(RuleConflictOutcome.LeftWins, jindan.ConflictDecision.Outcome);
            Assert.AreEqual("jindan_left", jindan.ConflictDecision.WinnerCandidateId);
            Assert.IsNull(jindan.NextState);
            Assert.IsNull(jindan.EmittedEvents);

            // 元婴：只受锚，不夹带金丹候选或请求，不降格金丹冲突。
            CharterRuleInvocationResult yuanying = runtime.EvaluateYuanying(preparation, 100, "applied", "applied");
            Assert.IsFalse(yuanying.Succeeded);
            Assert.AreEqual("TZ_CHARTER_CONFLICT_YUANYING_ANCHORED", yuanying.Reason);
            Assert.IsNotNull(yuanying.ConflictDecision);
            Assert.AreEqual(RuleConflictOutcome.Anchored, yuanying.ConflictDecision.Outcome);
            Assert.IsNull(yuanying.NextState);
            Assert.IsNull(yuanying.EmittedEvents);

            // 正式：不带冲突介入，只提交一次完整 NextState。
            CharterRuleInvocationResult formal = runtime.EvaluateFormal(preparation, null, 100, "applied", "applied");
            Assert.IsTrue(formal.Succeeded, formal.Reason);
            Assert.AreEqual(CharterRuleRuntimeReasons.Ok, formal.Reason);
            Assert.IsNull(formal.ConflictDecision);
            Assert.IsNotNull(formal.NextState);
            Assert.That(formal.NextState.TryValidate(
                LoadProductionStaticCatalog().Definitions,
                LoadProductionStaticCatalog().ReferenceCatalog,
                out string stateReason), Is.True, stateReason);
            CollectionAssert.Contains(formal.NextState.registeredRuleEntryIds, RuleEntryId);
            Assert.IsNotNull(formal.EmittedEvents);
            Assert.AreEqual(2, formal.EmittedEvents.Length);
            Assert.AreEqual("env_guanzhong_wild", formal.EmittedEvents[0].environmentProfileId);

            // 三份请求互不共享可变实例：同一 preparation 的两次正式调用返回独立 NextState。
            CharterRuleInvocationResult formalAgain = runtime.EvaluateFormal(preparation, null, 100, "applied", "applied");
            Assert.IsTrue(formalAgain.Succeeded, formalAgain.Reason);
            Assert.AreNotSame(formal.NextState, formalAgain.NextState);

            // 空结果状态声明 → 请求无效，失败关闭。
            CharterRuleInvocationResult emptyOutcomes = runtime.EvaluateFormal(preparation, null, 100, "", "");
            Assert.IsFalse(emptyOutcomes.Succeeded);
            Assert.AreEqual(CharterRuleRuntimeReasons.InvalidRequest, emptyOutcomes.Reason);
            Assert.IsNull(emptyOutcomes.NextState);
        }

        [Test]
        public void FirstFormalSuccessThenRepeatedConsumptionRejectedAtRuleLevel()
        {
            CharterSiteInteractionRuntime runtime = CreateRuntime(LoadProductionSite());
            CompleteAllSteps(runtime);
            Assert.That(runtime.TryCreatePreparation(out CharterInvocationPreparation preparation, out string reason),
                Is.True, reason);

            CharterRuleInvocationResult first = runtime.EvaluateFormal(preparation, null, 100, "applied", "applied");
            Assert.IsTrue(first.Succeeded, first.Reason);
            Assert.AreEqual(
                3,
                first.NextState.realitySupplyStates.Count(supply => supply.state == CharterRuleRuntime.AllocatedSupplyState));

            // 已有长期状态必须继续消费当前状态：同一候选不再自举，allocated 供给拒绝重复消费。
            CharterRuleInvocationResult second = runtime.EvaluateFormal(preparation, first.NextState, 100, "applied", "applied");
            Assert.IsFalse(second.Succeeded);
            Assert.AreEqual(CharterRuleRuntimeReasons.RealitySupplyUnavailable, second.Reason);
            Assert.IsNull(second.NextState);
            Assert.IsNull(second.EmittedEvents);
        }

        [Test]
        public void DuplicateIdsInAnyActionSetRejectedWithoutAdvancing()
        {
            CharterSiteInteractionRuntime runtime = CreateRuntime(LoadProductionSite());
            AssertOk(runtime.VerifyPassage(CapabilityId, OperatorId, TargetId));
            AssertOk(runtime.VerifyManagement(ManagerId, BeneficiaryId));

            // 重复节点：左侧重复 ID 只补齐长度、不能补齐缺失项，未完整集合不得误判为通过。
            AssertRejected(CharterSiteInteractionReasons.NodeSetMismatch,
                runtime.ConnectNodes(new[] { CharterNode, CharterNode, WaterworksNode }));
            Assert.IsNull(runtime.Progress.ConnectedNodeIds);
            Assert.IsFalse(runtime.Progress.RuleEntryRegistrationVerified);

            AssertOk(runtime.ConnectNodes(AllNodes));

            // 重复授权版本：集合仍不完整（缺水面授权），但重复 ID 可把长度补到与右侧一致。
            AssertRejected(CharterSiteInteractionReasons.AuthorizationMismatch,
                runtime.VerifyRuleEntryRegistration(RuleEntryId, CharterRelicId,
                    new[] { SealAuthorization, SealAuthorization }));
            Assert.IsFalse(runtime.Progress.RuleEntryRegistrationVerified);

            AssertOk(runtime.VerifyRuleEntryRegistration(RuleEntryId, CharterRelicId, AllAuthorizations));

            // 重复供给：集合仍不完整（缺湿地承载），但重复 ID 可把长度补到与声明并集一致。
            AssertRejected(CharterSiteInteractionReasons.SupplySetMismatch,
                runtime.PrepareRealitySupplies(new[] { SupplyRain, SupplyRain, SupplyBalance }));
            Assert.IsNull(runtime.Progress.RegisteredRealitySupplyIds);

            // 任何重复拒绝后，正确完整集合仍只推进自身证明。
            AssertOk(runtime.PrepareRealitySupplies(AllSupplies));
            Assert.IsTrue(runtime.Progress.IsComplete);
        }

        private void CompleteAllSteps(CharterSiteInteractionRuntime runtime)
        {
            AssertOk(runtime.VerifyPassage(CapabilityId, OperatorId, TargetId));
            AssertOk(runtime.VerifyManagement(ManagerId, BeneficiaryId));
            AssertOk(runtime.ConnectNodes(AllNodes));
            AssertOk(runtime.VerifyRuleEntryRegistration(RuleEntryId, CharterRelicId, AllAuthorizations));
            AssertOk(runtime.PrepareRealitySupplies(AllSupplies));
        }

        private void AssertPreparationIncomplete(CharterSiteInteractionRuntime runtime)
        {
            Assert.That(runtime.TryCreatePreparation(out CharterInvocationPreparation preparation, out string reason),
                Is.False);
            Assert.AreEqual(CharterSiteInteractionReasons.PreparationIncomplete, reason);
            Assert.IsNull(preparation);
        }

        private static void AssertOk(CharterInteractionActionResult result)
        {
            Assert.IsTrue(result.Succeeded, result.Reason);
            Assert.AreEqual(CharterSiteInteractionReasons.Ok, result.Reason);
        }

        private static void AssertRejected(string expectedReason, CharterInteractionActionResult result)
        {
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(expectedReason, result.Reason);
        }

        private static CharterSiteInteractionRuntime CreateRuntime(CharterSiteData site)
        {
            Assert.That(CharterSiteInteractionRuntime.TryCreate(
                site, LoadProductionStaticCatalog(), SettlementId, out CharterSiteInteractionRuntime runtime, out string reason),
                Is.True, reason);
            return runtime;
        }

        private static CharterSiteData LoadProductionSite()
        {
            var site = AssetDatabase.LoadAssetAtPath<CharterSiteData>(
                "Assets/Data/CharterSites/CharterSite_charter_site_old_water_station.asset");
            Assert.IsNotNull(site, "The single approved charter site asset is missing.");
            return site;
        }

        private static CharterRuleStaticCatalogData LoadProductionStaticCatalog()
        {
            var staticCatalog = AssetDatabase.LoadAssetAtPath<CharterRuleStaticCatalogData>(
                "Assets/Data/CharterRuleStaticCatalog/CharterRuleStaticCatalog.asset");
            Assert.IsNotNull(staticCatalog, "The single approved charter static catalog asset is missing.");
            return staticCatalog;
        }

        private static CharterSiteData CreateFixtureSite(
            string settlementId = SettlementId,
            string structureState = "intact")
        {
            var site = ScriptableObject.CreateInstance<CharterSiteData>();
            site.siteId = "charter_site_fixture";
            site.settlementId = settlementId;
            site.ruleEntryId = RuleEntryId;
            site.passageCapabilityId = CapabilityId;
            site.passageOperatorId = OperatorId;
            site.passageTargetId = TargetId;
            site.passageProtocolState = "compatible";
            site.passageStructureState = structureState;
            site.passagePowerState = "available";
            site.recognitionTiming = "instant";
            site.operationTiming = "sustained_guided";
            site.cancellationPolicy = "no_commit_on_cancel";
            return site;
        }
    }
}
