using UnityEngine;
using TianZhang.Game;

namespace TianZhang.Settlement
{
    public class SettlementSceneController : MonoBehaviour
    {
        private readonly SettlementDefinition[] definitions =
        {
            new SettlementDefinition { id = "taiyi_sect", displayName = "太一道庭", settlementType = SettlementType.Sect, regionId = "jiangzuo", ownerFactionId = "taiyi", availableServices = new[] { "修炼", "功法", "任务", "法坛" }, adventureEntrances = new[] { "taiyi_trial" }, visualTheme = "water_talisman" },
            new SettlementDefinition { id = "guanzhong_city", displayName = "关中城", settlementType = SettlementType.City, regionId = "guanzhong", ownerFactionId = "neutral", availableServices = new[] { "坊市", "悬赏", "客栈", "情报" }, adventureEntrances = new[] { "guanzhong_wild" }, visualTheme = "city_earth" },
            new SettlementDefinition { id = "zhongzhou_city", displayName = "中州城", settlementType = SettlementType.City, regionId = "zhongzhou", ownerFactionId = "neutral", availableServices = new[] { "坊市", "传送", "悬赏", "情报" }, adventureEntrances = new[] { "zhongzhou_wild" }, visualTheme = "capital" }
        };

        private void Start()
        {
            Debug.Log("[SettlementScene] definitions=" + definitions.Length);
        }

        public void ReturnToWorld()
        {
            var nodeId = GameSession.Instance != null ? GameSession.Instance.CurrentWorldNodeId : "jiangzuo_hub";
            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.EnterWorld(nodeId);
        }
    }
}