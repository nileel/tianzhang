using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Content;
using TianZhang.Entity;

namespace TianZhang.Cultivation
{
    public static class NpcCultivationUseCaseReasons
    {
        public const string NpcNotFound = "NPC_CULTIVATION_NPC_NOT_FOUND";
        public const string MissingState = "NPC_CULTIVATION_STATE_MISSING";
        public const string InvalidRequest = "NPC_CULTIVATION_INVALID_REQUEST";
        public const string UndeclaredTrigger = "NPC_CULTIVATION_UNDECLARED_TRIGGER";
        public const string NoLegalAction = "NPC_CULTIVATION_NO_LEGAL_ACTION";
    }

    public sealed class NpcCultivationCandidate
    {
        public string ActionStableId { get; set; }
        public bool HardRequirementsMet { get; set; }
        public bool AllowsJindanProof { get; set; }
        public string ActionStateId { get; set; }
        public string TargetRef { get; set; }
        public string FixedCycleDefinitionId { get; set; }
        public string InitialStableBoundaryId { get; set; }
        public string ProgressChannelId { get; set; }
        public IEnumerable<string> NumericProfileRefs { get; set; }
    }

    public sealed class NpcCultivationRequest
    {
        public string TriggerStableId { get; set; }
        public NpcCultivationActionWeightProfileRuntime WeightProfile { get; set; }
        public IEnumerable<NpcCultivationCandidate> Candidates { get; set; }
        public IEnumerable<string> SelectorRefs { get; set; }
        public IReadOnlyDictionary<string, float> RiskAssessments { get; set; }
    }

    public sealed class NpcCultivationResult
    {
        private NpcCultivationResult(
            bool succeeded,
            string selectedActionStableId,
            string failureReason,
            NpcCultivationActionDecisionResult ranking,
            FoundationPurpleMansionSaveData state)
        {
            Succeeded = succeeded;
            SelectedActionStableId = selectedActionStableId;
            FailureReason = failureReason;
            Ranking = ranking;
            State = state;
        }

        public bool Succeeded { get; }
        public string SelectedActionStableId { get; }
        public string FailureReason { get; }
        public NpcCultivationActionDecisionResult Ranking { get; }
        public FoundationPurpleMansionSaveData State { get; }

        public static NpcCultivationResult Selected(
            string actionStableId,
            NpcCultivationActionDecisionResult ranking,
            FoundationPurpleMansionSaveData state)
        {
            return new NpcCultivationResult(true, actionStableId, null, ranking, state);
        }

        public static NpcCultivationResult Rejected(
            string reason,
            NpcCultivationActionDecisionResult ranking = null)
        {
            return new NpcCultivationResult(false, null, reason, ranking, null);
        }
    }

    /// <summary>Selects and applies one declared NPC cultivation action without owning NPC storage.</summary>
    public sealed class NpcCultivationUseCase
    {
        public NpcCultivationResult Recalculate(
            FoundationPurpleMansionSaveData savedState,
            NpcCultivationRequest request)
        {
            FoundationPurpleMansionRuntimeState state;
            string restoreReason = NpcCultivationUseCaseReasons.MissingState;
            if (savedState == null || !FoundationPurpleMansionRuntimeState.TryRestore(savedState, out state, out restoreReason))
                return NpcCultivationResult.Rejected(restoreReason ?? NpcCultivationUseCaseReasons.MissingState);
            if (request == null || request.WeightProfile == null ||
                string.IsNullOrWhiteSpace(request.TriggerStableId) || request.Candidates == null)
            {
                return NpcCultivationResult.Rejected(NpcCultivationUseCaseReasons.InvalidRequest);
            }
            if (!request.WeightProfile.DeclaresRecalculationTrigger(request.TriggerStableId))
                return NpcCultivationResult.Rejected(NpcCultivationUseCaseReasons.UndeclaredTrigger);

            NpcCultivationCandidate[] candidates = request.Candidates.ToArray();
            if (candidates.Any(candidate => !IsWellFormed(candidate)) ||
                candidates.Select(candidate => candidate.ActionStableId).Distinct(StringComparer.Ordinal).Count() != candidates.Length)
            {
                return NpcCultivationResult.Rejected(NpcCultivationUseCaseReasons.InvalidRequest);
            }

            var legalActionIds = new List<string>();
            foreach (NpcCultivationCandidate candidate in candidates)
            {
                CultivationActionKind kind;
                if (!candidate.HardRequirementsMet || !TryGetActionKind(candidate.ActionStableId, out kind) ||
                    kind == CultivationActionKind.JindanProof && !candidate.AllowsJindanProof)
                {
                    continue;
                }
                if (state.CanStartCultivationAction(kind, candidate.TargetRef).Succeeded)
                    legalActionIds.Add(candidate.ActionStableId);
            }

            NpcCultivationActionDecisionResult ranking = request.WeightProfile.Evaluate(
                new NpcCultivationActionDecisionContext(legalActionIds, request.SelectorRefs, request.RiskAssessments));
            if (string.IsNullOrEmpty(ranking.selectedActionStableId))
                return NpcCultivationResult.Rejected(NpcCultivationUseCaseReasons.NoLegalAction, ranking);

            NpcCultivationCandidate selected = candidates.Single(candidate =>
                candidate.ActionStableId == ranking.selectedActionStableId);
            CultivationActionKind selectedKind;
            if (!TryGetActionKind(selected.ActionStableId, out selectedKind))
                return NpcCultivationResult.Rejected(NpcCultivationUseCaseReasons.InvalidRequest, ranking);
            FoundationPurpleMansionOperationResult start = state.TryStartCultivationAction(
                selectedKind,
                selected.ActionStateId,
                selected.TargetRef,
                selected.FixedCycleDefinitionId,
                selected.InitialStableBoundaryId,
                selected.ProgressChannelId,
                selected.NumericProfileRefs);
            return start.Succeeded
                ? NpcCultivationResult.Selected(selected.ActionStableId, ranking, state.CaptureSaveData())
                : NpcCultivationResult.Rejected(start.FailureReason, ranking);
        }

        private static bool IsWellFormed(NpcCultivationCandidate candidate)
        {
            return candidate != null && !string.IsNullOrWhiteSpace(candidate.ActionStableId) &&
                !string.IsNullOrWhiteSpace(candidate.ActionStateId) && !string.IsNullOrWhiteSpace(candidate.TargetRef) &&
                !string.IsNullOrWhiteSpace(candidate.FixedCycleDefinitionId) &&
                !string.IsNullOrWhiteSpace(candidate.InitialStableBoundaryId) &&
                !string.IsNullOrWhiteSpace(candidate.ProgressChannelId) && candidate.NumericProfileRefs != null &&
                candidate.NumericProfileRefs.Any() && !candidate.NumericProfileRefs.Any(string.IsNullOrWhiteSpace);
        }

        private static bool TryGetActionKind(string actionStableId, out CultivationActionKind kind)
        {
            switch (actionStableId)
            {
                case "FOUNDATION_TRIAL": kind = CultivationActionKind.FoundationTrial; return true;
                case "FOUNDATION_NURTURE": kind = CultivationActionKind.FoundationNurture; return true;
                case "MANSION_EMBRYO_NURTURE": kind = CultivationActionKind.MansionEmbryoNurture; return true;
                case "MANSION_OPENING_TRIAL": kind = CultivationActionKind.MansionOpeningTrial; return true;
                case "JINDAN_PROOF": kind = CultivationActionKind.JindanProof; return true;
                default: kind = default(CultivationActionKind); return false;
            }
        }
    }
}
