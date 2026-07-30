using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TianZhang.Cultivation;
using TianZhang.Cultivation.JindanProof;
using TianZhang.Entity;
using TianZhang.Game;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class NpcCultivationActionCoordinatorTests
    {
        [TearDown]
        public void TearDown()
        {
            if (GameSession.Instance != null)
                UnityEngine.Object.DestroyImmediate(GameSession.Instance.gameObject);
        }

        [TestCase(CultivationActionKind.FoundationTrial, "foundation_npc")]
        [TestCase(CultivationActionKind.FoundationNurture, "foundation_npc")]
        [TestCase(CultivationActionKind.MansionEmbryoNurture, "embryo_hun")]
        [TestCase(CultivationActionKind.MansionOpeningTrial, "embryo_hun")]
        [TestCase(CultivationActionKind.JindanProof, "foundation_npc")]
        public void EveryActionUsesTheSameStableLifecycle(
            CultivationActionKind actionKind,
            string targetRef)
        {
            FoundationPurpleMansionRuntimeState state = CreateRuntime(
                actionKind != CultivationActionKind.JindanProof);

            Assert.That(state.TryStartCultivationAction(
                actionKind,
                "action_" + actionKind,
                targetRef,
                "cycle_definition_01",
                "boundary_started",
                "progress_01",
                new[] { "numeric_profile_01" }).Succeeded, Is.True);
            Assert.That(state.TryAdvanceCultivationAction("boundary_midpoint").Succeeded, Is.True);
            Assert.That(state.TryCommitCultivationActionCycle("world_day_12").Succeeded, Is.True);
            Assert.That(state.TryCommitCultivationActionCycle("world_day_12").Succeeded, Is.False);
            Assert.That(state.TryPauseCultivationAction("RESOURCE_INSUFFICIENT").Succeeded, Is.True);
            Assert.That(state.TryTerminateCultivationAction("ENVIRONMENT_INVALIDATED").Succeeded, Is.True);

            FoundationPurpleMansionSaveData saved = state.CaptureSaveData();
            Assert.That(saved.cultivationActionState.actionKind, Is.EqualTo(actionKind));
            Assert.That(saved.cultivationActionState.targetRef, Is.EqualTo(targetRef));
            Assert.That(saved.cultivationActionState.committedCycleIds,
                Is.EquivalentTo(new[] { "world_day_12" }));
            Assert.That(saved.lastClosedRetreatStopReason,
                Is.EqualTo("ENVIRONMENT_INVALIDATED"));
            Assert.That(FoundationPurpleMansionRuntimeState.TryRestore(
                saved,
                out FoundationPurpleMansionRuntimeState restored,
                out string failureReason), Is.True, failureReason);
            Assert.That(restored.GetCultivationActionState().status,
                Is.EqualTo(CultivationActionStatus.Terminated));
        }

        [Test]
        public void RecalculationFiltersHardGatesBeforeStableDataRanking()
        {
            FoundationPurpleMansionRuntimeState state = CreateRuntime(includeEmbryo: true);
            NpcCultivationActionRecalculationResult result = new NpcCultivationActionCoordinator()
                .Recalculate(state, Request(
                    "CURRENT_ACTION_STABLE_BOUNDARY",
                    Candidate("JINDAN_PROOF", "foundation_npc", hardRequirementsMet: true,
                        proofDecision: ReadyProofDecision()),
                    Candidate("FOUNDATION_NURTURE", "foundation_npc", hardRequirementsMet: true)));

            Assert.That(result.Succeeded, Is.True, result.FailureReason);
            Assert.That(result.SelectedActionStableId, Is.EqualTo("FOUNDATION_NURTURE"));
            Assert.That(result.Ranking.candidates.Single(candidate =>
                candidate.actionStableId == "JINDAN_PROOF").rejectionReason,
                Is.EqualTo(NpcCultivationActionWeightProfileRuntime.IllegalAction));
        }

        [Test]
        public void FixedInputsUseLexicographicTieBreakWithoutHardcodedActionPriority()
        {
            FoundationPurpleMansionRuntimeState state = CreateRuntime(includeEmbryo: true);
            NpcCultivationActionRecalculationResult result = new NpcCultivationActionCoordinator()
                .Recalculate(state, Request(
                    "CURRENT_ACTION_STABLE_BOUNDARY",
                    Candidate("FOUNDATION_NURTURE", "foundation_npc", hardRequirementsMet: true),
                    Candidate("FOUNDATION_TRIAL", "foundation_npc", hardRequirementsMet: true)));

            Assert.That(result.Succeeded, Is.True, result.FailureReason);
            Assert.That(result.SelectedActionStableId, Is.EqualTo("FOUNDATION_NURTURE"));
            Assert.That(state.GetCultivationActionState().actionStateId,
                Is.EqualTo("action_foundation_nurture"));
        }

        [Test]
        public void OnlyDeclaredEventsCanRecalculateAndResourcesCanRejectAllActions()
        {
            FoundationPurpleMansionRuntimeState state = CreateRuntime(includeEmbryo: true);
            var coordinator = new NpcCultivationActionCoordinator();

            NpcCultivationActionRecalculationResult undeclared = coordinator.Recalculate(
                state,
                Request("WORLD_DAY_ADVANCED",
                    Candidate("FOUNDATION_NURTURE", "foundation_npc", hardRequirementsMet: true)));
            Assert.That(undeclared.Succeeded, Is.False);
            Assert.That(undeclared.FailureReason,
                Is.EqualTo(NpcCultivationActionCoordinator.UndeclaredTrigger));

            NpcCultivationActionRecalculationResult resourcesMissing = coordinator.Recalculate(
                state,
                Request("RESOURCE_AVAILABILITY_CHANGED",
                    Candidate("FOUNDATION_NURTURE", "foundation_npc", hardRequirementsMet: false)));
            Assert.That(resourcesMissing.Succeeded, Is.False);
            Assert.That(resourcesMissing.FailureReason,
                Is.EqualTo(NpcCultivationActionCoordinator.NoLegalAction));
            Assert.That(state.GetCultivationActionState(), Is.Null);
        }

        [Test]
        public void GameSessionDoesNotScanOnWorldDayAndPersistsSelectedNpcAction()
        {
            var sessionObject = new GameObject("NpcCultivationSession");
            GameSession session = sessionObject.AddComponent<GameSession>();
            FoundationPurpleMansionRuntimeState initial = CreateRuntime(includeEmbryo: true);
            session.NpcStates.Set(new NpcStateSnapshot(
                "npc_cultivator",
                "jiangzuo_hub",
                Steps(),
                initial.CaptureSaveData()));

            session.AdvanceWorldDay();
            Assert.That(session.NpcStates.TryGet("npc_cultivator", out NpcStateSnapshot unchanged), Is.True);
            Assert.That(unchanged.FoundationPurpleMansionState.cultivationActionState, Is.Null);

            NpcCultivationActionRecalculationResult result = session.RecalculateNpcCultivation(
                "npc_cultivator",
                new NpcCultivationActionCoordinator(),
                Request("RESOURCE_AVAILABILITY_CHANGED",
                    Candidate("FOUNDATION_NURTURE", "foundation_npc", hardRequirementsMet: true)));
            Assert.That(result.Succeeded, Is.True, result.FailureReason);
            Assert.That(session.NpcStates.TryGet("npc_cultivator", out NpcStateSnapshot updated), Is.True);
            Assert.That(updated.FoundationPurpleMansionState.cultivationActionState.actionStateId,
                Is.EqualTo("action_foundation_nurture"));
        }

        private static NpcCultivationActionRecalculationRequest Request(
            string triggerStableId,
            params NpcCultivationActionCandidate[] candidates)
        {
            return new NpcCultivationActionRecalculationRequest
            {
                TriggerStableId = triggerStableId,
                WeightProfile = CreateWeightRuntime(),
                Candidates = candidates,
                SelectorRefs = Array.Empty<string>(),
                RiskAssessments = new Dictionary<string, float>(),
            };
        }

        private static NpcCultivationActionCandidate Candidate(
            string actionStableId,
            string targetRef,
            bool hardRequirementsMet,
            NpcProofDecision proofDecision = null)
        {
            return new NpcCultivationActionCandidate
            {
                ActionStableId = actionStableId,
                HardRequirementsMet = hardRequirementsMet,
                ActionStateId = "action_" + actionStableId.ToLowerInvariant(),
                TargetRef = targetRef,
                FixedCycleDefinitionId = "cycle_" + actionStableId.ToLowerInvariant(),
                InitialStableBoundaryId = "boundary_started",
                ProgressChannelId = "progress_" + actionStableId.ToLowerInvariant(),
                NumericProfileRefs = new[] { "numeric_" + actionStableId.ToLowerInvariant() },
                JindanProofDecision = proofDecision,
            };
        }

        private static NpcCultivationActionWeightProfileRuntime CreateWeightRuntime()
        {
            string[] actions =
            {
                "FOUNDATION_TRIAL",
                "FOUNDATION_NURTURE",
                "MANSION_EMBRYO_NURTURE",
                "MANSION_OPENING_TRIAL",
                "JINDAN_PROOF",
            };
            var data = ScriptableObject.CreateInstance<NpcCultivationActionWeightProfileData>();
            data.schemaId = NpcCultivationActionWeightProfileRuntime.SchemaId;
            data.schemaVersion = NpcCultivationActionWeightProfileRuntime.SchemaVersion;
            data.profileId = "npc-test-profile";
            data.sourceContentHash = new string('a', 64);
            data.authorityKind = "CSV_SOURCE_SET";
            data.tieBreakPolicy = "LEXICOGRAPHIC_ASC";
            data.actionWeightRows = actions.Select(action =>
                new NpcCultivationActionWeightRecord
                {
                    recordId = "record_" + action.ToLowerInvariant(),
                    actionStableId = action,
                    legalityRuleSetRef = "rule_" + action.ToLowerInvariant(),
                    baseWeight = 100f,
                    enabled = true,
                    actionTotalCapPolicyRef = "action_total",
                }).ToArray();
            data.modifierRows = Array.Empty<NpcCultivationWeightModifierRecord>();
            data.capPolicies = new[]
            {
                new NpcCultivationWeightCapPolicy
                {
                    capPolicyId = "action_total",
                    scope = "ACTION_TOTAL",
                    minimum = 0f,
                    maximum = 100f,
                },
            };
            data.diminishingPolicies = Array.Empty<NpcCultivationWeightDiminishingPolicy>();
            data.riskGates = Array.Empty<NpcCultivationRiskGate>();
            data.recalculationTriggers = new[]
            {
                new NpcCultivationRecalculationTrigger
                {
                    triggerStableId = "CURRENT_ACTION_STABLE_BOUNDARY",
                },
                new NpcCultivationRecalculationTrigger
                {
                    triggerStableId = "RESOURCE_AVAILABILITY_CHANGED",
                },
            };

            Assert.That(NpcCultivationActionWeightProfileRuntime.TryCreate(
                data,
                out NpcCultivationActionWeightProfileRuntime runtime,
                out string failureReason), Is.True, failureReason);
            UnityEngine.Object.DestroyImmediate(data);
            return runtime;
        }

        private static FoundationPurpleMansionRuntimeState CreateRuntime(bool includeEmbryo)
        {
            var data = ScriptableObject.CreateInstance<FoundationPurpleMansionStateData>();
            data.schemaId = "foundationPurpleMansionState";
            data.schemaVersion = 1;
            data.characterId = "npc_cultivator";
            data.foundationState = new FoundationStateRecord
            {
                foundationInstanceId = "foundation_npc",
                foundationDefinitionId = "foundation_definition",
                sourceGongFaId = "gongfa_npc",
                phase = FoundationPhase.Phase4,
                continuousProgress = 400f,
                phaseBoundarySetId = "phase_boundaries",
                naturalMansionCapacity = includeEmbryo ? 2 : 1,
                releasedNaturalCapacity = includeEmbryo ? 2 : 1,
                expansionGrants = Array.Empty<FoundationExpansionGrant>(),
                expandedMansionCapacity = 0,
                totalMansionCapacity = includeEmbryo ? 2 : 1,
            };
            data.mansionStates = new[]
            {
                CompleteMing(),
                includeEmbryo ? EmbryoHun() : NotBuilt(PurpleMansionKind.Hun),
                NotBuilt(PurpleMansionKind.Shi),
                NotBuilt(PurpleMansionKind.Wu),
                NotBuilt(PurpleMansionKind.Yun),
            };
            data.effectBindings = new[]
            {
                new FoundationEffectBinding
                {
                    effectBindingId = "MANSION_BODY_MING_YUAN_HUIHU",
                    carrierKind = FoundationEffectCarrierKind.MansionBody,
                    carrierId = "mansion_ming",
                    order = 1,
                    trigger = "fixture",
                    conditions = Array.Empty<string>(),
                    target = "fixture",
                    atomicEffectType = "fixture",
                    parameters = Array.Empty<string>(),
                },
            };
            data.guardianAbilities = new[]
            {
                new GuardianAbilityRecord
                {
                    abilityInstanceId = "guardian_ming",
                    abilityDefinitionId = "ability_ming",
                    mansionInstanceId = "mansion_ming",
                    sourceSpellId = "spell_ming",
                    upgradePlanId = "upgrade_ming",
                    sourceSpellDisposition = "RETAIN",
                    form = GuardianAbilityForm.Passive,
                    effectBindingIds = Array.Empty<string>(),
                },
            };
            data.enhancementNodes = Array.Empty<EnhancementNodeRecord>();
            data.jindanLock = new JindanLockRecord { status = JindanLockStatus.PreJindan };

            Assert.That(FoundationPurpleMansionRuntimeState.TryCreate(
                data,
                out FoundationPurpleMansionRuntimeState runtime,
                out string failureReason), Is.True, failureReason);
            UnityEngine.Object.DestroyImmediate(data);
            return runtime;
        }

        private static PurpleMansionStateRecord CompleteMing()
        {
            return new PurpleMansionStateRecord
            {
                mansionKind = PurpleMansionKind.Ming,
                state = PurpleMansionBuildState.Complete,
                mansionInstanceId = "mansion_ming",
                mansionBodyEffectBindingId = "MANSION_BODY_MING_YUAN_HUIHU",
                guardianAbilityInstanceId = "guardian_ming",
                sourceSpellId = "spell_ming",
                upgradePlanId = "upgrade_ming",
                sourceSpellDisposition = "RETAIN",
            };
        }

        private static PurpleMansionStateRecord EmbryoHun()
        {
            return new PurpleMansionStateRecord
            {
                mansionKind = PurpleMansionKind.Hun,
                state = PurpleMansionBuildState.Embryo,
                embryoId = "embryo_hun",
                sourceSpellId = "spell_hun",
                upgradePlanId = "upgrade_hun",
                sourceSpellDisposition = "RETAIN",
                continuousProgress = 10f,
                progressChannelId = "progress_hun",
                relatedActionStateId = "prior_action_hun",
            };
        }

        private static PurpleMansionStateRecord NotBuilt(PurpleMansionKind kind)
        {
            return new PurpleMansionStateRecord
            {
                mansionKind = kind,
                state = PurpleMansionBuildState.NotBuilt,
            };
        }

        private static StateStepSnapshot Steps()
        {
            return new StateStepSnapshot(false, false, false, false, false, false, false);
        }

        private static NpcProofDecision ReadyProofDecision()
        {
            return new NpcJindanProofPolicy(70, 55, 40, 180, 20).Evaluate(
                new NpcProofDecisionInput
                {
                    IsPersistentNpc = true,
                    IsPurpleMansionComplete = true,
                    HardRequirementsMet = true,
                    KnowsVacancy = true,
                    KnowsUsableSite = true,
                    HasCompatibleCarrier = true,
                    HasFacilities = true,
                    HasResources = true,
                    HasGuard = true,
                    RiskDisposition = NpcRiskDisposition.Normal,
                    SubjectiveSuccessPercent = 80,
                    DaysOfLifeRemaining = 1000,
                });
        }
    }
}
