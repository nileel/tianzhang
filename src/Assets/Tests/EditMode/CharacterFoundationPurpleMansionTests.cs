using System;
using NUnit.Framework;
using TianZhang.Cultivation.JindanProof;
using TianZhang.Entity;
using UnityEngine;

using TianZhang.Spatial;

namespace TianZhang.Tests
{
    public sealed class CharacterFoundationPurpleMansionTests
    {
        [Test]
        public void CharacterFromDataUsesFoundationRootWithoutLegacyMansionSlotFallback()
        {
            FoundationPurpleMansionStateData state = CreateCompleteState();
            CharacterData data = CreateCharacterData(state);
            try
            {
                var character = Character.FromData(data, new HexCoord(0, 0));

                Assert.IsTrue(character.HasFoundationPurpleMansionState);
                Assert.AreEqual(FoundationPhase.Phase4, character.FoundationPurpleMansionState.Phase);
                Assert.AreEqual(400f, character.FoundationPurpleMansionState.ContinuousProgress);
                Assert.AreEqual(1, character.FoundationPurpleMansionState.TotalMansionCapacity);
                Assert.AreEqual(PurpleMansionBuildState.Complete,
                    character.FoundationPurpleMansionState.GetMansionBuildState(PurpleMansionKind.Ming));
                Assert.AreEqual("guardian_ming",
                    character.FoundationPurpleMansionState.GetGuardianAbilityInstanceId(PurpleMansionKind.Ming));
                Assert.AreEqual(5, character.MaxSpellSlots);
                Assert.AreEqual(2, character.MaxSkillSlots);
            }
            finally
            {
                Destroy(data, state);
            }
        }

        [Test]
        public void MaximumTwoExpansionGrantsProvideFiveMansionsButCannotAddAnotherGrant()
        {
            FoundationPurpleMansionStateData state = CreateMaximumCapacityState();
            try
            {
                Assert.IsTrue(FoundationPurpleMansionRuntimeState.TryCreate(
                    state,
                    out FoundationPurpleMansionRuntimeState runtimeState,
                    out string failureReason), failureReason);

                Assert.AreEqual(3, runtimeState.NaturalMansionCapacity);
                Assert.AreEqual(2, runtimeState.ExpandedMansionCapacity);
                Assert.AreEqual(5, runtimeState.TotalMansionCapacity);
                Assert.AreEqual(PurpleMansionBuildState.Complete,
                    runtimeState.GetMansionBuildState(PurpleMansionKind.Yun));

                FoundationPurpleMansionOperationResult result = runtimeState.CanExpandMansionCapacity();
                Assert.IsFalse(result.Succeeded);
                Assert.AreEqual(FoundationPurpleMansionRuntimeState.CapacityOverflow, result.FailureReason);
            }
            finally
            {
                Destroy(state);
            }
        }

        [Test]
        public void ClosedRetreatRepeatsOnlyItsCurrentActionAndOpeningFailureKeepsEmbryo()
        {
            FoundationPurpleMansionStateData state = CreatePausedEmbryoState();
            CharacterData data = CreateCharacterData(state);
            try
            {
                var character = Character.FromData(data, new HexCoord(0, 0));

                FoundationPurpleMansionOperationResult stopped =
                    character.TryRepeatClosedRetreatCycle("unused", false);
                Assert.IsTrue(stopped.Succeeded);
                Assert.AreEqual("INSUFFICIENT_NEXT_CYCLE_RESOURCES",
                    character.FoundationPurpleMansionState.LastClosedRetreatStopReason);

                FoundationPurpleMansionOperationResult committed =
                    character.TryRepeatClosedRetreatCycle("cycle_hun_1", true);
                Assert.IsTrue(committed.Succeeded);
                Assert.AreEqual(CultivationActionStatus.Active,
                    character.FoundationPurpleMansionState.GetCultivationActionState().status);
                Assert.AreEqual(1,
                    character.FoundationPurpleMansionState.GetCultivationActionState().committedCycleIds.Length);
                Assert.IsFalse(character.TryRepeatClosedRetreatCycle("cycle_hun_1", true).Succeeded);

                state.cultivationActionState.actionKind = CultivationActionKind.MansionOpeningTrial;
                CharacterData openingData = CreateCharacterData(state);
                try
                {
                    var openingCharacter = Character.FromData(openingData, new HexCoord(0, 0));
                    FoundationPurpleMansionOperationResult failed =
                        openingCharacter.TryFailMansionOpeningTrial(PurpleMansionKind.Hun);
                    Assert.IsTrue(failed.Succeeded);
                    Assert.AreEqual(PurpleMansionBuildState.Embryo,
                        openingCharacter.FoundationPurpleMansionState.GetMansionBuildState(PurpleMansionKind.Hun));
                    Assert.IsNull(openingCharacter.FoundationPurpleMansionState
                        .GetGuardianAbilityInstanceId(PurpleMansionKind.Hun));
                    Assert.AreEqual(CultivationActionStatus.Failed,
                        openingCharacter.FoundationPurpleMansionState.GetCultivationActionState().status);
                }
                finally
                {
                    Destroy(openingData);
                }
            }
            finally
            {
                Destroy(data, state);
            }
        }

