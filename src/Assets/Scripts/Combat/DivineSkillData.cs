using UnityEngine;

namespace TianZhang.Combat
{
    /// <summary>
    /// 神通模板数据（ScriptableObject）
    /// 神通：冷却较长（CD5=50刻），威力更大，有领域/血脉等特殊效果
    /// </summary>
    [CreateAssetMenu(fileName = "Skill_", menuName = "天章/神通数据")]
    public class DivineSkillData : ScriptableObject
    {
        [Header("基础信息")]
        public string skillName = "无名神通";
        public SpellType type = SpellType.Magic;
        public SpellRange range = SpellRange.Ranged;
        public int minRange = 1;
        public int maxRange = 3;

        [Header("五行属性")]
        public string element = ""; // 金/木/水/火/土，空字符串=继承功法属性

        [Header("消耗")]
        public int mpCost = 40;
        public int cooldownTicks = 50; // 神通通常CD5=50刻

        [Header("效能")]
        public float damageMultiplier = 1.8f;
        public int healAmount;
        public float buffMultiplier = 1f;

        [Header("特殊效果")]
        public bool cannotBlock;
        public bool cannotDodge;
        public bool penetratingShield;
        public float stunChance;
        public int stunDuration = 1;

        [Header("领域/血脉效果（预留）")]
        public bool isDomain;     // 是否领域神通
        public bool isBloodline;  // 是否血脉神通
        public string specialEffectDesc; // 特殊效果描述（供UI显示）

        [Header("境界适配")]
        public float realmScaleBase = 1f;
    }
}
