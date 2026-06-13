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

        [Header("消耗")]
        public int mpCost = 20;
        public int cooldownTicks = 30; // 冷却刻数（术法通常CD3=30刻）

        [Header("效能")]
        public float damageMultiplier = 1.2f; // 伤害倍率
        public int healAmount;                 // 治疗量（类型为Heal时）
        public float buffMultiplier = 1f;      // Buff倍率

        [Header("特殊效果")]
        public bool cannotBlock;        // 无法格挡
        public bool cannotDodge;        // 无法闪避
        public bool penetratingShield;  // 穿透魂盾
        public float stunChance;        // 眩晕概率（%）
        public int stunDuration = 1;    // 眩晕回合数

        [Header("境界适配（倍率因境界调整，在运行时由倍数表覆盖）")]
        public float realmScaleBase = 1f;
    }
}
