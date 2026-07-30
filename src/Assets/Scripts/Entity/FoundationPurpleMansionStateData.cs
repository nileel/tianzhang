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
        JindanProof,
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

    /// <summary>
    /// 可写入会话存档的道基／紫府运行时快照。
    /// 它只承载角色运行期间已经形成的事实，不替代导入的 ScriptableObject 定义。
    /// </summary>
    [Serializable]
    public sealed class FoundationPurpleMansionSaveData
    {
        public string schemaId;
        public int schemaVersion;
        public string characterId;
        public FoundationStateRecord foundationState;
        public PurpleMansionStateRecord[] mansionStates;
        public FoundationEffectBinding[] effectBindings;
        public GuardianAbilityRecord[] guardianAbilities;
        public EnhancementNodeRecord[] enhancementNodes;
        public bool hasCultivationActionState;
        public CultivationActionStateRecord cultivationActionState;
        public bool hasClosedRetreatPlan;
        public ClosedRetreatPlanRecord closedRetreatPlan;
        public JindanLockRecord jindanLock;
        public bool hasJindanFormationSnapshot;
        public string lastClosedRetreatStopReason;
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

        private readonly string characterId;
        private readonly FoundationStateRecord foundationState;
        private readonly PurpleMansionStateRecord[] mansionStates;
        private readonly FoundationEffectBinding[] effectBindings;
        private readonly GuardianAbilityRecord[] guardianAbilities;
        private readonly EnhancementNodeRecord[] enhancementNodes;
        private CultivationActionStateRecord cultivationActionState;
        private readonly ClosedRetreatPlanRecord closedRetreatPlan;
        private JindanLockRecord jindanLock;

        private FoundationPurpleMansionRuntimeState(FoundationPurpleMansionSaveData source)
        {
            characterId = source.characterId;
            foundationState = Copy(source.foundationState);
            mansionStates = source.mansionStates.Select(Copy).ToArray();
            effectBindings = source.effectBindings.Select(Copy).ToArray();
            guardianAbilities = source.guardianAbilities.Select(Copy).ToArray();
            enhancementNodes = source.enhancementNodes.Select(Copy).ToArray();
            cultivationActionState = Copy(source.cultivationActionState);
            closedRetreatPlan = Copy(source.closedRetreatPlan);
            jindanLock = Copy(source.jindanLock);
            LastClosedRetreatStopReason = source.lastClosedRetreatStopReason;
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
            return TryRestore(CreateSaveData(source), out runtimeState, out failureReason);
        }

        public static bool TryRestore(
            FoundationPurpleMansionSaveData source,
            out FoundationPurpleMansionRuntimeState runtimeState,
            out string failureReason)
        {
            runtimeState = null;
            NormalizeOptionalState(source);
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

        public GuardianAbilityRecord[] GetGuardianAbilities()
        {
            return guardianAbilities.Select(Copy).ToArray();
        }

        public EnhancementNodeRecord[] GetEnhancementNodes()
        {
            return enhancementNodes.Select(Copy).ToArray();
        }

        public CultivationActionStateRecord GetCultivationActionState()
        {
            return Copy(cultivationActionState);
        }

        public FoundationPurpleMansionOperationResult CanStartCultivationAction(
            CultivationActionKind actionKind,
            string targetRef)
        {
            if (IsJindanFormed)
                return Rejected(JindanLockMutation);
            if (!Enum.IsDefined(typeof(CultivationActionKind), actionKind) ||
                string.IsNullOrWhiteSpace(targetRef) || HasBlockingCultivationAction() ||
                !HasActionTarget(actionKind, targetRef))
            {
                return Rejected(InvalidAction);
            }

            return Succeeded();
        }

        public FoundationPurpleMansionOperationResult TryStartCultivationAction(
            CultivationActionKind actionKind,
            string actionStateId,
            string targetRef,
            string fixedCycleDefinitionId,
            string initialStableBoundaryId,
            string progressChannelId,
            IEnumerable<string> numericProfileRefs)
        {
            FoundationPurpleMansionOperationResult gate = CanStartCultivationAction(
                actionKind,
                targetRef);
            if (!gate.Succeeded || string.IsNullOrWhiteSpace(actionStateId) ||
                string.IsNullOrWhiteSpace(fixedCycleDefinitionId) ||
                string.IsNullOrWhiteSpace(initialStableBoundaryId) ||
                string.IsNullOrWhiteSpace(progressChannelId))
            {
                return Rejected(InvalidAction);
            }

            string[] profiles = numericProfileRefs == null
                ? null
                : numericProfileRefs.ToArray();
            if (profiles == null || profiles.Length == 0 ||
                profiles.Any(string.IsNullOrWhiteSpace) ||
                profiles.Distinct(StringComparer.Ordinal).Count() != profiles.Length ||
                cultivationActionState != null &&
                cultivationActionState.actionStateId == actionStateId)
            {
                return Rejected(InvalidAction);
            }

            cultivationActionState = new CultivationActionStateRecord
            {
                actionStateId = actionStateId,
                actionKind = actionKind,
                status = CultivationActionStatus.Active,
                targetRef = targetRef,
                fixedCycleDefinitionId = fixedCycleDefinitionId,
                lastStableBoundaryId = initialStableBoundaryId,
                committedCycleIds = Array.Empty<string>(),
                progressChannelId = progressChannelId,
                numericProfileRefs = profiles,
            };
            LastClosedRetreatStopReason = null;
            return Succeeded();
        }

        public FoundationPurpleMansionOperationResult TryAdvanceCultivationAction(
            string stableBoundaryId)
        {
            if (cultivationActionState == null ||
                cultivationActionState.status != CultivationActionStatus.Active ||
                string.IsNullOrWhiteSpace(stableBoundaryId))
            {
                return Rejected(InvalidAction);
            }

            cultivationActionState.lastStableBoundaryId = stableBoundaryId;
            return Succeeded();
        }

        public FoundationPurpleMansionOperationResult TryCommitCultivationActionCycle(
            string cycleId)
        {
            return TryCommitCurrentCycle(cycleId);
        }

        public FoundationPurpleMansionOperationResult TryPauseCultivationAction(
            string stopReason)
        {
            if (cultivationActionState == null ||
                cultivationActionState.status != CultivationActionStatus.Active ||
                string.IsNullOrWhiteSpace(stopReason))
            {
                return Rejected(InvalidAction);
            }

            cultivationActionState.status = CultivationActionStatus.Paused;
            LastClosedRetreatStopReason = stopReason;
            return Succeeded();
        }

        public FoundationPurpleMansionOperationResult TryTerminateCultivationAction(
            string stopReason)
        {
            if (cultivationActionState == null ||
                (cultivationActionState.status != CultivationActionStatus.Active &&
                 cultivationActionState.status != CultivationActionStatus.Paused) ||
                string.IsNullOrWhiteSpace(stopReason))
            {
                return Rejected(InvalidAction);
            }

            cultivationActionState.status = CultivationActionStatus.Terminated;
            LastClosedRetreatStopReason = stopReason;
            return Succeeded();
        }

        public FoundationPurpleMansionSaveData CaptureSaveData()
        {
            return new FoundationPurpleMansionSaveData
            {
                schemaId = "foundationPurpleMansionState",
                schemaVersion = 1,
                characterId = characterId,
                foundationState = Copy(foundationState),
                mansionStates = mansionStates.Select(Copy).ToArray(),
                effectBindings = effectBindings.Select(Copy).ToArray(),
                guardianAbilities = guardianAbilities.Select(Copy).ToArray(),
                enhancementNodes = enhancementNodes.Select(Copy).ToArray(),
                hasCultivationActionState = cultivationActionState != null,
                cultivationActionState = Copy(cultivationActionState),
                hasClosedRetreatPlan = closedRetreatPlan != null,
                closedRetreatPlan = Copy(closedRetreatPlan),
                jindanLock = Copy(jindanLock),
                hasJindanFormationSnapshot = jindanLock.formationSnapshot != null,
                lastClosedRetreatStopReason = LastClosedRetreatStopReason,
            };
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

            if (cultivationActionState.status == CultivationActionStatus.Paused)
                cultivationActionState.status = CultivationActionStatus.Active;
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
                cultivationActionState.status != CultivationActionStatus.Active ||
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

        private bool HasBlockingCultivationAction()
        {
            return cultivationActionState != null &&
                (cultivationActionState.status == CultivationActionStatus.Ready ||
                 cultivationActionState.status == CultivationActionStatus.Active ||
                 cultivationActionState.status == CultivationActionStatus.Paused);
        }

        private bool HasActionTarget(CultivationActionKind actionKind, string targetRef)
        {
            switch (actionKind)
            {
                case CultivationActionKind.FoundationTrial:
                case CultivationActionKind.FoundationNurture:
                    return targetRef == foundationState.foundationInstanceId;
                case CultivationActionKind.MansionEmbryoNurture:
                case CultivationActionKind.MansionOpeningTrial:
                    return mansionStates.Any(mansion =>
                        mansion.state == PurpleMansionBuildState.Embryo &&
                        mansion.embryoId == targetRef);
                case CultivationActionKind.JindanProof:
                    return targetRef == foundationState.foundationInstanceId &&
                        foundationState.phase == FoundationPhase.Phase4 &&
                        mansionStates.Any(mansion =>
                            mansion.state == PurpleMansionBuildState.Complete) &&
                        !mansionStates.Any(mansion =>
                            mansion.state == PurpleMansionBuildState.Embryo);
                default:
                    return false;
            }
        }

        private PurpleMansionStateRecord GetMansion(PurpleMansionKind mansionKind)
        {
            return mansionStates.Single(mansion => mansion.mansionKind == mansionKind);
        }

        private static bool HasRuntimeRoot(
            FoundationPurpleMansionSaveData source,
            out string failureReason)
        {
            failureReason = InvalidRuntimeState;
            if (source == null || source.schemaId != "foundationPurpleMansionState" ||
                source.schemaVersion != 1 || source.foundationState == null ||
                source.mansionStates == null || source.effectBindings == null ||
                source.guardianAbilities == null || source.enhancementNodes == null ||
                source.jindanLock == null || source.foundationState.expansionGrants == null ||
                string.IsNullOrWhiteSpace(source.characterId) ||
                string.IsNullOrWhiteSpace(source.foundationState.foundationInstanceId))
            {
                return false;
            }

            if (source.hasCultivationActionState != (source.cultivationActionState != null) ||
                source.hasClosedRetreatPlan != (source.closedRetreatPlan != null) ||
                source.hasJindanFormationSnapshot !=
                    (source.jindanLock.formationSnapshot != null))
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

            if (source.effectBindings.Any(binding => binding == null ||
                    binding.conditions == null || binding.parameters == null) ||
                source.guardianAbilities.Any(ability => ability == null || ability.effectBindingIds == null) ||
                source.enhancementNodes.Any(node => node == null ||
                    node.requirements == null || node.effectBindingIds == null))
            {
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
                (!Enum.IsDefined(
                    typeof(CultivationActionKind),
                    source.cultivationActionState.actionKind) ||
                 !Enum.IsDefined(
                    typeof(CultivationActionStatus),
                    source.cultivationActionState.status) ||
                 string.IsNullOrWhiteSpace(source.cultivationActionState.actionStateId) ||
                 string.IsNullOrWhiteSpace(source.cultivationActionState.targetRef) ||
                 string.IsNullOrWhiteSpace(source.cultivationActionState.fixedCycleDefinitionId) ||
                 string.IsNullOrWhiteSpace(source.cultivationActionState.lastStableBoundaryId) ||
                 string.IsNullOrWhiteSpace(source.cultivationActionState.progressChannelId) ||
                 source.cultivationActionState.committedCycleIds == null ||
                 source.cultivationActionState.numericProfileRefs == null ||
                 source.cultivationActionState.numericProfileRefs.Length == 0 ||
                 source.cultivationActionState.committedCycleIds.Any(string.IsNullOrWhiteSpace) ||
                 source.cultivationActionState.committedCycleIds.Distinct().Count() !=
                    source.cultivationActionState.committedCycleIds.Length ||
                 source.cultivationActionState.numericProfileRefs.Any(string.IsNullOrWhiteSpace) ||
                 source.cultivationActionState.numericProfileRefs.Distinct().Count() !=
                    source.cultivationActionState.numericProfileRefs.Length ||
                 !HasActionTarget(source, source.cultivationActionState)))
            {
                failureReason = InvalidAction;
                return false;
            }

            if (source.closedRetreatPlan != null &&
                (source.cultivationActionState == null ||
                 source.closedRetreatPlan.stopConditions == null ||
                 source.closedRetreatPlan.stopConditions.Length == 0 ||
                 (source.lastClosedRetreatStopReason != null &&
                  !source.closedRetreatPlan.stopConditions.Contains(source.lastClosedRetreatStopReason))))
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
                                   source.mansionStates.Any(mansion => mansion.state == PurpleMansionBuildState.Complete) &&
                                   MatchesFormationSnapshot(source);
            if (!validFormedLock)
                failureReason = JindanLockMutation;
            return validFormedLock;
        }

        private static bool MatchesFormationSnapshot(FoundationPurpleMansionSaveData source)
        {
            JindanFormationSnapshot snapshot = source.jindanLock.formationSnapshot;
            if (snapshot.mansionStates == null ||
                snapshot.mansionStates.Length != source.mansionStates.Length ||
                snapshot.mansionStates.Any(mansion => mansion == null) ||
                snapshot.mansionStates.Select(mansion => mansion.mansionKind).Distinct().Count() !=
                    snapshot.mansionStates.Length ||
                snapshot.foundationInstanceId != source.foundationState.foundationInstanceId ||
                snapshot.phase != source.foundationState.phase ||
                snapshot.naturalMansionCapacity != source.foundationState.naturalMansionCapacity ||
                snapshot.expansionGrantIds == null ||
                !snapshot.expansionGrantIds.SequenceEqual(
                    source.foundationState.expansionGrants.Select(grant => grant.grantId)))
            {
                return false;
            }

            foreach (PurpleMansionStateRecord mansion in source.mansionStates)
            {
                PurpleMansionSnapshot captured = snapshot.mansionStates.Single(item =>
                    item.mansionKind == mansion.mansionKind);
                if (captured.state != mansion.state ||
                    captured.mansionBodyEffectBindingId != mansion.mansionBodyEffectBindingId ||
                    captured.guardianAbilityInstanceId != mansion.guardianAbilityInstanceId)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasActionTarget(
            FoundationPurpleMansionSaveData source,
            CultivationActionStateRecord action)
        {
            switch (action.actionKind)
            {
                case CultivationActionKind.FoundationTrial:
                case CultivationActionKind.FoundationNurture:
                    return action.targetRef == source.foundationState.foundationInstanceId;
                case CultivationActionKind.MansionEmbryoNurture:
                case CultivationActionKind.MansionOpeningTrial:
                    return source.mansionStates.Any(mansion =>
                        mansion.state == PurpleMansionBuildState.Embryo &&
                        mansion.embryoId == action.targetRef);
                case CultivationActionKind.JindanProof:
                    return action.targetRef == source.foundationState.foundationInstanceId &&
                        source.foundationState.phase == FoundationPhase.Phase4 &&
                        source.mansionStates.Any(mansion =>
                            mansion.state == PurpleMansionBuildState.Complete) &&
                        !source.mansionStates.Any(mansion =>
                            mansion.state == PurpleMansionBuildState.Embryo);
                default:
                    return false;
            }
        }

        private static FoundationPurpleMansionSaveData CreateSaveData(
            FoundationPurpleMansionStateData source)
        {
            return source == null
                ? null
                : new FoundationPurpleMansionSaveData
                {
                    schemaId = source.schemaId,
                    schemaVersion = source.schemaVersion,
                    characterId = source.characterId,
                    foundationState = source.foundationState,
                    mansionStates = source.mansionStates,
                    effectBindings = source.effectBindings,
                    guardianAbilities = source.guardianAbilities,
                    enhancementNodes = source.enhancementNodes,
                    hasCultivationActionState = source.cultivationActionState != null,
                    cultivationActionState = source.cultivationActionState,
                    hasClosedRetreatPlan = source.closedRetreatPlan != null,
                    closedRetreatPlan = source.closedRetreatPlan,
                    jindanLock = source.jindanLock,
                    hasJindanFormationSnapshot = source.jindanLock != null &&
                        source.jindanLock.formationSnapshot != null,
                };
        }

        private static void NormalizeOptionalState(FoundationPurpleMansionSaveData source)
        {
            if (source == null)
                return;

            if (!source.hasCultivationActionState && IsEmpty(source.cultivationActionState))
                source.cultivationActionState = null;
            if (!source.hasClosedRetreatPlan && IsEmpty(source.closedRetreatPlan))
                source.closedRetreatPlan = null;
            if (source.jindanLock != null && !source.hasJindanFormationSnapshot &&
                IsEmpty(source.jindanLock.formationSnapshot))
            {
                source.jindanLock.formationSnapshot = null;
            }
            if (string.IsNullOrEmpty(source.lastClosedRetreatStopReason))
                source.lastClosedRetreatStopReason = null;
        }

        private static bool IsEmpty(CultivationActionStateRecord value)
        {
            return value != null && string.IsNullOrEmpty(value.actionStateId) &&
                string.IsNullOrEmpty(value.targetRef) &&
                string.IsNullOrEmpty(value.fixedCycleDefinitionId) &&
                string.IsNullOrEmpty(value.lastStableBoundaryId) &&
                (value.committedCycleIds == null || value.committedCycleIds.Length == 0) &&
                string.IsNullOrEmpty(value.progressChannelId) &&
                (value.numericProfileRefs == null || value.numericProfileRefs.Length == 0);
        }

        private static bool IsEmpty(ClosedRetreatPlanRecord value)
        {
            return value != null && string.IsNullOrEmpty(value.actionStateId) &&
                string.IsNullOrEmpty(value.targetRef) &&
                (value.stopConditions == null || value.stopConditions.Length == 0);
        }

        private static bool IsEmpty(JindanFormationSnapshot value)
        {
            return value != null && string.IsNullOrEmpty(value.foundationInstanceId) &&
                (value.expansionGrantIds == null || value.expansionGrantIds.Length == 0) &&
                (value.mansionStates == null || value.mansionStates.Length == 0);
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

        private static GuardianAbilityRecord Copy(GuardianAbilityRecord source) => new GuardianAbilityRecord
        {
            abilityInstanceId = source.abilityInstanceId,
            abilityDefinitionId = source.abilityDefinitionId,
            mansionInstanceId = source.mansionInstanceId,
            sourceSpellId = source.sourceSpellId,
            upgradePlanId = source.upgradePlanId,
            sourceSpellDisposition = source.sourceSpellDisposition,
            form = source.form,
            effectBindingIds = source.effectBindingIds.ToArray(),
        };

        private static EnhancementNodeRecord Copy(EnhancementNodeRecord source) => new EnhancementNodeRecord
        {
            nodeId = source.nodeId,
            abilityInstanceId = source.abilityInstanceId,
            nodeKind = source.nodeKind,
            requirements = source.requirements.ToArray(),
            effectBindingIds = source.effectBindingIds.ToArray(),
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
