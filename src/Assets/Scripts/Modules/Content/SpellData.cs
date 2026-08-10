using UnityEngine;

namespace TianZhang.Combat
{
    public enum SpellType
    {
        Physical,
        Magic,
        Heal,
        Buff,
        Debuff,
        Movement,
        Hybrid,
    }

    public enum SpellRange
    {
        Melee,
        Ranged,
        Self,
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
        public string contentScope = "player";

        [Header("使用限制")]
        public string realmRequirement = "realm_fanren";
        public string elementRequirement = "element_none";

        [Header("来源元数据（非使用限制）")]
        public string sourceAffiliation = "";

        [Header("五行属性")]
        public string element = "";

        [Header("消耗")]
        public int mpCost = 20;
        public int cooldownTicks = 30;

        [Header("效能")]
        public float physicalDamageMultiplier;
        public float soulDamageMultiplier;
        public int healAmount;
        public float buffMultiplier = 1f;

        [System.Obsolete("Use physicalDamageMultiplier or soulDamageMultiplier.")]
        public float damageMultiplier
        {
            get => type == SpellType.Physical ? physicalDamageMultiplier : soulDamageMultiplier;
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
        public bool cannotBlock;
        public bool cannotDodge;
        public bool penetratingShield;
        public float stunChance;
        public int stunDuration = 1;

        [Header("境界适配（倍率因境界调整，在运行时由倍数表覆盖）")]
        public float realmScaleBase = 1f;
    }
}
