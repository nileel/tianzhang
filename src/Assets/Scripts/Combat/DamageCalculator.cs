using System.Collections.Generic;
using UnityEngine;
using TianZhang.Entity;

namespace TianZhang.Combat
{
    /// <summary>
    /// 伤害计算器。
    /// 当前实现：双线伤害公式 + v5.10 五行内外圈匹配；BattleSim 同源化仍需后续切片。
    /// </summary>
    public static class DamageCalculator
    {
        private const float MinHitRate = 5f;
        private const float MaxHitRate = 100f;
        private const float BaseHitRate = 100f;

        private static readonly Dictionary<string, string> GongFaElement = new()
        {
            ["抱元守一经"] = "水", ["gongfa_baoyuanshouyi"] = "水",
            ["云篆度人经"] = "风", ["gongfa_yunzhuandurenjing"] = "风",
            ["秋水游心经"] = "水", ["gongfa_qiushuiyouxin"] = "水",

            ["九霄雷劫录"] = "雷", ["gongfa_jiuxiaoleijie"] = "雷",
            ["苦行剑典"] = "金", ["gongfa_kuxingjiandian"] = "金",
            ["疾雷破山经"] = "雷", ["gongfa_jileiposhan"] = "雷",
            ["雷池淬体功"] = "雷", ["gongfa_leicnicuiti"] = "雷",

            ["含弘光大典"] = "土", ["gongfa_hanhongguangda"] = "土",
            ["白屋青云录"] = "土", ["gongfa_baiwuqingyun"] = "土",
            ["混元同尘典"] = "土", ["gongfa_hunyuantongchen"] = "土",
            ["绳墨正法录"] = "土", ["gongfa_shengmozhengfa"] = "土",

            ["万物不迁法"] = "暗", ["gongfa_wanwubuqian"] = "暗",
            ["不真自虚法"] = "暗", ["gongfa_buzhenzixu"] = "暗",
            ["南华玄感录"] = "暗", ["gongfa_nanhuaxuangan"] = "暗",
            ["心无性有法"] = "暗", ["gongfa_xinwuxingyou"] = "暗",

            ["南华大梦书"] = "水", ["gongfa_nanhuadamengshu"] = "水",
            ["南华阐衍典"] = "风", ["gongfa_nanhuachanyandian"] = "风",
            ["大洞炼真经"] = "土", ["gongfa_dadonglianzhenjing"] = "土",
            ["太易山藏经"] = "土", ["gongfa_taiyishancangjing"] = "土",
            ["太易玄义笺"] = "土", ["gongfa_taiyixuanyijian"] = "土",
            ["玄牝道藏"] = "混沌", ["gongfa_xuanpindaocang"] = "混沌",
            ["空无般若经"] = "水", ["gongfa_kongwuborejing"] = "水",
            ["见素抱朴经"] = "木", ["gongfa_jiansubaopujing"] = "木",
            ["通神三玄礼录"] = "金", ["gongfa_tongshensanxuanlilu"] = "金",
        };

        private readonly struct ElementMatch
        {
            public readonly float DamageMultiplier;
            public readonly float CritRateBonus;
            public readonly float CritDamageBonus;

            public ElementMatch(float damageMultiplier, float critRateBonus, float critDamageBonus)
            {
                DamageMultiplier = damageMultiplier;
                CritRateBonus = critRateBonus;
                CritDamageBonus = critDamageBonus;
            }
        }

        /// <summary>查询功法主五行属性。</summary>
        public static string GetGongFaElement(string gongFaName)
        {
            if (string.IsNullOrEmpty(gongFaName)) return "";
            return GongFaElement.TryGetValue(gongFaName, out string e) ? e : "";
        }

        /// <summary>兼容旧调用：以攻击方功法属性当作技能属性计算倍率。</summary>
        public static float GetElementMultiplier(string atkGongFa, string defGongFa)
        {
            string skillElement = GetGongFaElement(atkGongFa);
            return GetElementMatch(skillElement, atkGongFa, defGongFa).DamageMultiplier;
        }

        /// <summary>计算 v5.10 五行内外圈伤害倍率。</summary>
        public static float GetElementDamageMultiplier(string skillElement, string attackerGongFa, string defenderGongFa)
        {
            return GetElementMatch(skillElement, attackerGongFa, defenderGongFa).DamageMultiplier;
        }

