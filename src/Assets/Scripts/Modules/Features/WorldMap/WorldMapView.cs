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

        public void ShowSelectedNode(WorldNodeDefinition node, bool hasSettlement, bool hasAdventure, bool guanzhongOpen)
        {
            if (node == null) return;
            if (selectedNodeText != null) selectedNodeText.text = node.displayName;
            if (selectedNodeDescription != null)
            {
                string entry = hasSettlement
                    ? "可进入据点"
                    : hasAdventure ? "可进入副本"
                    : guanzhongOpen ? "本地尚未开放。选择「关陇玄域」进入关中城，可在城内接悬赏。"
                    : "暂无可进入地点";
                selectedNodeDescription.text = node.displayName + "（区域枢纽）\n" + entry;
            }
            if (enterLocationButton != null) enterLocationButton.interactable = hasSettlement || hasAdventure;
        }
    }
}
