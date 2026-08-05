using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Content;

namespace TianZhang.World
{
    /// <summary>
    /// Stable rejection reasons of the scene-independent charter site interaction bridge.
    /// Success is the empty reason; every step only advances the temporary progress when
    /// the static site identity matches the actual action result, and no step ever writes
    /// long-term state.
    /// </summary>
    public static class CharterSiteInteractionReasons
    {
        public const string Ok = "";
        public const string InvalidInput = "charter_interaction_input_invalid";
        public const string SiteUnavailable = "charter_interaction_site_unavailable";
        public const string SiteNotCurrentSettlement = "charter_interaction_site_not_current_settlement";
        public const string CatalogUnavailable = "charter_interaction_catalog_unavailable";
        public const string DefinitionMissing = "charter_interaction_definition_missing";
        public const string ActionOutOfOrder = "charter_interaction_action_out_of_order";
        public const string PassageUnavailable = "charter_interaction_passage_unavailable";
        public const string PassageMismatch = "charter_interaction_passage_mismatch";
        public const string ManagementMismatch = "charter_interaction_management_mismatch";
        public const string SealDeclarationUnresolved = "charter_interaction_seal_declaration_unresolved";
        public const string NodeUnknown = "charter_interaction_node_unknown";
        public const string NodeSetMismatch = "charter_interaction_node_set_mismatch";
        public const string EntryMismatch = "charter_interaction_entry_mismatch";
        public const string RelicMismatch = "charter_interaction_relic_mismatch";
        public const string AuthorizationMismatch = "charter_interaction_authorization_mismatch";
        public const string SupplyUnknown = "charter_interaction_supply_unknown";
        public const string SupplySetMismatch = "charter_interaction_supply_set_mismatch";
        public const string PreparationIncomplete = "charter_interaction_preparation_incomplete";
        public const string GrantInvalid = "charter_interaction_grant_invalid";
        public const string CandidateInvalid = "charter_interaction_candidate_invalid";
    }

    /// <summary>Result of one temporary interaction action; it never carries long-term state.</summary>
    public sealed class CharterInteractionActionResult
    {
        public bool Succeeded;
        public string Reason;

        public static CharterInteractionActionResult OkResult()
        {
            return new CharterInteractionActionResult { Succeeded = true, Reason = CharterSiteInteractionReasons.Ok };
        }

        public static CharterInteractionActionResult Rejected(string reason)
        {
            return new CharterInteractionActionResult { Succeeded = false, Reason = reason };
        }
    }

    /// <summary>
    /// Short-lived proof of this site opening. It records only what the five action steps
    /// actually verified; it is never saved and never assigned to the session. Leaving the
    /// site discards it together with the runtime instance.
    /// </summary>
    public sealed class CharterSiteInteractionProgress
    {
        public bool PassageVerified;
        /// <summary>Only the operator verified by the passage action; always equals the site declaration.</summary>
        public string PassageOperatorId;
        /// <summary>Only the target verified by the passage action; always equals the site declaration.</summary>
        public string PassageTargetId;
        public bool ManagementVerified;
        public string[] ConnectedNodeIds;
        public bool RuleEntryRegistrationVerified;
        public string[] RegisteredRealitySupplyIds;

        /// <summary>All five proofs present; only then can a preparation be constructed.</summary>
        public bool IsComplete =>
            PassageVerified &&
            ManagementVerified &&
            ConnectedNodeIds != null && ConnectedNodeIds.Length > 0 &&
            RuleEntryRegistrationVerified &&
            RegisteredRealitySupplyIds != null && RegisteredRealitySupplyIds.Length > 0;

        public CharterSiteInteractionProgress CreateCopy()
        {
            return new CharterSiteInteractionProgress
            {
                PassageVerified = PassageVerified,
                PassageOperatorId = PassageOperatorId,
                PassageTargetId = PassageTargetId,
                ManagementVerified = ManagementVerified,
                ConnectedNodeIds = CopyStrings(ConnectedNodeIds),
                RuleEntryRegistrationVerified = RuleEntryRegistrationVerified,
                RegisteredRealitySupplyIds = CopyStrings(RegisteredRealitySupplyIds),
            };
        }

