using System;

namespace TianZhang.Cultivation.JindanProof
{
    public enum NpcRiskDisposition
    {
        Cautious,
        Normal,
        Bold
    }

    public enum NpcProofDecisionReason
    {
        Ready,
        HardGateFailed,
        SubjectiveRiskTooHigh
    }

    public sealed class NpcProofDecisionInput
    {
        public bool IsPersistentNpc;
        public bool IsPurpleMansionComplete;
        public bool HardRequirementsMet;
        public bool KnowsVacancy;
        public bool KnowsUsableSite;
        public bool HasCompatibleCarrier;
        public bool HasFacilities;
        public bool HasResources;
        public bool HasGuard;
        public bool HasHigherPrioritySurvivalDuty;
        public NpcRiskDisposition RiskDisposition;
        public int SubjectiveSuccessPercent;
        public int DaysOfLifeRemaining;
    }

    public sealed class NpcProofDecision
    {
        public bool ShouldStart { get; }
        public NpcProofDecisionReason Reason { get; }
        public int RequiredSubjectivePercent { get; }

        public NpcProofDecision(
            bool shouldStart,
            NpcProofDecisionReason reason,
            int requiredSubjectivePercent)
        {
            ShouldStart = shouldStart;
            Reason = reason;
            RequiredSubjectivePercent = requiredSubjectivePercent;
        }
    }

    public sealed class NpcJindanProofPolicy
    {
        private readonly int cautiousThreshold;
        private readonly int normalThreshold;
        private readonly int boldThreshold;
        private readonly int lifespanDangerDays;
        private readonly int lifespanThresholdReduction;

        public NpcJindanProofPolicy(
            int cautiousThreshold,
            int normalThreshold,
            int boldThreshold,
            int lifespanDangerDays,
            int lifespanThresholdReduction)
        {
            this.cautiousThreshold =
                ValidatePercent(cautiousThreshold, nameof(cautiousThreshold));
            this.normalThreshold =
                ValidatePercent(normalThreshold, nameof(normalThreshold));
            this.boldThreshold =
                ValidatePercent(boldThreshold, nameof(boldThreshold));
            if (lifespanDangerDays < 0)
                throw new ArgumentOutOfRangeException(nameof(lifespanDangerDays));
            this.lifespanDangerDays = lifespanDangerDays;
            this.lifespanThresholdReduction = ValidatePercent(
                lifespanThresholdReduction,
                nameof(lifespanThresholdReduction));
        }

        public NpcProofDecision Evaluate(NpcProofDecisionInput input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            ValidatePercent(
                input.SubjectiveSuccessPercent,
                nameof(input.SubjectiveSuccessPercent));
            if (input.DaysOfLifeRemaining < -1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(input.DaysOfLifeRemaining));
            }
            if (!Enum.IsDefined(typeof(NpcRiskDisposition), input.RiskDisposition))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(input.RiskDisposition));
            }

            int threshold = BaseThreshold(input.RiskDisposition);
            if (input.DaysOfLifeRemaining >= 0 &&
                input.DaysOfLifeRemaining <= lifespanDangerDays)
            {
                threshold = Math.Max(1, threshold - lifespanThresholdReduction);
            }

            bool hardGate =
                input.IsPersistentNpc &&
                input.IsPurpleMansionComplete &&
                input.HardRequirementsMet &&
                input.KnowsVacancy &&
                input.KnowsUsableSite &&
                input.HasCompatibleCarrier &&
                input.HasFacilities &&
                input.HasResources &&
                input.HasGuard &&
                !input.HasHigherPrioritySurvivalDuty;

            if (!hardGate)
            {
                return new NpcProofDecision(
                    false,
                    NpcProofDecisionReason.HardGateFailed,
                    threshold);
            }

            if (input.SubjectiveSuccessPercent < threshold)
            {
                return new NpcProofDecision(
                    false,
                    NpcProofDecisionReason.SubjectiveRiskTooHigh,
                    threshold);
            }

            return new NpcProofDecision(
                true,
                NpcProofDecisionReason.Ready,
                threshold);
        }

        private int BaseThreshold(NpcRiskDisposition disposition)
        {
            switch (disposition)
            {
                case NpcRiskDisposition.Cautious:
                    return cautiousThreshold;
                case NpcRiskDisposition.Bold:
                    return boldThreshold;
                default:
                    return normalThreshold;
            }
        }

        private static int ValidatePercent(int value, string parameterName)
        {
            if (value < 0 || value > 100)
                throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }
    }
}
