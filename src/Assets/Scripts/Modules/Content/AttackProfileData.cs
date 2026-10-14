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
        UnarmedBody,
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
        RestoreSpirit,
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
        ForwardPair,
        AdjacentPair,
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
        public float spiritRecoveryMultiplier;
        public int minimumSpiritRecovery;
        public int otherTargetResourceCost;
        public float otherTargetRecoveryMultiplier;

        [Header("资源、行动力与距离")]
        public AttackResourceKind resourceKind;
        public int resourceCost;
        // 统一行动力公共合同：四个字段替换旧单一 cooldownTicks，单位各自独立且均为必填整数。
        public int actionPointCostUnits;   // AP 最小单位，主动收益攻击 >0；S=1000
        public int minimumNetCostUnits;    // AP 最小单位，>0 且 <= actionPointCostUnits
        public int ctPenalty;              // CT，0～int.MaxValue-100
        public int reuseCooldownTicks;     // 全局整数刻重用冷却，>=0
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

        [Header("AP-GROWTH只读派生与窄载荷")]
        public string ruleSetVersion;
        public string abilityTierId;
        public string cooldownClass;
        public string effectBudgetMode;
        public int efficiencyNumerator;
        public int efficiencyDenominator;
        public int damageMigrationNumerator;
        public int damageMigrationDenominator;
        public int damageWeightNumerator;
        public int damageWeightDenominator;
        public string statusId;
        public string statusAttributeId;
        public int statusRelativePermille;
        public int statusDurationOwnTurnEnds;
        public int statusStackLimit;
        public string statusRefreshPolicy;
        public int controlChancePermille;
        public bool controlUsesMindContest;
        public string ruleSetSourceDigest;
        public int attackSchemaVersion;
        public string unarmedFamilyId;
        public string unarmedRealmId;
        public int unarmedPhysicalAttackCapacity;

        public bool TryValidate(out string reason)
        {
            if (attackSchemaVersion != 2)
            { reason = "attack_schema_version_unknown"; return false; }
            if (attackProfileId == "basic_unarmed" || basicBindingKind == BasicAttackBindingKind.UnarmedFallback || effectBudgetMode == "legacy_unarmed")
            { reason = "legacy_unarmed_rejected"; return false; }
            bool unarmed = effectBudgetMode == "fixed_unarmed";
            if (unarmed)
            {
                bool human = unarmedFamilyId == "body_human" && unarmedRealmId is "realm_fanren" or "realm_lianqi" or "realm_zhuji" or "realm_jindan";
                bool beast = unarmedFamilyId == "body_shijiahou" && unarmedRealmId == "realm_lianqi";
                if (!(human || beast) || attackProfileId != "basic_" + unarmedFamilyId + "_" + unarmedRealmId.Substring(6) ||
                    abilityTierId != (unarmedRealmId == "realm_fanren" ? "tier_lianqi" : "tier_" + unarmedRealmId.Substring(6)) ||
                    unarmedPhysicalAttackCapacity <= 0)
                { reason = "unarmed_identity_or_capacity_invalid"; return false; }
                if (ruleSetVersion != "AP-GROWTH@0.1" || profileKind != AttackProfileKind.Basic ||
                    basicBindingKind != BasicAttackBindingKind.UnarmedBody || cooldownClass != "basic_repeat" ||
                    displayNameKey != "attack_profile_" + unarmedFamilyId ||
                    effectType != AttackEffectType.Physical || damageElementId != "element_none" || physicalDamageMultiplier <= 0 ||
                    soulDamageMultiplier != 0 || healAmount != 0 || buffMultiplier != 0 || defensePenetration != 0 ||
                    targetingMode != AttackTargetingMode.Single || minCastRange != 1 || maxCastRange != 1 ||
                    controlChancePermille != 0 || controlUsesMindContest)
                { reason = "fixed_unarmed_contract_invalid"; return false; }
            }
            else if (basicBindingKind == BasicAttackBindingKind.UnarmedBody || !string.IsNullOrEmpty(unarmedFamilyId) ||
                !string.IsNullOrEmpty(unarmedRealmId) || unarmedPhysicalAttackCapacity != 0)
            { reason = "non_unarmed_fields_present"; return false; }
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
                !Enum.IsDefined(typeof(AttackTargetingMode), targetingMode) || targetingMode == AttackTargetingMode.Unknown ||
                float.IsNaN(physicalDamageMultiplier) || float.IsInfinity(physicalDamageMultiplier) || physicalDamageMultiplier < 0f ||
                float.IsNaN(soulDamageMultiplier) || float.IsInfinity(soulDamageMultiplier) || soulDamageMultiplier < 0f ||
                resourceCost < 0 || minCastRange < 0 ||
                maxCastRange < minCastRange)
            {
                reason = "attack_profile_required_field_invalid";
                return false;
            }

            // 免费主动收益与越界下限必须整批拒绝；0/负/分数/溢出都在导入期先行失败，
            // 这里只复核已投影的整数，不提供任何兜底默认。
            if (actionPointCostUnits <= 0 || minimumNetCostUnits <= 0 ||
                minimumNetCostUnits > actionPointCostUnits ||
                ctPenalty < 0 || ctPenalty > int.MaxValue - 100 || reuseCooldownTicks < 0)
            {
                reason = "attack_profile_action_cost_contract_invalid";
                return false;
            }

            if (!string.IsNullOrEmpty(ruleSetVersion) &&
                (ruleSetVersion != "AP-GROWTH@0.1" || ruleSetSourceDigest == null ||
                 !System.Text.RegularExpressions.Regex.IsMatch(ruleSetSourceDigest, "^[a-f0-9]{64}$")))
            { reason = "attack_profile_rule_identity_invalid"; return false; }
            if (!string.IsNullOrEmpty(ruleSetVersion))
            {
                if (!(abilityTierId is "tier_lianqi" or "tier_zhuji" or "tier_jindan") ||
                    !(cooldownClass is "basic_repeat" or "burst" or "control" or "recovery"))
                { reason = "attack_profile_rule_reference_invalid"; return false; }
                bool damage = effectType is AttackEffectType.Physical or AttackEffectType.Magic or AttackEffectType.Hybrid;
                bool status = effectType is AttackEffectType.Buff or AttackEffectType.Debuff;
                if (!(unarmed || effectBudgetMode == "fixed_weapon" && damage && basicBindingKind == BasicAttackBindingKind.MainEquipment ||
                      effectBudgetMode == "damage_budget" && damage && profileKind != AttackProfileKind.Basic ||
                      effectBudgetMode == "prototype_restore" && effectType == AttackEffectType.RestoreSpirit ||
                      effectBudgetMode == "status_only" && status || effectBudgetMode == "fixed_output" && effectType == AttackEffectType.Heal))
                { reason = "attack_profile_effect_budget_mode_invalid"; return false; }
                bool budget = effectBudgetMode == "damage_budget";
                if (budget ? efficiencyNumerator <= 0 || efficiencyDenominator <= 0 || damageMigrationNumerator <= 0 ||
                    damageMigrationDenominator <= 0 || damageWeightNumerator <= 0 || damageWeightDenominator <= 0 :
                    efficiencyNumerator != 0 || efficiencyDenominator != 0 || damageMigrationNumerator != 0 ||
                    damageMigrationDenominator != 0 || damageWeightNumerator != 0 || damageWeightDenominator != 0)
                { reason = "attack_profile_budget_ratio_invalid"; return false; }
                if (status)
                {
                    if (string.IsNullOrWhiteSpace(statusId) || !System.Text.RegularExpressions.Regex.IsMatch(statusId, "^[a-z][a-z0-9_]*$") ||
                        !(statusAttributeId is "physical_attack" or "soul_attack" or "physical_defense" or "soul_defense") ||
                        statusRelativePermille == 0 || statusRelativePermille < -1000 || statusRelativePermille > 1000 ||
                        (effectType == AttackEffectType.Buff) != (statusRelativePermille > 0) || statusDurationOwnTurnEnds <= 0 ||
                        statusStackLimit != 1 || statusRefreshPolicy != "replace_stronger" || buffMultiplier != 0f)
                    { reason = "attack_profile_status_payload_invalid"; return false; }
                }
                else if (!string.IsNullOrEmpty(statusId) || !string.IsNullOrEmpty(statusAttributeId) || statusRelativePermille != 0 ||
                         statusDurationOwnTurnEnds != 0 || statusStackLimit != 0 || !string.IsNullOrEmpty(statusRefreshPolicy))
                { reason = "attack_profile_non_status_fields_present"; return false; }
                if (controlChancePermille < 0 || controlChancePermille > 1000 || (!damage && (controlChancePermille != 0 || controlUsesMindContest)))
                { reason = "attack_profile_control_probability_invalid"; return false; }
            }
            if (effectType == AttackEffectType.RestoreSpirit)
            {
                if (float.IsNaN(spiritRecoveryMultiplier) || float.IsInfinity(spiritRecoveryMultiplier) ||
                    spiritRecoveryMultiplier <= 0f || minimumSpiritRecovery < 0 || otherTargetResourceCost < 0 ||
                    float.IsNaN(otherTargetRecoveryMultiplier) || float.IsInfinity(otherTargetRecoveryMultiplier) ||
                    otherTargetRecoveryMultiplier <= 0f || otherTargetRecoveryMultiplier > 1f ||
                    resourceKind != AttackResourceKind.Mp || targetingMode != AttackTargetingMode.Single ||
                    physicalDamageMultiplier != 0f || soulDamageMultiplier != 0f || healAmount != 0 ||
                    buffMultiplier != 0f || defensePenetration != 0f || !string.IsNullOrEmpty(damageElementId))
                {
                    reason = "spirit_recovery_profile_contract_invalid";
                    return false;
                }
            }
            else if (spiritRecoveryMultiplier != 0f || minimumSpiritRecovery != 0 ||
                     otherTargetResourceCost != 0 || otherTargetRecoveryMultiplier != 0f)
            {
                reason = "non_recovery_fields_present";
                return false;
            }

            if (profileKind == AttackProfileKind.Basic)
            {
                if (basicBindingKind == BasicAttackBindingKind.Unknown ||
                    !string.IsNullOrEmpty(contentScope) ||
                    !string.IsNullOrEmpty(sourceAffiliation) ||
                    !string.IsNullOrEmpty(realmRequirementId) ||
                    !string.IsNullOrEmpty(elementRequirementId) ||
                    resourceKind != AttackResourceKind.None || resourceCost != 0 ||
                    ctPenalty != 0 || reuseCooldownTicks != 0)
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

            if (!Enum.IsDefined(typeof(AttackAreaCenterKind), areaCenterKind) || areaCenterKind == AttackAreaCenterKind.Unknown ||
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
                case AttackAreaShapeKind.ForwardPair:
                case AttackAreaShapeKind.AdjacentPair:
                    bool forward = areaShapeKind == AttackAreaShapeKind.ForwardPair;
                    if (attackProfileId != (forward ? "basic_iron_sword" : "basic_iron_blade") ||
                        profileKind != AttackProfileKind.Basic || basicBindingKind != BasicAttackBindingKind.MainEquipment ||
                        effectType != AttackEffectType.Physical || effectBudgetMode != "fixed_weapon" ||
                        areaCenterKind != AttackAreaCenterKind.Caster || areaRadius != 0 ||
                        areaLength != (forward ? 2 : 1) || areaFanHalfAngleSteps != 0 || areaFacing != -1 || areaInnerRadius != 0 ||
                        areaEffectBlockers != AttackAreaEffectBlocker.DirectedEdge ||
                        areaAllowedFactions != AttackAreaTargetFaction.Enemy || areaAllowedStates != AttackAreaTargetState.Alive)
                    { reason = "two_cell_weapon_contract_invalid"; return false; }
                    reason = string.Empty;
                    return true;
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
