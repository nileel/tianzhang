using System;
using System.Collections.Generic;

namespace TianZhang.Cultivation.JindanProof
{
    public static class JindanProofEligibility
    {
        public static ProofEligibilityResult Evaluate(
            JindanProofProfileDefinition profile,
            DaoProofLedger ledger)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            if (ledger == null)
                throw new ArgumentNullException(nameof(ledger));

            var unmet = new List<string>();
            foreach (JindanProofRequirement requirement in profile.Requirements)
            {
                int current = ReadCurrentValue(requirement, ledger);
                if (current < requirement.MinimumValue)
                    unmet.Add(requirement.RecordId);
            }

            return new ProofEligibilityResult(unmet.Count == 0, unmet);
        }

        internal static int ReadCurrentValue(
            JindanProofRequirement requirement,
            DaoProofLedger ledger)
        {
            return requirement.Type == ProofRequirementType.SharedMetric
                ? ledger.GetMetricValue(requirement.RecordId)
                : ledger.HasAchievement(requirement.RecordId) ? 1 : 0;
        }
    }

    public static class JindanProofKnowledge
    {
        public static JindanProofView Project(
            JindanProofProfileDefinition profile,
            DaoProofLedger ledger,
            ProofKnowledgeLevel knowledgeLevel,
            int adaptationPercent,
            int estimatedSuccessPercent)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            if (ledger == null)
                throw new ArgumentNullException(nameof(ledger));

            if (knowledgeLevel == ProofKnowledgeLevel.RoadDirection)
            {
                return new JindanProofView(
                    profile.RoadId,
                    Array.Empty<ProofRequirementView>(),
                    null,
                    null,
                    false);
            }

            if (knowledgeLevel != ProofKnowledgeLevel.FullProfile)
            {
                return new JindanProofView(
                    null,
                    Array.Empty<ProofRequirementView>(),
                    null,
                    null,
                    false);
            }

            var requirements = new List<ProofRequirementView>();
            foreach (JindanProofRequirement requirement in profile.Requirements)
            {
                int current = JindanProofEligibility.ReadCurrentValue(requirement, ledger);
                requirements.Add(new ProofRequirementView(
                    requirement.RecordId,
                    current,
                    requirement.MinimumValue));
            }

            return new JindanProofView(
                profile.RoadId,
                requirements,
                ClampPercent(adaptationPercent),
                ClampPercent(estimatedSuccessPercent),
                true);
        }

        private static int ClampPercent(int value)
        {
            return Math.Max(0, Math.Min(100, value));
        }
    }
}
