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
        public CharacterData combatTemplate;
        public EnemyDropEntry[] dropEntries = Array.Empty<EnemyDropEntry>();
    }
}
