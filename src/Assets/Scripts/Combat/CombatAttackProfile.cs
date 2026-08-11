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
    }

    /// <summary>Immutable non-Unity projection of one resolved attack profile.</summary>
    public sealed class CombatAttackProfile
    {
        public CombatAttackProfile(
            string id,
            CombatAttackKind kind,
            CombatAttackEffect effect,
            int minimumRange,
            int maximumRange,
            float physicalMultiplier = 0f,
            float soulMultiplier = 0f,
            int healAmount = 0,
            int spiritCost = 0,
            int cooldownTicks = 0,
            string damageElement = "",
            float soulDefensePenetration = 0f)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Attack profile ID is required.", nameof(id));
            if (minimumRange < 0 || maximumRange < minimumRange || spiritCost < 0 || cooldownTicks < 0 || healAmount < 0)
                throw new ArgumentOutOfRangeException(nameof(minimumRange));

            Id = id;
            Kind = kind;
            Effect = effect;
            MinimumRange = minimumRange;
            MaximumRange = maximumRange;
            PhysicalMultiplier = physicalMultiplier;
            SoulMultiplier = soulMultiplier;
            HealAmount = healAmount;
            SpiritCost = spiritCost;
            CooldownTicks = cooldownTicks;
            DamageElement = damageElement ?? string.Empty;
            SoulDefensePenetration = soulDefensePenetration;
        }

        public string Id { get; }
        public CombatAttackKind Kind { get; }
        public CombatAttackEffect Effect { get; }
        public int MinimumRange { get; }
        public int MaximumRange { get; }
        public float PhysicalMultiplier { get; }
        public float SoulMultiplier { get; }
        public int HealAmount { get; }
        public int SpiritCost { get; }
        public int CooldownTicks { get; }
        public string DamageElement { get; }
        public float SoulDefensePenetration { get; }
    }
}
