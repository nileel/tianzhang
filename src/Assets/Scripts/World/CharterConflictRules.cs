using System;
using System.Collections.Generic;
namespace TianZhang.World
{
    /// <summary>Explicitly versioned, side-effect-free charter conflict contract.</summary>
    public enum CrossTierChallengeSourceKind { JindanProtection, YuanyingOrthodoxy, DedicatedGreatFormation, NarrativeRelic }
    public sealed class CrossTierChallengeGrant
    {
        public CrossTierChallengeGrant(
            string grantId,
            int definitionVersion,
            string targetVariableId,
            string challengerId,
            CrossTierChallengeSourceKind qualificationSource,
            string allowedOperationId,
            string targetId,
            string scopeId,
            string beneficiaryId,
            string realityAnchorId,
            string resourceLedgerRef,
            string capacityLedgerRef,
            int challengeRuleTier,
            int effectiveAtTick,
            int expiresAtTick,
            bool isRevoked,
            string revocationReason,
            string displaySource)
        {
            GrantId = grantId;
            DefinitionVersion = definitionVersion;
            TargetVariableId = targetVariableId;
            ChallengerId = challengerId;
            QualificationSource = qualificationSource;
            AllowedOperationId = allowedOperationId;
            TargetId = targetId;
            ScopeId = scopeId;
            BeneficiaryId = beneficiaryId;
            RealityAnchorId = realityAnchorId;
            ResourceLedgerRef = resourceLedgerRef;
            CapacityLedgerRef = capacityLedgerRef;
            ChallengeRuleTier = challengeRuleTier;
            EffectiveAtTick = effectiveAtTick;
            ExpiresAtTick = expiresAtTick;
            IsRevoked = isRevoked;
            RevocationReason = revocationReason;
            DisplaySource = displaySource;
        }
        public string GrantId { get; }
        public int DefinitionVersion { get; }
        public string TargetVariableId { get; }
        public string ChallengerId { get; }
        public CrossTierChallengeSourceKind QualificationSource { get; }
        public string AllowedOperationId { get; }
        public string TargetId { get; }
        public string ScopeId { get; }
        public string BeneficiaryId { get; }
        public string RealityAnchorId { get; }
        public string ResourceLedgerRef { get; }
        public string CapacityLedgerRef { get; }
        public int ChallengeRuleTier { get; }
        public int EffectiveAtTick { get; }
        public int ExpiresAtTick { get; }
        public bool IsRevoked { get; }
        public string RevocationReason { get; }
        public string DisplaySource { get; }
    }
    public sealed class CrossTierChallengeRequest
    {
        public CrossTierChallengeRequest(
            string challengeEventId,
            string grantId,
            int expectedDefinitionVersion,
            string targetVariableId,
            string challengerId,
            int worldTick)
        {
            ChallengeEventId = challengeEventId;
            GrantId = grantId;
            ExpectedDefinitionVersion = expectedDefinitionVersion;
            TargetVariableId = targetVariableId;
            ChallengerId = challengerId;
            WorldTick = worldTick;
        }
        public string ChallengeEventId { get; }
        public string GrantId { get; }
        public int ExpectedDefinitionVersion { get; }
        public string TargetVariableId { get; }
        public string ChallengerId { get; }
        public int WorldTick { get; }
    }
    public sealed class CrossTierChallengeResolution
    {
        public CrossTierChallengeResolution(bool isEligible, string reason, CrossTierChallengeGrant grant)
        {
            IsEligible = isEligible;
            Reason = reason;
            Grant = grant;
        }
        public bool IsEligible { get; }
        public string Reason { get; }
        public CrossTierChallengeGrant Grant { get; }
        public static CrossTierChallengeResolution Rejected(string reason)
        {
            return new CrossTierChallengeResolution(false, reason, null);
        }
    }
    /// <summary>Versioned grant archive. Resolving never writes resources or outcomes.</summary>
    public sealed class CrossTierChallengeArchive
    {
        private readonly IReadOnlyDictionary<string, CrossTierChallengeGrant> grants;
        private readonly bool isAvailable;
        public CrossTierChallengeArchive(IEnumerable<CrossTierChallengeGrant> grants)
        {
            if (grants == null)
                throw new ArgumentNullException(nameof(grants));
            var indexed = new Dictionary<string, CrossTierChallengeGrant>(StringComparer.Ordinal);
            foreach (CrossTierChallengeGrant grant in grants)
            {
                if (grant == null || string.IsNullOrWhiteSpace(grant.GrantId) || indexed.ContainsKey(grant.GrantId))
                    throw new ArgumentException("Cross-tier challenge grant ids must be unique and non-empty.", nameof(grants));
                indexed.Add(grant.GrantId, grant);
            }
            this.grants = indexed;
            isAvailable = indexed.Count > 0;
        }
        public CrossTierChallengeResolution Resolve(CrossTierChallengeRequest request)
        {
            if (!isAvailable)
                return CrossTierChallengeResolution.Rejected("JD_CHALLENGE_ARCHIVE_UNAVAILABLE");
            if (request == null || string.IsNullOrWhiteSpace(request.ChallengeEventId) ||
                string.IsNullOrWhiteSpace(request.GrantId) || string.IsNullOrWhiteSpace(request.TargetVariableId) ||
                string.IsNullOrWhiteSpace(request.ChallengerId) || request.ExpectedDefinitionVersion <= 0 || request.WorldTick < 0)
            {
                return CrossTierChallengeResolution.Rejected("JD_CHALLENGE_REQUEST_INVALID");
            }
            CrossTierChallengeGrant grant;
            if (!grants.TryGetValue(request.GrantId, out grant))
                return CrossTierChallengeResolution.Rejected("JD_CHALLENGE_GRANT_UNKNOWN");
            if (!IsWellFormed(grant))
                return CrossTierChallengeResolution.Rejected("JD_CHALLENGE_GRANT_INVALID");
            if (grant.DefinitionVersion != request.ExpectedDefinitionVersion)
                return CrossTierChallengeResolution.Rejected("JD_CHALLENGE_VERSION_MISMATCH");
            if (grant.IsRevoked)
                return CrossTierChallengeResolution.Rejected("JD_CHALLENGE_REVOKED");
            if (request.WorldTick < grant.EffectiveAtTick)
                return CrossTierChallengeResolution.Rejected("JD_CHALLENGE_NOT_YET_EFFECTIVE");
            if (request.WorldTick > grant.ExpiresAtTick)
                return CrossTierChallengeResolution.Rejected("JD_CHALLENGE_EXPIRED");
            if (!string.Equals(grant.TargetVariableId, request.TargetVariableId, StringComparison.Ordinal))
                return CrossTierChallengeResolution.Rejected("JD_CHALLENGE_TARGET_MISMATCH");
            if (!string.Equals(grant.ChallengerId, request.ChallengerId, StringComparison.Ordinal))
                return CrossTierChallengeResolution.Rejected("JD_CHALLENGE_CHALLENGER_MISMATCH");
            return new CrossTierChallengeResolution(true, "JD_CHALLENGE_AUTHORIZED", grant);
        }
        private static bool IsWellFormed(CrossTierChallengeGrant grant)
        {
            return grant.DefinitionVersion > 0 && grant.ChallengeRuleTier > 0 && grant.EffectiveAtTick >= 0 &&
                   grant.ExpiresAtTick >= grant.EffectiveAtTick && IsKnownSource(grant.QualificationSource) &&
                   !string.IsNullOrWhiteSpace(grant.TargetVariableId) && !string.IsNullOrWhiteSpace(grant.ChallengerId) &&
                   !string.IsNullOrWhiteSpace(grant.AllowedOperationId) && !string.IsNullOrWhiteSpace(grant.TargetId) &&
                   !string.IsNullOrWhiteSpace(grant.ScopeId) && !string.IsNullOrWhiteSpace(grant.BeneficiaryId) &&
                   !string.IsNullOrWhiteSpace(grant.RealityAnchorId) && !string.IsNullOrWhiteSpace(grant.ResourceLedgerRef) &&
                   !string.IsNullOrWhiteSpace(grant.CapacityLedgerRef) && !string.IsNullOrWhiteSpace(grant.DisplaySource);
        }
        private static bool IsKnownSource(CrossTierChallengeSourceKind source)
        {
            return source == CrossTierChallengeSourceKind.JindanProtection ||
                   source == CrossTierChallengeSourceKind.YuanyingOrthodoxy ||
                   source == CrossTierChallengeSourceKind.DedicatedGreatFormation ||
                   source == CrossTierChallengeSourceKind.NarrativeRelic;
        }
    }
    public enum RuleConflictKind { JindanSameVariable, YuanyingAnchored }
    public enum RuleConflictOutcome { LeftWins, RightWins, Neutral, Rejected, Anchored }
    public sealed class RuleConflictCandidate
    {
        public RuleConflictCandidate(
            string candidateId,
            string targetVariableId,
            string targetId,
            bool hasVariableAuthority,
            bool hasLegalTarget,
            int positionRank,
            int realityAnchorRank,
            int alreadyPaidCost,
            bool hasActiveContinuousCarrier,
            int conflictReserve,
            int pulseCost,
            int settlementCooldown)
        {
            CandidateId = candidateId;
            TargetVariableId = targetVariableId;
            TargetId = targetId;
            HasVariableAuthority = hasVariableAuthority;
            HasLegalTarget = hasLegalTarget;
            PositionRank = positionRank;
            RealityAnchorRank = realityAnchorRank;
            AlreadyPaidCost = alreadyPaidCost;
            HasActiveContinuousCarrier = hasActiveContinuousCarrier;
            ConflictReserve = conflictReserve;
            PulseCost = pulseCost;
            SettlementCooldown = settlementCooldown;
        }
        public string CandidateId { get; }
        public string TargetVariableId { get; }
        public string TargetId { get; }
        public bool HasVariableAuthority { get; }
        public bool HasLegalTarget { get; }
        public int PositionRank { get; }
        public int RealityAnchorRank { get; }
        public int AlreadyPaidCost { get; }
        public bool HasActiveContinuousCarrier { get; }
        public int ConflictReserve { get; }
        public int PulseCost { get; }
        public int SettlementCooldown { get; }
    }
    public sealed class RuleConflictDecision
    {
        public RuleConflictDecision(
            RuleConflictOutcome outcome,
            string reason,
            string winnerCandidateId,
            int leftPulses,
            int rightPulses,
            int leftReserveSpent,
            int rightReserveSpent,
            int leftSettlementCooldown,
            int rightSettlementCooldown,
            int rejectedCandidateCount,
            bool requiresLedgerSettlement,
            CrossTierChallengeResolution crossTierAuthorization)
        {
            Outcome = outcome;
            Reason = reason;
            WinnerCandidateId = winnerCandidateId;
            LeftPulses = leftPulses;
            RightPulses = rightPulses;
            LeftReserveSpent = leftReserveSpent;
            RightReserveSpent = rightReserveSpent;
            LeftSettlementCooldown = leftSettlementCooldown;
            RightSettlementCooldown = rightSettlementCooldown;
            RejectedCandidateCount = rejectedCandidateCount;
            RequiresLedgerSettlement = requiresLedgerSettlement;
            CrossTierAuthorization = crossTierAuthorization;
        }
        public RuleConflictOutcome Outcome { get; }
        public string Reason { get; }
        public string WinnerCandidateId { get; }
        public int LeftPulses { get; }
        public int RightPulses { get; }
        public int LeftReserveSpent { get; }
        public int RightReserveSpent { get; }
        public int LeftSettlementCooldown { get; }
        public int RightSettlementCooldown { get; }
        public int RejectedCandidateCount { get; }
        public bool RequiresLedgerSettlement { get; }
        public CrossTierChallengeResolution CrossTierAuthorization { get; }
        public static RuleConflictDecision Rejected(
            string reason,
            int rejectedCandidateCount,
            CrossTierChallengeResolution crossTierAuthorization)
        {
            return new RuleConflictDecision(
                RuleConflictOutcome.Rejected,
                reason,
                string.Empty,
                0,
                0,
                0,
                0,
                0,
                0,
                rejectedCandidateCount,
                false,
                crossTierAuthorization);
        }
    }
    /// <summary>Short-lived v1 request and its deterministic, side-effect-free conflict decision.</summary>
    public sealed class RuleConflictInstance
    {
        public const int ContractVersionV1 = 1;
        public RuleConflictInstance(
            int contractVersion,
            string conflictEventId,
            RuleConflictKind kind,
            string ruleEntryId,
            string targetVariableId,
            string allowedOperationId,
            string targetId,
            string scopeId,
            string beneficiaryId,
            string realityAnchorId,
            string resourceLedgerRef,
            string capacityLedgerRef,
            int worldTick,
            RuleConflictCandidate leftCandidate,
            RuleConflictCandidate rightCandidate,
            CrossTierChallengeRequest crossTierChallengeRequest)
        {
            ContractVersion = contractVersion;
            ConflictEventId = conflictEventId;
            Kind = kind;
            RuleEntryId = ruleEntryId;
            TargetVariableId = targetVariableId;
            AllowedOperationId = allowedOperationId;
            TargetId = targetId;
            ScopeId = scopeId;
            BeneficiaryId = beneficiaryId;
            RealityAnchorId = realityAnchorId;
            ResourceLedgerRef = resourceLedgerRef;
            CapacityLedgerRef = capacityLedgerRef;
            WorldTick = worldTick;
            LeftCandidate = leftCandidate;
            RightCandidate = rightCandidate;
            CrossTierChallengeRequest = crossTierChallengeRequest;
        }
        public int ContractVersion { get; }
        public string ConflictEventId { get; }
        public RuleConflictKind Kind { get; }
        public string RuleEntryId { get; }
        public string TargetVariableId { get; }
        public string AllowedOperationId { get; }
        public string TargetId { get; }
        public string ScopeId { get; }
        public string BeneficiaryId { get; }
        public string RealityAnchorId { get; }
        public string ResourceLedgerRef { get; }
        public string CapacityLedgerRef { get; }
        public int WorldTick { get; }
        public RuleConflictCandidate LeftCandidate { get; }
        public RuleConflictCandidate RightCandidate { get; }
        public CrossTierChallengeRequest CrossTierChallengeRequest { get; }
        public RuleConflictDecision Decide(CrossTierChallengeArchive crossTierChallengeArchive)
        {
            if (ContractVersion != ContractVersionV1)
                return RuleConflictDecision.Rejected("TZ_CHARTER_CONFLICT_CONTRACT_VERSION_UNSUPPORTED", 0, null);
            if (!HasRequiredIdentity() || !IsKnownKind(Kind))
                return RuleConflictDecision.Rejected("TZ_CHARTER_CONFLICT_INPUT_INVALID", 0, null);
            if (Kind == RuleConflictKind.YuanyingAnchored)
            {
                if (LeftCandidate != null || RightCandidate != null || CrossTierChallengeRequest != null)
                    return RuleConflictDecision.Rejected("TZ_CHARTER_CONFLICT_INPUT_INVALID", 0, null);
                return new RuleConflictDecision(
                    RuleConflictOutcome.Anchored,
                    "TZ_CHARTER_CONFLICT_YUANYING_ANCHORED",
                    string.Empty,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    false,
                    null);
            }
            if (!IsWellFormedCandidate(LeftCandidate) || !IsWellFormedCandidate(RightCandidate) ||
                !MatchesConflict(LeftCandidate) || !MatchesConflict(RightCandidate))
            {
                return RuleConflictDecision.Rejected("TZ_CHARTER_CONFLICT_INPUT_INVALID", 0, null);
            }
            CrossTierChallengeResolution authorization = null;
            if (CrossTierChallengeRequest != null)
            {
                if (crossTierChallengeArchive == null)
                    return RuleConflictDecision.Rejected("JD_CHALLENGE_ARCHIVE_UNAVAILABLE", 0, null);
                authorization = crossTierChallengeArchive.Resolve(CrossTierChallengeRequest);
                if (!authorization.IsEligible)
                    return RuleConflictDecision.Rejected(authorization.Reason, 0, authorization);
                string grantMismatchReason = GetGrantMismatchReason(authorization.Grant);
                if (!string.IsNullOrEmpty(grantMismatchReason))
                    return RuleConflictDecision.Rejected(grantMismatchReason, 0, authorization);
            }
            int comparison = ComparePriority(LeftCandidate, RightCandidate, out string reason);
            if (comparison > 0)
                return CreatePriorityDecision(RuleConflictOutcome.LeftWins, reason, LeftCandidate.CandidateId, authorization);
            if (comparison < 0)
                return CreatePriorityDecision(RuleConflictOutcome.RightWins, reason, RightCandidate.CandidateId, authorization);
            int leftPulses = LeftCandidate.ConflictReserve / LeftCandidate.PulseCost;
            int rightPulses = RightCandidate.ConflictReserve / RightCandidate.PulseCost;
            int leftReserveSpent = leftPulses * LeftCandidate.PulseCost;
            int rightReserveSpent = rightPulses * RightCandidate.PulseCost;
            RuleConflictOutcome outcome = leftPulses > rightPulses
                ? RuleConflictOutcome.LeftWins
                : rightPulses > leftPulses
                    ? RuleConflictOutcome.RightWins
                    : RuleConflictOutcome.Neutral;
            string winnerCandidateId = outcome == RuleConflictOutcome.LeftWins
                ? LeftCandidate.CandidateId
                : outcome == RuleConflictOutcome.RightWins
                    ? RightCandidate.CandidateId
                    : string.Empty;
            string pulseReason = outcome == RuleConflictOutcome.Neutral ? "PULSE_NEUTRAL" : "PULSE_ADVANTAGE";
            return new RuleConflictDecision(
                outcome,
                pulseReason,
                winnerCandidateId,
                leftPulses,
                rightPulses,
                leftReserveSpent,
                rightReserveSpent,
                LeftCandidate.SettlementCooldown,
                RightCandidate.SettlementCooldown,
                0,
                true,
                authorization);
        }
        private bool HasRequiredIdentity()
        {
            return !string.IsNullOrWhiteSpace(ConflictEventId) && !string.IsNullOrWhiteSpace(RuleEntryId) &&
                   !string.IsNullOrWhiteSpace(TargetVariableId) && !string.IsNullOrWhiteSpace(AllowedOperationId) &&
                   !string.IsNullOrWhiteSpace(TargetId) && !string.IsNullOrWhiteSpace(ScopeId) &&
                   !string.IsNullOrWhiteSpace(BeneficiaryId) && !string.IsNullOrWhiteSpace(RealityAnchorId) &&
                   !string.IsNullOrWhiteSpace(ResourceLedgerRef) && !string.IsNullOrWhiteSpace(CapacityLedgerRef) &&
                   WorldTick >= 0;
        }
        private static bool IsKnownKind(RuleConflictKind kind)
        {
            return kind == RuleConflictKind.JindanSameVariable || kind == RuleConflictKind.YuanyingAnchored;
        }
        private static bool IsWellFormedCandidate(RuleConflictCandidate candidate)
        {
            return candidate != null && !string.IsNullOrWhiteSpace(candidate.CandidateId) &&
                   !string.IsNullOrWhiteSpace(candidate.TargetVariableId) && !string.IsNullOrWhiteSpace(candidate.TargetId) &&
                   candidate.PositionRank >= 0 && candidate.RealityAnchorRank >= 0 && candidate.AlreadyPaidCost >= 0 &&
                   candidate.ConflictReserve >= 0 && candidate.PulseCost > 0 && candidate.SettlementCooldown >= 0;
        }
        private bool MatchesConflict(RuleConflictCandidate candidate)
        {
            return string.Equals(candidate.TargetVariableId, TargetVariableId, StringComparison.Ordinal) &&
                   string.Equals(candidate.TargetId, TargetId, StringComparison.Ordinal);
        }
        private string GetGrantMismatchReason(CrossTierChallengeGrant grant)
        {
            if (!string.Equals(grant.AllowedOperationId, AllowedOperationId, StringComparison.Ordinal))
                return "TZ_CHARTER_CONFLICT_GRANT_OPERATION_MISMATCH";
            if (!string.Equals(grant.TargetId, TargetId, StringComparison.Ordinal))
                return "TZ_CHARTER_CONFLICT_GRANT_TARGET_MISMATCH";
            if (!string.Equals(grant.ScopeId, ScopeId, StringComparison.Ordinal))
                return "TZ_CHARTER_CONFLICT_GRANT_SCOPE_MISMATCH";
            if (!string.Equals(grant.BeneficiaryId, BeneficiaryId, StringComparison.Ordinal))
                return "TZ_CHARTER_CONFLICT_GRANT_BENEFICIARY_MISMATCH";
            if (!string.Equals(grant.RealityAnchorId, RealityAnchorId, StringComparison.Ordinal))
                return "TZ_CHARTER_CONFLICT_GRANT_ANCHOR_MISMATCH";
            if (!string.Equals(grant.ResourceLedgerRef, ResourceLedgerRef, StringComparison.Ordinal))
                return "TZ_CHARTER_CONFLICT_GRANT_RESOURCE_LEDGER_MISMATCH";
            if (!string.Equals(grant.CapacityLedgerRef, CapacityLedgerRef, StringComparison.Ordinal))
                return "TZ_CHARTER_CONFLICT_GRANT_CAPACITY_LEDGER_MISMATCH";
            return string.Empty;
        }
        private static RuleConflictDecision CreatePriorityDecision(
            RuleConflictOutcome outcome,
            string reason,
            string winnerCandidateId,
            CrossTierChallengeResolution authorization)
        {
            return new RuleConflictDecision(outcome, reason, winnerCandidateId, 0, 0, 0, 0, 0, 0, 0, false, authorization);
        }
        private static int ComparePriority(RuleConflictCandidate left, RuleConflictCandidate right, out string reason)
        {
            int comparison = AuthorityAndTargetRank(left).CompareTo(AuthorityAndTargetRank(right));
            if (comparison != 0)
            {
                reason = "VARIABLE_AUTHORITY_AND_TARGET";
                return comparison;
            }
            comparison = left.PositionRank.CompareTo(right.PositionRank);
            if (comparison != 0)
            {
                reason = "POSITION_TIER";
                return comparison;
            }
            comparison = left.RealityAnchorRank.CompareTo(right.RealityAnchorRank);
            if (comparison != 0)
            {
                reason = "REALITY_ANCHOR";
                return comparison;
            }
            comparison = left.AlreadyPaidCost.CompareTo(right.AlreadyPaidCost);
            if (comparison != 0)
            {
                reason = "ALREADY_PAID_COST";
                return comparison;
            }
            comparison = left.HasActiveContinuousCarrier.CompareTo(right.HasActiveContinuousCarrier);
            reason = comparison == 0 ? "PULSE" : "ACTIVE_CONTINUOUS_CARRIER";
            return comparison;
        }
        private static int AuthorityAndTargetRank(RuleConflictCandidate candidate)
        {
            return (candidate.HasVariableAuthority ? 2 : 0) + (candidate.HasLegalTarget ? 1 : 0);
        }
    }
}
