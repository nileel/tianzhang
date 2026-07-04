using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TianZhang.Game;

namespace TianZhang.World
{
    public class WorldSceneController : MonoBehaviour
    {
        private static readonly WorldNodeDefinition[] PrototypeNodes =
        {
            new WorldNodeDefinition { id = "jiangzuo_hub", regionId = "jiangzuo", displayName = "江左天域", nodeType = WorldNodeType.RegionHub, connectedNodeIds = new[] { "guanzhong_hub" }, settlementId = "taiyi_sect" },
            new WorldNodeDefinition { id = "guanzhong_hub", regionId = "guanzhong", displayName = "关陇玄域", nodeType = WorldNodeType.RegionHub, connectedNodeIds = new[] { "jiangzuo_hub", "longxi_hub" }, settlementId = "guanzhong_city" },
            new WorldNodeDefinition { id = "longxi_hub", regionId = "longxi", displayName = "陇西雷域", nodeType = WorldNodeType.RegionHub, connectedNodeIds = new[] { "guanzhong_hub", "zhongzhou_hub" }, adventureIds = new[] { "longxi_trial" } },
            new WorldNodeDefinition { id = "zhongzhou_hub", regionId = "zhongzhou", displayName = "中州天域", nodeType = WorldNodeType.RegionHub, connectedNodeIds = new[] { "longxi_hub" }, settlementId = "zhongzhou_city" }
        };

        private Text selectedNodeText;
        private Text selectedNodeDescription;
        private Button enterLocationButton;

        public IReadOnlyList<WorldNodeDefinition> Nodes => PrototypeNodes;
        public string SelectedNodeId { get; private set; } = "jiangzuo_hub";
        public WorldNodeDefinition SelectedNode { get; private set; }

        private void Start()
        {
            BuildWorldNodeUi();
            if (!SelectNode(GameSession.Instance?.CurrentWorldNodeId ?? SelectedNodeId))
                SelectNode("jiangzuo_hub");

            Debug.Log("[WorldScene] nodes=" + PrototypeNodes.Length);
        }

        public bool TryGetNode(string nodeId, out WorldNodeDefinition node)
        {
            node = null;
            if (string.IsNullOrEmpty(nodeId))
                return false;

            foreach (var candidate in PrototypeNodes)
            {
                if (candidate.id == nodeId)
                {
                    node = candidate;
                    return true;
                }
            }

            return false;
        }

        public bool SelectNode(string nodeId)
        {
            if (!TryGetNode(nodeId, out var node))
                return false;

            SelectedNode = node;
            SelectedNodeId = node.id;
            if (GameSession.Instance != null)
                GameSession.Instance.SetWorldNode(node.id);

            RefreshSelectedNodeUi();
            return true;
        }

        public void EnterSelectedLocation()
        {
            if (SelectedNode == null && !SelectNode(GameSession.Instance?.CurrentWorldNodeId ?? "jiangzuo_hub"))
                return;

            if (!string.IsNullOrEmpty(SelectedNode.settlementId))
            {
                EnterSettlement(SelectedNode.settlementId);
                return;
            }

            if (SelectedNode.adventureIds != null && SelectedNode.adventureIds.Length > 0)
                EnterAdventure(SelectedNode.adventureIds[0]);
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
                SceneFlowManager.Instance.EnterAdventure(
                    adventureId,
                    SceneReturnTarget.World(GameSession.Instance?.CurrentWorldNodeId ?? SelectedNodeId));
        }

        private void BuildWorldNodeUi()
        {
            if (GameObject.Find("WorldNodePanel") != null)
            {
                selectedNodeText = GameObject.Find("SelectedWorldNodeText")?.GetComponent<Text>();
                selectedNodeDescription = GameObject.Find("SelectedWorldNodeDescription")?.GetComponent<Text>();
                enterLocationButton = GameObject.Find("EnterLocationButton")?.GetComponent<Button>();
                return;
            }

            var canvas = EnsureUICanvas();

            var panelGo = new GameObject("WorldNodePanel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(canvas.transform, false);
            var panelRt = panelGo.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0f, 0f);
            panelRt.anchorMax = new Vector2(0f, 1f);
            panelRt.pivot = new Vector2(0f, 0.5f);
            panelRt.anchoredPosition = new Vector2(24f, 0f);
            panelRt.sizeDelta = new Vector2(360f, -48f);
            panelGo.GetComponent<Image>().color = new Color(0.02f, 0.04f, 0.05f, 0.88f);

            var title = CreateText("WorldTitle", panelGo.transform, "主世界", 30, Color.white, TextAnchor.MiddleCenter);
            var titleRt = title.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -42f);
            titleRt.sizeDelta = new Vector2(-40f, 44f);

