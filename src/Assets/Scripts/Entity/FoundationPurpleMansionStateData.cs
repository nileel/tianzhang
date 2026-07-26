using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TianZhang.Entity
{
    public enum FoundationPhase
    {
        Phase1,
        Phase2,
        Phase3,
        Phase4,
    }

    public enum PurpleMansionKind
    {
        Ming,
        Hun,
        Shi,
        Wu,
        Yun,
    }

    public enum PurpleMansionBuildState
    {
        NotBuilt,
        Embryo,
        Complete,
    }

    public enum FoundationEffectCarrierKind
    {
        Foundation,
        MansionBody,
        GuardianAbility,
        EnhancementNode,
        ExpansionGrant,
        CultivationAction,
    }

    public enum GuardianAbilityForm
    {
        Active,
        Passive,
        Triggered,
    }

    public enum EnhancementNodeKind
    {
        Behavior,
        Cultivation,
        Resource,
        InterMansion,
        Special,
    }

    public enum CultivationActionKind
    {
        FoundationTrial,
        FoundationNurture,
        MansionEmbryoNurture,
        MansionOpeningTrial,
    }

    public enum CultivationActionStatus
    {
        Ready,
        Active,
        Paused,
        Completed,
        Failed,
        Terminated,
    }

    public enum JindanLockStatus
    {
        PreJindan,
        Formed,
    }

    [Serializable]
    public sealed class FoundationStateRecord
    {
        public string foundationInstanceId;
        public string foundationDefinitionId;
        public string sourceGongFaId;
        public FoundationPhase phase;
        public float continuousProgress;
        public string phaseBoundarySetId;
        public int naturalMansionCapacity;
        public int releasedNaturalCapacity;
        public FoundationExpansionGrant[] expansionGrants;
        public int expandedMansionCapacity;
        public int totalMansionCapacity;
    }

    [Serializable]
    public sealed class FoundationExpansionGrant
    {
        public string grantId;
        public string sourceItemId;
        public string capacityEffectBindingId;
    }

    [Serializable]
    public sealed class PurpleMansionStateRecord
    {
        public PurpleMansionKind mansionKind;
        public PurpleMansionBuildState state;
        public string embryoId;
        public string mansionInstanceId;
        public string mansionBodyEffectBindingId;
        public string guardianAbilityInstanceId;
        public string sourceSpellId;
        public string upgradePlanId;
        public string sourceSpellDisposition;
        public float continuousProgress;
        public string progressChannelId;
        public string relatedActionStateId;
    }

    [Serializable]
    public sealed class FoundationEffectBinding
    {
        public string effectBindingId;
        public FoundationEffectCarrierKind carrierKind;
        public string carrierId;
        public int order;
        public string trigger;
        public string[] conditions;
        public string target;
        public string atomicEffectType;
        public string[] parameters;
    }

    [Serializable]
    public sealed class GuardianAbilityRecord
    {
        public string abilityInstanceId;
        public string abilityDefinitionId;
        public string mansionInstanceId;
        public string sourceSpellId;
        public string upgradePlanId;
        public string sourceSpellDisposition;
        public GuardianAbilityForm form;
        public string[] effectBindingIds;
    }

    [Serializable]
    public sealed class EnhancementNodeRecord
    {
        public string nodeId;
        public string abilityInstanceId;
        public EnhancementNodeKind nodeKind;
        public string[] requirements;
        public string[] effectBindingIds;
    }

    [Serializable]
    public sealed class CultivationActionStateRecord
    {
        public string actionStateId;
        public CultivationActionKind actionKind;
        public CultivationActionStatus status;
        public string targetRef;
        public string fixedCycleDefinitionId;
        public string lastStableBoundaryId;
        public string[] committedCycleIds;
        public string progressChannelId;
        public string[] numericProfileRefs;
    }

    [Serializable]
    public sealed class ClosedRetreatPlanRecord
    {
        public string actionStateId;
        public string targetRef;
        public string[] stopConditions;
    }

    [Serializable]
    public sealed class JindanFormationSnapshot
    {
        public string foundationInstanceId;
        public FoundationPhase phase;
        public int naturalMansionCapacity;
        public string[] expansionGrantIds;
        public PurpleMansionSnapshot[] mansionStates;
    }

    [Serializable]
    public sealed class PurpleMansionSnapshot
    {
        public PurpleMansionKind mansionKind;
        public PurpleMansionBuildState state;
        public string mansionBodyEffectBindingId;
        public string guardianAbilityInstanceId;
    }

    [Serializable]
    public sealed class JindanLockRecord
    {
        public JindanLockStatus status;
        public JindanFormationSnapshot formationSnapshot;
    }

    [CreateAssetMenu(fileName = "FoundationPurpleMansionState_", menuName = "天章/道基紫府状态数据")]
    public class FoundationPurpleMansionStateData : ScriptableObject
    {
        public string schemaId;
        public int schemaVersion;
        public string characterId;
        public FoundationStateRecord foundationState;
        public PurpleMansionStateRecord[] mansionStates;
        public FoundationEffectBinding[] effectBindings;
        public GuardianAbilityRecord[] guardianAbilities;
        public EnhancementNodeRecord[] enhancementNodes;
        public CultivationActionStateRecord cultivationActionState;
        public ClosedRetreatPlanRecord closedRetreatPlan;
        public JindanLockRecord jindanLock;
    }

    /// <summary>
    /// 角色持有的道基／紫府运行时投影。静态字段的完整合法性仍只由导入器校验；
    /// 此处只拒绝破坏运行时连续性的根结构并记录当前行动与不可逆结丹锁。
    /// </summary>
    public sealed class FoundationPurpleMansionRuntimeState
    {
        public const string InvalidRuntimeState = "FPM_INVALID_RUNTIME_STATE";
        public const string CapacityOverflow = "FPM_CAPACITY_OVERFLOW";
        public const string DuplicateMansionKind = "FPM_DUPLICATE_MANSION_KIND";
        public const string InvalidAction = "FPM_INVALID_ACTION";
        public const string InvalidClosedRetreat = "FPM_INVALID_CLOSED_RETREAT";
        public const string JindanLockMutation = "FPM_JINDAN_LOCK_MUTATION";

        private readonly FoundationStateRecord foundationState;
        private readonly PurpleMansionStateRecord[] mansionStates;
        private readonly FoundationEffectBinding[] effectBindings;
        private CultivationActionStateRecord cultivationActionState;
        private readonly ClosedRetreatPlanRecord closedRetreatPlan;
        private JindanLockRecord jindanLock;

        private FoundationPurpleMansionRuntimeState(FoundationPurpleMansionStateData source)
        {
            foundationState = Copy(source.foundationState);
            mansionStates = source.mansionStates.Select(Copy).ToArray();
            effectBindings = source.effectBindings.Select(Copy).ToArray();
            cultivationActionState = Copy(source.cultivationActionState);
            closedRetreatPlan = Copy(source.closedRetreatPlan);
            jindanLock = Copy(source.jindanLock);
        }

        public FoundationPhase Phase => foundationState.phase;
        public float ContinuousProgress => foundationState.continuousProgress;
        public int NaturalMansionCapacity => foundationState.naturalMansionCapacity;
        public int ReleasedNaturalCapacity => foundationState.releasedNaturalCapacity;
        public int ExpandedMansionCapacity => foundationState.expandedMansionCapacity;
        public int TotalMansionCapacity => foundationState.totalMansionCapacity;
        public bool IsJindanFormed => jindanLock.status == JindanLockStatus.Formed;
        public string LastClosedRetreatStopReason { get; private set; }

        public static bool TryCreate(
            FoundationPurpleMansionStateData source,
            out FoundationPurpleMansionRuntimeState runtimeState,
            out string failureReason)
        {
            runtimeState = null;
            if (!HasRuntimeRoot(source, out failureReason))
                return false;

            runtimeState = new FoundationPurpleMansionRuntimeState(source);
            return true;
        }

        public PurpleMansionBuildState GetMansionBuildState(PurpleMansionKind mansionKind)
        {
            return GetMansion(mansionKind).state;
        }

        public string GetMansionBodyEffectBindingId(PurpleMansionKind mansionKind)
        {
            return GetMansion(mansionKind).mansionBodyEffectBindingId;
        }

        public string GetGuardianAbilityInstanceId(PurpleMansionKind mansionKind)
        {
            return GetMansion(mansionKind).guardianAbilityInstanceId;
        }

        public FoundationEffectBinding[] GetFlatEffectBindings()
        {
            return effectBindings.Select(Copy).ToArray();
        }

        public CultivationActionStateRecord GetCultivationActionState()
        {
            return Copy(cultivationActionState);
        }

        public FoundationPurpleMansionOperationResult TryNurtureFoundationCycle(string cycleId)
        {
            if (IsJindanFormed)
                return Rejected(JindanLockMutation);
            if (Phase == FoundationPhase.Phase4 || cultivationActionState == null ||
                cultivationActionState.actionKind != CultivationActionKind.FoundationNurture ||
                cultivationActionState.targetRef != foundationState.foundationInstanceId)
            {
                return Rejected(InvalidAction);
            }

            return TryCommitCurrentCycle(cycleId);
        }

        public FoundationPurpleMansionOperationResult CanExpandMansionCapacity()
        {
            if (IsJindanFormed)
                return Rejected(JindanLockMutation);
            return foundationState.expansionGrants.Length >= 2
                ? Rejected(CapacityOverflow)
                : Succeeded();
        }

        public FoundationPurpleMansionOperationResult TryOpenMansionCycle(
            PurpleMansionKind mansionKind,
            string cycleId)
        {
            if (IsJindanFormed)
                return Rejected(JindanLockMutation);

            PurpleMansionStateRecord mansion = GetMansion(mansionKind);
            if (mansion.state != PurpleMansionBuildState.Embryo ||
                cultivationActionState == null ||
                cultivationActionState.actionKind != CultivationActionKind.MansionOpeningTrial ||
                cultivationActionState.targetRef != mansion.embryoId ||
                cultivationActionState.actionStateId != mansion.relatedActionStateId)
            {
                return Rejected(InvalidAction);
            }

            return TryCommitCurrentCycle(cycleId);
        }

        public FoundationPurpleMansionOperationResult TryFailMansionOpeningTrial(
            PurpleMansionKind mansionKind)
        {
            if (IsJindanFormed)
                return Rejected(JindanLockMutation);

            PurpleMansionStateRecord mansion = GetMansion(mansionKind);
            if (mansion.state != PurpleMansionBuildState.Embryo ||
                cultivationActionState == null ||
                cultivationActionState.actionKind != CultivationActionKind.MansionOpeningTrial ||
                cultivationActionState.targetRef != mansion.embryoId)
            {
                return Rejected(InvalidAction);
            }

            cultivationActionState.status = CultivationActionStatus.Failed;
            return Succeeded();
        }

        public FoundationPurpleMansionOperationResult TryRepeatClosedRetreatCycle(
            string cycleId,
            bool hasNextCycleResources)
        {
            if (IsJindanFormed)
                return Rejected(JindanLockMutation);
            if (closedRetreatPlan == null || cultivationActionState == null ||
                closedRetreatPlan.actionStateId != cultivationActionState.actionStateId ||
                closedRetreatPlan.targetRef != cultivationActionState.targetRef)
            {
                return Rejected(InvalidClosedRetreat);
            }

            if (!hasNextCycleResources)
            {
                if (!closedRetreatPlan.stopConditions.Contains("INSUFFICIENT_NEXT_CYCLE_RESOURCES"))
                    return Rejected(InvalidClosedRetreat);

                cultivationActionState.status = CultivationActionStatus.Paused;
                LastClosedRetreatStopReason = "INSUFFICIENT_NEXT_CYCLE_RESOURCES";
                return Succeeded();
            }

            return TryCommitCurrentCycle(cycleId);
        }

        internal FoundationPurpleMansionOperationResult TryFormJindanLock()
        {
            if (IsJindanFormed || Phase != FoundationPhase.Phase4 ||
                !mansionStates.Any(mansion => mansion.state == PurpleMansionBuildState.Complete) ||
                mansionStates.Any(mansion => mansion.state == PurpleMansionBuildState.Embryo))
            {
                return Rejected(JindanLockMutation);
            }

            jindanLock = new JindanLockRecord
            {
                status = JindanLockStatus.Formed,
                formationSnapshot = new JindanFormationSnapshot
                {
                    foundationInstanceId = foundationState.foundationInstanceId,
                    phase = foundationState.phase,
                    naturalMansionCapacity = foundationState.naturalMansionCapacity,
                    expansionGrantIds = foundationState.expansionGrants.Select(grant => grant.grantId).ToArray(),
                    mansionStates = mansionStates.Select(mansion => new PurpleMansionSnapshot
                    {
                        mansionKind = mansion.mansionKind,
                        state = mansion.state,
                        mansionBodyEffectBindingId = mansion.mansionBodyEffectBindingId,
                        guardianAbilityInstanceId = mansion.guardianAbilityInstanceId,
                    }).ToArray(),
                },
            };
            return Succeeded();
        }

        private FoundationPurpleMansionOperationResult TryCommitCurrentCycle(string cycleId)
        {
            if (string.IsNullOrWhiteSpace(cycleId) || cultivationActionState == null ||
                cultivationActionState.status == CultivationActionStatus.Completed ||
                cultivationActionState.status == CultivationActionStatus.Failed ||
                cultivationActionState.status == CultivationActionStatus.Terminated ||
                cultivationActionState.committedCycleIds.Contains(cycleId))
            {
                return Rejected(InvalidAction);
            }

            var cycleIds = new List<string>(cultivationActionState.committedCycleIds) { cycleId };
            cultivationActionState.committedCycleIds = cycleIds.ToArray();
            cultivationActionState.status = CultivationActionStatus.Active;
            LastClosedRetreatStopReason = null;
            return Succeeded();
        }

        private PurpleMansionStateRecord GetMansion(PurpleMansionKind mansionKind)
        {
            return mansionStates.Single(mansion => mansion.mansionKind == mansionKind);
        }

        private static bool HasRuntimeRoot(
            FoundationPurpleMansionStateData source,
            out string failureReason)
        {
            failureReason = InvalidRuntimeState;
            if (source == null || source.schemaId != "foundationPurpleMansionState" ||
                source.schemaVersion != 1 || source.foundationState == null ||
                source.mansionStates == null || source.effectBindings == null ||
                source.jindanLock == null || source.foundationState.expansionGrants == null)
            {
                return false;
            }

            FoundationStateRecord foundation = source.foundationState;
            if (!Enum.IsDefined(typeof(FoundationPhase), foundation.phase) ||
                foundation.naturalMansionCapacity < 0 || foundation.naturalMansionCapacity > 3 ||
                foundation.expansionGrants.Length > 2)
            {
                failureReason = CapacityOverflow;
                return false;
            }

            int releasedCapacity = Math.Min(
                foundation.naturalMansionCapacity,
                (int)foundation.phase);
            if (foundation.releasedNaturalCapacity != releasedCapacity ||
                foundation.expandedMansionCapacity != foundation.expansionGrants.Length ||
                foundation.totalMansionCapacity != releasedCapacity + foundation.expansionGrants.Length)
            {
                failureReason = CapacityOverflow;
                return false;
            }

            if (source.mansionStates.Length != 5 ||
                source.mansionStates.Any(mansion => mansion == null) ||
                source.mansionStates.Select(mansion => mansion.mansionKind).Distinct().Count() != 5)
            {
                failureReason = DuplicateMansionKind;
                return false;
            }

            int committedCapacity = source.mansionStates.Count(mansion =>
                mansion.state == PurpleMansionBuildState.Embryo ||
                mansion.state == PurpleMansionBuildState.Complete);
            if (committedCapacity > foundation.totalMansionCapacity)
            {
                failureReason = CapacityOverflow;
                return false;
            }

            if (source.cultivationActionState != null &&
                (source.cultivationActionState.committedCycleIds == null ||
                 source.cultivationActionState.numericProfileRefs == null ||
                 source.cultivationActionState.numericProfileRefs.Length == 0))
            {
                failureReason = InvalidAction;
                return false;
            }

            if (source.closedRetreatPlan != null &&
                (source.cultivationActionState == null ||
                 source.closedRetreatPlan.stopConditions == null ||
                 source.closedRetreatPlan.stopConditions.Length == 0))
            {
                failureReason = InvalidClosedRetreat;
                return false;
            }

            if (source.jindanLock.status == JindanLockStatus.PreJindan)
                return source.jindanLock.formationSnapshot == null;

            bool validFormedLock = source.jindanLock.status == JindanLockStatus.Formed &&
                                   source.jindanLock.formationSnapshot != null &&
                                   foundation.phase == FoundationPhase.Phase4 &&
                                   !source.mansionStates.Any(mansion => mansion.state == PurpleMansionBuildState.Embryo) &&
                                   source.mansionStates.Any(mansion => mansion.state == PurpleMansionBuildState.Complete);
            if (!validFormedLock)
                failureReason = JindanLockMutation;
            return validFormedLock;
        }

        private static FoundationPurpleMansionOperationResult Succeeded()
        {
            return new FoundationPurpleMansionOperationResult(true, null);
        }

        private static FoundationPurpleMansionOperationResult Rejected(string reason)
        {
            return new FoundationPurpleMansionOperationResult(false, reason);
        }

        private static FoundationStateRecord Copy(FoundationStateRecord source) => new FoundationStateRecord
        {
            foundationInstanceId = source.foundationInstanceId,
            foundationDefinitionId = source.foundationDefinitionId,
            sourceGongFaId = source.sourceGongFaId,
            phase = source.phase,
            continuousProgress = source.continuousProgress,
            phaseBoundarySetId = source.phaseBoundarySetId,
            naturalMansionCapacity = source.naturalMansionCapacity,
            releasedNaturalCapacity = source.releasedNaturalCapacity,
            expansionGrants = source.expansionGrants.Select(Copy).ToArray(),
            expandedMansionCapacity = source.expandedMansionCapacity,
            totalMansionCapacity = source.totalMansionCapacity,
        };

        private static FoundationExpansionGrant Copy(FoundationExpansionGrant source) => new FoundationExpansionGrant
        {
            grantId = source.grantId,
            sourceItemId = source.sourceItemId,
            capacityEffectBindingId = source.capacityEffectBindingId,
        };

        private static PurpleMansionStateRecord Copy(PurpleMansionStateRecord source) => new PurpleMansionStateRecord
        {
            mansionKind = source.mansionKind,
            state = source.state,
            embryoId = source.embryoId,
            mansionInstanceId = source.mansionInstanceId,
            mansionBodyEffectBindingId = source.mansionBodyEffectBindingId,
            guardianAbilityInstanceId = source.guardianAbilityInstanceId,
            sourceSpellId = source.sourceSpellId,
            upgradePlanId = source.upgradePlanId,
            sourceSpellDisposition = source.sourceSpellDisposition,
            continuousProgress = source.continuousProgress,
            progressChannelId = source.progressChannelId,
            relatedActionStateId = source.relatedActionStateId,
        };

        private static FoundationEffectBinding Copy(FoundationEffectBinding source) => new FoundationEffectBinding
        {
            effectBindingId = source.effectBindingId,
            carrierKind = source.carrierKind,
            carrierId = source.carrierId,
            order = source.order,
            trigger = source.trigger,
            conditions = source.conditions.ToArray(),
            target = source.target,
            atomicEffectType = source.atomicEffectType,
            parameters = source.parameters.ToArray(),
        };

        private static CultivationActionStateRecord Copy(CultivationActionStateRecord source)
        {
            return source == null ? null : new CultivationActionStateRecord
            {
                actionStateId = source.actionStateId,
                actionKind = source.actionKind,
                status = source.status,
                targetRef = source.targetRef,
                fixedCycleDefinitionId = source.fixedCycleDefinitionId,
                lastStableBoundaryId = source.lastStableBoundaryId,
                committedCycleIds = source.committedCycleIds.ToArray(),
                progressChannelId = source.progressChannelId,
                numericProfileRefs = source.numericProfileRefs.ToArray(),
            };
        }

        private static ClosedRetreatPlanRecord Copy(ClosedRetreatPlanRecord source)
        {
            return source == null ? null : new ClosedRetreatPlanRecord
            {
                actionStateId = source.actionStateId,
                targetRef = source.targetRef,
                stopConditions = source.stopConditions.ToArray(),
            };
        }

        private static JindanLockRecord Copy(JindanLockRecord source)
        {
            return new JindanLockRecord
            {
                status = source.status,
                formationSnapshot = source.formationSnapshot == null ? null : new JindanFormationSnapshot
                {
                    foundationInstanceId = source.formationSnapshot.foundationInstanceId,
                    phase = source.formationSnapshot.phase,
                    naturalMansionCapacity = source.formationSnapshot.naturalMansionCapacity,
                    expansionGrantIds = source.formationSnapshot.expansionGrantIds.ToArray(),
                    mansionStates = source.formationSnapshot.mansionStates.Select(snapshot => new PurpleMansionSnapshot
                    {
                        mansionKind = snapshot.mansionKind,
                        state = snapshot.state,
                        mansionBodyEffectBindingId = snapshot.mansionBodyEffectBindingId,
                        guardianAbilityInstanceId = snapshot.guardianAbilityInstanceId,
                    }).ToArray(),
                },
            };
        }
    }

    public sealed class FoundationPurpleMansionOperationResult
    {
        public bool Succeeded { get; }
        public string FailureReason { get; }

        public FoundationPurpleMansionOperationResult(bool succeeded, string failureReason)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
        }
    }
}
