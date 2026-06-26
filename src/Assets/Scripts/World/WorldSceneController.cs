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

        public void EnterSettlement(string settlementId)
        {
            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.EnterSettlement(settlementId);
        }

        public void EnterAdventure(string adventureId)
        {
            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.EnterAdventure(adventureId, SceneReturnTarget.World(GameSession.Instance?.CurrentWorldNodeId));
        }
    }
}