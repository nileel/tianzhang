using System;
using System.Linq;
using TianZhang.Cultivation;
using UnityEngine;

namespace TianZhang.Entity
{
    /// <summary>Frozen demo content. Mutable resources and effects belong to the Combat session.</summary>
    [CreateAssetMenu(fileName = "JindanDemoCatalog", menuName = "天章/金丹演武目录")]
    public sealed class JindanDemoCatalogData : ScriptableObject
    {
        public string contentVersion;
        public JindanDemoPresetDefinition[] presets;
        public JindanDemoSeatDefinition[] seats;
        public JindanDemoAbilityDefinition[] abilities;
        public JindanDemoCommandDefinition[] commands;
        public JindanDemoCompatibilityContract[] contracts;
        public JindanDemoSegmentDefinition[] segments;
        public JindanDemoLoadoutTuning loadoutTuning;

        public JindanDemoPresetDefinition GetPreset(string id) => presets?.SingleOrDefault(x => x.characterId == id);
        public JindanDemoSeatDefinition GetSeat(string id) => seats?.SingleOrDefault(x => x.seatInstanceId == id);
        public JindanDemoAbilityDefinition GetAbility(string id) => abilities?.SingleOrDefault(x => x.definitionId == id);
        public JindanDemoCommandDefinition GetCommand(string id) => commands?.SingleOrDefault(x => x.commandId == id);
        public JindanDemoSegmentDefinition GetSegment(string id) => segments?.SingleOrDefault(x => x.segmentId == id);
        public bool Validate(out string reason) => JindanDemoCatalogValidator.Validate(this, true, out reason);
    }

    [Serializable]
    public sealed class JindanDemoPresetDefinition
    {
        public string characterId;
        public string displayNameKey;
        public string realmId;
        public string realmSubstage;
        public float realmMultiplier;
        public string basicAttackProfileId;
        public int basicAttackSourceModeMask;
        public int basicAttackTargetModeMask;
        public string sourceGongFaId;
        public string sourceGongFaDocument;
        public string sourceCharacterDefinitionId;
        public string[] rootElements;
        // 0 凡品 / 1 下品 / 2 中品 / 3 上品 / 4 极品, matching the explicit source table.
        public int rootGrade;
        public int naturalMansionCapacity;
        public int actionPointMaximumUnits;
        public int movementEfficiencyMilliUnits;
        public int manaMaximum;
        public int normalVisionRange;
        public string[] seatInstanceIds;
        public string flightAbilityInstanceId;
        public string danxiangNameKey;
        public string danxiangPresentationProfileId;
        public string foundationDefinitionId;
        public string phaseBoundarySetId;
        public string carryingCapacityProfileId;
        public string sourceDocument;
        public CharacterData sourceCharacterDefinition;
        public GongFaGrowthData sourceGongFa;
        public FoundationPurpleMansionStateData foundationState;
        public JindanStaticStateData jindanState;
    }

    [Serializable]
    public sealed class JindanDemoSegmentDefinition
    {
        public string segmentId;
        public string displayNameKey;
        public JindanDemoSegmentActor[] actors;
    }

    [Serializable]
    public sealed class JindanDemoSegmentActor
    {
        public string characterId;
        public int team;
        public int q;
        public int r;
        public int facing;
    }

    [Serializable]
    public sealed class JindanDemoSeatDefinition
    {
        public string seatInstanceId;
        public string characterId;
        public string positionId;
        public string positionVersion;
        public string proofProfileId;
        public string proofVersion;
        public string roadId;
        public JindanStaticPositionType positionType;
        public string guardianAbilityInstanceId;
        public string guardianDefinitionId;
        public string[] candidateEffectIds;
        public string defaultEffectId;
        public string sourceDocument;
        public string[] allowedObjects;
        public string[] allowedOperations;
        public string[] allowedMedia;
        public string[] allowedResults;
    }

    [Serializable]
    public sealed class JindanDemoAbilityDefinition
    {
        public string definitionId;
        public string definitionVersion;
        public string displayNameKey;
        public string kind;
        public string sourceDocument;
        public string sourceSpellId;
        public string upgradePlanId;
        public string sourceDisposition;
        public string[] compatibleMansions;
        public string[] objects;
        public string[] operations;
        public string[] media;
        public string[] results;
    }

    [Serializable]
    public sealed class JindanDemoCommandDefinition
    {
        public string commandId;
        public string displayNameKey;
        public string abilityDefinitionId;
        public string effectId;
        public string kind;
        public int actionPointCostUnits;
        public int manaCost;
        public int ctPenalty;
        public int cooldownTicks;
        public int upkeepMana;
        public string upkeepClock;
        public int durationCount;
        public string durationClock;
        public int maximumInstances;
        public int capacity;
        public int range;
        public int verticalReachRank;
        public int supportDurability;
        public int remoteOriginManaCost;
        public int sourceModeMask;
        public int targetModeMask;
        public int[] metricDistanceModes;
        public string failurePolicy;
        public string capacityUnit;
        public string additionalRulesKey;
        public bool enabled;
        public string unavailableReasonKey;
        public string sourceDocument;
    }

    [Serializable]
    public sealed class JindanDemoLoadoutTuning
    {
        public string tuningId;
        public string combatSwitchActionMode;
        public int maxSwitchesPerOwnAction;
        public string switchCostProfileId;
        public string safeStateSwitchMode;
        public string switchActionPointCostMode;
        public int switchManaCost;
        public int switchCtPenalty;
        public int switchCooldownTicks;
        public string activationGate;
        public string sourceDocument;
    }

    /// <summary>Finite, explicit contracts for this demo, without nested or cross-effect combinations.</summary>
    [Serializable]
    public sealed class JindanDemoCompatibilityContract
    {
        public string contractId;
        public string contractVersion;
        public string positionId;
        public string positionProfileVersion;
        public string roadId;
        public JindanStaticPositionType positionType;
        public string effectId;
        public string carrierDefinitionId;
        public string[] objectSchema;
        public string[] operationSchema;
        public string[] mediumSchema;
        public string filterSchema;
        public string scopeSchema;
        public string[] resultCarrierSchema;
        public string costSchemaId;
        public string capacitySchemaId;
        public string[] forbiddenContractIds;
        public string compatibilityProfileId;
    }
}