            var buttonContainerGo = new GameObject("WorldNodeButtonContainer", typeof(RectTransform), typeof(VerticalLayoutGroup));
            buttonContainerGo.transform.SetParent(panelGo.transform, false);
            var buttonRt = buttonContainerGo.GetComponent<RectTransform>();
            buttonRt.anchorMin = new Vector2(0f, 1f);
            buttonRt.anchorMax = new Vector2(1f, 1f);
            buttonRt.anchoredPosition = new Vector2(0f, -110f);
            buttonRt.sizeDelta = new Vector2(-48f, 210f);
            var layout = buttonContainerGo.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            foreach (var node in PrototypeNodes)
            {
                var button = CreateButton("WorldNode_" + node.id, buttonContainerGo.transform, node.displayName, new Color(0.16f, 0.28f, 0.24f, 1f));
                var capturedNodeId = node.id;
                button.GetComponent<Button>().onClick.AddListener(() => SelectNode(capturedNodeId));
            }

            selectedNodeText = CreateText("SelectedWorldNodeText", panelGo.transform, "", 22, Color.yellow, TextAnchor.MiddleCenter).GetComponent<Text>();
            var selectedRt = selectedNodeText.GetComponent<RectTransform>();
            selectedRt.anchorMin = new Vector2(0f, 0f);
            selectedRt.anchorMax = new Vector2(1f, 0f);
            selectedRt.anchoredPosition = new Vector2(0f, 180f);
            selectedRt.sizeDelta = new Vector2(-40f, 36f);

            selectedNodeDescription = CreateText("SelectedWorldNodeDescription", panelGo.transform, "", 15, Color.white, TextAnchor.MiddleCenter).GetComponent<Text>();
            var descRt = selectedNodeDescription.GetComponent<RectTransform>();
            descRt.anchorMin = new Vector2(0f, 0f);
            descRt.anchorMax = new Vector2(1f, 0f);
            descRt.anchoredPosition = new Vector2(0f, 128f);
            descRt.sizeDelta = new Vector2(-40f, 72f);

            enterLocationButton = CreateButton("EnterLocationButton", panelGo.transform, "进入地点", new Color(0.32f, 0.45f, 0.28f, 1f)).GetComponent<Button>();
            var enterRt = enterLocationButton.GetComponent<RectTransform>();
            enterRt.anchorMin = new Vector2(0.5f, 0f);
            enterRt.anchorMax = new Vector2(0.5f, 0f);
            enterRt.anchoredPosition = new Vector2(0f, 54f);
            enterRt.sizeDelta = new Vector2(220f, 48f);
            enterLocationButton.onClick.AddListener(EnterSelectedLocation);
        }

        private static GameObject EnsureUICanvas()
        {
            var canvasGo = GameObject.Find("UICanvas");
            if (canvasGo != null)
                return canvasGo;

            canvasGo = new GameObject("UICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 80;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            return canvasGo;
        }

        private static GameObject CreateText(string name, Transform parent, string text, int fontSize, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<Text>();
            label.text = text;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = anchor;
            return go;
        }

        private static GameObject CreateButton(string name, Transform parent, string labelText, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            go.GetComponent<LayoutElement>().preferredHeight = 42f;

            var label = CreateText("Label", go.transform, labelText, 18, Color.white, TextAnchor.MiddleCenter);
            var labelRt = label.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.sizeDelta = Vector2.zero;
            return go;
        }

        private void RefreshSelectedNodeUi()
        {
            if (SelectedNode == null)
                return;

            if (selectedNodeText != null)
                selectedNodeText.text = SelectedNode.displayName;

            if (selectedNodeDescription != null)
                selectedNodeDescription.text = BuildSelectedNodeDescription(SelectedNode);

            if (enterLocationButton != null)
                enterLocationButton.interactable = HasLocationEntry(SelectedNode);
        }

        private static string BuildSelectedNodeDescription(WorldNodeDefinition node)
        {
            var entry = "暂无可进入地点";
            if (!string.IsNullOrEmpty(node.settlementId))
                entry = "据点: " + node.settlementId;
            else if (node.adventureIds != null && node.adventureIds.Length > 0)
                entry = "副本: " + node.adventureIds[0];

            return node.regionId + " / " + node.nodeType + "\n" + entry;
        }

        private static bool HasLocationEntry(WorldNodeDefinition node)
        {
            return !string.IsNullOrEmpty(node.settlementId) ||
                   (node.adventureIds != null && node.adventureIds.Length > 0);
        }
    }
}
