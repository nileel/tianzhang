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
        /// settlementId 传入 SceneFlowManager 但不持久化；SettlementSceneController 启动时查自身定义列表。
        /// ID 持久化方案延后至 TQ-014 Task 7（开始菜单迁移）统一处理。
        /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro；变更范围：补 ID 传递边界注释
        /// </summary>
        public void EnterSettlement(string settlementId)
        {
            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.EnterSettlement(settlementId);
        }

        /// <summary>
        /// adventureId 传入 SceneFlowManager 但不持久化；AdventureSceneController 通过 returnTarget 恢复上下文。
        /// ID 持久化方案延后至 TQ-014 Task 7（开始菜单迁移）统一处理。
        /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro；变更范围：补 ID 传递边界注释
        /// </summary>
        public void EnterAdventure(string adventureId)
        {
            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.EnterAdventure(adventureId, SceneReturnTarget.World(GameSession.Instance?.CurrentWorldNodeId));
        }
    }
}