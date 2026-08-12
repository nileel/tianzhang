using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TianZhang.Cultivation;
using TianZhang.Entity;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class NpcCultivationUseCaseTests
    {
        private NpcCultivationActionWeightProfileData profileData;

        [TearDown]
        public void TearDown()
        {
            if (profileData != null) UnityEngine.Object.DestroyImmediate(profileData);
        }

        [Test]
        public void DeclaredTriggerSelectsAndReturnsASeparateUpdatedSnapshot()
        {
            FoundationPurpleMansionSaveData original = CreateState();
            NpcCultivationResult result = new NpcCultivationUseCase().Recalculate(
                original,
                CreateRequest("CURRENT_ACTION_STABLE_BOUNDARY"));

            Assert.That(result.Succeeded, Is.True, result.FailureReason);
            Assert.That(result.SelectedActionStableId, Is.EqualTo("FOUNDATION_NURTURE"));
            Assert.That(original.cultivationActionState, Is.Null);
            Assert.That(result.State.cultivationActionState.actionStateId, Is.EqualTo("action_foundation_nurture"));
            Assert.That(result.State.cultivationActionState.status, Is.EqualTo(CultivationActionStatus.Active));
        }

        [Test]
        public void UndeclaredTriggerFailsWithoutReturningCandidateState()
        {
            NpcCultivationResult result = new NpcCultivationUseCase().Recalculate(
                CreateState(),
                CreateRequest("WORLD_DAY_ADVANCED"));

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(NpcCultivationUseCaseReasons.UndeclaredTrigger));
            Assert.That(result.State, Is.Null);
        }

        private NpcCultivationRequest CreateRequest(string trigger)
        {
            profileData = ScriptableObject.CreateInstance<NpcCultivationActionWeightProfileData>();
            profileData.schemaId = NpcCultivationActionWeightProfileRuntime.SchemaId;
            profileData.schemaVersion = NpcCultivationActionWeightProfileRuntime.SchemaVersion;
            profileData.profileId = "npc-test-profile";
            profileData.sourceContentHash = new string('a', 64);
            profileData.authorityKind = "CSV_SOURCE_SET";
            profileData.tieBreakPolicy = "LEXICOGRAPHIC_ASC";
            string[] actions =
            {
                "FOUNDATION_TRIAL",
                "FOUNDATION_NURTURE",
                "MANSION_EMBRYO_NURTURE",
                "MANSION_OPENING_TRIAL",
                "JINDAN_PROOF",
            };
            profileData.actionWeightRows = actions.Select(action => new NpcCultivationActionWeightRecord
            {
                recordId = "record_" + action.ToLowerInvariant(),
                actionStableId = action,
                legalityRuleSetRef = "rules_" + action.ToLowerInvariant(),
                baseWeight = 100f,
                enabled = true,
                actionTotalCapPolicyRef = "action_total",
            }).ToArray();
            profileData.modifierRows = Array.Empty<NpcCultivationWeightModifierRecord>();
            profileData.capPolicies = new[]
            {
                new NpcCultivationWeightCapPolicy
                {
                    capPolicyId = "action_total",
                    scope = "ACTION_TOTAL",
                    minimum = 0f,
                    maximum = 100f,
                },
            };
            profileData.diminishingPolicies = Array.Empty<NpcCultivationWeightDiminishingPolicy>();
            profileData.riskGates = Array.Empty<NpcCultivationRiskGate>();
            profileData.recalculationTriggers = new[]
            {
                new NpcCultivationRecalculationTrigger { triggerStableId = "CURRENT_ACTION_STABLE_BOUNDARY" },
            };
            Assert.That(NpcCultivationActionWeightProfileRuntime.TryCreate(
                profileData,
                out NpcCultivationActionWeightProfileRuntime profile,
                out string reason), Is.True, reason);
            return new NpcCultivationRequest
            {
                TriggerStableId = trigger,
                WeightProfile = profile,
                Candidates = new[]
                {
                    new NpcCultivationCandidate
                    {
                        ActionStableId = "FOUNDATION_NURTURE",
                        HardRequirementsMet = true,
                        ActionStateId = "action_foundation_nurture",
                        TargetRef = "foundation_npc",
                        FixedCycleDefinitionId = "cycle_foundation_nurture",
                        InitialStableBoundaryId = "boundary_started",
                        ProgressChannelId = "progress_foundation_nurture",
                        NumericProfileRefs = new[] { "numeric_foundation_nurture" },
                    },
                },
                SelectorRefs = Array.Empty<string>(),
                RiskAssessments = new Dictionary<string, float>(),
            };
        }

        private static FoundationPurpleMansionSaveData CreateState()
        {
            return new FoundationPurpleMansionSaveData
            {
                schemaId = "foundationPurpleMansionState",
                schemaVersion = 1,
                characterId = "npc_cultivator",
                foundationState = new FoundationStateRecord
                {
                    foundationInstanceId = "foundation_npc",
                    foundationDefinitionId = "foundation_definition",
                    sourceGongFaId = "gongfa_npc",
                    phase = FoundationPhase.Phase1,
                    continuousProgress = 100f,
                    phaseBoundarySetId = "phase_boundaries",
                    naturalMansionCapacity = 1,
                    releasedNaturalCapacity = 0,
                    expansionGrants = Array.Empty<FoundationExpansionGrant>(),
                    expandedMansionCapacity = 0,
                    totalMansionCapacity = 0,
                },
                mansionStates = Enum.GetValues(typeof(PurpleMansionKind)).Cast<PurpleMansionKind>()
                    .Select(kind => new PurpleMansionStateRecord
                    {
                        mansionKind = kind,
                        state = PurpleMansionBuildState.NotBuilt,
                    }).ToArray(),
                effectBindings = Array.Empty<FoundationEffectBinding>(),
                guardianAbilities = Array.Empty<GuardianAbilityRecord>(),
                enhancementNodes = Array.Empty<EnhancementNodeRecord>(),
                jindanLock = new JindanLockRecord { status = JindanLockStatus.PreJindan },
            };
        }
    }
}
