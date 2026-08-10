using System;
using TianZhang.Cultivation;
using UnityEngine;

namespace TianZhang.Combat
{
    public enum AttackProfileKind
    {
        Unknown,
        Basic,
        Art,
        Divine,
    }

    public enum BasicAttackBindingKind
    {
        Unknown,
        MainEquipment,
        UnarmedFallback,
    }

    public enum AttackEffectType
    {
        Unknown,
        Physical,
        Magic,
        Heal,
        Buff,
        Debuff,
        Movement,
        Hybrid,
    }

    public enum AttackResourceKind
    {
        Unknown,
        None,
        Mp,
    }

    public enum AttackTargetingMode
    {
        Unknown,
        Single,
        Area,
    }

    public enum AttackAreaCenterKind
    {
        Unknown,
        Caster,
        TargetCell,
    }

    public enum AttackAreaShapeKind
    {
        Unknown,
        Circle,
        Line,
        Fan,
    }

    [Flags]
    public enum AttackAreaEffectBlocker
    {
        None = 0,
        DirectedEdge = 1,
    }

    [Flags]
    public enum AttackAreaTargetFaction
    {
        None = 0,
        Enemy = 1,
        Ally = 2,
        Self = 4,
    }

    [Flags]
    public enum AttackAreaTargetState
    {
        None = 0,
        Alive = 1,
        Corpse = 2,
    }

    /// <summary>
    /// AttackProfiles.csv 中单一 attackProfileId 行的 Unity 只读投影。
    /// 此 asset 只由 ContentImportCoordinator 生成或更新，运行时不得从遗留术法/神通字段回填。
    /// </summary>
    [CreateAssetMenu(fileName = "AttackProfile_", menuName = "天章/攻击档案数据")]
    public sealed class AttackProfileData : ScriptableObject
    {
        [Header("身份与展示")]
        public string attackProfileId;
        public string displayNameKey;
        public AttackProfileKind profileKind;
        public BasicAttackBindingKind basicBindingKind;

        [Header("内容可用性元数据")]
        public string contentScope;
        public string sourceAffiliation;
        public string realmRequirementId;
        public string elementRequirementId;

        [Header("效果")]
        public AttackEffectType effectType;
        public string damageElementId;
        public float physicalDamageMultiplier;
        public float soulDamageMultiplier;
        public int healAmount;
        public float buffMultiplier;
        public float defensePenetration;

        [Header("资源、冷却与距离")]
        public AttackResourceKind resourceKind;
        public int resourceCost;
        public int cooldownTicks;
        public int minCastRange;
        public int maxCastRange;

        [Header("范围目标")]
        public AttackTargetingMode targetingMode;
        public AttackAreaCenterKind areaCenterKind;
        public AttackAreaShapeKind areaShapeKind;
        public int areaRadius;
        public int areaLength;
        public int areaFanHalfAngleSteps;
        public int areaFacing = -1;
        public int areaInnerRadius;
        public AttackAreaEffectBlocker areaEffectBlockers;
        public AttackAreaTargetFaction areaAllowedFactions;
        public AttackAreaTargetState areaAllowedStates;

        [Header("神通显示元数据")]
        public bool isDomain;
        public bool isBloodline;
        public string specialEffectTextKey;

