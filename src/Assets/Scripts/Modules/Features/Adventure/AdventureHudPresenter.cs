using System;
using TianZhang.Content;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.Adventure
{
    public sealed class AdventureHudPresenter : MonoBehaviour
    {
        [SerializeField] private Transform nodeContainer;
        [SerializeField] private Text adventureText;
        [SerializeField] private Text statusText;
        [SerializeField] private Font nodeFont;
        [SerializeField] private Sprite nodeNormal;
        [SerializeField] private Sprite nodeHover;
        [SerializeField] private Sprite nodeSelected;

        public void Present(AdventureSession session, Func<string, bool> selectNode, string failureReason)
        {
            if (session == null) return;
            if (adventureText != null) adventureText.text = session.Map.displayNameKey;
            if (statusText != null) statusText.text = string.IsNullOrWhiteSpace(failureReason) ? session.Status : failureReason;
            if (nodeContainer == null || nodeContainer.childCount > 0) return;
            foreach (AdventureNodeData node in session.Map.nodes)
            {
                var go = new GameObject(
                    "AdventureNode_" + node.nodeId,
                    typeof(RectTransform),
                    typeof(Image),
                    typeof(Button),
                    typeof(LayoutElement));
                go.transform.SetParent(nodeContainer, false);
                go.GetComponent<LayoutElement>().minHeight = 56f;
                go.GetComponent<LayoutElement>().preferredHeight = 56f;
                Image image = go.GetComponent<Image>();
                image.sprite = nodeNormal;
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 6.4f;
                Button button = go.GetComponent<Button>();
                button.transition = Selectable.Transition.SpriteSwap;
                button.spriteState = new SpriteState
                {
                    highlightedSprite = nodeHover,
                    pressedSprite = nodeSelected,
                    selectedSprite = nodeSelected,
                    disabledSprite = nodeNormal,
                };
                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
                labelGo.transform.SetParent(go.transform, false);
                Text label = labelGo.GetComponent<Text>();
                label.font = nodeFont;
                label.fontSize = 18;
                label.color = new Color32(244, 234, 212, 255);
                label.raycastTarget = false;
                label.alignment = TextAnchor.MiddleCenter;
                label.text = node.nodeId + " (" + node.q + "," + node.r + ")";
                RectTransform rect = labelGo.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(12, 4);
                rect.offsetMax = new Vector2(-12, -4);
                string nodeId = node.nodeId;
                button.onClick.AddListener(() => selectNode(nodeId));
            }
        }
    }
}
