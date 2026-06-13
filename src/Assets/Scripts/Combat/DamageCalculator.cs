using UnityEngine;
using TianZhang.Entity;

namespace TianZhang.Combat
{
    /// <summary>
    /// 伤害计算器 — 与 BattleSim Program.cs 公式一致
    /// 物理/神魂双线独立结算 + 格挡/魂盾/闪避/暴击/抗性
    /// </summary>
    public static class DamageCalculator
    {
        public struct DamageResult
        {
            public int FinalDamage;
            public bool IsHit;
            public bool IsBlocked;
            public bool IsSoulShielded;
            public bool IsCrit;
            public bool IsDodged;
            public string Log;
        }

        /// <summary>物理伤害计算</summary>
        public static DamageResult CalcPhysical(int rawAtk, float skillMultiplier,
            Character attacker, Character defender, bool cannotBlock = false)
        {
            var result = new DamageResult();

            // 1. 命中判定
            float baseHit = 90f;
            float hitRate = baseHit + attacker.HitRateBonus - defender.DodgeRate;
            hitRate *= defender.GetFacingHitModifier(attacker);
            hitRate = Mathf.Clamp(hitRate, 5f, 100f);

            if (Random.value * 100f > hitRate)
            {
                result.IsHit = false;
                result.Log = "未命中";
                return result;
            }
            result.IsHit = true;

            // 2. 闪避判定
            float finalDodge = defender.DodgeRate * defender.GetFacingHitModifier(attacker);
            if (Random.value * 100f < finalDodge)
            {
                result.IsDodged = true;
                result.Log = "闪避";
                return result;
            }

            // 3. 基础伤害
            float baseDamage = rawAtk * skillMultiplier;
            float defense = cannotBlock ? defender.PhysDef * 0.5f : defender.PhysDef;
            float damage = baseDamage / (defense * 0.01f + 1f);

            // 4. 朝向伤害修正
            damage *= defender.GetFacingDamageModifier(attacker);

            // 5. 格挡判定
            float blockChance = attacker.GetFacingHitModifier(defender) < 1f
                ? defender.BlockRate * 1.5f : defender.BlockRate;
            if (!cannotBlock && Random.value * 100f < blockChance)
            {
                result.IsBlocked = true;
                damage *= (1f - defender.BlockReduction / 100f);
            }

            // 6. 暴击判定
            float critChance = attacker.CritRate *
                (defender.GetFacingDamageModifier(attacker) > 1f ? 1.3f : 1f);
            if (Random.value * 100f < critChance)
            {
                result.IsCrit = true;
                damage *= (1f + attacker.CritDamage / 100f);
            }

            // 7. 防御姿态减伤
            if (defender.IsGuarding)
                damage *= 0.5f;

            result.FinalDamage = Mathf.Max(1, Mathf.RoundToInt(damage));
            result.Log = $"物理伤害 {result.FinalDamage}" +
                (result.IsBlocked ? " [格挡]" : "") +
                (result.IsCrit ? " [暴击]" : "");
            return result;
        }

        /// <summary>神魂伤害计算</summary>
        public static DamageResult CalcMagic(int rawAtk, float skillMultiplier,
            Character attacker, Character defender)
        {
            var result = new DamageResult();

            // 1. 命中判定（神魂攻击命中率基础较低，但受朝向影响小）
            float baseHit = 85f;
            float hitRate = baseHit + attacker.HitRateBonus - defender.DodgeRate * 0.5f;
            hitRate = Mathf.Clamp(hitRate, 5f, 100f);

            if (Random.value * 100f > hitRate)
            {
                result.IsHit = false;
                result.Log = "神魂未命中";
                return result;
            }
            result.IsHit = true;

            // 2. 基础伤害
            float baseDamage = rawAtk * skillMultiplier;
            float damage = baseDamage / (defender.MagDef * 0.01f + 1f);

            // 3. 魂盾判定
            if (Random.value * 100f < defender.SoulShieldRate)
            {
                result.IsSoulShielded = true;
                damage *= (1f - defender.SoulShieldReduction / 100f);
            }

            // 4. 暴击
            if (Random.value * 100f < attacker.CritRate)
            {
                result.IsCrit = true;
                damage *= (1f + attacker.CritDamage / 100f);
            }

            result.FinalDamage = Mathf.Max(1, Mathf.RoundToInt(damage));
            result.Log = $"神魂伤害 {result.FinalDamage}" +
                (result.IsSoulShielded ? " [魂盾]" : "") +
                (result.IsCrit ? " [暴击]" : "");
            return result;
        }
    }
}