        private static string[] CopyStrings(string[] values)
        {
            if (values == null)
                return null;
            var copy = new string[values.Length];
            Array.Copy(values, copy, values.Length);
            return copy;
        }
    }

    /// <summary>
    /// The fixed, non-persistable candidate produced by the complete interaction. It fixes the
    /// candidate dynamic state, the site static reference, the temporary cross-tier challenge
    /// archive and the static catalog version, and never carries a single mutable invocation
    /// request. It is not a second long-term state: it is never saved and never assigned to
    /// <c>GameSession.CharterRuntimeState</c>.
    /// </summary>
    public sealed class CharterInvocationPreparation
    {
        internal CharterInvocationPreparation(
            CharterRuntimeStateData candidateState,
            CharterSiteData site,
            CrossTierChallengeArchive challengeArchive,
            int catalogVersion)
        {
            CandidateState = candidateState;
            Site = site;
            ChallengeArchive = challengeArchive;
            CatalogVersion = catalogVersion;
        }

        public CharterRuntimeStateData CandidateState { get; }
        public CharterSiteData Site { get; }
        public CrossTierChallengeArchive ChallengeArchive { get; }
        public int CatalogVersion { get; }
    }

    /// <summary>
    /// Scene-independent producer of the temporary interaction proofs and of the fixed
    /// <see cref="CharterInvocationPreparation"/>. It owns only this opening's short-lived
    /// <see cref="CharterSiteInteractionProgress"/>; candidate and progress are not a second
    /// long-term state and cannot be persisted. The five proofs follow the fixed order:
    /// passage, seal management and beneficiary, node connection, rule entry registration with
    /// charter instance and authorization versions, and reality supply preparation. Each action
    /// validates the site static identity against the actual action result and only advances its
    /// own proof. The three evaluations derive their own <see cref="CharterRuleInvocationRequest"/>
    /// from the same preparation without sharing any mutable instance.
    /// </summary>
    public sealed class CharterSiteInteractionRuntime
    {
        public const string CompatibleProtocolState = "compatible";
        public const string IntactStructureState = "intact";
        public const string AvailablePowerState = "available";
        public const string InstantRecognitionTiming = "instant";
        public const string SustainedGuidedOperationTiming = "sustained_guided";
        public const string NoCommitOnCancelPolicy = "no_commit_on_cancel";

        private readonly CharterSiteData site;
        private readonly CharterRuleStaticCatalogData staticCatalog;
        private readonly CharterRuleReferenceCatalog catalog;
        private readonly CharterRuleDefinitionData definition;
        private readonly string stateId;
        private readonly CharterSiteInteractionProgress progress = new CharterSiteInteractionProgress();

        private CharterSiteInteractionRuntime(
            CharterSiteData site,
            CharterRuleStaticCatalogData staticCatalog,
            CharterRuleReferenceCatalog catalog,
            CharterRuleDefinitionData definition,
            string stateId)
        {
            this.site = site;
            this.staticCatalog = staticCatalog;
            this.catalog = catalog;
            this.definition = definition;
            this.stateId = stateId;
        }

