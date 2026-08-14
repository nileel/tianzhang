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
                go.GetComponent<LayoutElement>().preferredHeight = 44f;
                go.GetComponent<Image>().color = new Color(0.2f, 0.34f, 0.3f, 1f);
                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
                labelGo.transform.SetParent(go.transform, false);
                Text label = labelGo.GetComponent<Text>();
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                label.fontSize = 18;
                label.color = new Color(0.91f, 0.88f, 0.77f, 1f);
                label.alignment = TextAnchor.MiddleCenter;
                label.text = node.nodeId + " (" + node.q + "," + node.r + ")";
                RectTransform rect = labelGo.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.sizeDelta = Vector2.zero;
                string nodeId = node.nodeId;
                go.GetComponent<Button>().onClick.AddListener(() => selectNode(nodeId));
            }
        }
    }
}
