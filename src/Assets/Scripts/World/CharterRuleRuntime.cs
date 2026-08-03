using System;
using System.Collections.Generic;
using TianZhang.Content;

namespace TianZhang.World
{
    /// <summary>
    /// Stable rejection reasons of the pure charter rule transaction. Success is the empty
    /// reason; a conflict rejection surfaces the shared decision reason verbatim, and an
    /// anchored hit surfaces the shared anchored reason without any state replacement.
    /// </summary>
    public static class CharterRuleRuntimeReasons
    {
        public const string Ok = "";
        public const string InvalidRequest = "charter_invocation_request_invalid";
        public const string PassageDenied = "charter_passage_denied";
        public const string SealManagementDenied = "charter_seal_management_denied";
        public const string AuthorizationVersionDenied = "charter_authorization_version_denied";
        public const string NodeDisconnected = "charter_node_disconnected";
        public const string CoverageOutOfBoundary = "charter_coverage_out_of_boundary";
        public const string RealitySupplyUnavailable = "charter_reality_supply_unavailable";
        public const string AtomicCommitIncomplete = "charter_atomic_commit_incomplete";
        public const string VariableOutOfBoundary = "charter_variable_out_of_boundary";
        public const string UnknownConflictGrant = "charter_unknown_conflict_grant";
    }

    /// <summary>
    /// One explicit invocation request. It carries only stable IDs and declared result states;
    /// the runtime never defaults a missing passage, authorization, node, coverage, supply,
    /// commit, or outcome, and never copies a resolver or grant.
    /// </summary>
    public sealed class CharterRuleInvocationRequest
    {
        public string ruleEntryId;
        public string passageOperatorId;
        public string passageTargetId;
        public string sealManagerId;
        public string sealBeneficiaryId;
        public string[] involvedNodeIds;
        public string[] coverageIds;
        public string[] realitySupplyIds;
        public string ruleEntryOccupancyId;
        public string nodeOccupancyId;
        public string positiveCommitId;
        public string negativeCommitId;
        public string positiveCommitResultState;
        public string negativeCommitResultState;
        public bool hasConflictIntervention;
        public RuleConflictKind conflictKind;
        public string conflictEventId;
        public string targetVariableId;
        public string allowedOperationId;
        public string targetId;
        public string scopeId;
        public string realityAnchorId;
        public string resourceLedgerRef;
        public string capacityLedgerRef;
        public RuleConflictCandidate leftCandidate;
        public RuleConflictCandidate rightCandidate;
        public CrossTierChallengeRequest crossTierChallengeRequest;
        public int worldTick;
    }

    /// <summary>Declared event output reference; it is only an output, never a state or environment write.</summary>
    public sealed class CharterRuleEventOutput
    {
        public string eventId;
        public string environmentProfileId;
    }

    /// <summary>
    /// Result of one rule transaction. NextState is non-null only when every check passed;
    /// a rejected or anchored call never replaces state and never emits events.
    /// </summary>
    public sealed class CharterRuleInvocationResult
    {
        public bool Succeeded;
        public string Reason;
        public CharterRuntimeStateData NextState;
        public CharterRuleEventOutput[] EmittedEvents;
        public RuleConflictDecision ConflictDecision;
    }

    /// <summary>
    /// Pure rule entry of the charter model. It owns no scene, Unity object, singleton, save,
    /// or BattleSim ledger; every input is a validated definition, the reference directory,
    /// the current dynamic state, stable IDs, and one explicit invocation request. It follows
    /// the fixed transaction order (passage, seal management and beneficiary, authorization
    /// versions, node connectivity, propagation scope, reality supply and both atomic commits,
    /// then the conflict scan) and either returns a stable rejection or one atomically built
    /// next state carrying only the definition's declared event output references.
    /// </summary>
    public static class CharterRuleRuntime
    {
        public const string RecognizedState = "recognized";
        public const string ConnectedNodeState = "connected";
        public const string RegisteredSupplyState = "registered";
        public const string AllocatedSupplyState = "allocated";