        /// <summary>
        /// Fail-closed entry to the bridge: the site must exist and belong to the current
        /// settlement, the single static directory must validate, and the site's rule entry must
        /// resolve inside that directory. No Editor factory, fixture or default substitutes.
        /// </summary>
        public static bool TryCreate(
            CharterSiteData site,
            CharterRuleStaticCatalogData staticCatalog,
            string currentSettlementId,
            out CharterSiteInteractionRuntime runtime,
            out string reason)
        {
            if (site == null)
            {
                runtime = null;
                reason = CharterSiteInteractionReasons.SiteUnavailable;
                return false;
            }
            if (string.IsNullOrWhiteSpace(currentSettlementId) ||
                !string.Equals(site.settlementId, currentSettlementId, StringComparison.Ordinal))
            {
                runtime = null;
                reason = CharterSiteInteractionReasons.SiteNotCurrentSettlement;
                return false;
            }
            if (staticCatalog == null || !staticCatalog.TryValidateDefinitions(out string catalogReason))
            {
                runtime = null;
                reason = CharterSiteInteractionReasons.CatalogUnavailable;
                return false;
            }
            CharterRuleDefinitionData definition = FindDefinition(staticCatalog, site.ruleEntryId);
            if (definition == null)
            {
                runtime = null;
                reason = CharterSiteInteractionReasons.DefinitionMissing;
                return false;
            }

            runtime = new CharterSiteInteractionRuntime(
                site,
                staticCatalog,
                staticCatalog.ReferenceCatalog,
                definition,
                BuildStateId(site));
            reason = CharterSiteInteractionReasons.Ok;
            return true;
        }

        public CharterSiteInteractionProgress Progress => progress.CreateCopy();

        /// <summary>
        /// Step 1: recognize and open the declared gate with the 《开阖九章》capability. The action
        /// must use exactly the site-declared capability, operator and target, and the declared
        /// protocol, structure, power and interaction-time states must all be operable.
        /// </summary>
        public CharterInteractionActionResult VerifyPassage(string capabilityId, string operatorId, string targetId)
        {
            if (string.IsNullOrWhiteSpace(capabilityId) ||
                string.IsNullOrWhiteSpace(operatorId) ||
                string.IsNullOrWhiteSpace(targetId))
            {
                return CharterInteractionActionResult.Rejected(CharterSiteInteractionReasons.InvalidInput);
            }
            if (!HasOperableGate())
                return CharterInteractionActionResult.Rejected(CharterSiteInteractionReasons.PassageUnavailable);
            if (!string.Equals(capabilityId, site.passageCapabilityId, StringComparison.Ordinal) ||
                !string.Equals(operatorId, site.passageOperatorId, StringComparison.Ordinal) ||
                !string.Equals(targetId, site.passageTargetId, StringComparison.Ordinal))
            {
                return CharterInteractionActionResult.Rejected(CharterSiteInteractionReasons.PassageMismatch);
            }

            progress.PassageVerified = true;
            progress.PassageOperatorId = operatorId;
            progress.PassageTargetId = targetId;
            return CharterInteractionActionResult.OkResult();
        }

        /// <summary>
        /// Step 2: confirm the seal management and beneficiary with the declared facility role.
        /// Passage never grants management; the manager and beneficiary must equal the site
        /// declaration and the seal relic and authorization version must resolve in the catalog.
        /// </summary>
        public CharterInteractionActionResult VerifyManagement(string sealManagerId, string sealBeneficiaryId)
        {
            if (string.IsNullOrWhiteSpace(sealManagerId) || string.IsNullOrWhiteSpace(sealBeneficiaryId))
                return CharterInteractionActionResult.Rejected(CharterSiteInteractionReasons.InvalidInput);
            if (!progress.PassageVerified)
                return CharterInteractionActionResult.Rejected(CharterSiteInteractionReasons.ActionOutOfOrder);
            if (!catalog.ContainsRelic(site.sealRelicId) ||
                !catalog.ContainsOrganizationAuthorizationVersion(site.sealAuthorizationVersionId))
            {
                return CharterInteractionActionResult.Rejected(CharterSiteInteractionReasons.SealDeclarationUnresolved);
            }
            if (!string.Equals(sealManagerId, site.sealManagerId, StringComparison.Ordinal) ||
                !string.Equals(sealBeneficiaryId, site.sealBeneficiaryId, StringComparison.Ordinal))
            {
                return CharterInteractionActionResult.Rejected(CharterSiteInteractionReasons.ManagementMismatch);
            }

            progress.ManagementVerified = true;
            return CharterInteractionActionResult.OkResult();
        }

