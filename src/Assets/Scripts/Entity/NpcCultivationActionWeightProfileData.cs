using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace TianZhang.Entity
{
    [Serializable]
    public sealed class NpcCultivationActionWeightRecord
    {
        public string recordId;
        public string actionStableId;
        public string legalityRuleSetRef;
        public float baseWeight;
        public string subjectiveRiskGateRef;
        public bool enabled;
        public string actionTotalCapPolicyRef;
    }

    [Serializable]
    public sealed class NpcCultivationWeightModifierRecord
    {
        public string modifierId;
        public string sourceKind;
        public string actionStableId;
        public string selectorRef;
        public float priorityDelta;
        public int applicationOrder;
        public string capPolicyRef;
        public string diminishingPolicyRef;
        public float riskThresholdDelta;
    }

    [Serializable]
    public sealed class NpcCultivationWeightCapPolicy
    {
        public string capPolicyId;
        public string scope;
        public float minimum;
        public float maximum;
        public string appliesAfterSourceKind;
    }

    [Serializable]
    public sealed class NpcCultivationWeightDiminishingPolicy
    {
        public string diminishingPolicyId;
        public string scope;
        public string inputBasis;
        public float activationThreshold;
        public string segments;
        public float outputBound;
    }

    [Serializable]
    public sealed class NpcCultivationRiskGate
    {
        public string riskGateRef;
        public string[] knownEvidenceRefs;
        public string riskAssessmentRef;
        public float baseRiskThreshold;
        public string lifespanCapPolicyRef;
    }

    [Serializable]
    public sealed class NpcCultivationRecalculationTrigger
    {
        public string triggerStableId;
    }

    [CreateAssetMenu(fileName = "NpcCultivationActionWeightProfile_", menuName = "天章/NPC 修炼行动权重数据")]
    public sealed class NpcCultivationActionWeightProfileData : ScriptableObject
    {
        public string schemaId;
        public int schemaVersion;
        public string profileId;
        public string sourceContentHash;
        public string authorityKind;
        public string tieBreakPolicy;
        public NpcCultivationActionWeightRecord[] actionWeightRows;
        public NpcCultivationWeightModifierRecord[] modifierRows;
        public NpcCultivationWeightCapPolicy[] capPolicies;
        public NpcCultivationWeightDiminishingPolicy[] diminishingPolicies;
        public NpcCultivationRiskGate[] riskGates;
        public NpcCultivationRecalculationTrigger[] recalculationTriggers;
    }

    public sealed class NpcCultivationActionDecisionContext
    {
        public NpcCultivationActionDecisionContext(
            IEnumerable<string> legalActionStableIds,
            IEnumerable<string> selectorRefs,
            IReadOnlyDictionary<string, float> riskAssessments)
        {
            LegalActionStableIds = new HashSet<string>(legalActionStableIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            SelectorRefs = new HashSet<string>(selectorRefs ?? Array.Empty<string>(), StringComparer.Ordinal);
            RiskAssessments = riskAssessments ?? new Dictionary<string, float>(StringComparer.Ordinal);
        }

        public ISet<string> LegalActionStableIds { get; }
        public ISet<string> SelectorRefs { get; }
        public IReadOnlyDictionary<string, float> RiskAssessments { get; }
    }

    public sealed class NpcCultivationActionDecisionCandidate
    {
        public string actionStableId;
        public bool accepted;
        public string rejectionReason;
        public float score;
        public string[] matchedModifierIds;
    }

    public sealed class NpcCultivationActionDecisionResult
    {
        public NpcCultivationActionDecisionCandidate[] candidates;
        public string selectedActionStableId;
    }

    /// <summary>
    /// Runtime projection for a validated NPC cultivation profile. It only ranks an already legal
    /// action set and deliberately owns no action execution, world scan, or hidden-information path.
    /// </summary>
    public sealed class NpcCultivationActionWeightProfileRuntime
    {
        public const string SchemaId = "npcCultivationActionWeightProfile";
        public const int SchemaVersion = 1;
        public const string IllegalAction = "NPC_WEIGHT_ILLEGAL_ACTION";
        public const string RiskGateRejected = "NPC_WEIGHT_RISK_GATE_REJECTED";
        public const string NoLegalAction = "NPC_WEIGHT_NO_LEGAL_ACTION";

        private static readonly string[] RequiredActions =
        {
            "FOUNDATION_TRIAL",
            "FOUNDATION_NURTURE",
            "MANSION_EMBRYO_NURTURE",
            "MANSION_OPENING_TRIAL",
            "JINDAN_PROOF",
        };

        private static readonly string[] SourceOrder =
        {
            "PERSONALITY",
            "SECT",
            "REALM_GOAL",
            "LIFESPAN",
            "RESOURCE",
            "ENVIRONMENT",
        };

        private readonly Dictionary<string, NpcCultivationActionWeightRecord> actions;
        private readonly NpcCultivationWeightModifierRecord[] modifiers;
        private readonly Dictionary<string, NpcCultivationWeightCapPolicy> caps;
        private readonly Dictionary<string, NpcCultivationWeightDiminishingPolicy> diminishing;
        private readonly Dictionary<string, NpcCultivationRiskGate> riskGates;

        private NpcCultivationActionWeightProfileRuntime(NpcCultivationActionWeightProfileData source)
        {
            actions = source.actionWeightRows.ToDictionary(row => row.actionStableId, StringComparer.Ordinal);
            modifiers = source.modifierRows.ToArray();
            caps = source.capPolicies.ToDictionary(row => row.capPolicyId, StringComparer.Ordinal);
            diminishing = source.diminishingPolicies.ToDictionary(row => row.diminishingPolicyId, StringComparer.Ordinal);
            riskGates = source.riskGates.ToDictionary(row => row.riskGateRef, StringComparer.Ordinal);
        }

        public static bool TryCreate(
            NpcCultivationActionWeightProfileData source,
            out NpcCultivationActionWeightProfileRuntime runtime,
            out string failureReason)
        {
            runtime = null;
            if (!IsValidSource(source, out failureReason))
                return false;

            runtime = new NpcCultivationActionWeightProfileRuntime(source);
            return true;
        }

        public NpcCultivationActionDecisionResult Evaluate(NpcCultivationActionDecisionContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var candidates = new List<NpcCultivationActionDecisionCandidate>();
            foreach (string actionId in RequiredActions)
            {
                var action = actions[actionId];
                if (!action.enabled || !context.LegalActionStableIds.Contains(actionId))
                {
                    candidates.Add(new NpcCultivationActionDecisionCandidate
                    {
                        actionStableId = actionId,
                        accepted = false,
                        rejectionReason = IllegalAction,
                        matchedModifierIds = Array.Empty<string>(),
                    });
                    continue;
                }

                var matched = modifiers
                    .Where(row => row.actionStableId == actionId && context.SelectorRefs.Contains(row.selectorRef))
                    .OrderBy(row => Array.IndexOf(SourceOrder, row.sourceKind))
                    .ThenBy(row => row.applicationOrder)
                    .ThenBy(row => row.modifierId, StringComparer.Ordinal)
                    .ToArray();

                if (!PassesRiskGate(action, matched, context, out string riskFailure))
                {
                    candidates.Add(new NpcCultivationActionDecisionCandidate
                    {
                        actionStableId = actionId,
                        accepted = false,
                        rejectionReason = riskFailure,
                        matchedModifierIds = matched.Select(row => row.modifierId).ToArray(),
                    });
                    continue;
                }

                float score = action.baseWeight;
                foreach (string sourceKind in SourceOrder)
                {
                    var group = matched.Where(row => row.sourceKind == sourceKind).ToArray();
                    if (group.Length == 0)
                        continue;

                    var cap = caps[group[0].capPolicyRef];
                    var policy = diminishing[group[0].diminishingPolicyRef];
                    float delta = ApplyDiminishing(group.Sum(row => row.priorityDelta), policy);
                    score += ApplyCap(delta, cap);
                }

                score = ApplyCap(score, caps[action.actionTotalCapPolicyRef]);
                candidates.Add(new NpcCultivationActionDecisionCandidate
                {
                    actionStableId = actionId,
                    accepted = true,
                    score = score,
                    matchedModifierIds = matched.Select(row => row.modifierId).ToArray(),
                });
            }

            var ordered = candidates
                .Where(candidate => candidate.accepted)
                .OrderByDescending(candidate => candidate.score)
                .ThenBy(candidate => candidate.actionStableId, StringComparer.Ordinal)
                .ToArray();
            return new NpcCultivationActionDecisionResult
            {
                candidates = candidates.ToArray(),
                selectedActionStableId = ordered.Length == 0 ? null : ordered[0].actionStableId,
            };
        }

        private bool PassesRiskGate(
            NpcCultivationActionWeightRecord action,
            NpcCultivationWeightModifierRecord[] matched,
            NpcCultivationActionDecisionContext context,
            out string failureReason)
        {
            failureReason = null;
            if (string.IsNullOrEmpty(action.subjectiveRiskGateRef))
                return true;

            var gate = riskGates[action.subjectiveRiskGateRef];
            if (gate.knownEvidenceRefs.Any(reference => !context.SelectorRefs.Contains(reference)) ||
                !context.RiskAssessments.TryGetValue(gate.riskAssessmentRef, out float assessment))
            {
                failureReason = RiskGateRejected;
                return false;
            }

            float threshold = gate.baseRiskThreshold + matched
                .Where(row => row.sourceKind == "LIFESPAN")
                .Sum(row => row.riskThresholdDelta);
            threshold = ApplyCap(threshold, caps[gate.lifespanCapPolicyRef]);
            if (assessment < threshold)
            {
                failureReason = RiskGateRejected;
                return false;
            }
            return true;
        }

        private static float ApplyCap(float value, NpcCultivationWeightCapPolicy policy) =>
            Mathf.Clamp(value, policy.minimum, policy.maximum);

        private static float ApplyDiminishing(float value, NpcCultivationWeightDiminishingPolicy policy)
        {
            float sign = value < 0 ? -1f : 1f;
            float magnitude = Mathf.Abs(value);
            float output = 0;
            foreach (string segment in policy.segments.Split('|'))
            {
                string[] parts = segment.Split('@');
                string[] bounds = parts[0].Split('-');
                float lower = float.Parse(bounds[0], CultureInfo.InvariantCulture);
                float upper = float.Parse(bounds[1], CultureInfo.InvariantCulture);
                float multiplier = float.Parse(parts[1], CultureInfo.InvariantCulture);
                if (magnitude <= lower)
                    continue;
                output += (Mathf.Min(magnitude, upper) - lower) * multiplier;
            }
            return sign * Mathf.Min(output, policy.outputBound);
        }

        private static bool IsValidSource(NpcCultivationActionWeightProfileData source, out string failureReason)
        {
            failureReason = "NPC_WEIGHT_INVALID_RUNTIME_PROFILE";
            if (source == null || source.schemaId != SchemaId || source.schemaVersion != SchemaVersion ||
                string.IsNullOrWhiteSpace(source.profileId) || string.IsNullOrWhiteSpace(source.sourceContentHash) ||
                source.authorityKind != "CSV_SOURCE_SET" ||
                source.sourceContentHash.Length != 64 || source.tieBreakPolicy != "LEXICOGRAPHIC_ASC" ||
                source.actionWeightRows == null || source.modifierRows == null || source.capPolicies == null ||
                source.diminishingPolicies == null || source.riskGates == null || source.recalculationTriggers == null)
                return false;

            if (source.actionWeightRows.Length != RequiredActions.Length || source.actionWeightRows.Any(row => row == null) ||
                source.actionWeightRows.Select(row => row.actionStableId).Distinct(StringComparer.Ordinal).Count() != RequiredActions.Length ||
                !RequiredActions.All(actionId => source.actionWeightRows.Any(row => row.actionStableId == actionId)))
                return false;

            if (source.actionWeightRows.Any(row => string.IsNullOrWhiteSpace(row.recordId) ||
                                                   string.IsNullOrWhiteSpace(row.legalityRuleSetRef) ||
                                                   string.IsNullOrWhiteSpace(row.actionTotalCapPolicyRef) ||
                                                   float.IsNaN(row.baseWeight) || float.IsInfinity(row.baseWeight)))
                return false;

            if (source.modifierRows.Any(row => row == null || !SourceOrder.Contains(row.sourceKind) ||
                                               !RequiredActions.Contains(row.actionStableId) ||
                                               string.IsNullOrWhiteSpace(row.modifierId) || string.IsNullOrWhiteSpace(row.selectorRef) ||
                                               string.IsNullOrWhiteSpace(row.capPolicyRef) || string.IsNullOrWhiteSpace(row.diminishingPolicyRef) ||
                                               float.IsNaN(row.priorityDelta) || float.IsInfinity(row.priorityDelta)))
                return false;

            if (source.capPolicies.Any(row => row == null || string.IsNullOrWhiteSpace(row.capPolicyId) ||
                                               row.minimum > row.maximum ||
                                               (row.scope != "SOURCE_GROUP" && row.scope != "ACTION_TOTAL" && row.scope != "RISK_THRESHOLD")) ||
                source.capPolicies.Select(row => row.capPolicyId).Distinct(StringComparer.Ordinal).Count() != source.capPolicies.Length ||
                source.diminishingPolicies.Any(row => row == null || string.IsNullOrWhiteSpace(row.diminishingPolicyId) ||
                                                       row.scope != "SOURCE_GROUP" || string.IsNullOrWhiteSpace(row.segments) || row.outputBound < 0) ||
                source.diminishingPolicies.Select(row => row.diminishingPolicyId).Distinct(StringComparer.Ordinal).Count() != source.diminishingPolicies.Length ||
                source.riskGates.Any(row => row == null || string.IsNullOrWhiteSpace(row.riskGateRef) ||
                                             row.knownEvidenceRefs == null || string.IsNullOrWhiteSpace(row.riskAssessmentRef) ||
                                             string.IsNullOrWhiteSpace(row.lifespanCapPolicyRef)) ||
                source.riskGates.Select(row => row.riskGateRef).Distinct(StringComparer.Ordinal).Count() != source.riskGates.Length ||
                source.recalculationTriggers.Any(row => row == null || string.IsNullOrWhiteSpace(row.triggerStableId)) ||
                source.recalculationTriggers.Select(row => row.triggerStableId).Distinct(StringComparer.Ordinal).Count() != source.recalculationTriggers.Length)
                return false;

            var capIds = new HashSet<string>(source.capPolicies.Select(row => row.capPolicyId), StringComparer.Ordinal);
            var diminishingIds = new HashSet<string>(source.diminishingPolicies.Select(row => row.diminishingPolicyId), StringComparer.Ordinal);
            var riskGateIds = new HashSet<string>(source.riskGates.Select(row => row.riskGateRef), StringComparer.Ordinal);
            if (source.actionWeightRows.Any(row => !capIds.Contains(row.actionTotalCapPolicyRef) ||
                                                   (!string.IsNullOrEmpty(row.subjectiveRiskGateRef) && !riskGateIds.Contains(row.subjectiveRiskGateRef))) ||
                source.modifierRows.Any(row => !capIds.Contains(row.capPolicyRef) || !diminishingIds.Contains(row.diminishingPolicyRef)) ||
                source.riskGates.Any(row => !capIds.Contains(row.lifespanCapPolicyRef)))
                return false;

            return true;
        }
    }
}
