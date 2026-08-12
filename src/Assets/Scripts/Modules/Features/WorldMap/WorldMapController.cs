using System;
using System.Collections.Generic;
using TianZhang.Gameplay.Contracts;
using UnityEngine;

namespace TianZhang.Features.WorldMap
{
    public sealed class WorldMapController : MonoBehaviour
    {
        private static readonly WorldNodeDefinition[] PrototypeNodes =
        {
            new WorldNodeDefinition { id = "jiangzuo_hub", regionId = "jiangzuo", displayName = "江左天域", nodeType = WorldNodeType.RegionHub, connectedNodeIds = new[] { "guanzhong_hub" }, settlementId = "taiyi_sect" },
            new WorldNodeDefinition { id = "guanzhong_hub", regionId = "guanzhong", displayName = "关陇玄域", nodeType = WorldNodeType.RegionHub, connectedNodeIds = new[] { "jiangzuo_hub", "longxi_hub" }, settlementId = "guanzhong_city" },
            new WorldNodeDefinition { id = "longxi_hub", regionId = "longxi", displayName = "陇西雷域", nodeType = WorldNodeType.RegionHub, connectedNodeIds = new[] { "guanzhong_hub", "zhongzhou_hub" } },
            new WorldNodeDefinition { id = "zhongzhou_hub", regionId = "zhongzhou", displayName = "中州天域", nodeType = WorldNodeType.RegionHub, connectedNodeIds = new[] { "longxi_hub" }, settlementId = "zhongzhou_city" },
        };

        private INavigationUseCase navigation;
        private WorldMapView view;
        private Action<string> loadScene;
        private string initialNodeId;

        public IReadOnlyList<WorldNodeDefinition> Nodes => PrototypeNodes;
        public string SelectedNodeId { get; private set; } = "jiangzuo_hub";
        public WorldNodeDefinition SelectedNode { get; private set; }

        public void Configure(
            INavigationUseCase navigationUseCase,
            WorldMapView worldMapView,
            string currentNodeId,
            Action<string> sceneLoader)
        {
            navigation = navigationUseCase ?? throw new ArgumentNullException(nameof(navigationUseCase));
            view = worldMapView;
            initialNodeId = currentNodeId;
            loadScene = sceneLoader ?? throw new ArgumentNullException(nameof(sceneLoader));
        }

        private void Start()
        {
            view?.Configure(PrototypeNodes, SelectNode, EnterSelectedLocation);
            if (!SelectNode(string.IsNullOrWhiteSpace(initialNodeId) ? SelectedNodeId : initialNodeId))
                SelectNode("jiangzuo_hub");
        }

        public static string NodeDisplayName(string nodeId)
        {
            foreach (WorldNodeDefinition node in PrototypeNodes)
                if (node.id == nodeId) return node.displayName;
            return nodeId;
        }

        public bool TryGetNode(string nodeId, out WorldNodeDefinition node)
        {
            node = null;
            if (string.IsNullOrWhiteSpace(nodeId)) return false;
            foreach (WorldNodeDefinition candidate in PrototypeNodes)
            {
                if (!string.Equals(candidate.id, nodeId, StringComparison.Ordinal)) continue;
                node = candidate;
                return true;
            }
            return false;
        }

        public bool SelectNode(string nodeId)
        {
            if (!TryGetNode(nodeId, out WorldNodeDefinition node)) return false;
            SelectedNode = node;
            SelectedNodeId = node.id;
            navigation?.EnterWorld(node.id);
            view?.ShowSelectedNode(node);
            return true;
        }

        public void EnterSelectedLocation()
        {
            if (SelectedNode == null && !SelectNode(SelectedNodeId)) return;
            if (!string.IsNullOrWhiteSpace(SelectedNode.settlementId))
            {
                loadScene(navigation.EnterSettlement(SelectedNode.settlementId));
                return;
            }
            if (SelectedNode.adventureIds != null && SelectedNode.adventureIds.Length > 0)
                loadScene(navigation.EnterAdventure(SelectedNode.adventureIds[0], BuildAdventureReturnTarget()));
        }

        public void EnterSettlement(string settlementId)
        {
            loadScene(navigation.EnterSettlement(settlementId));
        }

        public void EnterAdventure(string adventureId)
        {
            loadScene(navigation.EnterAdventure(adventureId, BuildAdventureReturnTarget()));
        }

        public SceneReturnTarget BuildAdventureReturnTarget()
        {
            return SceneReturnTarget.World(SelectedNodeId);
        }
    }
}