        /// <summary>
        /// Step 3: connect the charter, waterworks and river/wetland nodes. Every connected node
        /// must exist in the catalog and the set must exactly equal the definition anchor set;
        /// a missing, unknown or out-of-boundary node never advances.
        /// </summary>
        public CharterInteractionActionResult ConnectNodes(string[] nodeIds)
        {
            if (!HasNonEmptyIds(nodeIds))
                return CharterInteractionActionResult.Rejected(CharterSiteInteractionReasons.InvalidInput);
            if (!progress.ManagementVerified)
                return CharterInteractionActionResult.Rejected(CharterSiteInteractionReasons.ActionOutOfOrder);
            foreach (string nodeId in nodeIds)
            {
                if (!catalog.ContainsNode(nodeId))
                    return CharterInteractionActionResult.Rejected(CharterSiteInteractionReasons.NodeUnknown);
            }
            if (!SameIdSet(nodeIds, definition.anchorNodeIds))
                return CharterInteractionActionResult.Rejected(CharterSiteInteractionReasons.NodeSetMismatch);

            progress.ConnectedNodeIds = CopyStrings(nodeIds);
            return CharterInteractionActionResult.OkResult();
        }

        /// <summary>
        /// Step 4: register the rule entry, confirm the charter instance and the authorization
        /// version combination. The entry must equal the site declaration, the charter relic must
        /// be the authority relic, and the authorization versions must exactly equal the full
        /// authority combination including the site-declared seal authorization version.
        /// </summary>
        public CharterInteractionActionResult VerifyRuleEntryRegistration(
            string ruleEntryId,
            string charterRelicId,
            string[] authorizationVersionIds)
        {
            if (string.IsNullOrWhiteSpace(ruleEntryId) ||
                string.IsNullOrWhiteSpace(charterRelicId) ||
                !HasNonEmptyIds(authorizationVersionIds))
            {
                return CharterInteractionActionResult.Rejected(CharterSiteInteractionReasons.InvalidInput);
            }
            if (progress.ConnectedNodeIds == null || progress.ConnectedNodeIds.Length == 0)
                return CharterInteractionActionResult.Rejected(CharterSiteInteractionReasons.ActionOutOfOrder);
            if (!string.Equals(ruleEntryId, site.ruleEntryId, StringComparison.Ordinal) ||
                !catalog.ContainsRuleEntry(ruleEntryId))
            {
                return CharterInteractionActionResult.Rejected(CharterSiteInteractionReasons.EntryMismatch);
            }

            CharterAuthorityRequirement authority = catalog.FindAuthority(definition.requiredAuthority);
            if (authority == null || !string.Equals(charterRelicId, authority.relicId, StringComparison.Ordinal) ||
                !catalog.ContainsRelic(charterRelicId))
            {
                return CharterInteractionActionResult.Rejected(CharterSiteInteractionReasons.RelicMismatch);
            }
            if (!SameIdSet(authorizationVersionIds, authority.organizationAuthorizationVersionIds) ||
                !ContainsId(authorizationVersionIds, site.sealAuthorizationVersionId))
            {
                return CharterInteractionActionResult.Rejected(CharterSiteInteractionReasons.AuthorizationMismatch);
            }

            progress.RuleEntryRegistrationVerified = true;
            return CharterInteractionActionResult.OkResult();
        }

        /// <summary>
        /// Step 5: prepare the three existing reality supplies. Every supply must exist in the
        /// catalog and the set must exactly cover the supplies declared by both atomic commits.
        /// </summary>
        public CharterInteractionActionResult PrepareRealitySupplies(string[] realitySupplyIds)
        {
            if (!HasNonEmptyIds(realitySupplyIds))
                return CharterInteractionActionResult.Rejected(CharterSiteInteractionReasons.InvalidInput);
            if (!progress.RuleEntryRegistrationVerified)
                return CharterInteractionActionResult.Rejected(CharterSiteInteractionReasons.ActionOutOfOrder);
            foreach (string supplyId in realitySupplyIds)
            {
                if (!catalog.ContainsRealitySupply(supplyId))
                    return CharterInteractionActionResult.Rejected(CharterSiteInteractionReasons.SupplyUnknown);
            }
            string[] declaredSupplies = ResolveDeclaredSupplies(definition, catalog);
            if (!SameIdSet(realitySupplyIds, declaredSupplies))
                return CharterInteractionActionResult.Rejected(CharterSiteInteractionReasons.SupplySetMismatch);

            progress.RegisteredRealitySupplyIds = CopyStrings(realitySupplyIds);
            return CharterInteractionActionResult.OkResult();
        }

