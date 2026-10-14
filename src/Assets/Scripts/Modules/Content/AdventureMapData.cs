using System;
using UnityEngine;

namespace TianZhang.Content
{
    [Serializable]
    public sealed class AdventureEnemySpawnData
    {
        public string instanceId;
        public int q;
        public int r;
    }

    [Serializable]
    public sealed class AdventureNodeData
    {
        public string nodeId;
        public string nodeTypeId;
        public int q;
        public int r;
        public string contentId;
        public AdventureEnemySpawnData[] additionalEnemies = Array.Empty<AdventureEnemySpawnData>();
    }

    [CreateAssetMenu(fileName = "AdventureMap_", menuName = "天章/内容/冒险地图")]
    public sealed class AdventureMapData : ScriptableObject
    {
        public string adventureId;
        public string displayNameKey;
        public string contentScope;
        public AdventureNodeData[] nodes = Array.Empty<AdventureNodeData>();
    }
}
