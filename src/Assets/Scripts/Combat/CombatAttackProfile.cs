using System;

namespace TianZhang.Combat
{
    public enum CombatAttackKind
    {
        Basic,
        Art,
        Divine,
    }

    public enum CombatAttackEffect
    {
        Physical,
        Soul,
        Hybrid,
        Heal,
        RestoreSpirit,
        Buff,
        Debuff,
    }

    /// <summary>Resolved fixed-tier reference and declared narrow payload; no combat algorithms.</summary>
    public sealed class CombatAttackRuleMetadata
    {
        public string RuleSetVersion { get; }
        public string SourceDigest { get; }
        public CombatRuleSet.EffectTier Tier { get; }
        public string CooldownClass { get; }
        public string EffectBudgetMode { get; }
        public CombatRuleRatio? Efficiency { get; }
        public CombatRuleRatio? DamageMigration { get; }
        public CombatRuleRatio? DamageWeight { get; }
        public string StatusId { get; }
        public string StatusAttributeId { get; }
        public int StatusRelativePermille { get; }
        public int StatusDurationOwnTurnEnds { get; }
        public int StatusStackLimit { get; }
        public string StatusRefreshPolicy { get; }
        public int ControlChancePermille { get; }
        public bool ControlUsesMindContest { get; }
        // Finite SIM prototypes, never inferred from a display name or granted to player content.
        public string PrototypeDefinitionRef { get; }
        public decimal BaseEffectBudget { get; }
        public string UnarmedFamilyId { get; }
        public string UnarmedRealmId { get; }
        public int UnarmedPhysicalAttackCapacity { get; }
        public CombatAttackRuleMetadata(string version, string digest, CombatRuleSet.EffectTier tier,
            string cooldownClass, string effectBudgetMode, CombatRuleRatio? efficiency,
            CombatRuleRatio? migration, CombatRuleRatio? weight, string statusId, string attribute,
            int relative, int duration, int stackLimit, string refresh, int controlChance, bool mindContest,
            string prototypeDefinitionRef = "", decimal baseEffectBudget = 0,
            string unarmedFamilyId = "", string unarmedRealmId = "", int unarmedPhysicalAttackCapacity = 0)
        {
            if (version != CombatRuleSet.Version || digest == null || !System.Text.RegularExpressions.Regex.IsMatch(digest, "^[a-f0-9]{64}$") || tier == null)
                throw new ArgumentException("attack_rule_identity_invalid");
            if (prototypeDefinitionRef != "" &&
                (Array.IndexOf(CombatRuleSet.DefinitionIds, prototypeDefinitionRef) < 0 || baseEffectBudget <= 0 ||
                 effectBudgetMode != "damage_budget"))
                throw new ArgumentException("attack_prototype_payload_invalid");
            if (effectBudgetMode == "fixed_unarmed")
            {
                bool human = unarmedFamilyId == "body_human" && unarmedRealmId is "realm_fanren" or "realm_lianqi" or "realm_zhuji" or "realm_jindan";
                bool beast = unarmedFamilyId == "body_shijiahou" && unarmedRealmId == "realm_lianqi";
                if (!(human || beast) || unarmedPhysicalAttackCapacity <= 0 ||
                    tier.AbilityTierId != (unarmedRealmId == "realm_fanren" ? "tier_lianqi" : "tier_" + unarmedRealmId.Substring(6)) ||
                    cooldownClass != "basic_repeat" || efficiency.HasValue || migration.HasValue || weight.HasValue ||
                    !string.IsNullOrEmpty(statusId) || !string.IsNullOrEmpty(attribute) || relative != 0 || duration != 0 ||
                    stackLimit != 0 || !string.IsNullOrEmpty(refresh) || controlChance != 0 || mindContest ||
                    !string.IsNullOrEmpty(prototypeDefinitionRef) || baseEffectBudget != 0)
                    throw new ArgumentException("attack_unarmed_payload_invalid");
            }
            else if (!string.IsNullOrEmpty(unarmedFamilyId) || !string.IsNullOrEmpty(unarmedRealmId) || unarmedPhysicalAttackCapacity != 0)
                throw new ArgumentException("attack_non_unarmed_payload_present");
            UnarmedFamilyId = unarmedFamilyId ?? ""; UnarmedRealmId = unarmedRealmId ?? "";
            UnarmedPhysicalAttackCapacity = unarmedPhysicalAttackCapacity;
            PrototypeDefinitionRef = prototypeDefinitionRef; BaseEffectBudget = baseEffectBudget;
            RuleSetVersion = version; SourceDigest = digest; Tier = tier; CooldownClass = cooldownClass;
            EffectBudgetMode = effectBudgetMode; Efficiency = efficiency; DamageMigration = migration; DamageWeight = weight;
            StatusId = statusId; StatusAttributeId = attribute; StatusRelativePermille = relative;
            StatusDurationOwnTurnEnds = duration; StatusStackLimit = stackLimit; StatusRefreshPolicy = refresh;
            ControlChancePermille = controlChance; ControlUsesMindContest = mindContest;
        }
    }