        /// <summary>
        /// Builds the single fixed candidate from the complete progress. It fails closed when any
        /// proof is missing, when the site grant cannot form the temporary archive, or when the
        /// candidate does not pass the existing static/dynamic validation. The candidate never
        /// leaves this opening's lifecycle and is never assigned to the session.
        /// </summary>
        public bool TryCreatePreparation(
            out CharterInvocationPreparation preparation,
            out string reason)
        {
            if (!progress.IsComplete)
            {
                preparation = null;
                reason = CharterSiteInteractionReasons.PreparationIncomplete;
                return false;
            }

            CrossTierChallengeArchive archive = BuildChallengeArchive(site);
            if (archive == null)
            {
                preparation = null;
                reason = CharterSiteInteractionReasons.GrantInvalid;
                return false;
            }

            CharterRuntimeStateData candidate = BuildCandidateState(progress, definition, catalog, stateId);
            if (!candidate.TryValidate(staticCatalog.Definitions, catalog, out string stateReason))
            {
                preparation = null;
                reason = CharterSiteInteractionReasons.CandidateInvalid;
                return false;
            }

            preparation = new CharterInvocationPreparation(
                candidate,
                site,
                archive,
                staticCatalog.DefinitionCatalogVersion);
            reason = CharterSiteInteractionReasons.Ok;
            return true;
        }

        /// <summary>
        /// Jindan evaluation: derives its own request with the complete left/right candidates and
        /// the versioned cross-tier challenge request from the same preparation. The shared
        /// decision deterministically returns <c>charter_conflict_not_won</c> for this site; the
        /// result never carries a NextState or events and never commits.
        /// </summary>
        public CharterRuleInvocationResult EvaluateJindan(
            CharterInvocationPreparation preparation,
            int worldTick,
            string positiveCommitResultState,
            string negativeCommitResultState)
        {
            if (preparation == null || !HasNonEmptyResultStates(positiveCommitResultState, negativeCommitResultState))
                return InvalidRequestResult();
            CharterRuleInvocationRequest request = BuildBaseRequest(
                preparation, worldTick, positiveCommitResultState, negativeCommitResultState);
            request.hasConflictIntervention = true;
            request.conflictKind = RuleConflictKind.JindanSameVariable;
            request.conflictEventId = site.jindanConflictEventId;
            request.targetVariableId = site.jindanGrant.targetVariableId;
            request.allowedOperationId = site.jindanGrant.allowedOperationId;
            request.targetId = site.jindanGrant.targetId;
            request.scopeId = site.jindanGrant.scopeId;
            request.realityAnchorId = site.jindanGrant.realityAnchorId;
            request.resourceLedgerRef = site.jindanGrant.resourceLedgerRef;
            request.capacityLedgerRef = site.jindanGrant.capacityLedgerRef;
            request.leftCandidate = BuildCandidate(site.jindanGrant, site.leftCandidate);
            request.rightCandidate = BuildCandidate(site.jindanGrant, site.rightCandidate);
            request.crossTierChallengeRequest = new CrossTierChallengeRequest(
                site.jindanChallengeEventId,
                site.jindanGrant.grantId,
                site.jindanGrant.definitionVersion,
                site.jindanGrant.targetVariableId,
                site.jindanGrant.challengerId,
                worldTick);
            request.charterCandidateId = site.charterCandidateId;
            return CharterRuleRuntime.Invoke(definition, catalog, preparation.CandidateState, request, preparation.ChallengeArchive);
        }

