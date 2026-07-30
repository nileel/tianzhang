using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Cultivation.JindanProof;
using TianZhang.Entity;

namespace TianZhang.Cultivation
{
    /// <summary>
    /// 只在已声明事件中为单个 NPC 选择行动。它复用道基／紫府运行时状态，
    /// 不持有 NPC 列表、世界 tick 或任何玩家不可知的事实。
    /// </summary>
    public sealed class NpcCultivationActionCoordinator
    {
        public const string NpcNotFound = "NPC_CULTIVATION_NPC_NOT_FOUND";
        public const string MissingCultivationState = "NPC_CULTIVATION_STATE_MISSING";
        public const string InvalidRequest = "NPC_CULTIVATION_INVALID_REQUEST";
        public const string UndeclaredTrigger = "NPC_CULTIVATION_UNDECLARED_TRIGGER";
        public const string NoLegalAction = "NPC_CULTIVATION_NO_LEGAL_ACTION";

        public NpcCultivationActionRecalculationResult Recalculate(
            FoundationPurpleMansionRuntimeState state,
            NpcCultivationActionRecalculationRequest request)
        {
            if (state == null || request == null || request.WeightProfile == null ||
                string.IsNullOrWhiteSpace(request.TriggerStableId) ||
                request.Candidates == null)
            {
                return NpcCultivationActionRecalculationResult.Rejected(InvalidRequest);
            }
            if (!request.WeightProfile.DeclaresRecalculationTrigger(request.TriggerStableId))
                return NpcCultivationActionRecalculationResult.Rejected(UndeclaredTrigger);

            NpcCultivationActionCandidate[] candidates = request.Candidates.ToArray();
            if (candidates.Any(candidate => !IsWellFormed(candidate)) ||
                candidates.Select(candidate => candidate.ActionStableId)
                    .Distinct(StringComparer.Ordinal).Count() != candidates.Length)
            {
                return NpcCultivationActionRecalculationResult.Rejected(InvalidRequest);
            }

            var legalActionIds = new List<string>();
            foreach (NpcCultivationActionCandidate candidate in candidates)
            {
                if (!candidate.HardRequirementsMet ||
                    !TryGetActionKind(candidate.ActionStableId, out CultivationActionKind actionKind) ||
                    actionKind == CultivationActionKind.JindanProof &&
                    (candidate.JindanProofDecision == null ||
                     !candidate.JindanProofDecision.ShouldStart))
                {
                    continue;
                }

                FoundationPurpleMansionOperationResult gate = state.CanStartCultivationAction(
                    actionKind,
                    candidate.TargetRef);
                if (gate.Succeeded)
                    legalActionIds.Add(candidate.ActionStableId);
            }

            var ranking = request.WeightProfile.Evaluate(
                new NpcCultivationActionDecisionContext(
                    legalActionIds,
                    request.SelectorRefs,
                    request.RiskAssessments));
            if (string.IsNullOrEmpty(ranking.selectedActionStableId))
            {
                return NpcCultivationActionRecalculationResult.Rejected(
                    NoLegalAction,
                    ranking);
            }

            NpcCultivationActionCandidate selected = candidates.Single(candidate =>
                candidate.ActionStableId == ranking.selectedActionStableId);
            if (!TryGetActionKind(selected.ActionStableId, out CultivationActionKind selectedKind))
                return NpcCultivationActionRecalculationResult.Rejected(InvalidRequest, ranking);

            FoundationPurpleMansionOperationResult start = state.TryStartCultivationAction(
                selectedKind,
                selected.ActionStateId,
                selected.TargetRef,
                selected.FixedCycleDefinitionId,
                selected.InitialStableBoundaryId,
                selected.ProgressChannelId,
                selected.NumericProfileRefs);
            return start.Succeeded
                ? NpcCultivationActionRecalculationResult.Selected(
                    selected.ActionStableId,
                    ranking)
                : NpcCultivationActionRecalculationResult.Rejected(
                    start.FailureReason,
                    ranking);
        }

        private static bool IsWellFormed(NpcCultivationActionCandidate candidate)
        {
            return candidate != null &&
                !string.IsNullOrWhiteSpace(candidate.ActionStableId) &&
                !string.IsNullOrWhiteSpace(candidate.ActionStateId) &&
                !string.IsNullOrWhiteSpace(candidate.TargetRef) &&
                !string.IsNullOrWhiteSpace(candidate.FixedCycleDefinitionId) &&
                !string.IsNullOrWhiteSpace(candidate.InitialStableBoundaryId) &&
                !string.IsNullOrWhiteSpace(candidate.ProgressChannelId) &&
                candidate.NumericProfileRefs != null &&
                candidate.NumericProfileRefs.Any() &&
                !candidate.NumericProfileRefs.Any(string.IsNullOrWhiteSpace);
        }

        private static bool TryGetActionKind(
            string actionStableId,
            out CultivationActionKind actionKind)
        {
            switch (actionStableId)
            {
                case "FOUNDATION_TRIAL":
                    actionKind = CultivationActionKind.FoundationTrial;
                    return true;
                case "FOUNDATION_NURTURE":
                    actionKind = CultivationActionKind.FoundationNurture;
                    return true;
                case "MANSION_EMBRYO_NURTURE":
                    actionKind = CultivationActionKind.MansionEmbryoNurture;
                    return true;
                case "MANSION_OPENING_TRIAL":
                    actionKind = CultivationActionKind.MansionOpeningTrial;
                    return true;
                case "JINDAN_PROOF":
                    actionKind = CultivationActionKind.JindanProof;
                    return true;
                default:
                    actionKind = default;
                    return false;
            }
        }
    }

    public sealed class NpcCultivationActionCandidate
    {
        public string ActionStableId { get; set; }
        public bool HardRequirementsMet { get; set; }
        public string ActionStateId { get; set; }
        public string TargetRef { get; set; }
        public string FixedCycleDefinitionId { get; set; }
        public string InitialStableBoundaryId { get; set; }
        public string ProgressChannelId { get; set; }
        public IEnumerable<string> NumericProfileRefs { get; set; }
        public NpcProofDecision JindanProofDecision { get; set; }
    }

    public sealed class NpcCultivationActionRecalculationRequest
    {
        public string TriggerStableId { get; set; }
        public NpcCultivationActionWeightProfileRuntime WeightProfile { get; set; }
        public IEnumerable<NpcCultivationActionCandidate> Candidates { get; set; }
        public IEnumerable<string> SelectorRefs { get; set; }
        public IReadOnlyDictionary<string, float> RiskAssessments { get; set; }
    }

    public sealed class NpcCultivationActionRecalculationResult
    {
        private NpcCultivationActionRecalculationResult(
            bool succeeded,
            string selectedActionStableId,
            string failureReason,
            NpcCultivationActionDecisionResult ranking)
        {
            Succeeded = succeeded;
            SelectedActionStableId = selectedActionStableId;
            FailureReason = failureReason;
            Ranking = ranking;
        }

        public bool Succeeded { get; }
        public string SelectedActionStableId { get; }
        public string FailureReason { get; }
        public NpcCultivationActionDecisionResult Ranking { get; }

        public static NpcCultivationActionRecalculationResult Selected(
            string actionStableId,
            NpcCultivationActionDecisionResult ranking)
        {
            return new NpcCultivationActionRecalculationResult(
                true,
                actionStableId,
                null,
                ranking);
        }

        public static NpcCultivationActionRecalculationResult Rejected(
            string failureReason,
            NpcCultivationActionDecisionResult ranking = null)
        {
            return new NpcCultivationActionRecalculationResult(
                false,
                null,
                failureReason,
                ranking);
        }
    }
}
