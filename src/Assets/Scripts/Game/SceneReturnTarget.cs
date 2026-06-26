namespace TianZhang.Game
{
    /// <summary>
    /// 场景返回目标（会话流转用）
    /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro；变更范围：新增文件
    /// </summary>
    [System.Serializable]
    public struct SceneReturnTarget
    {
        public string SceneName;
        public string WorldNodeId;
        public string SettlementId;
        public string AdventureId;

        public static SceneReturnTarget World(string nodeId)
        {
            return new SceneReturnTarget { SceneName = "WorldScene", WorldNodeId = nodeId };
        }

        public static SceneReturnTarget Settlement(string settlementId)
        {
            return new SceneReturnTarget { SceneName = "SettlementScene", SettlementId = settlementId };
        }
    }
}