        /// <summary>
        /// Yuanying evaluation: derives its own anchored request from the same preparation. It only
        /// anchors the declared target and never carries jindan candidates, a challenge request or a
        /// charter candidate; the result is the stable anchored reason without state or events.
        /// </summary>
        public CharterRuleInvocationResult EvaluateYuanying(
            CharterInvocationPreparation preparation,
            int worldTick,
            string positiveCommitResultState,
            string negativeCommitResultState)
        {
            if (preparation == null || !HasNonEmptyResultStates(positiveCommitResultState, negativeCommitResultState))
                return InvalidRequestResult();
            CharterRuleInvocationRequest request = BuildBaseRequest(
                preparation, worldTick, positiveCommitResultState, negativeCommitResultState);
            request.hasConflictIntervention = true;
            request.conflictKind = RuleConflictKind.YuanyingAnchored;
            request.conflictEventId = site.yuanyingConflictEventId;
            request.targetVariableId = site.yuanyingTargetVariableId;
            request.allowedOperationId = site.jindanGrant.allowedOperationId;
            request.targetId = site.yuanyingTargetId;
            request.scopeId = site.yuanyingScopeId;
            request.realityAnchorId = site.yuanyingRealityAnchorId;
            request.resourceLedgerRef = site.jindanGrant.resourceLedgerRef;
            request.capacityLedgerRef = site.jindanGrant.capacityLedgerRef;
            return CharterRuleRuntime.Invoke(definition, catalog, preparation.CandidateState, request, null);
        }

        /// <summary>
        /// Formal evaluation: derives its own non-conflict request from the same preparation.
        /// The first invocation bootstraps from the candidate; once a long-term state exists the
        /// caller must pass it so allocated supplies and recorded commits keep rejecting repeated
        /// consumption — a fresh registered candidate never bypasses it.
        /// </summary>
        public CharterRuleInvocationResult EvaluateFormal(
            CharterInvocationPreparation preparation,
            CharterRuntimeStateData currentState,
            int worldTick,
            string positiveCommitResultState,
            string negativeCommitResultState)
        {
            if (preparation == null || !HasNonEmptyResultStates(positiveCommitResultState, negativeCommitResultState))
                return InvalidRequestResult();
            CharterRuleInvocationRequest request = BuildBaseRequest(
                preparation, worldTick, positiveCommitResultState, negativeCommitResultState);
            return CharterRuleRuntime.Invoke(
                definition,
                catalog,
                currentState ?? preparation.CandidateState,
                request,
                null);
        }

        private CharterRuleInvocationRequest BuildBaseRequest(
            CharterInvocationPreparation preparation,
            int worldTick,
            string positiveCommitResultState,
            string negativeCommitResultState)
        {
            // 请求只从固定 preparation 派生：站点静态身份、candidate 的实际节点／覆盖／供给；
            // 同一 operator／target 已由通行动作证明等于站点声明，不携带可变请求或第二输入源。
            return new CharterRuleInvocationRequest
            {
                ruleEntryId = definition.ruleEntryId,
                passageOperatorId = preparation.Site.passageOperatorId,
                passageTargetId = preparation.Site.passageTargetId,
                sealManagerId = preparation.Site.sealManagerId,
                sealBeneficiaryId = preparation.Site.sealBeneficiaryId,
                involvedNodeIds = CopyNodeIds(preparation.CandidateState.nodeStates),
                coverageIds = CopyStrings(preparation.CandidateState.currentCoverageSet),
                realitySupplyIds = CopySupplyIds(preparation.CandidateState.realitySupplyStates),
                ruleEntryOccupancyId = preparation.Site.ruleEntryOccupancyId,
                nodeOccupancyId = preparation.Site.nodeOccupancyId,
                positiveCommitId = definition.positiveCommit,
                negativeCommitId = definition.negativeCommit,
                positiveCommitResultState = positiveCommitResultState,
                negativeCommitResultState = negativeCommitResultState,
                worldTick = worldTick,
            };
        }

