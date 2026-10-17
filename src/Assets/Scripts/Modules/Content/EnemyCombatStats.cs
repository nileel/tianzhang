using System;

namespace TianZhang.Content
{
    /// <summary>
    /// Enemy-only combat projection. Derive the unscaled template first, then scale each
    /// independent result once; scaled primary attributes must never be derived again.
    /// Shared verbatim with BattleSim. Realm, abilities, costs and drops are not attributes.
    /// </summary>
    [Serializable]
    public sealed class EnemyCombatStats
    {
        public int rootBone, physique, spirit, mind, reaction, talent;
        public int maximumHealth, maximumSpirit, physicalAttack, soulAttack, physicalDefense, soulDefense;
        public int actionPointMaximumUnits, movementEfficiencyMilliUnits;
        public float hpBonus, mpBonus, physAtkBonus, magAtkBonus, physDefBonus, magDefBonus;
        public float blockRate, blockReduction, soulShieldRate, soulShieldReduction;
        public float dodgeRate, critRate, critDamage, hitRateBonus;

        public static void ValidateScale(float scale)
        {
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f || scale > 1f)
                throw new ArgumentOutOfRangeException(nameof(scale), "Enemy combat scale must be in (0, 1].");
        }

        public EnemyCombatStats Scale(float scale)
        {
            ValidateScale(scale);
            int Rounded(int value, int minimum = 0) => Math.Max(minimum,
                checked((int)Math.Round((double)value * scale, MidpointRounding.AwayFromZero)));
            return new EnemyCombatStats
            {
                rootBone = Rounded(rootBone, 1), physique = Rounded(physique, 1),
                spirit = Rounded(spirit), mind = Rounded(mind), reaction = Rounded(reaction, 1), talent = Rounded(talent),
                maximumHealth = Rounded(maximumHealth, 1), maximumSpirit = Rounded(maximumSpirit),
                physicalAttack = Rounded(physicalAttack), soulAttack = Rounded(soulAttack),
                physicalDefense = Rounded(physicalDefense), soulDefense = Rounded(soulDefense),
                actionPointMaximumUnits = Rounded(actionPointMaximumUnits, 1),
                movementEfficiencyMilliUnits = Rounded(movementEfficiencyMilliUnits),
                hpBonus = hpBonus * scale, mpBonus = mpBonus * scale,
                physAtkBonus = physAtkBonus * scale, magAtkBonus = magAtkBonus * scale,
                physDefBonus = physDefBonus * scale, magDefBonus = magDefBonus * scale,
                blockRate = blockRate * scale, blockReduction = blockReduction * scale,
                soulShieldRate = soulShieldRate * scale, soulShieldReduction = soulShieldReduction * scale,
                dodgeRate = dodgeRate * scale, critRate = critRate * scale,
                critDamage = critDamage * scale, hitRateBonus = hitRateBonus * scale,
            };
        }
    }
}