        [Test]
        public void RuntimeSaveDataRoundTripPreservesGuardianNodesStopReasonAndCommittedCycles()
        {
            FoundationPurpleMansionStateData state = CreatePausedEmbryoState();
            state.enhancementNodes = new[]
            {
                new EnhancementNodeRecord
                {
                    nodeId = "node_ming_1",
                    abilityInstanceId = "guardian_ming",
                    nodeKind = EnhancementNodeKind.Cultivation,
                    requirements = Array.Empty<string>(),
                    effectBindingIds = Array.Empty<string>(),
                },
            };
            CharacterData data = CreateCharacterData(state);
            try
            {
                var character = Character.FromData(data, new HexCoord(0, 0));
                Assert.IsTrue(character.TryRepeatClosedRetreatCycle("unused", false).Succeeded);

                FoundationPurpleMansionSaveData pausedSave =
                    character.CaptureFoundationPurpleMansionSaveData();
                Assert.IsTrue(FoundationPurpleMansionRuntimeState.TryRestore(
                    pausedSave,
                    out FoundationPurpleMansionRuntimeState pausedState,
                    out string failureReason), failureReason);
                Assert.AreEqual("INSUFFICIENT_NEXT_CYCLE_RESOURCES",
                    pausedState.LastClosedRetreatStopReason);
                Assert.AreEqual("guardian_ming", pausedState.GetGuardianAbilities()[0].abilityInstanceId);
                Assert.AreEqual("node_ming_1", pausedState.GetEnhancementNodes()[0].nodeId);

                Assert.IsTrue(pausedState.TryRepeatClosedRetreatCycle("cycle_hun_1", true).Succeeded);
                FoundationPurpleMansionSaveData committedSave = pausedState.CaptureSaveData();
                Assert.IsTrue(FoundationPurpleMansionRuntimeState.TryRestore(
                    committedSave,
                    out FoundationPurpleMansionRuntimeState restoredState,
                    out failureReason), failureReason);
                Assert.IsFalse(restoredState.TryRepeatClosedRetreatCycle("cycle_hun_1", true).Succeeded);
            }
            finally
            {
                Destroy(data, state);
            }
        }

        [Test]
        public void JindanCoordinatorLocksFoundationNurtureExpansionAndMansionOpening()
        {
            FoundationPurpleMansionStateData state = CreateCompleteState();
            CharacterData data = CreateCharacterData(state);
            try
            {
                var character = Character.FromData(data, new HexCoord(0, 0));
                var coordinator = new JindanProofCoordinator();

                FoundationPurpleMansionOperationResult formed =
                    coordinator.TryFormFoundationPurpleMansionLock(character);
                Assert.IsTrue(formed.Succeeded);
                Assert.IsTrue(character.FoundationPurpleMansionState.IsJindanFormed);

                AssertLocked(character.TryNurtureFoundationCycle("cycle_foundation"));
                AssertLocked(character.CanExpandMansionCapacity());
                AssertLocked(character.TryOpenMansionCycle(PurpleMansionKind.Ming, "cycle_opening"));
            }
            finally
            {
                Destroy(data, state);
            }
        }