    /// <summary>Immutable non-Unity projection of one resolved attack profile.</summary>
    public sealed class CombatAttackProfile
    {
        private readonly Func<string, string, string, bool> qualification;

        public CombatAttackProfile(
            string id,
            CombatAttackKind kind,
            CombatAttackEffect effect,
            int minimumRange,
            int maximumRange,
            int actionPointCostUnits,
            int minimumNetCostUnits,
            int ctPenalty,
            int reuseCooldownTicks,
            float physicalMultiplier = 0f,
            float soulMultiplier = 0f,
            int healAmount = 0,
            int spiritCost = 0,
            string damageElement = "",
            float soulDefensePenetration = 0f,
            float spiritRecoveryMultiplier = 0f,
            int minimumSpiritRecovery = 0,
            int otherTargetSpiritCost = 0,
            float otherTargetSpiritRecoveryMultiplier = 1f,
            Func<string, string, string, bool> qualification = null,
            bool isTalisman = false,
            CombatAttackRuleMetadata ruleMetadata = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Attack profile ID is required.", nameof(id));
            if (!Enum.IsDefined(typeof(CombatAttackKind), kind) || !Enum.IsDefined(typeof(CombatAttackEffect), effect) ||
                float.IsNaN(physicalMultiplier) || float.IsInfinity(physicalMultiplier) || physicalMultiplier < 0 ||
                float.IsNaN(soulMultiplier) || float.IsInfinity(soulMultiplier) || soulMultiplier < 0)
                throw new ArgumentException("Invalid attack effect or multiplier.");
            if (minimumRange < 0 || maximumRange < minimumRange || spiritCost < 0 || healAmount < 0)
                throw new ArgumentOutOfRangeException(nameof(minimumRange));
            // 统一行动力公共合同：四值以各自单位严格验证，不设默认、不从另一字段推导。
            if (actionPointCostUnits <= 0)
                throw new ArgumentOutOfRangeException(nameof(actionPointCostUnits));
            if (minimumNetCostUnits <= 0 || minimumNetCostUnits > actionPointCostUnits)
                throw new ArgumentOutOfRangeException(nameof(minimumNetCostUnits));
            if (ctPenalty < 0 || ctPenalty > int.MaxValue - 100)
                throw new ArgumentOutOfRangeException(nameof(ctPenalty));
            if (reuseCooldownTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(reuseCooldownTicks));
            if (float.IsNaN(spiritRecoveryMultiplier) || float.IsInfinity(spiritRecoveryMultiplier) ||
                spiritRecoveryMultiplier < 0f || minimumSpiritRecovery < 0 || otherTargetSpiritCost < 0 ||
                float.IsNaN(otherTargetSpiritRecoveryMultiplier) || float.IsInfinity(otherTargetSpiritRecoveryMultiplier) ||
                otherTargetSpiritRecoveryMultiplier < 0f)
                throw new ArgumentOutOfRangeException(nameof(spiritRecoveryMultiplier));
            if (effect is CombatAttackEffect.Buff or CombatAttackEffect.Debuff &&
                (ruleMetadata == null || ruleMetadata.EffectBudgetMode != "status_only" ||
                 string.IsNullOrWhiteSpace(ruleMetadata.StatusId) ||
                 !(ruleMetadata.StatusAttributeId is "physical_attack" or "soul_attack" or "physical_defense" or "soul_defense") ||
                 ruleMetadata.StatusRelativePermille == 0 || Math.Abs((long)ruleMetadata.StatusRelativePermille) > 1000 ||
                 (effect == CombatAttackEffect.Buff) != (ruleMetadata.StatusRelativePermille > 0) ||
                 ruleMetadata.StatusDurationOwnTurnEnds <= 0 || ruleMetadata.StatusStackLimit != 1 ||
                 ruleMetadata.StatusRefreshPolicy != "replace_stronger"))
                throw new ArgumentException("combat_status_payload_invalid");
            if (ruleMetadata?.PrototypeDefinitionRef is "KuxingAction" or "WaterDivine" &&
                (effect != CombatAttackEffect.Physical ||
                 kind != (ruleMetadata.PrototypeDefinitionRef == "WaterDivine" ? CombatAttackKind.Divine : CombatAttackKind.Art)))
                throw new ArgumentException("combat_prototype_effect_invalid");
            if (ruleMetadata?.EffectBudgetMode == "fixed_unarmed" &&
                (id != "basic_" + ruleMetadata.UnarmedFamilyId + "_" + ruleMetadata.UnarmedRealmId.Substring(6) ||
                 kind != CombatAttackKind.Basic || effect != CombatAttackEffect.Physical || minimumRange != 1 || maximumRange != 1 ||
                 physicalMultiplier <= 0 || float.IsNaN(physicalMultiplier) || float.IsInfinity(physicalMultiplier) ||
                 soulMultiplier != 0 || healAmount != 0 || spiritCost != 0 || ctPenalty != 0 || reuseCooldownTicks != 0 ||
                 damageElement != "element_none" || soulDefensePenetration != 0 || spiritRecoveryMultiplier != 0 ||
                 minimumSpiritRecovery != 0 || otherTargetSpiritCost != 0))
                throw new ArgumentException("combat_unarmed_effect_invalid");

            Id = id;
            Kind = kind;
            Effect = effect;
            MinimumRange = minimumRange;
            MaximumRange = maximumRange;
            ActionPointCostUnits = actionPointCostUnits;
            MinimumNetCostUnits = minimumNetCostUnits;
            CtPenalty = ctPenalty;
            ReuseCooldownTicks = reuseCooldownTicks;
            PhysicalMultiplier = physicalMultiplier;
            SoulMultiplier = soulMultiplier;
            HealAmount = healAmount;
            SpiritCost = spiritCost;
            DamageElement = damageElement ?? string.Empty;
            SoulDefensePenetration = soulDefensePenetration;
            SpiritRecoveryMultiplier = spiritRecoveryMultiplier;
            MinimumSpiritRecovery = minimumSpiritRecovery;
            OtherTargetSpiritCost = otherTargetSpiritCost;
            OtherTargetSpiritRecoveryMultiplier = otherTargetSpiritRecoveryMultiplier;
            this.qualification = qualification;
            IsTalisman = isTalisman;
            RuleMetadata = ruleMetadata;
        }

