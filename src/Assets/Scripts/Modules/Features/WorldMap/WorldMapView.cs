using System;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.WorldMap
{
    public sealed class WorldMapView : MonoBehaviour
    {
        [SerializeField] private Button[] nodeButtons = Array.Empty<Button>();
        [SerializeField] private Text[] nodeButtonLabels = Array.Empty<Text>();
        [SerializeField] private Text selectedNodeText;
        [SerializeField] private Text selectedNodeDescription;
        [SerializeField] private Button enterLocationButton;

        public void Configure(
            WorldNodeDefinition[] nodes,
            Func<string, bool> selectNode,
            Action enterLocation)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            if (selectNode == null) throw new ArgumentNullException(nameof(selectNode));
            for (int i = 0; i < nodeButtons.Length; i++)
            {
                Button button = nodeButtons[i];
                button.onClick.RemoveAllListeners();
                bool hasNode = i < nodes.Length;
                button.gameObject.SetActive(hasNode);
                if (!hasNode) continue;
                WorldNodeDefinition node = nodes[i];
                if (i < nodeButtonLabels.Length && nodeButtonLabels[i] != null)
                    nodeButtonLabels[i].text = node.displayName;
                string nodeId = node.id;
                button.onClick.AddListener(() => selectNode(nodeId));
            }
            if (enterLocationButton != null)
            {
                enterLocationButton.onClick.RemoveAllListeners();
                enterLocationButton.onClick.AddListener(() => enterLocation());
            }
        }

        public void ShowSelectedNode(WorldNodeDefinition node)
        {
            if (node == null) return;
            if (selectedNodeText != null) selectedNodeText.text = node.displayName;
            bool hasSettlement = !string.IsNullOrWhiteSpace(node.settlementId);
            bool hasAdventure = node.adventureIds != null && node.adventureIds.Length > 0;
            if (selectedNodeDescription != null)
            {
                string entry = hasSettlement
                    ? "据点: " + node.settlementId
                    : hasAdventure ? "副本: " + node.adventureIds[0] : "暂无可进入地点";
                selectedNodeDescription.text = node.displayName + "（区域枢纽）\n" + entry;
            }
            if (enterLocationButton != null) enterLocationButton.interactable = hasSettlement || hasAdventure;
        }
    }
}
