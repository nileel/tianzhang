using UnityEngine;
using TianZhang.Game;

namespace TianZhang.World
{
    public class WorldSceneController : MonoBehaviour
    {
        private readonly WorldNodeDefinition[] nodes =
        {
            new WorldNodeDefinition { id = "jiangzuo_hub", regionId = "jiangzuo", displayName = "江左天域", nodeType = WorldNodeType.RegionHub, connectedNodeIds = new[] { "guanzhong_hub" }, settlementId = "taiyi_sect" },
            new WorldNodeDefinition { id = "guanzhong_hub", regionId = "guanzhong", displayName = "关陇玄域", nodeType = WorldNodeType.RegionHub, connectedNodeIds = new[] { "jiangzuo_hub", "longxi_hub" }, settlementId = "guanzhong_city" },
            new WorldNodeDefinition { id = "longxi_hub", regionId = "longxi", displayName = "陇西雷域", nodeType = WorldNodeType.RegionHub, connectedNodeIds = new[] { "guanzhong_hub", "zhongzhou_hub" }, adventureIds = new[] { "longxi_trial" } },
            new WorldNodeDefinition { id = "zhongzhou_hub", regionId = "zhongzhou", displayName = "中州天域", nodeType = WorldNodeType.RegionHub, connectedNodeIds = new[] { "longxi_hub" }, settlementId = "zhongzhou_city" }
        };

        private void Start()
        {
            Debug.Log("[WorldScene] nodes=" + nodes.Length);
        }

        /// <summary>
        /// settlementId 经 SceneFlowManager 持久化到 GameSession.CurrentSettlementId。
        /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro；变更范围：TQ-014-DS-05 返工 — 更新注释反映 fb7f7ed 已持久化
        /// </summary>
        public void EnterSettlement(string settlementId)
        {
            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.EnterSettlement(settlementId);
        }

        /// <summary>
        /// adventureId 经 SceneFlowManager 持久化到 GameSession.CurrentAdventureId。
        /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro；变更范围：TQ-014-DS-05 返工 — 更新注释反映 fb7f7ed 已持久化
        /// </summary>
        public void EnterAdventure(string adventureId)
        {
            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.EnterAdventure(adventureId, SceneReturnTarget.World(GameSession.Instance?.CurrentWorldNodeId));
        }
    }
}