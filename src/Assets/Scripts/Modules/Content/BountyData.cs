using System;
using UnityEngine;

namespace TianZhang.Content
{
    [Serializable]
    public sealed class BountyRewardEntry
    {
        public string itemId;
        public int quantity;
    }

    [CreateAssetMenu(fileName = "Bounty_", menuName = "天章/内容/悬赏数据")]
    public sealed class BountyData : ScriptableObject
    {
        public string bountyId;
        public string titleKey;
        public string descriptionKey;
        public string contentScope;
        public string issuerSettlementId;
        public string objectiveType;
        public string targetEnemyId;
        public int requiredCount;
        public string allowedAdventureId;
        public BountyRewardEntry[] rewardEntries = Array.Empty<BountyRewardEntry>();
        public string repeatPolicy;
    }
}
