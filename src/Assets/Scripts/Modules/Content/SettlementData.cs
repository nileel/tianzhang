using System;
using UnityEngine;

namespace TianZhang.Content
{
    [Serializable]
    public sealed class SettlementFeatureData
    {
        public string featureId;
        public string displayNameKey;
        public string availability;
        public string disabledReasonKey;
    }

    [CreateAssetMenu(fileName = "Settlement_", menuName = "天章/内容/据点数据")]
    public sealed class SettlementData : ScriptableObject
    {
        public string settlementId;
        public string displayNameKey;
        public string contentScope;
        public string settlementType;
        public string regionId;
        public string ownerFactionId;
        public string visualThemeId;
        public SettlementFeatureData[] features = Array.Empty<SettlementFeatureData>();
        public string[] adventureEntranceIds = Array.Empty<string>();
    }
}