        private static CharterRuleInvocationResult InvalidRequestResult()
        {
            return new CharterRuleInvocationResult
            {
                Succeeded = false,
                Reason = CharterRuleRuntimeReasons.InvalidRequest,
            };
        }

        private static bool HasNonEmptyResultStates(string positiveCommitResultState, string negativeCommitResultState)
        {
            return !string.IsNullOrWhiteSpace(positiveCommitResultState) &&
                   !string.IsNullOrWhiteSpace(negativeCommitResultState);
        }

        private bool HasOperableGate()
        {
            return string.Equals(site.passageProtocolState, CompatibleProtocolState, StringComparison.Ordinal) &&
                   string.Equals(site.passageStructureState, IntactStructureState, StringComparison.Ordinal) &&
                   string.Equals(site.passagePowerState, AvailablePowerState, StringComparison.Ordinal) &&
                   string.Equals(site.recognitionTiming, InstantRecognitionTiming, StringComparison.Ordinal) &&
                   string.Equals(site.operationTiming, SustainedGuidedOperationTiming, StringComparison.Ordinal) &&
                   string.Equals(site.cancellationPolicy, NoCommitOnCancelPolicy, StringComparison.Ordinal);
        }

        private static CharterRuntimeStateData BuildCandidateState(
            CharterSiteInteractionProgress progress,
            CharterRuleDefinitionData definition,
            CharterRuleReferenceCatalog catalog,
            string stateId)
        {
            CharterAuthorityRequirement authority = catalog.FindAuthority(definition.requiredAuthority);
            return new CharterRuntimeStateData
            {
                stateId = stateId,
                charterRelicState = CharterRuleRuntime.RecognizedState,
                worldSealState = CharterRuleRuntime.RecognizedState,
                nodeStates = progress.ConnectedNodeIds
                    .Select(nodeId => new CharterNodeRuntimeStateData
                    {
                        nodeId = nodeId,
                        state = CharterRuleRuntime.ConnectedNodeState,
                    })
                    .ToArray(),
                organizationAuthorizationVersions = authority.organizationAuthorizationVersionIds
                    .Select(versionId => new CharterAuthorizationVersionStateData
                    {
                        authorizationVersionId = versionId,
                        state = CharterRuleRuntime.RecognizedState,
                    })
                    .ToArray(),
                currentCoverageSet = ResolveCoverage(definition, catalog),
                realitySupplyStates = progress.RegisteredRealitySupplyIds
                    .Select(supplyId => new CharterRealitySupplyStateData
                    {
                        realitySupplyId = supplyId,
                        state = CharterRuleRuntime.RegisteredSupplyState,
                    })
                    .ToArray(),
            };
        }

        /// <summary>
        /// Coverage only ever takes the intersection of the definition's current coverage and the
        /// propagation boundary's allowed set; nothing outside the boundary is invented.
        /// </summary>
        private static string[] ResolveCoverage(
            CharterRuleDefinitionData definition,
            CharterRuleReferenceCatalog catalog)
        {
            CharterPropagationBoundaryReference boundary =
                catalog.FindPropagationBoundary(definition.propagationBoundaryProfileId);
            if (boundary == null || boundary.allowedCoverageIds == null)
                return Array.Empty<string>();
            var allowed = new HashSet<string>(boundary.allowedCoverageIds, StringComparer.Ordinal);
            var resolved = new List<string>();
            foreach (string coverageId in definition.currentCoverageSet ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(coverageId) && allowed.Contains(coverageId))
                    resolved.Add(coverageId);
            }
            return resolved.ToArray();
        }

