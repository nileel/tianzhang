using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Content;
using TianZhang.Editor;
using TianZhang.World;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Tests
{
    /// <summary>
    /// Direct EditMode coverage of the pure charter rule transaction on the approved water
    /// bureau chronicle sample: passage is not management, management is not rule change,
    /// disconnected nodes, out-of-boundary coverage, atomic rejection, versioned cross-tier
    /// authorization, the charter side mapping of the shared decision (unique candidate id
    /// binding, losing side and neutral never commit), one-time reality supply consumption,
    /// jindan conflict, yuanying anchoring, and uniquely recorded declared event outputs.
    /// </summary>
    public sealed class CharterRuleRuntimeTests
    {
        private const string RuleEntryId = "charter_entry_suifu_diji";
        private const string CharterNode = "node_old_water_station_charter";
        private const string WaterworksNode = "node_old_water_station_waterworks";
        private const string RiverWetlandNode = "node_old_water_station_river_wetland";
        private const string CharterCoverage = "coverage_old_water_station_charter";
        private const string WaterworksCoverage = "coverage_old_water_station_waterworks";
        private const string RiverWetlandCoverage = "coverage_old_water_station_river_wetland";
        private const string SupplyRain = "supply_suifu_registered_seasonal_rain";
        private const string SupplyBalance = "supply_suifu_connected_water_balance";
        private const string SupplyLand = "supply_suifu_wetland_land_capacity";
        private const string BasinAuthorization = "authorization_suifu_water_basin_v1";
        private const string SealAuthorization = "authorization_taixuan_seal_old_water_station_management_v1";
        private const string PositiveCommit = "commit_suifu_diji_positive_ecology";
        private const string NegativeCommit = "commit_suifu_diji_negative_reallocation";
        private const string DeclaredGrantId = "cross_tier_charter_water_basin_v1";

        private CharterRuleDefinitionData definition;
        private CharterRuleReferenceCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            // 生产输入只来自唯一静态目录 asset 的批准目录与已导入定义，不调用 Editor factory。
            var staticCatalog = AssetDatabase.LoadAssetAtPath<CharterRuleStaticCatalogData>(
                "Assets/Data/CharterRuleStaticCatalog/CharterRuleStaticCatalog.asset");
            Assert.IsNotNull(staticCatalog, "The single approved charter static catalog asset is missing.");
            Assert.That(staticCatalog.TryValidateDefinitions(out string catalogReason), Is.True, catalogReason);
            catalog = staticCatalog.ReferenceCatalog;
            string sourceFilePath = Path.Combine(Application.dataPath, "DataConfig/CharterRuleDefinitions.csv");
            definition = ContentImportCoordinator.ParseCharterRuleDefinitions(
                File.ReadAllLines(sourceFilePath),
                sourceFilePath,
                catalog).Single();
        }

        [TearDown]
        public void TearDown()
        {
            if (definition != null)
                UnityEngine.Object.DestroyImmediate(definition);
        }

        [Test]
        public void RecognizedCharterWithOperatorAndTargetPassesPassageAndThenSealBoundary()
        {
            var succeeded = Invoke(BuildValidRequest());
            Assert.IsTrue(succeeded.Succeeded, succeeded.Reason);
            Assert.AreEqual(CharterRuleRuntimeReasons.Ok, succeeded.Reason);

            // 通行条件：《开阖九章》要求天章有效实例被识别。
            var unrecognizedRelic = BuildValidState();
            unrecognizedRelic.charterRelicState = "unrecognized";
            AssertRejected(
                CharterRuleRuntimeReasons.PassageDenied,
                CharterRuleRuntime.Invoke(definition, catalog, unrecognizedRelic, BuildValidRequest(), null));

            // 通行不等于管理：已有通行但太玄界印未认主 → 稳定拒绝且不替换状态。
            var passageOnlyState = BuildValidState();
            passageOnlyState.worldSealState = "unrecognized";
            var denied = CharterRuleRuntime.Invoke(definition, catalog, passageOnlyState, BuildValidRequest(), null);
            Assert.AreEqual(CharterRuleRuntimeReasons.SealManagementDenied, denied.Reason);
            Assert.IsNull(denied.NextState);
            Assert.IsNull(denied.EmittedEvents);
        }

        [Test]
        public void SealManagementDoesNotAuthorizeRuleApplicationWithoutTheFullAuthorizationCombination()
        {
            // 管理不等于改规则：界印管理版本被识别但缺少条目声明的流域组织授权 → 拒绝。
            var managementOnlyState = BuildValidState();
            managementOnlyState.organizationAuthorizationVersions = new[]
            {
                new CharterAuthorizationVersionStateData
                {
                    authorizationVersionId = SealAuthorization,
                    state = CharterRuleRuntime.RecognizedState,
                },
            };

            AssertRejected(
                CharterRuleRuntimeReasons.AuthorizationVersionDenied,
                CharterRuleRuntime.Invoke(definition, catalog, managementOnlyState, BuildValidRequest(), null));
        }

        [Test]
        public void DisconnectedOrUnlistedNodeRejectsWithoutReplacingState()
        {
            var disconnected = BuildValidState();
            disconnected.nodeStates[1] = new CharterNodeRuntimeStateData
            {
                nodeId = WaterworksNode,
                state = "disconnected",
            };
            AssertRejected(
                CharterRuleRuntimeReasons.NodeDisconnected,
                CharterRuleRuntime.Invoke(definition, catalog, disconnected, BuildValidRequest(), null));

            var absent = BuildValidState();
            absent.nodeStates = Array.Empty<CharterNodeRuntimeStateData>();
            AssertRejected(
                CharterRuleRuntimeReasons.NodeDisconnected,
                CharterRuleRuntime.Invoke(definition, catalog, absent, BuildValidRequest(), null));
        }

        [Test]
        public void CoverageOutsideTheConnectedWatershedRejects()
        {
            var shrunkState = BuildValidState();
            shrunkState.currentCoverageSet = new[] { CharterCoverage };
            AssertRejected(
                CharterRuleRuntimeReasons.CoverageOutOfBoundary,
                CharterRuleRuntime.Invoke(definition, catalog, shrunkState, BuildValidRequest(), null));

            var outOfBoundaryRequest = BuildValidRequest();
            outOfBoundaryRequest.coverageIds = new[] { "coverage_other_region" };
            AssertRejected(
                CharterRuleRuntimeReasons.CoverageOutOfBoundary,
                CharterRuleRuntime.Invoke(definition, catalog, BuildValidState(), outOfBoundaryRequest, null));
        }

        [Test]
        public void MissingEitherHalfOfTheAtomicCommitRejects()
        {
            var swappedNegative = BuildValidRequest();
            swappedNegative.negativeCommitId = "commit_suifu_diji_negative_reallocation_other";
            AssertRejected(
                CharterRuleRuntimeReasons.AtomicCommitIncomplete,
                CharterRuleRuntime.Invoke(definition, catalog, BuildValidState(), swappedNegative, null));

            var partialSupplies = BuildValidRequest();
            partialSupplies.realitySupplyIds = new[] { SupplyRain };
            AssertRejected(
                CharterRuleRuntimeReasons.RealitySupplyUnavailable,
                CharterRuleRuntime.Invoke(definition, catalog, BuildValidState(), partialSupplies, null));

            var unregisteredSupplies = BuildValidState();
            unregisteredSupplies.realitySupplyStates = Array.Empty<CharterRealitySupplyStateData>();
            AssertRejected(
                CharterRuleRuntimeReasons.RealitySupplyUnavailable,
                CharterRuleRuntime.Invoke(definition, catalog, unregisteredSupplies, BuildValidRequest(), null));
        }

        [Test]
        public void SuccessfulInvocationBuildsOneAtomicNextStateAndEmitsOnlyDeclaredEventReferences()
        {
            var state = BuildValidState();
            var result = CharterRuleRuntime.Invoke(definition, catalog, state, BuildValidRequest(), null);

            Assert.IsTrue(result.Succeeded, result.Reason);
            Assert.IsNotNull(result.NextState);
            CollectionAssert.Contains(result.NextState.registeredRuleEntryIds, RuleEntryId);
            CollectionAssert.Contains(result.NextState.currentRegionRuleEntryIds, RuleEntryId);
            Assert.AreEqual(1, result.NextState.ruleEntryOccupancies.Length);
            Assert.AreEqual(RuleEntryId, result.NextState.ruleEntryOccupancies[0].resourceId);
            Assert.AreEqual(3, result.NextState.nodeOccupancies.Length);
            // 三个已登记供给按稳定 ID 唯一结转为本轮 allocated，不残留旧 registered 记录。
            Assert.AreEqual(3, result.NextState.realitySupplyStates.Length);
            Assert.AreEqual(CharterRuleRuntime.AllocatedSupplyState, result.NextState.realitySupplyStates[0].state);
            Assert.AreEqual(
                3,
                result.NextState.realitySupplyStates.Count(supply => supply.state == CharterRuleRuntime.AllocatedSupplyState));
            Assert.AreEqual(
                3,
                result.NextState.realitySupplyStates.Select(supply => supply.realitySupplyId).Distinct().Count());
            Assert.AreEqual(1, result.NextState.positiveCommitResults.Length);
            Assert.AreEqual(1, result.NextState.negativeCommitResults.Length);
            Assert.IsTrue(result.NextState.TryValidate(new[] { definition }, catalog, out var stateReason), stateReason);

            // 输入状态不被替换或改写：原状态仍无任何登记记录。
            Assert.IsNull(state.registeredRuleEntryIds);
            Assert.IsNull(state.ruleEntryOccupancies);
            Assert.IsNull(state.positiveCommitResults);

            // 事件唯一记录：只输出定义已声明的 environmentProfile 引用，不反向改写环境档案。
            Assert.IsNotNull(result.EmittedEvents);
            Assert.AreEqual(definition.worldEventOutputs.Length, result.EmittedEvents.Length);
            Assert.AreEqual(
                definition.worldEventOutputs.Length,
                result.EmittedEvents.Select(evt => evt.eventId).Distinct().Count());
            foreach (var output in definition.worldEventOutputs)
            {
                Assert.AreEqual(
                    1,
                    result.EmittedEvents.Count(evt =>
                        evt.eventId == output.eventId && evt.environmentProfileId == output.environmentProfileId));
            }
        }

        [Test]
        public void JindanInterventionConstructsCompleteV1InstanceAndConsumesOnlyTheSharedDecision()
        {
            var request = BuildJindanRequest();

            // 无合法跨阶授权（无 archive）→ 拒绝覆盖，不替换状态。
            AssertRejected(
                "JD_CHALLENGE_ARCHIVE_UNAVAILABLE",
                CharterRuleRuntime.Invoke(definition, catalog, BuildValidState(), request, null));

            // 未声明的 grant → 稳定拒绝。
            var undeclaredGrant = BuildJindanRequest(grantId: "cross_tier_charter_other_basin_v1");
            AssertRejected(
                CharterRuleRuntimeReasons.UnknownConflictGrant,
                CharterRuleRuntime.Invoke(definition, catalog, BuildValidState(), undeclaredGrant, BuildEligibleArchive()));

            // 已声明但被撤销的 grant → 消费 shared 撤销理由。
            var revoked = new CrossTierChallengeArchive(new[] { BuildGrant(isRevoked: true) });
            AssertRejected(
                "JD_CHALLENGE_REVOKED",
                CharterRuleRuntime.Invoke(definition, catalog, BuildValidState(), request, revoked));

            // 请求未锁定哪一候选代表本次册界调用 → 请求无效，不进入 shared 决定。
            var undeclaredSide = BuildJindanRequest(charterCandidateId: "candidate_not_participating");
            AssertRejected(
                CharterRuleRuntimeReasons.InvalidRequest,
                CharterRuleRuntime.Invoke(definition, catalog, BuildValidState(), undeclaredSide, BuildEligibleArchive()));

            // 合法跨阶授权 → 只消费 shared 决定并原子应用。
            var authorized = CharterRuleRuntime.Invoke(definition, catalog, BuildValidState(), request, BuildEligibleArchive());
            Assert.IsTrue(authorized.Succeeded, authorized.Reason);
            Assert.IsNotNull(authorized.ConflictDecision);
            Assert.AreEqual(RuleConflictOutcome.LeftWins, authorized.ConflictDecision.Outcome);
            Assert.AreEqual("jindan_left", authorized.ConflictDecision.WinnerCandidateId);
            Assert.IsNotNull(authorized.NextState);
        }

        [Test]
        public void JindanInterventionWithoutCrossTierRequestCannotCommitRuleState()
        {
            // 版本化跨阶请求缺失时，即使 archive 可解析也无合法跨阶授权 → 拒绝覆盖，不替换状态。
            var request = BuildJindanRequest();
            request.crossTierChallengeRequest = null;

            AssertRejected(
                CharterRuleRuntimeReasons.CrossTierAuthorizationDenied,
                CharterRuleRuntime.Invoke(definition, catalog, BuildValidState(), request, BuildEligibleArchive()));
        }

        [Test]
        public void CharterSideThatLosesTheConflictDoesNotCommitRuleStateOrEvents()
        {
            // 本次册界调用声明右侧候选，shared 决定左侧获胜 → 败方不提交规则状态或事件。
            var request = BuildJindanRequest(charterCandidateId: "jindan_right");

            var result = CharterRuleRuntime.Invoke(definition, catalog, BuildValidState(), request, BuildEligibleArchive());

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(CharterRuleRuntimeReasons.ConflictNotWon, result.Reason);
            Assert.IsNotNull(result.ConflictDecision);
            Assert.AreEqual(RuleConflictOutcome.LeftWins, result.ConflictDecision.Outcome);
            Assert.AreEqual("jindan_left", result.ConflictDecision.WinnerCandidateId);
            Assert.IsNull(result.NextState);
            Assert.IsNull(result.EmittedEvents);
        }

        [Test]
        public void DuplicateCandidateIdsCannotDeclareWhichSideRepresentsTheCharterInvocation()
        {
            // 左右候选使用同一 CandidateId 时，charterCandidateId 无法唯一锁定哪一侧代表本次册界调用：
            // shared 任一侧获胜都会返回同一 WinnerCandidateId。请求无效，稳定拒绝且不进入 shared 决定。
            var request = BuildJindanRequest();
            request.rightCandidate = CreateCandidate("jindan_left", positionRank: 2);

            AssertRejected(
                CharterRuleRuntimeReasons.InvalidRequest,
                CharterRuleRuntime.Invoke(definition, catalog, BuildValidState(), request, BuildEligibleArchive()));
        }

        [Test]
        public void NeutralConflictDecisionDoesNotCommitRuleStateOrEvents()
        {
            // 同优先级同脉冲 → shared 返回中立且无赢家；本次册界调用未赢得冲突 → 不提交规则状态或事件。
            var request = BuildJindanRequest();
            request.leftCandidate = CreateCandidate("jindan_left", positionRank: 2);
            request.rightCandidate = CreateCandidate("jindan_right", positionRank: 2);

            var result = CharterRuleRuntime.Invoke(definition, catalog, BuildValidState(), request, BuildEligibleArchive());

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(CharterRuleRuntimeReasons.ConflictNotWon, result.Reason);
            Assert.IsNotNull(result.ConflictDecision);
            Assert.AreEqual(RuleConflictOutcome.Neutral, result.ConflictDecision.Outcome);
            Assert.IsNull(result.NextState);
            Assert.IsNull(result.EmittedEvents);
        }

        [Test]
        public void AllocatedRealitySupplyCannotBeConsumedAgain()
        {
            // 首次成功把三个已登记供给唯一结转为本轮 allocated，不残留旧 registered 记录。
            var request = BuildValidRequest();
            var first = CharterRuleRuntime.Invoke(definition, catalog, BuildValidState(), request, null);
            Assert.IsTrue(first.Succeeded, first.Reason);
            Assert.AreEqual(3, first.NextState.realitySupplyStates.Length);
            Assert.AreEqual(
                3,
                first.NextState.realitySupplyStates.Count(supply => supply.state == CharterRuleRuntime.AllocatedSupplyState));

            // 同一供给、正负提交与占用从结转后的状态再次调用 → 稳定拒绝，不替换状态。
            AssertRejected(
                CharterRuleRuntimeReasons.RealitySupplyUnavailable,
                CharterRuleRuntime.Invoke(definition, catalog, first.NextState, request, null));
        }

        [Test]
        public void YuanyingAnchorReturnsOnlyTheAnchoredDeterminationWithoutStateOrCommits()
        {
            var request = BuildValidRequest();
            request.hasConflictIntervention = true;
            request.conflictKind = RuleConflictKind.YuanyingAnchored;
            request.conflictEventId = "anchor_suifu_water_001";
            request.targetVariableId = "wetland_waterline_state";
            request.allowedOperationId = "charter_apply";
            request.targetId = RiverWetlandNode;
            request.scopeId = "scope_suifu_water_basin";
            request.realityAnchorId = "anchor_yuanying_road";
            request.resourceLedgerRef = "ledger_suifu_resource";
            request.capacityLedgerRef = "ledger_suifu_capacity";

            var result = CharterRuleRuntime.Invoke(definition, catalog, BuildValidState(), request, null);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual("TZ_CHARTER_CONFLICT_YUANYING_ANCHORED", result.Reason);
            Assert.IsNotNull(result.ConflictDecision);
            Assert.AreEqual(RuleConflictOutcome.Anchored, result.ConflictDecision.Outcome);
            Assert.IsFalse(result.ConflictDecision.RequiresLedgerSettlement);
            Assert.IsNull(result.NextState);
            Assert.IsNull(result.EmittedEvents);
        }

        private CharterRuleInvocationResult Invoke(CharterRuleInvocationRequest request)
        {
            return CharterRuleRuntime.Invoke(definition, catalog, BuildValidState(), request, null);
        }

        private void AssertRejected(string expectedReason, CharterRuleInvocationResult result)
        {
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(expectedReason, result.Reason);
            Assert.IsNull(result.NextState);
            Assert.IsNull(result.EmittedEvents);
        }

        private static CharterRuntimeStateData BuildValidState()
        {
            return new CharterRuntimeStateData
            {
                stateId = "charter_runtime_suifu_production",
                charterRelicState = CharterRuleRuntime.RecognizedState,
                worldSealState = CharterRuleRuntime.RecognizedState,
                nodeStates = new[]
                {
                    new CharterNodeRuntimeStateData { nodeId = CharterNode, state = CharterRuleRuntime.ConnectedNodeState },
                    new CharterNodeRuntimeStateData { nodeId = WaterworksNode, state = CharterRuleRuntime.ConnectedNodeState },
                    new CharterNodeRuntimeStateData { nodeId = RiverWetlandNode, state = CharterRuleRuntime.ConnectedNodeState },
                },
                organizationAuthorizationVersions = new[]
                {
                    new CharterAuthorizationVersionStateData
                    {
                        authorizationVersionId = BasinAuthorization,
                        state = CharterRuleRuntime.RecognizedState,
                    },
                    new CharterAuthorizationVersionStateData
                    {
                        authorizationVersionId = SealAuthorization,
                        state = CharterRuleRuntime.RecognizedState,
                    },
                },
                currentCoverageSet = new[] { CharterCoverage, WaterworksCoverage, RiverWetlandCoverage },
                realitySupplyStates = new[]
                {
                    new CharterRealitySupplyStateData { realitySupplyId = SupplyRain, state = CharterRuleRuntime.RegisteredSupplyState },
                    new CharterRealitySupplyStateData { realitySupplyId = SupplyBalance, state = CharterRuleRuntime.RegisteredSupplyState },
                    new CharterRealitySupplyStateData { realitySupplyId = SupplyLand, state = CharterRuleRuntime.RegisteredSupplyState },
                },
            };
        }

        private static CharterRuleInvocationRequest BuildValidRequest()
        {
            return new CharterRuleInvocationRequest
            {
                ruleEntryId = RuleEntryId,
                passageOperatorId = "operator_old_water_station",
                passageTargetId = "gate_old_water_station_pump",
                sealManagerId = "manager_old_water_station",
                sealBeneficiaryId = "beneficiary_water_basin",
                involvedNodeIds = new[] { CharterNode, WaterworksNode, RiverWetlandNode },
                coverageIds = new[] { CharterCoverage, WaterworksCoverage, RiverWetlandCoverage },
                realitySupplyIds = new[] { SupplyRain, SupplyBalance, SupplyLand },
                ruleEntryOccupancyId = "occupancy_suifu_diji_v1",
                nodeOccupancyId = "occupancy_suifu_waterworks_v1",
                positiveCommitId = PositiveCommit,
                negativeCommitId = NegativeCommit,
                positiveCommitResultState = "applied",
                negativeCommitResultState = "applied",
                worldTick = 100,
            };
        }

        private static CharterRuleInvocationRequest BuildJindanRequest(
            string grantId = DeclaredGrantId,
            string charterCandidateId = "jindan_left")
        {
            var request = BuildValidRequest();
            request.hasConflictIntervention = true;
            request.conflictKind = RuleConflictKind.JindanSameVariable;
            request.conflictEventId = "conflict_suifu_water_spirit_001";
            request.targetVariableId = "water_element_spirit_flow";
            request.allowedOperationId = "charter_apply";
            request.targetId = WaterworksNode;
            request.scopeId = "scope_suifu_water_basin";
            request.realityAnchorId = "anchor_suifu_waterway";
            request.resourceLedgerRef = "ledger_suifu_resource";
            request.capacityLedgerRef = "ledger_suifu_capacity";
            request.leftCandidate = CreateCandidate("jindan_left", positionRank: 3);
            request.rightCandidate = CreateCandidate("jindan_right", positionRank: 2);
            request.crossTierChallengeRequest = new CrossTierChallengeRequest(
                "challenge_suifu_001",
                grantId,
                1,
                "water_element_spirit_flow",
                "jindan_challenger",
                100);
            request.charterCandidateId = charterCandidateId;
            return request;
        }

        private static CrossTierChallengeArchive BuildEligibleArchive()
        {
            return new CrossTierChallengeArchive(new[] { BuildGrant() });
        }

        private static CrossTierChallengeGrant BuildGrant(bool isRevoked = false)
        {
            return new CrossTierChallengeGrant(
                DeclaredGrantId,
                1,
                "water_element_spirit_flow",
                "jindan_challenger",
                CrossTierChallengeSourceKind.JindanProtection,
                "charter_apply",
                WaterworksNode,
                "scope_suifu_water_basin",
                "beneficiary_water_basin",
                "anchor_suifu_waterway",
                "ledger_suifu_resource",
                "ledger_suifu_capacity",
                1,
                0,
                500,
                isRevoked,
                isRevoked ? "revoked" : string.Empty,
                "charter_fixture");
        }

        private static RuleConflictCandidate CreateCandidate(string candidateId, int positionRank)
        {
            return new RuleConflictCandidate(
                candidateId,
                "water_element_spirit_flow",
                WaterworksNode,
                true,
                true,
                positionRank,
                1,
                2,
                true,
                6,
                2,
                3);
        }
    }
}
