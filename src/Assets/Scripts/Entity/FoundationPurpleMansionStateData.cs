using System;
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
}