        private static void AssertLocked(FoundationPurpleMansionOperationResult result)
        {
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(FoundationPurpleMansionRuntimeState.JindanLockMutation, result.FailureReason);
        }

        private static CharacterData CreateCharacterData(FoundationPurpleMansionStateData state)
        {
            var data = ScriptableObject.CreateInstance<CharacterData>();
            data.charName = "runtime_fixture";
            data.realmMultiplier = 3f;
            data.foundationPurpleMansionState = state;
            data.developedMansions = new[] { "命府", "气府" };
            data.mansionBindings = new[] { "命府" };
            return data;
        }

        private static FoundationPurpleMansionStateData CreateCompleteState()
        {
            var state = CreateBaseState(FoundationPhase.Phase4, 1, 1, 1);
            state.mansionStates[0] = CompleteMansion(PurpleMansionKind.Ming);
            state.effectBindings = new[] { BodyEffect(PurpleMansionKind.Ming, "mansion_ming") };
            state.guardianAbilities = new[] { Guardian(PurpleMansionKind.Ming, "mansion_ming") };
            return state;
        }

        private static FoundationPurpleMansionStateData CreateMaximumCapacityState()
        {
            var state = CreateBaseState(FoundationPhase.Phase4, 3, 3, 5);
            state.foundationState.expansionGrants = new[]
            {
                new FoundationExpansionGrant
                {
                    grantId = "grant_one",
                    sourceItemId = "item_one",
                    capacityEffectBindingId = "capacity_effect_one",
                },
                new FoundationExpansionGrant
                {
                    grantId = "grant_two",
                    sourceItemId = "item_two",
                    capacityEffectBindingId = "capacity_effect_two",
                },
            };
            state.foundationState.expandedMansionCapacity = 2;
            PurpleMansionKind[] kinds =
            {
                PurpleMansionKind.Ming,
                PurpleMansionKind.Hun,
                PurpleMansionKind.Shi,
                PurpleMansionKind.Wu,
                PurpleMansionKind.Yun,
            };
            state.mansionStates = new PurpleMansionStateRecord[kinds.Length];
            state.effectBindings = new FoundationEffectBinding[kinds.Length + 2];
            state.guardianAbilities = new GuardianAbilityRecord[kinds.Length];
            for (int index = 0; index < kinds.Length; index++)
            {
                string mansionId = "mansion_" + kinds[index].ToString().ToLowerInvariant();
                state.mansionStates[index] = CompleteMansion(kinds[index], mansionId);
                state.effectBindings[index] = BodyEffect(kinds[index], mansionId);
                state.guardianAbilities[index] = Guardian(kinds[index], mansionId);
            }
            state.effectBindings[5] = CapacityEffect("capacity_effect_one", "grant_one");
            state.effectBindings[6] = CapacityEffect("capacity_effect_two", "grant_two");
            return state;
        }