        public bool TryValidate(out string reason)
        {
            if (string.IsNullOrWhiteSpace(attackProfileId) ||
                !System.Text.RegularExpressions.Regex.IsMatch(attackProfileId, "^[a-z][a-z0-9_]*$"))
            {
                reason = "attack_profile_id_invalid";
                return false;
            }

            if (string.IsNullOrWhiteSpace(displayNameKey) ||
                profileKind == AttackProfileKind.Unknown ||
                effectType == AttackEffectType.Unknown ||
                resourceKind == AttackResourceKind.Unknown ||
                targetingMode == AttackTargetingMode.Unknown ||
                resourceCost < 0 || cooldownTicks < 0 || minCastRange < 0 ||
                maxCastRange < minCastRange)
            {
                reason = "attack_profile_required_field_invalid";
                return false;
            }

            if (profileKind == AttackProfileKind.Basic)
            {
                if (basicBindingKind == BasicAttackBindingKind.Unknown ||
                    !string.IsNullOrEmpty(contentScope) ||
                    !string.IsNullOrEmpty(sourceAffiliation) ||
                    !string.IsNullOrEmpty(realmRequirementId) ||
                    !string.IsNullOrEmpty(elementRequirementId) ||
                    resourceKind != AttackResourceKind.None || resourceCost != 0 || cooldownTicks != 0)
                {
                    reason = "basic_attack_profile_contract_invalid";
                    return false;
                }
            }
            else if (basicBindingKind != BasicAttackBindingKind.Unknown ||
                     !ContentScopePolicy.IsKnown(contentScope) ||
                     string.IsNullOrWhiteSpace(realmRequirementId) ||
                     string.IsNullOrWhiteSpace(elementRequirementId))
            {
                reason = "non_basic_attack_profile_contract_invalid";
                return false;
            }

            if (profileKind != AttackProfileKind.Divine &&
                (isDomain || isBloodline || !string.IsNullOrEmpty(specialEffectTextKey)))
            {
                reason = "non_divine_special_metadata_invalid";
                return false;
            }

            if (targetingMode == AttackTargetingMode.Single)
            {
                if (areaCenterKind != AttackAreaCenterKind.Unknown ||
                    areaShapeKind != AttackAreaShapeKind.Unknown || areaRadius != 0 ||
                    areaLength != 0 || areaFanHalfAngleSteps != 0 || areaFacing != -1 ||
                    areaInnerRadius != 0 || areaEffectBlockers != AttackAreaEffectBlocker.None ||
                    areaAllowedFactions != AttackAreaTargetFaction.None ||
                    areaAllowedStates != AttackAreaTargetState.None)
                {
                    reason = "single_target_area_fields_present";
                    return false;
                }

                reason = string.Empty;
                return true;
            }

            if (areaCenterKind == AttackAreaCenterKind.Unknown ||
                areaShapeKind == AttackAreaShapeKind.Unknown || areaRadius < 0 ||
                areaLength < 0 || areaInnerRadius < 0 ||
                areaAllowedFactions == AttackAreaTargetFaction.None ||
                areaAllowedStates == AttackAreaTargetState.None ||
                (areaAllowedFactions & ~(AttackAreaTargetFaction.Enemy | AttackAreaTargetFaction.Ally | AttackAreaTargetFaction.Self)) != 0 ||
                (areaAllowedStates & ~(AttackAreaTargetState.Alive | AttackAreaTargetState.Corpse)) != 0 ||
                (areaEffectBlockers & ~AttackAreaEffectBlocker.DirectedEdge) != 0)
            {
                reason = "area_targeting_required_field_invalid";
                return false;
            }

            if (areaCenterKind == AttackAreaCenterKind.Caster &&
                (minCastRange != 0 || maxCastRange != 0))
            {
                reason = "caster_center_range_invalid";
                return false;
            }

            switch (areaShapeKind)
            {
                case AttackAreaShapeKind.Circle when areaLength == 0 &&
                                                     areaFanHalfAngleSteps == 0 &&
                                                     areaFacing == -1 &&
                                                     areaInnerRadius <= areaRadius:
                    reason = string.Empty;
                    return true;
                case AttackAreaShapeKind.Line when areaRadius == 0 && areaLength > 0 &&
                                                   areaFanHalfAngleSteps == 0 &&
                                                   areaFacing is >= 0 and < 6 &&
                                                   areaInnerRadius < areaLength:
                    reason = string.Empty;
                    return true;
                case AttackAreaShapeKind.Fan when areaRadius == 0 && areaLength > 0 &&
                                                  areaFanHalfAngleSteps is 0 or 1 &&
                                                  areaFacing is >= 0 and < 6 &&
                                                  areaInnerRadius < areaLength:
                    reason = string.Empty;
                    return true;
                default:
                    reason = "area_shape_contract_invalid";
                    return false;
            }
        }
    }
}