        public CombatAttackRuleMetadata RuleMetadata { get; }
        public string Id { get; }
        public CombatAttackKind Kind { get; }
        public CombatAttackEffect Effect { get; }
        public int MinimumRange { get; }
        public int MaximumRange { get; }
        public int ActionPointCostUnits { get; }
        public int MinimumNetCostUnits { get; }
        public int CtPenalty { get; }
        public int ReuseCooldownTicks { get; }
        public float PhysicalMultiplier { get; }
        public float SoulMultiplier { get; }
        public int HealAmount { get; }
        public int SpiritCost { get; }
        public string DamageElement { get; }
        public float SoulDefensePenetration { get; }
        public float SpiritRecoveryMultiplier { get; }
        public int MinimumSpiritRecovery { get; }
        public int OtherTargetSpiritCost { get; }
        public bool IsTalisman { get; }
        public float OtherTargetSpiritRecoveryMultiplier { get; }

        public bool MeetsRequirements(string realmStage, string rootElement, string rootGrade) =>
            qualification == null || qualification(realmStage, rootElement, rootGrade);

        public int GetSpiritCost(bool targetsSelf) =>
            Effect == CombatAttackEffect.RestoreSpirit && !targetsSelf ? OtherTargetSpiritCost : SpiritCost;

        public bool AllowsTarget(CombatTeam actor, CombatTeam target) => Effect switch
        {
            CombatAttackEffect.RestoreSpirit => true,
            CombatAttackEffect.Buff => actor == target,
            _ => actor != target,
        };
    }
}