        public static CharterRuleInvocationResult Invoke(
            CharterRuleDefinitionData definition,
            CharterRuleReferenceCatalog catalog,
            CharterRuntimeStateData currentState,
            CharterRuleInvocationRequest request,
            CrossTierChallengeArchive crossTierChallengeArchive)
        {
            if (!HasValidInputs(definition, catalog, currentState, request))
                return Rejected(CharterRuleRuntimeReasons.InvalidRequest);

            // 1. 《开阖九章》通行条件：天章有效实例被识别，门禁／传送阵目标与通行者明确。
            if (!string.Equals(currentState.charterRelicState, RecognizedState, StringComparison.Ordinal))
                return Rejected(CharterRuleRuntimeReasons.PassageDenied);

            // 2. 太玄界印公共设施管理／受益资格：界印被识别且管理者、受益者明确。
            if (!string.Equals(currentState.worldSealState, RecognizedState, StringComparison.Ordinal))
                return Rejected(CharterRuleRuntimeReasons.SealManagementDenied);

            // 3. 授权版本：条目声明的 requiredAuthority 组合版本必须全部被动态状态识别。
            if (!HasRecognizedAuthorizationVersions(definition, catalog, currentState))
                return Rejected(CharterRuleRuntimeReasons.AuthorizationVersionDenied);

            // 4. 节点连通：实际锚点必须参与调用，涉及节点必须已登记且保持连通。
            if (!HasConnectedInvolvedNodes(definition, catalog, currentState, request))
                return Rejected(CharterRuleRuntimeReasons.NodeDisconnected);

            // 5. 传播范围：覆盖必须落在条目传播边界的允许集合且未越出当前已接通覆盖。
            if (!HasInBoundaryCoverage(definition, catalog, currentState, request))
                return Rejected(CharterRuleRuntimeReasons.CoverageOutOfBoundary);

            // 6. 完整正负提交：正负提交必须与定义一致且都声明现实供给。
            if (!HasCompleteAtomicCommits(definition, catalog, request))
                return Rejected(CharterRuleRuntimeReasons.AtomicCommitIncomplete);

            // 7. 现实供给：本次供给必须已登记、可落地，并覆盖两个提交声明的全部供给。
            if (!HasRegisteredRealitySupplies(catalog, currentState, request))
                return Rejected(CharterRuleRuntimeReasons.RealitySupplyUnavailable);

            // 8. 冲突扫描：只构造完整 v1 实例并消费 shared 决定；元婴受锚只返回受锚结果。
            RuleConflictDecision conflictDecision = null;
            if (request.hasConflictIntervention)
            {
                if (!Contains(definition.affectedWorldVariables, request.targetVariableId))
                    return Rejected(CharterRuleRuntimeReasons.VariableOutOfBoundary);
                if (!HasDeclaredConflictGrant(definition, catalog, request))
                    return Rejected(CharterRuleRuntimeReasons.UnknownConflictGrant);

                conflictDecision = BuildConflictInstance(request).Decide(crossTierChallengeArchive);
                if (conflictDecision.Outcome == RuleConflictOutcome.Rejected)
                    return Rejected(conflictDecision.Reason, conflictDecision);
                if (conflictDecision.Outcome == RuleConflictOutcome.Anchored)
                    return Anchored(conflictDecision);
            }

            // 9. 全部通过后一次性构造新的动态状态，事件只输出定义已声明的引用。
            return Succeed(definition, currentState, request, conflictDecision);
        }

