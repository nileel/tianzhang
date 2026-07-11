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
        public int fortune = 10;        // 气运 → 非战斗创角/事件权重

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
        public float critDamage;        // 基础150%暴击倍率之上的附加百分比点
        public float hitRateBonus;      // 命中率加成

        [Header("境界倍率")]
        public float realmMultiplier = 1f; // 战斗强度兼容倍率，不作为境界事实源
        public string realmStage = "";     // 可选显示阶段：筑基初期/紫府初开/金丹圆满等
        public string gongFaName = "";         // 装备的功法名称

        [Header("术法槽位")]
        public string[] equippedSpells; // 装备的术法名称
        public string[] equippedSkills; // 装备的神通名称

        [Header("术法/神通库")]
        public string[] availableSpells;   // 已学会的全部术法
        public string[] availableSkills;   // 已学会的全部神通

        [Header("角色创建")]
        public int innatePurchasePointsLimit;
        public int innatePurchasePointsUsed;
        public int creationBudgetLimit;
        public int creationBudgetUsed;
        public int creationBudgetRefunded;
        public string originId = "";
        public string[] fateTagIds;
        public string[] craftSkillIds;
        public int[] craftSkillLevels;

        [Header("显性灵根")]
        public string visibleRootId = "";
        public string visibleRootKind = "";
        public string visibleRootGrade = "";
        public string visibleRootElement = "";
        public string visibleRootMotherElement = "";
        public float visibleRootCultivationMultiplier = 1f;
        public float visibleRootMpMultiplier = 1f;
        public string visibleRootRealmCap = "";
        public string visibleRootRegionAffinity = "";
        public string[] visibleRootLearnTags;

        [Header("隐藏灵根")]
        public string hiddenRootSeedId = "";
        public string hiddenRootId = "";
        public string hiddenRootKind = "";
        public string hiddenRootGrade = "";
        public string hiddenRootElement = "";
        public string hiddenRootMotherElement = "";
        public float hiddenRootCultivationMultiplier;
        public float hiddenRootMpMultiplier = 1f;
        public string hiddenRootRealmCap = "";
        public string hiddenRootRegionAffinity = "";
        public string[] hiddenRootLearnTags;
        public string hiddenRootState = "";
        public int hiddenRootRollSeed;

        [Header("槽位上限（0=按境界自动计算）")]
        public int maxSpellSlots;          // 术法槽位上限（0时从境界推算）
        public int maxSkillSlots;          // 神通槽位上限

        [Header("紫府/金丹兼容字段")]
        public string[] developedMansions;     // 已主修府位：命府/魂府/识府/气府/运府
        public string targetPosition = "";     // 目标源/化/界席位 ID
        public string positionOccupationState = ""; // 夺取/继承/敕封/暂寄/自辟
        public string danXiangId = "";         // 丹相 ID
        public string danPivotRole = "";       // none/main/auxiliary
        public string[] mansionBindings;       // 紫府神通绑定府位
        public string danArtifactForm = "";    // 丹器显化形态
        public string legacyDanJiType = "";    // 旧 danJiType 兼容读取字段
    }
}