        private static string[] ResolveDeclaredSupplies(
            CharterRuleDefinitionData definition,
            CharterRuleReferenceCatalog catalog)
        {
            var declared = new List<string>();
            foreach (string commitId in new[] { definition.positiveCommit, definition.negativeCommit })
            {
                CharterCommitReference commit = catalog.FindCommit(commitId);
                if (commit == null || commit.realitySupplyIds == null)
                    continue;
                foreach (string supplyId in commit.realitySupplyIds)
                {
                    if (!string.IsNullOrWhiteSpace(supplyId) && !declared.Contains(supplyId))
                        declared.Add(supplyId);
                }
            }
            return declared.ToArray();
        }

        private static CrossTierChallengeArchive BuildChallengeArchive(CharterSiteData site)
        {
            CharterSiteCrossTierChallengeGrantData data = site.jindanGrant;
            if (data == null ||
                !Enum.TryParse(data.qualificationSource, out CrossTierChallengeSourceKind qualificationSource))
            {
                return null;
            }
            try
            {
                return new CrossTierChallengeArchive(new[]
                {
                    new CrossTierChallengeGrant(
                        data.grantId,
                        data.definitionVersion,
                        data.targetVariableId,
                        data.challengerId,
                        qualificationSource,
                        data.allowedOperationId,
                        data.targetId,
                        data.scopeId,
                        data.beneficiaryId,
                        data.realityAnchorId,
                        data.resourceLedgerRef,
                        data.capacityLedgerRef,
                        data.challengeRuleTier,
                        data.effectiveAtTick,
                        data.expiresAtTick,
                        data.isRevoked,
                        data.revocationReason,
                        data.displaySource),
                });
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static RuleConflictCandidate BuildCandidate(
            CharterSiteCrossTierChallengeGrantData grant,
            CharterSiteRuleConflictCandidateData data)
        {
            if (data == null)
                return null;
            return new RuleConflictCandidate(
                data.candidateId,
                data.targetVariableId,
                data.targetId,
                data.hasVariableAuthority,
                data.hasLegalTarget,
                data.positionRank,
                data.realityAnchorRank,
                data.alreadyPaidCost,
                data.hasActiveContinuousCarrier,
                data.conflictReserve,
                data.pulseCost,
                data.settlementCooldown);
        }

        private static string BuildStateId(CharterSiteData site)
        {
            return "charter_runtime_" + site.siteId;
        }

        private static CharterRuleDefinitionData FindDefinition(
            CharterRuleStaticCatalogData staticCatalog,
            string ruleEntryId)
        {
            foreach (CharterRuleDefinitionData definition in staticCatalog.Definitions ?? Array.Empty<CharterRuleDefinitionData>())
            {
                if (definition != null &&
                    string.Equals(definition.ruleEntryId, ruleEntryId, StringComparison.Ordinal))
                {
                    return definition;
                }
            }
            return null;
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

        private static bool SameIdSet(string[] left, string[] right)
        {
            if (left == null || right == null)
                return false;
            var rightIds = new HashSet<string>(right, StringComparer.Ordinal);
            if (rightIds.Count != left.Length)
                return false;
            foreach (string id in left)
            {
                if (string.IsNullOrWhiteSpace(id) || !rightIds.Contains(id))
                    return false;
            }
            return true;
        }

        private static bool ContainsId(string[] values, string id)
        {
            if (string.IsNullOrWhiteSpace(id) || values == null)
                return false;
            foreach (string value in values)
            {
                if (string.Equals(value, id, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static string[] CopyNodeIds(CharterNodeRuntimeStateData[] values)
        {
            if (values == null)
                return null;
            var copy = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
                copy[i] = values[i] == null ? null : values[i].nodeId;
            return copy;
        }

        private static string[] CopySupplyIds(CharterRealitySupplyStateData[] values)
        {
            if (values == null)
                return null;
            var copy = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
                copy[i] = values[i] == null ? null : values[i].realitySupplyId;
            return copy;
        }

        private static string[] CopyStrings(string[] values)
        {
            if (values == null)
                return null;
            var copy = new string[values.Length];
            Array.Copy(values, copy, values.Length);
            return copy;
        }
    }
}
