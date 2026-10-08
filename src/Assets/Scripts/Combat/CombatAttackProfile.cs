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
            bool isTalisman = false)
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
        }

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
    }
}
