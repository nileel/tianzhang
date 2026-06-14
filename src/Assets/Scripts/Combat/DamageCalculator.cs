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
        // ═══ 五行元素克制（土→水→火→金→土） ═══
        private static readonly System.Collections.Generic.Dictionary<string, string> GongFaElement = new()
        {
            // 混元山（土）
            ["含弘光大典"] = "土", ["白屋青云录"] = "土", ["混元同尘典"] = "土", ["绳墨正法录"] = "土",
            // 玉清崖（金）
            ["疾雷破山经"] = "金", ["九霄雷劫录"] = "金", ["雷池淬体功"] = "金", ["苦行剑典"] = "金",
            // 太一道庭（水）
            ["抱元守一经"] = "水", ["云篆度人经"] = "水",
            // 太虚观（火）
            ["南华玄感录"] = "火", ["万物不迁法"] = "火", ["不真自虚法"] = "火", ["心无性有法"] = "火",
            // 散修（水）
            ["秋水游心经"] = "水",
        };

        /// <summary>元素克制关系：土→水→火→金→土</summary>
        private static bool IsCounter(string attackerElem, string defenderElem) =>
            (attackerElem, defenderElem) switch
            {
                ("土", "水") => true,
                ("水", "火") => true,
                ("火", "金") => true,
                ("金", "土") => true,
                _ => false
            };

        /// <summary>获取元素克制倍率（克制+25%，被克-25%，无关系×1）</summary>
        public static float GetElementMultiplier(string atkGongFa, string defGongFa)
        {
            if (string.IsNullOrEmpty(atkGongFa) || string.IsNullOrEmpty(defGongFa)) return 1f;
            if (!GongFaElement.TryGetValue(atkGongFa, out string ae)) return 1f;
            if (!GongFaElement.TryGetValue(defGongFa, out string de)) return 1f;
            if (IsCounter(ae, de)) return 1.25f;   // 克制
            if (IsCounter(de, ae)) return 0.75f;   // 被克
            return 1f;
        }

        /// <summary>查询功法的五行属性（公开给UI用）</summary>
        public static string GetGongFaElement(string gongFaName)
        {
            if (string.IsNullOrEmpty(gongFaName)) return "";
            return GongFaElement.TryGetValue(gongFaName, out string e) ? e : "";
        }

        /// <summary>解析CSV中的elementReq字段为标准五行（金/水/火/土/空）</summary>
        public static string ResolveElement(string elementReq)
        {
            if (string.IsNullOrEmpty(elementReq)) return "";
            string lower = elementReq.ToLower();
            if (lower.Contains("water")) return "水";
            if (lower.Contains("fire")) return "火";
            if (lower.Contains("earth")) return "土";
            if (lower.Contains("metal") || lower.Contains("thunder")) return "金";
            if (lower.Contains("wood")) return "木";
            if (lower.Contains("any")) return "";
            return "";
        }

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

            // 3. 守一减伤（抱元守一经：每受击消耗1层，减伤20%）
            float shouyiDR = 1f;
            if (defender.GongFaName == "抱元守一经" && defender.ShouyiStacks > 0)
            {
                shouyiDR = 0.80f;
                defender.ShouyiStacks--;
            }

            // 4. 基础伤害
            float elemMult = GetElementMultiplier(attacker.GongFaName, defender.GongFaName);
            float baseDamage = rawAtk * skillMultiplier * elemMult;
            float defense = cannotBlock ? defender.PhysDef * 0.5f : defender.PhysDef;
            float damage = baseDamage / (defense * 0.01f + 1f) * shouyiDR;

            // 5. 朝向伤害修正
            damage *= defender.GetFacingDamageModifier(attacker);

            // 6. 格挡判定
            float blockChance = attacker.GetFacingHitModifier(defender) < 1f
                ? defender.BlockRate * 1.5f : defender.BlockRate;
            if (!cannotBlock && Random.value * 100f < blockChance)
            {
                result.IsBlocked = true;
                damage *= (1f - defender.BlockReduction / 100f);
            }

            // 7. 暴击判定
            float critChance = attacker.CritRate *
                (defender.GetFacingDamageModifier(attacker) > 1f ? 1.3f : 1f);
            if (Random.value * 100f < critChance)
            {
                result.IsCrit = true;
                damage *= (1f + attacker.CritDamage / 100f);
            }

            // 8. 防御姿态减伤
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

            // 2. 守一减伤（抱元守一经：每受击消耗1层，减伤20%）
            float shouyiDR = 1f;
            if (defender.GongFaName == "抱元守一经" && defender.ShouyiStacks > 0)
            {
                shouyiDR = 0.80f;
                defender.ShouyiStacks--;
            }

            // 3. 守一满层神魂防（抱元守一经：满层+15%神魂抗性）
            float magResistBonus = 0f;
            if (defender.GongFaName == "抱元守一经" && defender.ShouyiStacks == defender.MaxShouyi())
                magResistBonus = 15f;

            // 4. 基础伤害
            float elemMult = GetElementMultiplier(attacker.GongFaName, defender.GongFaName);
            float baseDamage = rawAtk * skillMultiplier * elemMult;
            float magicDef = defender.MagDef * (1f + magResistBonus / 100f);
            float damage = baseDamage / (magicDef * 0.01f + 1f) * shouyiDR;

            // 5. 魂盾判定
            if (Random.value * 100f < defender.SoulShieldRate)
            {
                result.IsSoulShielded = true;
                damage *= (1f - defender.SoulShieldReduction / 100f);
            }

            // 6. 暴击
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