        private static bool HasValidInputs(
            CharterRuleDefinitionData definition,
            CharterRuleReferenceCatalog catalog,
            CharterRuntimeStateData currentState,
            CharterRuleInvocationRequest request)
        {
            if (definition == null || catalog == null || !catalog.HasDeclaredAuthority ||
                currentState == null || request == null ||
                string.IsNullOrWhiteSpace(currentState.stateId) ||
                string.IsNullOrWhiteSpace(currentState.charterRelicState) ||
                string.IsNullOrWhiteSpace(currentState.worldSealState))
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(request.ruleEntryId) ||
                !string.Equals(request.ruleEntryId, definition.ruleEntryId, StringComparison.Ordinal) ||
                !catalog.ContainsRuleEntry(definition.ruleEntryId))
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(request.passageOperatorId) ||
                string.IsNullOrWhiteSpace(request.passageTargetId) ||
                string.IsNullOrWhiteSpace(request.sealManagerId) ||
                string.IsNullOrWhiteSpace(request.sealBeneficiaryId) ||
                string.IsNullOrWhiteSpace(request.ruleEntryOccupancyId) ||
                string.IsNullOrWhiteSpace(request.nodeOccupancyId) ||
                string.IsNullOrWhiteSpace(request.positiveCommitId) ||
                string.IsNullOrWhiteSpace(request.negativeCommitId) ||
                string.IsNullOrWhiteSpace(request.positiveCommitResultState) ||
                string.IsNullOrWhiteSpace(request.negativeCommitResultState) ||
                request.worldTick < 0)
            {
                return false;
            }
            if (!HasNonEmptyIds(request.involvedNodeIds) ||
                !HasNonEmptyIds(request.coverageIds) ||
                !HasNonEmptyIds(request.realitySupplyIds))
            {
                return false;
            }
            if (request.hasConflictIntervention)
            {
                if (!IsKnownConflictKind(request.conflictKind) ||
                    string.IsNullOrWhiteSpace(request.conflictEventId) ||
                    string.IsNullOrWhiteSpace(request.targetVariableId) ||
                    string.IsNullOrWhiteSpace(request.allowedOperationId) ||
                    string.IsNullOrWhiteSpace(request.targetId) ||
                    string.IsNullOrWhiteSpace(request.scopeId) ||
                    string.IsNullOrWhiteSpace(request.realityAnchorId) ||
                    string.IsNullOrWhiteSpace(request.resourceLedgerRef) ||
                    string.IsNullOrWhiteSpace(request.capacityLedgerRef))
                {
                    return false;
                }
                if (request.conflictKind == RuleConflictKind.JindanSameVariable &&
                    (request.leftCandidate == null || request.rightCandidate == null))
                {
                    return false;
                }
                if (request.conflictKind == RuleConflictKind.YuanyingAnchored &&
                    (request.leftCandidate != null || request.rightCandidate != null ||
                     request.crossTierChallengeRequest != null))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasRecognizedAuthorizationVersions(
            CharterRuleDefinitionData definition,
            CharterRuleReferenceCatalog catalog,
            CharterRuntimeStateData currentState)
        {
            CharterAuthorityRequirement authority = catalog.FindAuthority(definition.requiredAuthority);
            if (authority == null || authority.organizationAuthorizationVersionIds == null)
                return false;
            foreach (string requiredVersion in authority.organizationAuthorizationVersionIds)
            {
                if (string.IsNullOrWhiteSpace(requiredVersion) ||
                    !HasRecognizedAuthorizationVersion(currentState, requiredVersion))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasRecognizedAuthorizationVersion(CharterRuntimeStateData state, string versionId)
        {
            foreach (var record in state.organizationAuthorizationVersions ?? Array.Empty<CharterAuthorizationVersionStateData>())
            {
                if (record != null &&
                    string.Equals(record.authorizationVersionId, versionId, StringComparison.Ordinal) &&
                    string.Equals(record.state, RecognizedState, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasConnectedInvolvedNodes(
            CharterRuleDefinitionData definition,
            CharterRuleReferenceCatalog catalog,
            CharterRuntimeStateData currentState,
            CharterRuleInvocationRequest request)
        {
            foreach (string anchor in definition.anchorNodeIds ?? Array.Empty<string>())
            {
                if (!Contains(request.involvedNodeIds, anchor))
                    return false;
            }
            foreach (string nodeId in request.involvedNodeIds)
            {
                if (!catalog.ContainsNode(nodeId) || !IsConnectedNode(currentState, nodeId))
                    return false;
            }
            return true;
        }

        private static bool IsConnectedNode(CharterRuntimeStateData state, string nodeId)
        {
            foreach (var record in state.nodeStates ?? Array.Empty<CharterNodeRuntimeStateData>())
            {
                if (record != null &&
                    string.Equals(record.nodeId, nodeId, StringComparison.Ordinal) &&
                    string.Equals(record.state, ConnectedNodeState, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasInBoundaryCoverage(
            CharterRuleDefinitionData definition,
            CharterRuleReferenceCatalog catalog,
            CharterRuntimeStateData currentState,
            CharterRuleInvocationRequest request)
        {
            CharterPropagationBoundaryReference boundary =
                catalog.FindPropagationBoundary(definition.propagationBoundaryProfileId);
            if (boundary == null || boundary.allowedCoverageIds == null)
                return false;
            foreach (string coverageId in request.coverageIds)
            {
                if (string.IsNullOrWhiteSpace(coverageId) ||
                    !Contains(boundary.allowedCoverageIds, coverageId) ||
                    !Contains(currentState.currentCoverageSet, coverageId))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasCompleteAtomicCommits(
            CharterRuleDefinitionData definition,
            CharterRuleReferenceCatalog catalog,
            CharterRuleInvocationRequest request)
        {
            if (!string.Equals(request.positiveCommitId, definition.positiveCommit, StringComparison.Ordinal) ||
                !string.Equals(request.negativeCommitId, definition.negativeCommit, StringComparison.Ordinal))
            {
                return false;
            }
            CharterCommitReference positive = catalog.FindCommit(definition.positiveCommit);
            CharterCommitReference negative = catalog.FindCommit(definition.negativeCommit);
            return positive != null && negative != null &&
                   HasNonEmptyIds(positive.realitySupplyIds) &&
                   HasNonEmptyIds(negative.realitySupplyIds);
        }

        private static bool HasRegisteredRealitySupplies(
            CharterRuleReferenceCatalog catalog,
            CharterRuntimeStateData currentState,
            CharterRuleInvocationRequest request)
        {
            foreach (string supplyId in request.realitySupplyIds)
            {
                if (!catalog.ContainsRealitySupply(supplyId) || !IsRegisteredSupply(currentState, supplyId))
                    return false;
            }
            foreach (string commitId in new[] { request.positiveCommitId, request.negativeCommitId })
            {
                CharterCommitReference commit = catalog.FindCommit(commitId);
                if (commit == null)
                    return false;
                foreach (string supplyId in commit.realitySupplyIds ?? Array.Empty<string>())
                {
                    if (!Contains(request.realitySupplyIds, supplyId))
                        return false;
                }
            }
            return true;
        }

        private static bool IsRegisteredSupply(CharterRuntimeStateData state, string supplyId)
        {
            foreach (var record in state.realitySupplyStates ?? Array.Empty<CharterRealitySupplyStateData>())
            {
                if (record != null &&
                    string.Equals(record.realitySupplyId, supplyId, StringComparison.Ordinal) &&
                    string.Equals(record.state, RegisteredSupplyState, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasDeclaredConflictGrant(
            CharterRuleDefinitionData definition,
            CharterRuleReferenceCatalog catalog,
            CharterRuleInvocationRequest request)
        {
            if (request.crossTierChallengeRequest == null)
                return true;
            CharterConflictReference conflict = catalog.FindConflict(definition.conflictProfileId);
            if (conflict == null || conflict.crossTierChallengeGrantIds == null)
                return false;
            return Contains(conflict.crossTierChallengeGrantIds, request.crossTierChallengeRequest.GrantId);
        }

        private static RuleConflictInstance BuildConflictInstance(CharterRuleInvocationRequest request)
        {
            bool isJindan = request.conflictKind == RuleConflictKind.JindanSameVariable;
            return new RuleConflictInstance(
                RuleConflictInstance.ContractVersionV1,
                request.conflictEventId,
                request.conflictKind,
                request.ruleEntryId,
                request.targetVariableId,
                request.allowedOperationId,
                request.targetId,
                request.scopeId,
                request.sealBeneficiaryId,
                request.realityAnchorId,
                request.resourceLedgerRef,
                request.capacityLedgerRef,
                request.worldTick,
                isJindan ? request.leftCandidate : null,
                isJindan ? request.rightCandidate : null,
                isJindan ? request.crossTierChallengeRequest : null);
        }

        private static CharterRuleInvocationResult Succeed(
            CharterRuleDefinitionData definition,
            CharterRuntimeStateData currentState,
            CharterRuleInvocationRequest request,
            RuleConflictDecision conflictDecision)
        {
            CharterRuntimeStateData next = currentState.CreateCopy();
            next.registeredRuleEntryIds = AppendUnique(next.registeredRuleEntryIds, definition.ruleEntryId);
            next.currentRegionRuleEntryIds = AppendUnique(next.currentRegionRuleEntryIds, definition.ruleEntryId);
            next.ruleEntryOccupancies = Append(next.ruleEntryOccupancies, new CharterOccupancyStateData
            {
                resourceId = definition.ruleEntryId,
                occupancyId = request.ruleEntryOccupancyId,
            });
            foreach (string nodeId in request.involvedNodeIds)
            {
                next.nodeOccupancies = Append(next.nodeOccupancies, new CharterOccupancyStateData
                {
                    resourceId = nodeId,
                    occupancyId = request.nodeOccupancyId,
                });
            }
            foreach (string supplyId in request.realitySupplyIds)
            {
                next.realitySupplyStates = Append(next.realitySupplyStates, new CharterRealitySupplyStateData
                {
                    realitySupplyId = supplyId,
                    state = AllocatedSupplyState,
                });
            }
            next.positiveCommitResults = Append(next.positiveCommitResults, new CharterCommitResultStateData
            {
                commitId = request.positiveCommitId,
                resultState = request.positiveCommitResultState,
            });
            next.negativeCommitResults = Append(next.negativeCommitResults, new CharterCommitResultStateData
            {
                commitId = request.negativeCommitId,
                resultState = request.negativeCommitResultState,
            });

            return new CharterRuleInvocationResult
            {
                Succeeded = true,
                Reason = CharterRuleRuntimeReasons.Ok,
                NextState = next,
                EmittedEvents = BuildEmittedEvents(definition),
                ConflictDecision = conflictDecision,
            };
        }

        private static CharterRuleEventOutput[] BuildEmittedEvents(CharterRuleDefinitionData definition)
        {
            var events = new List<CharterRuleEventOutput>();
            foreach (CharterWorldEventOutputData output in definition.worldEventOutputs ?? Array.Empty<CharterWorldEventOutputData>())
            {
                events.Add(new CharterRuleEventOutput
                {
                    eventId = output.eventId,
                    environmentProfileId = output.environmentProfileId,
                });
            }
            return events.ToArray();
        }

        private static CharterRuleInvocationResult Rejected(string reason)
        {
            return new CharterRuleInvocationResult { Succeeded = false, Reason = reason };
        }

        private static CharterRuleInvocationResult Rejected(string reason, RuleConflictDecision decision)
        {
            return new CharterRuleInvocationResult { Succeeded = false, Reason = reason, ConflictDecision = decision };
        }

        private static CharterRuleInvocationResult Anchored(RuleConflictDecision decision)
        {
            return new CharterRuleInvocationResult { Succeeded = false, Reason = decision.Reason, ConflictDecision = decision };
        }

        private static bool IsKnownConflictKind(RuleConflictKind kind)
        {
            return kind == RuleConflictKind.JindanSameVariable || kind == RuleConflictKind.YuanyingAnchored;
        }

        private static bool Contains(string[] values, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || values == null)
                return false;
            foreach (string candidate in values)
            {
                if (string.Equals(candidate, value, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool HasNonEmptyIds(string[] values)
        {
            if (values == null || values.Length == 0)
                return false;
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return false;
            }
            return true;
        }

        private static T[] Append<T>(T[] values, T value)
        {
            var list = new List<T>(values ?? Array.Empty<T>());
            list.Add(value);
            return list.ToArray();
        }

        private static string[] AppendUnique(string[] values, string value)
        {
            var list = new List<string>(values ?? Array.Empty<string>());
            if (list.Contains(value))
                return list.ToArray();
            list.Add(value);
            return list.ToArray();
        }
    }
}
