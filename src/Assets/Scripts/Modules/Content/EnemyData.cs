using System;
using TianZhang.Entity;
using UnityEngine;

namespace TianZhang.Content
{
    [Serializable]
    public sealed class EnemyDropEntry
    {
        public string itemId;
        public int dropChancePercent;
        public int quantity;
    }

    [CreateAssetMenu(fileName = "Enemy_", menuName = "天章/内容/敌人数据")]
    public sealed class EnemyData : ScriptableObject
    {
        public string enemyId;
        public string displayNameKey;
        public string descriptionKey;
        public string contentScope;
        public string enemyTypeId;
        public string aiProfileId;
        public string realmId;
        public string footprintKind;
        public int bodyScaleRank;
        public float combatStatScale = 1f;
        /// <summary>移动效率独立倍率，默认 1；最终移动效率 = 境界镜像 × combatStatScale × 本值。</summary>
        public float movementEfficiencyScale = 1f;
        public CharacterData combatTemplate;
        public EnemyDropEntry[] dropEntries = Array.Empty<EnemyDropEntry>();
    }
}
