using UnityEngine;

namespace TianZhang.Entity
{
    /// <summary>
    /// 角色模板数据（ScriptableObject）
    /// 字段与 BattleSim GameData 对齐
    /// </summary>
    [CreateAssetMenu(fileName = "Char_", menuName = "天章/角色数据")]
    public class CharacterData : ScriptableObject
    {
        [Header("基础信息")]
        public string charName = "无名修士";
        public int baseLevel = 1;       // 小境界等级

        [Header("一级属性")]
        public int rootBone = 10;       // 根骨 → HP/肉攻
        public int physique = 10;       // 体魄 → 肉防
        public int spirit = 10;         // 魂魄 → MP/神攻
        public int mind = 10;           // 神识 → 神防
        public int reaction = 10;       // 反应 → CT速度
        public int talent = 10;         // 资质 → 修炼速度

        [Header("二级属性（派生，可覆盖）")]
        public float hpBonus;
        public float mpBonus;
        public float physAtkBonus;
        public float magAtkBonus;
        public float physDefBonus;
        public float magDefBonus;

        [Header("二级概率（%）")]
        public float blockRate;         // 格挡率
        public float blockReduction;    // 格挡减伤率
        public float soulShieldRate;    // 魂盾率
        public float soulShieldReduction; // 魂盾减伤率
        public float dodgeRate;         // 闪避率
        public float critRate;          // 暴击率
        public float critDamage;        // 暴击伤害加成
        public float hitRateBonus;      // 命中率加成

        [Header("境界倍率")]
        public float realmMultiplier = 1f; // 境界属性倍率（凡人1.0→练气1.5→筑基3.0→...）

        [Header("术法槽位")]
        public string[] equippedSpells; // 装备的术法名称
        public string[] equippedSkills; // 装备的神通名称
    }
}