        /// <summary>解析CSV字段为标准五行/变异/特殊属性。</summary>
        public static string ResolveElement(string elementReq)
        {
            if (string.IsNullOrWhiteSpace(elementReq)) return "";

            string value = elementReq.Trim().ToLowerInvariant();
            if (value == "-" || value.Contains("any")) return "";
            if (value.Contains("chaos")) return "混沌";
            if (value.Contains("thunder")) return "雷";
            if (value.Contains("wind")) return "风";
            if (value.Contains("ice")) return "冰";
            if (value.Contains("dark")) return "暗";
            if (value.Contains("star")) return "星";
            if (value.Contains("poison") || value.Contains("toxin")) return "毒";
            if (value.Contains("water")) return "水";
            if (value.Contains("fire")) return "火";
            if (value.Contains("earth")) return "土";
            if (value.Contains("metal")) return "金";
            if (value.Contains("wood")) return "木";

            return NormalizeElement(elementReq);
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

        public static DamageResult CalcPhysical(int rawAtk, float skillMultiplier,
            Character attacker, Character defender, bool cannotBlock = false,
            string skillElement = "", bool cannotDodge = false)
        {
            var result = new DamageResult();

            if (!RollHit(attacker, defender, cannotDodge, ref result))
                return result;

            ElementMatch elementMatch = GetElementMatch(skillElement, attacker.GongFaName, defender.GongFaName);
            float critMultiplier = RollCrit(attacker, elementMatch, ref result);
            float damage = CalculateLineDamage(
                rawAtk,
                defender.PhysDef,
                0f,
                skillMultiplier * critMultiplier,
                attacker.RealmMultiplier,
                defender.RealmMultiplier,
                elementMatch.DamageMultiplier);

            damage *= defender.GetFacingDamageModifier(attacker);

            bool backAttack = IsBackAttack(attacker, defender);
            if (!cannotBlock && !backAttack && Random.value * 100f < defender.BlockRate)
            {
                result.IsBlocked = true;
                damage *= 1f - defender.BlockReduction / 100f;
            }

            damage = ApplyCommonDamageReductions(damage, defender);

            result.FinalDamage = Mathf.Max(1, Mathf.RoundToInt(damage));
            result.Log = $"物理伤害 {result.FinalDamage}" +
                (result.IsBlocked ? " [格挡]" : "") +
                (result.IsCrit ? " [暴击]" : "");
            return result;
        }

        public static DamageResult CalcMagic(int rawAtk, float skillMultiplier,
            Character attacker, Character defender, string skillElement = "",
            bool cannotDodge = false, bool penetratingShield = false)
        {
            var result = new DamageResult();

            if (!RollHit(attacker, defender, cannotDodge, ref result))
                return result;

            ElementMatch elementMatch = GetElementMatch(skillElement, attacker.GongFaName, defender.GongFaName);
            float soulResist = 0f;
            if (defender.GongFaName == "抱元守一经" && defender.ShouyiStacks == defender.MaxShouyi())
                soulResist += 15f;

            float critMultiplier = RollCrit(attacker, elementMatch, ref result);
            float damage = CalculateLineDamage(
                rawAtk,
                defender.MagDef,
                soulResist,
                skillMultiplier * critMultiplier,
                attacker.RealmMultiplier,
                defender.RealmMultiplier,
                elementMatch.DamageMultiplier);

            bool backAttack = IsBackAttack(attacker, defender);
            if (!penetratingShield && !backAttack && Random.value * 100f < defender.SoulShieldRate)
            {
                result.IsSoulShielded = true;
                damage *= 1f - defender.SoulShieldReduction / 100f;
            }

            damage = ApplyCommonDamageReductions(damage, defender);

            result.FinalDamage = Mathf.Max(1, Mathf.RoundToInt(damage));
            result.Log = $"神魂伤害 {result.FinalDamage}" +
                (result.IsSoulShielded ? " [魂盾]" : "") +
                (result.IsCrit ? " [暴击]" : "");
            return result;
        }

        private static bool RollHit(Character attacker, Character defender, bool cannotDodge, ref DamageResult result)
        {
            float dodge = cannotDodge ? 0f : defender.DodgeRate;
            float hitRate = BaseHitRate + attacker.HitRateBonus - dodge;
            hitRate *= defender.GetFacingHitModifier(attacker);
            hitRate = Mathf.Clamp(hitRate, MinHitRate, MaxHitRate);

            if (Random.value * 100f <= hitRate)
            {
                result.IsHit = true;
                return true;
            }

            result.IsHit = false;
            result.IsDodged = !cannotDodge && defender.DodgeRate > 0f;
            result.Log = result.IsDodged ? "闪避" : "未命中";
            return false;
        }

        private static float RollCrit(Character attacker, ElementMatch elementMatch, ref DamageResult result)
        {
            float critRate = attacker.CritRate + elementMatch.CritRateBonus;
            if (Random.value * 100f >= critRate) return 1f;

            result.IsCrit = true;
            return 1f + (attacker.CritDamage + elementMatch.CritDamageBonus) / 100f;
        }

        private static float CalculateLineDamage(int atk, int def, float resistPercent,
            float skillMultiplier, float attackerRealm, float defenderRealm, float elementMultiplier)
        {
            float safeAtk = Mathf.Max(1f, atk);
            float safeDef = Mathf.Max(0f, def);
            float atkRealm = Mathf.Max(1f, attackerRealm);
            float defRealm = Mathf.Max(1f, defenderRealm);
            float realmRatio = atkRealm / defRealm;
            float defenseRatio = safeAtk / (safeAtk + safeDef);
            float resistTerm = 1f - resistPercent / 100f * Mathf.Sqrt(defRealm / atkRealm);
            resistTerm = Mathf.Clamp01(resistTerm);

            return safeAtk * skillMultiplier * realmRatio * defenseRatio * resistTerm * elementMultiplier;
        }

        private static float ApplyCommonDamageReductions(float damage, Character defender)
        {
            if (defender.IsGuarding)
                damage *= 0.5f;

            if (defender.GongFaName == "抱元守一经" && defender.ShouyiStacks > 0 && damage > 0f)
            {
                damage *= 0.80f;
                defender.ShouyiStacks--;
            }

            return damage;
        }

        private static ElementMatch GetElementMatch(string skillElement, string attackerGongFa, string defenderGongFa)
        {
            string actionElement = NormalizeElement(skillElement);
            if (string.IsNullOrEmpty(actionElement) || actionElement == "混沌")
                return new ElementMatch(1f, 0f, 0f);

            float damageMultiplier = 1f;
            float critRateBonus = 0f;
            float critDamageBonus = 0f;

            string attackerElement = NormalizeElement(GetGongFaElement(attackerGongFa));
            if (!string.IsNullOrEmpty(attackerElement) && attackerElement != "混沌")
            {
                string attackerBase = ToBaseElement(attackerElement);
                string actionBase = ToBaseElement(actionElement);
                if (attackerBase != actionBase)
                {
                    if (Generates(attackerBase, actionBase))
                    {
                        damageMultiplier *= 1.10f;
                    }
                    else if (Overcomes(attackerBase, actionBase))
                    {
                        damageMultiplier *= 0.90f;
                        critRateBonus += 5f;
                        critDamageBonus += 10f;
                    }
                }
            }

            string defenderElement = NormalizeElement(GetGongFaElement(defenderGongFa));
            if (!string.IsNullOrEmpty(defenderElement) && defenderElement != "混沌")
            {
                string actionBase = ToBaseElement(actionElement);
                string defenderBase = ToBaseElement(defenderElement);
                bool variant = IsVariantElement(actionElement);

                if (actionBase != defenderBase)
                {
                    if (Overcomes(actionBase, defenderBase))
                    {
                        damageMultiplier *= variant ? 1.15f : 1.10f;
                    }
                    else if (Overcomes(defenderBase, actionBase))
                    {
                        damageMultiplier *= variant ? 0.85f : 0.90f;
                    }
                    else if (Generates(actionBase, defenderBase))
                    {
                        damageMultiplier *= 0.95f;
                    }
                    else if (Generates(defenderBase, actionBase))
                    {
                        damageMultiplier *= 1.05f;
                    }
                }
            }

            return new ElementMatch(damageMultiplier, critRateBonus, critDamageBonus);
        }

        private static string NormalizeElement(string element)
        {
            if (string.IsNullOrWhiteSpace(element)) return "";
            return element.Trim() switch
            {
                "金" or "木" or "水" or "火" or "土" or "风" or "雷" or "冰" or "暗" or "星" or "毒" or "混沌" => element.Trim(),
                "element_metal" or "element_metal_root" => "金",
                "element_wood" or "element_wood_root" => "木",
                "element_water" or "element_water_root" => "水",
                "element_fire" or "element_fire_root" => "火",
                "element_earth" or "element_earth_root" => "土",
                "element_wind" or "element_wind_root" => "风",
                "element_thunder" or "element_thunder_root" => "雷",
                "element_ice" or "element_ice_root" => "冰",
                "element_dark" or "element_dark_root" => "暗",
                "element_star" or "element_star_root" => "星",
                "element_poison" or "element_poison_root" => "毒",
                "element_chaos" or "element_chaos_root" => "混沌",
                _ => ""
            };
        }

        private static string ToBaseElement(string element) => element switch
        {
            "风" or "毒" => "木",
            "雷" => "金",
            "冰" => "水",
            "暗" => "土",
            "星" => "火",
            _ => element,
        };

        private static bool IsVariantElement(string element) =>
            element is "风" or "雷" or "冰" or "暗" or "星";

        private static bool Generates(string source, string target) => (source, target) switch
        {
            ("木", "火") or ("火", "土") or ("土", "金") or ("金", "水") or ("水", "木") => true,
            _ => false
        };

        private static bool Overcomes(string source, string target) => (source, target) switch
        {
            ("木", "土") or ("土", "水") or ("水", "火") or ("火", "金") or ("金", "木") => true,
            _ => false
        };

        private static bool IsBackAttack(Character attacker, Character defender)
        {
            return defender.GetFacingDamageModifier(attacker) >= 1.25f;
        }
    }
}
