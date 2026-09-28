using System;
using NUnit.Framework;
using TianZhang.Cultivation.JindanProof;
using TianZhang.Entity;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class CharacterFoundationPurpleMansionTests
    {
        [Test]
        public void CompleteHistorySurvivesExpansionAndSaveRoundTrip()
        {
            FoundationPurpleMansionStateData state = CreateCompleteState(1, 1, 1);
            try
            {
                state.foundationState.expansionGrants = new[]
                {
                    new FoundationExpansionGrant
                    {
                        grantId = "grant_one",
                        sourceItemId = "item_one",
                        capacityEffectBindingId = "capacity_effect_one",
                    },
                };
                state.foundationState.expandedMansionCapacity = 1;
                state.effectBindings = new[]
                {
                    BodyEffect(PurpleMansionKind.Ming, "mansion_ming"),
                    new FoundationEffectBinding
                    {
                        effectBindingId = "capacity_effect_one",
                        carrierKind = FoundationEffectCarrierKind.ExpansionGrant,
                        carrierId = "grant_one",
                        order = 1,
                        trigger = "grant_applied",
                        conditions = Array.Empty<string>(),
                        target = "mansion_capacity",
                        atomicEffectType = "MANSION_CAPACITY_PLUS_ONE",
                        parameters = new[] { "profileRef:fixture" },
                    },
                };

                FoundationPurpleMansionRuntimeState runtime = CreateRuntimeState(state);
                Assert.That(runtime.Stage, Is.EqualTo(FoundationStage.Complete));
                Assert.That(runtime.SelfMansionCapacity, Is.EqualTo(2));
                Assert.That(runtime.CompletionMansionCapacity, Is.EqualTo(1));

                FoundationPurpleMansionSaveData save = runtime.CaptureSaveData();
                Assert.That(FoundationPurpleMansionRuntimeState.TryRestore(
                    save, out FoundationPurpleMansionRuntimeState restored, out string reason), Is.True, reason);
                Assert.That(restored.Stage, Is.EqualTo(FoundationStage.Complete));
                Assert.That(restored.CompletionMansionCapacity, Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(state); }
        }

        [Test]
        public void PausedEmbryoKeepsItsCommittedCapacityAndCycleIdempotence()
        {
            FoundationPurpleMansionStateData state = CreatePausedEmbryoState();
            try
            {
                FoundationPurpleMansionRuntimeState runtime = CreateRuntimeState(state);
                Assert.That(runtime.CommittableMansionCapacity, Is.EqualTo(2));
                Assert.That(runtime.TryRepeatClosedRetreatCycle("cycle_hun_1", true).Succeeded, Is.True);
                Assert.That(runtime.TryRepeatClosedRetreatCycle("cycle_hun_1", true).Succeeded, Is.False);
                Assert.That(runtime.TryRepeatClosedRetreatCycle("unused", false).Succeeded, Is.True);
                Assert.That(runtime.LastClosedRetreatStopReason, Is.EqualTo("INSUFFICIENT_NEXT_CYCLE_RESOURCES"));
            }
            finally { UnityEngine.Object.DestroyImmediate(state); }
        }

        [Test]
        public void JindanLockRejectsAllFoundationAndMansionMutationEntrypoints()
        {
            FoundationPurpleMansionStateData state = CreateCompleteState(1, 1, 1);
            try
            {
                FoundationPurpleMansionRuntimeState runtime = CreateRuntimeState(state);
                FoundationPurpleMansionOperationResult formed = new JindanProofCoordinator()
                    .TryFormFoundationPurpleMansionLock(runtime);
                Assert.That(formed.Succeeded, Is.True);
                Assert.That(runtime.TryNurtureFoundationCycle("cycle_foundation").FailureReason,
                    Is.EqualTo(FoundationPurpleMansionRuntimeState.JindanLockMutation));
                Assert.That(runtime.CanExpandMansionCapacity().FailureReason,
                    Is.EqualTo(FoundationPurpleMansionRuntimeState.JindanLockMutation));
                Assert.That(runtime.TryOpenMansionCycle(PurpleMansionKind.Ming, "cycle_opening").FailureReason,
                    Is.EqualTo(FoundationPurpleMansionRuntimeState.JindanLockMutation));
            }
            finally { UnityEngine.Object.DestroyImmediate(state); }
        }

        private static FoundationPurpleMansionRuntimeState CreateRuntimeState(FoundationPurpleMansionStateData state)
        {
            Assert.That(FoundationPurpleMansionRuntimeState.TryCreate(
                state, out FoundationPurpleMansionRuntimeState runtime, out string reason), Is.True, reason);
            return runtime;
        }

        private static FoundationPurpleMansionStateData CreateCompleteState(
            int naturalCapacity,
            int carryingCapacity,
            int completionCapacity)
        {
            var state = CreateBaseState(FoundationStage.Complete, naturalCapacity, carryingCapacity, completionCapacity);
            state.mansionStates[0] = CompleteMansion(PurpleMansionKind.Ming, "mansion_ming");
            state.effectBindings = new[] { BodyEffect(PurpleMansionKind.Ming, "mansion_ming") };
            state.guardianAbilities = new[] { Guardian(PurpleMansionKind.Ming, "mansion_ming") };
            return state;
        }

        private static FoundationPurpleMansionStateData CreatePausedEmbryoState()
        {
            FoundationPurpleMansionStateData state = CreateBaseState(FoundationStage.Mansion, 2, 2, 0);
            state.mansionStates[0] = CompleteMansion(PurpleMansionKind.Ming, "mansion_ming");
            state.mansionStates[1] = new PurpleMansionStateRecord
            {
                mansionKind = PurpleMansionKind.Hun,
                state = PurpleMansionBuildState.Embryo,
                embryoId = "embryo_hun",
                sourceSpellId = "spell_hun",
                upgradePlanId = "upgrade_hun",
                continuousProgress = 20f,
                progressChannelId = "progress_hun",
                relatedActionStateId = "action_hun",
            };
            state.effectBindings = new[] { BodyEffect(PurpleMansionKind.Ming, "mansion_ming") };
            state.guardianAbilities = new[] { Guardian(PurpleMansionKind.Ming, "mansion_ming") };
            state.cultivationActionState = new CultivationActionStateRecord
            {
                actionStateId = "action_hun",
                actionKind = CultivationActionKind.MansionEmbryoNurture,
                status = CultivationActionStatus.Paused,
                targetRef = "embryo_hun",
                fixedCycleDefinitionId = "cycle_30_days",
                lastStableBoundaryId = "boundary_0",
                committedCycleIds = Array.Empty<string>(),
                progressChannelId = "progress_hun",
                numericProfileRefs = new[] { "cultivation_profile" },
            };
            state.closedRetreatPlan = new ClosedRetreatPlanRecord
            {
                actionStateId = "action_hun",
                targetRef = "embryo_hun",
                stopConditions = new[] { "INSUFFICIENT_NEXT_CYCLE_RESOURCES", "MANUAL_PAUSE" },
            };
            return state;
        }

        private static FoundationPurpleMansionStateData CreateBaseState(
            FoundationStage stage,
            int naturalCapacity,
            int carryingCapacity,
            int completionCapacity)
        {
            return new FoundationPurpleMansionStateData
            {
                schemaId = "foundationPurpleMansionState",
                schemaVersion = 2,
                characterId = "runtime_fixture",
                foundationState = new FoundationStateRecord
                {
                    foundationInstanceId = "foundation_runtime",
                    foundationDefinitionId = "foundation_definition",
                    sourceGongFaId = "gongfa_runtime",
                    stageId = stage,
                    stageCode = (int)stage,
                    continuousProgress = 100f,
                    phaseBoundarySetId = "phase_boundaries",
                    naturalMansionCapacity = naturalCapacity,
                    expansionGrants = Array.Empty<FoundationExpansionGrant>(),
                    expandedMansionCapacity = 0,
                    carryingCapacityProfileId = "carrying_profile",
                    currentMansionCarryingCapacity = carryingCapacity,
                    completionMansionCapacity = completionCapacity,
                },
                mansionStates = new[]
                {
                    NotBuilt(PurpleMansionKind.Ming), NotBuilt(PurpleMansionKind.Hun), NotBuilt(PurpleMansionKind.Shi),
                    NotBuilt(PurpleMansionKind.Wu), NotBuilt(PurpleMansionKind.Yun),
                },
                enhancementNodes = Array.Empty<EnhancementNodeRecord>(),
                jindanLock = new JindanLockRecord { status = JindanLockStatus.PreJindan },
            };
        }

        private static PurpleMansionStateRecord NotBuilt(PurpleMansionKind kind) => new PurpleMansionStateRecord
        {
            mansionKind = kind,
            state = PurpleMansionBuildState.NotBuilt,
        };

        private static PurpleMansionStateRecord CompleteMansion(PurpleMansionKind kind, string mansionId) => new PurpleMansionStateRecord
        {
            mansionKind = kind,
            state = PurpleMansionBuildState.Complete,
            mansionInstanceId = mansionId,
            mansionBodyEffectBindingId = RequiredBodyBinding(kind),
            guardianAbilityInstanceId = "guardian_" + kind.ToString().ToLowerInvariant(),
            sourceSpellId = "spell_" + kind.ToString().ToLowerInvariant(),
            upgradePlanId = "upgrade_" + kind.ToString().ToLowerInvariant(),
            sourceSpellDisposition = "RETAIN",
        };

        private static FoundationEffectBinding BodyEffect(PurpleMansionKind kind, string mansionId) => new FoundationEffectBinding
        {
            effectBindingId = RequiredBodyBinding(kind),
            carrierKind = FoundationEffectCarrierKind.MansionBody,
            carrierId = mansionId,
            order = 1,
            trigger = "fixture_trigger",
            conditions = Array.Empty<string>(),
            target = "fixture_target",
            atomicEffectType = "fixture_atomic",
            parameters = new[] { "profileRef:fixture" },
        };

        private static GuardianAbilityRecord Guardian(PurpleMansionKind kind, string mansionId) => new GuardianAbilityRecord
        {
            abilityInstanceId = "guardian_" + kind.ToString().ToLowerInvariant(),
            abilityDefinitionId = "ability_" + kind.ToString().ToLowerInvariant(),
            mansionInstanceId = mansionId,
            sourceSpellId = "spell_" + kind.ToString().ToLowerInvariant(),
            upgradePlanId = "upgrade_" + kind.ToString().ToLowerInvariant(),
            sourceSpellDisposition = "RETAIN",
            form = GuardianAbilityForm.Passive,
            effectBindingIds = Array.Empty<string>(),
        };

        private static string RequiredBodyBinding(PurpleMansionKind kind)
        {
            return kind switch
            {
                PurpleMansionKind.Ming => "MANSION_BODY_MING_YUAN_HUIHU",
                PurpleMansionKind.Hun => "MANSION_BODY_HUN_LINGTAI_DINGPO",
                PurpleMansionKind.Shi => "MANSION_BODY_SHI_SHENGUAN_RUWEI",
                PurpleMansionKind.Wu => "MANSION_BODY_WU_WUJI_SHANCHENG",
                PurpleMansionKind.Yun => "MANSION_BODY_YUN_JIYUAN_SHIZHAO",
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }
    }
}