        private static FoundationPurpleMansionStateData CreatePausedEmbryoState()
        {
            var state = CreateBaseState(FoundationPhase.Phase3, 2, 2, 2);
            state.mansionStates[0] = CompleteMansion(PurpleMansionKind.Ming);
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
            FoundationPhase phase,
            int naturalCapacity,
            int releasedCapacity,
            int totalCapacity)
        {
            var state = ScriptableObject.CreateInstance<FoundationPurpleMansionStateData>();
            state.schemaId = "foundationPurpleMansionState";
            state.schemaVersion = 1;
            state.characterId = "runtime_fixture";
            state.foundationState = new FoundationStateRecord
            {
                foundationInstanceId = "foundation_runtime",
                foundationDefinitionId = "foundation_definition",
                sourceGongFaId = "gongfa_runtime",
                phase = phase,
                continuousProgress = phase == FoundationPhase.Phase4 ? 400f : 250f,
                phaseBoundarySetId = "phase_boundaries",
                naturalMansionCapacity = naturalCapacity,
                releasedNaturalCapacity = releasedCapacity,
                expansionGrants = Array.Empty<FoundationExpansionGrant>(),
                expandedMansionCapacity = 0,
                totalMansionCapacity = totalCapacity,
            };
            state.mansionStates = new[]
            {
                NotBuilt(PurpleMansionKind.Ming),
                NotBuilt(PurpleMansionKind.Hun),
                NotBuilt(PurpleMansionKind.Shi),
                NotBuilt(PurpleMansionKind.Wu),
                NotBuilt(PurpleMansionKind.Yun),
            };
            state.effectBindings = Array.Empty<FoundationEffectBinding>();
            state.guardianAbilities = Array.Empty<GuardianAbilityRecord>();
            state.enhancementNodes = Array.Empty<EnhancementNodeRecord>();
            state.jindanLock = new JindanLockRecord { status = JindanLockStatus.PreJindan };
            return state;
        }

        private static PurpleMansionStateRecord NotBuilt(PurpleMansionKind kind)
        {
            return new PurpleMansionStateRecord
            {
                mansionKind = kind,
                state = PurpleMansionBuildState.NotBuilt,
            };
        }

        private static PurpleMansionStateRecord CompleteMansion(PurpleMansionKind kind, string mansionId = "mansion_ming")
        {
            string lowerKind = kind.ToString().ToLowerInvariant();
            return new PurpleMansionStateRecord
            {
                mansionKind = kind,
                state = PurpleMansionBuildState.Complete,
                mansionInstanceId = mansionId,
                mansionBodyEffectBindingId = RequiredBodyBinding(kind),
                guardianAbilityInstanceId = "guardian_" + lowerKind,
                sourceSpellId = "spell_" + lowerKind,
                upgradePlanId = "upgrade_" + lowerKind,
                sourceSpellDisposition = "RETAIN",
            };
        }

        private static FoundationEffectBinding BodyEffect(PurpleMansionKind kind, string mansionId)
        {
            return new FoundationEffectBinding
            {
                effectBindingId = RequiredBodyBinding(kind),
                carrierKind = FoundationEffectCarrierKind.MansionBody,
                carrierId = mansionId,
                order = 1,
                trigger = "fixture_trigger",
                conditions = Array.Empty<string>(),
                target = "fixture_target",
                atomicEffectType = "fixture_atomic",
                parameters = new[] { "profileRef:fixture_numeric" },
            };
        }

        private static FoundationEffectBinding CapacityEffect(string effectId, string grantId)
        {
            return new FoundationEffectBinding
            {
                effectBindingId = effectId,
                carrierKind = FoundationEffectCarrierKind.ExpansionGrant,
                carrierId = grantId,
                order = 1,
                trigger = "grant_applied",
                conditions = Array.Empty<string>(),
                target = "mansion_capacity",
                atomicEffectType = "MANSION_CAPACITY_PLUS_ONE",
                parameters = new[] { "profileRef:fixture_numeric" },
            };
        }

        private static GuardianAbilityRecord Guardian(PurpleMansionKind kind, string mansionId)
        {
            string lowerKind = kind.ToString().ToLowerInvariant();
            return new GuardianAbilityRecord
            {
                abilityInstanceId = "guardian_" + lowerKind,
                abilityDefinitionId = "ability_" + lowerKind,
                mansionInstanceId = mansionId,
                sourceSpellId = "spell_" + lowerKind,
                upgradePlanId = "upgrade_" + lowerKind,
                sourceSpellDisposition = "RETAIN",
                form = GuardianAbilityForm.Passive,
                effectBindingIds = Array.Empty<string>(),
            };
        }

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

        private static void Destroy(params UnityEngine.Object[] objects)
        {
            foreach (UnityEngine.Object item in objects)
            {
                if (item != null)
                    UnityEngine.Object.DestroyImmediate(item);
            }
        }
    }
}
