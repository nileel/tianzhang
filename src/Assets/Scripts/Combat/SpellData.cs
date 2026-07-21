using UnityEngine;

namespace TianZhang.Combat
{
    public enum SpellType
    {
        Physical,   // 物理攻击
        Magic,      // 神魂攻击
        Heal,       // 治疗
        Buff,       // 增益
        Debuff,     // 减益
        Movement,   // 位移
        Hybrid,     // 物理与神魂独立结算
    }

    public enum SpellRange
    {
        Melee,      // 近战（邻格）
        Ranged,     // 远程（2-4格）
        Self,       // 自身
    }

    /// <summary>
    /// 术法模板数据（ScriptableObject）
    /// 字段与 docs/角色养成/术法/术法设计.txt 模版对齐
    /// </summary>
    [CreateAssetMenu(fileName = "Spell_", menuName = "天章/术法数据")]
    public class SpellData : ScriptableObject
    {
        [Header("基础信息")]
        public string spellName = "无名术法";
        public SpellType type = SpellType.Physical;
        public SpellRange range = SpellRange.Melee;
        public int minRange = 1;
        public int maxRange = 1;
        public string contentScope = "player"; // player/reserved

        [Header("使用限制")]
        public string realmRequirement = "realm_fanren";
        public string elementRequirement = "element_none";

        [Header("来源元数据（非使用限制）")]
        public string sourceAffiliation = "";

        [Header("五行属性")]
        public string element = ""; // 金/木/水/火/土/风/雷/冰/暗/星/毒/混沌，空字符串表示无属性或待数据补齐

        [Header("消耗")]
        public int mpCost = 20;
        public int cooldownTicks = 30; // 冷却刻数（术法通常CD3=30刻）

        [Header("效能")]
        public float physicalDamageMultiplier; // 物理伤害倍率
        public float soulDamageMultiplier;     // 神魂伤害倍率
        public int healAmount;                 // 治疗量（类型为Heal时）
        public float buffMultiplier = 1f;      // Buff倍率

        // 仅供尚未迁移的调用方在编译期兼容；CSV 与 asset 均使用双倍率字段。
        [System.Obsolete("Use physicalDamageMultiplier or soulDamageMultiplier.")]
        public float damageMultiplier
        {
            get
            {
                return type == SpellType.Physical ? physicalDamageMultiplier : soulDamageMultiplier;
            }
            set
            {
                if (type == SpellType.Physical)
                    physicalDamageMultiplier = value;
                else if (type == SpellType.Hybrid)
                {
                    physicalDamageMultiplier = value;
                    soulDamageMultiplier = value;
                }
                else
                    soulDamageMultiplier = value;
            }
        }

        [Header("特殊效果")]
        public bool cannotBlock;        // 无法格挡
        public bool cannotDodge;        // 无法闪避
        public bool penetratingShield;  // 穿透魂盾
        public float stunChance;        // 眩晕概率（%）
        public int stunDuration = 1;    // 眩晕回合数

        [Header("境界适配（倍率因境界调整，在运行时由倍数表覆盖）")]
        public float realmScaleBase = 1f;

        public bool IsAvailableTo(TianZhang.Entity.Character character) =>
            AbilityRequirementPolicy.IsSatisfied(
                character,
                realmRequirement,
                elementRequirement);
    }

    public static class AbilityRequirementPolicy
    {
        private static readonly System.Collections.Generic.Dictionary<string, float> RealmThresholds = new()
        {
            ["realm_fanren"] = 1f,
            ["realm_lianqi"] = 1.5f,
            ["realm_zhuji"] = 3f,
            ["realm_jindan"] = 6f,
            ["realm_yuanying"] = 12f,
            ["realm_huashen"] = 24f,
        };

        public static bool IsSatisfied(
            TianZhang.Entity.Character character,
            string realmRequirement,
            string elementRequirement)
        {
            if (character == null)
                return false;

            return MeetsRealmRequirement(character, realmRequirement)
                && MeetsElementRequirement(character, elementRequirement);
        }

        private static bool MeetsRealmRequirement(TianZhang.Entity.Character character, string requirement)
        {
            if (string.IsNullOrWhiteSpace(requirement)
                || !RealmThresholds.TryGetValue(requirement.Trim(), out float requiredRealm))
            {
                return false;
            }

            return character.RealmMultiplier >= requiredRealm;
        }

        private static bool MeetsElementRequirement(TianZhang.Entity.Character character, string requirement)
        {
            if (string.Equals(requirement?.Trim(), "element_none", System.StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.IsNullOrWhiteSpace(requirement)
                || string.IsNullOrWhiteSpace(character.VisibleRootElement))
            {
                return false;
            }

            const string prefix = "element_";
            string normalized = requirement.Trim().ToLowerInvariant();
            if (!normalized.StartsWith(prefix, System.StringComparison.Ordinal))
                return false;

            string[] alternatives = normalized.Substring(prefix.Length)
                .Replace("_root", "")
                .Split(new[] { "_or_" }, System.StringSplitOptions.RemoveEmptyEntries);
            string characterElement = DamageCalculator.ResolveElement(character.VisibleRootElement);
            foreach (string alternative in alternatives)
            {
                string requiredElement = DamageCalculator.ResolveElement(prefix + alternative);
                if (!string.IsNullOrEmpty(requiredElement) && requiredElement == characterElement)
                    return true;
            }

            return false;
        }
    }
}
