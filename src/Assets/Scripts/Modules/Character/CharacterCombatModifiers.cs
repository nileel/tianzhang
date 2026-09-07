using TianZhang.Entity;
using UnityEngine;

namespace TianZhang.Character
{
    /// <summary>
    /// Immutable projection of the fourteen explicit combat fields carried from <see cref="CharacterData"/>
    /// into battle. It stores raw floats verbatim; the six first-order bonuses are rounded to integers only
    /// when consumed by <see cref="CharacterAttributes.Derive"/>.
    /// </summary>
    public sealed class CharacterCombatModifiers
    {
        public static readonly CharacterCombatModifiers Empty = new CharacterCombatModifiers();

        public CharacterCombatModifiers(
            float hpBonus,
            float mpBonus,
            float physAtkBonus,
            float magAtkBonus,
            float physDefBonus,
            float magDefBonus,
            float blockRate,
            float blockReduction,
            float soulShieldRate,
            float soulShieldReduction,
            float dodgeRate,
            float critRate,
            float critDamage,
            float hitRateBonus)
        {
            HpBonus = hpBonus;
            MpBonus = mpBonus;
            PhysAtkBonus = physAtkBonus;
            MagAtkBonus = magAtkBonus;
            PhysDefBonus = physDefBonus;
            MagDefBonus = magDefBonus;
            BlockRate = blockRate;
            BlockReduction = blockReduction;
            SoulShieldRate = soulShieldRate;
            SoulShieldReduction = soulShieldReduction;
            DodgeRate = dodgeRate;
            CritRate = critRate;
            CritDamage = critDamage;
            HitRateBonus = hitRateBonus;
        }

        private CharacterCombatModifiers() { }

        public float HpBonus { get; }
        public float MpBonus { get; }
        public float PhysAtkBonus { get; }
        public float MagAtkBonus { get; }
        public float PhysDefBonus { get; }
        public float MagDefBonus { get; }
        public float BlockRate { get; }
        public float BlockReduction { get; }
        public float SoulShieldRate { get; }
        public float SoulShieldReduction { get; }
        public float DodgeRate { get; }
        public float CritRate { get; }
        public float CritDamage { get; }
        public float HitRateBonus { get; }

        public static CharacterCombatModifiers FromDefinition(CharacterData definition)
        {
            if (definition == null) throw new System.ArgumentNullException(nameof(definition));
            return new CharacterCombatModifiers(
                definition.hpBonus,
                definition.mpBonus,
                definition.physAtkBonus,
                definition.magAtkBonus,
                definition.physDefBonus,
                definition.magDefBonus,
                definition.blockRate,
                definition.blockReduction,
                definition.soulShieldRate,
                definition.soulShieldReduction,
                definition.dodgeRate,
                definition.critRate,
                definition.critDamage,
                definition.hitRateBonus);
        }

        /// <summary>Converts the six first-order bonuses to integer bonuses at the Derive boundary only.</summary>
        public CharacterAttributeBonuses ToAttributeBonuses()
        {
            return new CharacterAttributeBonuses
            {
                Health = Mathf.RoundToInt(HpBonus),
                SpiritResource = Mathf.RoundToInt(MpBonus),
                PhysicalAttack = Mathf.RoundToInt(PhysAtkBonus),
                MagicAttack = Mathf.RoundToInt(MagAtkBonus),
                PhysicalDefense = Mathf.RoundToInt(PhysDefBonus),
                MagicDefense = Mathf.RoundToInt(MagDefBonus),
            };
        }
    }
}
