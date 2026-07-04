using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TianZhang.Game;

namespace TianZhang.Settlement
{
    public class SettlementSceneController : MonoBehaviour
    {
        private const string DefaultSettlementId = "taiyi_sect";

        private static readonly SettlementDefinition[] PrototypeSettlements =
        {
            new SettlementDefinition { id = "taiyi_sect", displayName = "太一道庭", settlementType = SettlementType.Sect, regionId = "jiangzuo", ownerFactionId = "taiyi", availableServices = new[] { "修炼", "功法", "任务", "法坛" }, adventureEntrances = new[] { "taiyi_trial" }, visualTheme = "water_talisman" },
            new SettlementDefinition { id = "guanzhong_city", displayName = "关中城", settlementType = SettlementType.City, regionId = "guanzhong", ownerFactionId = "neutral", availableServices = new[] { "坊市", "悬赏", "客栈", "情报" }, adventureEntrances = new[] { "guanzhong_wild" }, visualTheme = "city_earth" },
            new SettlementDefinition { id = "zhongzhou_city", displayName = "中州城", settlementType = SettlementType.City, regionId = "zhongzhou", ownerFactionId = "neutral", availableServices = new[] { "坊市", "传送", "悬赏", "情报" }, adventureEntrances = new[] { "zhongzhou_wild" }, visualTheme = "capital" }
        };

        private Text settlementNameText;
        private Text settlementTypeText;
        private Text settlementDetailText;
        private Text returnContextText;
        private Transform serviceListParent;
        private Transform adventureListParent;
        private Button returnToWorldButton;

        public IReadOnlyList<SettlementDefinition> Settlements => PrototypeSettlements;
        public SettlementDefinition CurrentSettlement { get; private set; }
        public string CurrentSettlementId => CurrentSettlement?.id ?? DefaultSettlementId;

        private void Start()
        {
            BuildSettlementUi();
            if (!SelectSettlement(GameSession.Instance?.CurrentSettlementId))
                SelectSettlement(DefaultSettlementId);

            Debug.Log("[SettlementScene] definitions=" + PrototypeSettlements.Length);
        }

        public bool TryGetSettlement(string settlementId, out SettlementDefinition settlement)
        {
            settlement = null;
            if (string.IsNullOrEmpty(settlementId))
                return false;

            foreach (var candidate in PrototypeSettlements)
            {
                if (candidate.id == settlementId)
                {
                    settlement = candidate;
                    return true;
                }
            }

            return false;
        }

        public bool SelectSettlement(string settlementId)
        {
            if (!TryGetSettlement(settlementId, out var settlement))
                return false;

            CurrentSettlement = settlement;
            if (GameSession.Instance != null)
                GameSession.Instance.SetSettlementId(settlement.id);

            RefreshSettlementUi();
            return true;
        }

        public void ReturnToWorld()
        {
            var nodeId = ResolveReturnWorldNodeId();
            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.EnterWorld(nodeId);
        }

        public bool EnterAdventure(string adventureId)
        {
            if (string.IsNullOrEmpty(adventureId) || SceneFlowManager.Instance == null)
                return false;

            SceneFlowManager.Instance.EnterAdventure(adventureId, BuildAdventureReturnTarget());
            return true;
        }

        public SceneReturnTarget BuildAdventureReturnTarget()
        {
            return SceneReturnTarget.Settlement(CurrentSettlementId);
        }

        public string ResolveReturnWorldNodeId()
        {
            return GameSession.Instance != null ? GameSession.Instance.CurrentWorldNodeId : "jiangzuo_hub";
        }

        private void BuildSettlementUi()
        {
            if (GameObject.Find("SettlementPanel") != null)
            {
                settlementNameText = GameObject.Find("SettlementNameText")?.GetComponent<Text>();
                settlementTypeText = GameObject.Find("SettlementTypeText")?.GetComponent<Text>();
                settlementDetailText = GameObject.Find("SettlementDetailText")?.GetComponent<Text>();
                returnContextText = GameObject.Find("SettlementReturnContextText")?.GetComponent<Text>();
                serviceListParent = GameObject.Find("SettlementServiceList")?.transform;
                adventureListParent = GameObject.Find("SettlementAdventureList")?.transform;
                returnToWorldButton = GameObject.Find("ReturnToWorldButton")?.GetComponent<Button>();
                return;
            }

            var canvas = EnsureUICanvas();

            var panelGo = new GameObject("SettlementPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panelGo.transform.SetParent(canvas.transform, false);
            var panelRt = panelGo.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0f, 0f);
            panelRt.anchorMax = new Vector2(0f, 1f);
            panelRt.pivot = new Vector2(0f, 0.5f);
            panelRt.anchoredPosition = new Vector2(24f, 0f);
            panelRt.sizeDelta = new Vector2(420f, -48f);
            panelGo.GetComponent<Image>().color = new Color(0.06f, 0.05f, 0.04f, 0.9f);
            var panelLayout = panelGo.GetComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(24, 24, 24, 24);
            panelLayout.spacing = 10f;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;

            CreateText("SettlementTitle", panelGo.transform, "据点", 30, Color.white, TextAnchor.MiddleCenter, 44f);
            settlementNameText = CreateText("SettlementNameText", panelGo.transform, "", 24, Color.yellow, TextAnchor.MiddleCenter, 38f).GetComponent<Text>();
            settlementTypeText = CreateText("SettlementTypeText", panelGo.transform, "", 16, Color.white, TextAnchor.MiddleCenter, 28f).GetComponent<Text>();
            settlementDetailText = CreateText("SettlementDetailText", panelGo.transform, "", 14, new Color(0.85f, 0.85f, 0.75f), TextAnchor.MiddleCenter, 56f).GetComponent<Text>();

            CreateText("SettlementServiceTitle", panelGo.transform, "可用服务", 18, Color.white, TextAnchor.MiddleLeft, 30f);
            serviceListParent = CreateListContainer("SettlementServiceList", panelGo.transform);

            CreateText("SettlementAdventureTitle", panelGo.transform, "副本入口占位", 18, Color.white, TextAnchor.MiddleLeft, 30f);
            adventureListParent = CreateListContainer("SettlementAdventureList", panelGo.transform);

            returnContextText = CreateText("SettlementReturnContextText", panelGo.transform, "", 14, Color.gray, TextAnchor.MiddleCenter, 34f).GetComponent<Text>();
            returnToWorldButton = CreateButton("ReturnToWorldButton", panelGo.transform, "返回主世界", new Color(0.32f, 0.38f, 0.28f, 1f)).GetComponent<Button>();
            returnToWorldButton.onClick.AddListener(ReturnToWorld);
        }

        private void RefreshSettlementUi()
        {
            if (CurrentSettlement == null)
                return;

            if (settlementNameText != null)
                settlementNameText.text = CurrentSettlement.displayName;

            if (settlementTypeText != null)
                settlementTypeText.text = GetSettlementTypeLabel(CurrentSettlement.settlementType) + " / " + CurrentSettlement.id;

            if (settlementDetailText != null)
                settlementDetailText.text = "区域: " + CurrentSettlement.regionId + "\n势力: " + CurrentSettlement.ownerFactionId;

            if (returnContextText != null)
                returnContextText.text = "返回主世界节点: " + ResolveReturnWorldNodeId();

            RebuildServiceList();
            RebuildAdventureList();
        }

        private void RebuildServiceList()
        {
            if (serviceListParent == null)
                return;

            ClearChildren(serviceListParent);
            var services = CurrentSettlement.availableServices;
            if (services == null || services.Length == 0)
            {
                CreateText("SettlementService_Empty", serviceListParent, "暂无服务", 14, Color.gray, TextAnchor.MiddleLeft, 28f);
                return;
            }

            foreach (var service in services)
            {
                var button = CreateButton("SettlementService_" + service, serviceListParent, service, new Color(0.18f, 0.22f, 0.18f, 1f)).GetComponent<Button>();
                button.interactable = false;
            }
        }

        private void RebuildAdventureList()
        {
            if (adventureListParent == null)
                return;

            ClearChildren(adventureListParent);
            var entrances = CurrentSettlement.adventureEntrances;
            if (entrances == null || entrances.Length == 0)
            {
                CreateText("SettlementAdventure_Empty", adventureListParent, "暂无副本入口", 14, Color.gray, TextAnchor.MiddleLeft, 28f);
                return;
            }

            foreach (var adventureId in entrances)
            {
                var button = CreateButton("SettlementAdventure_" + adventureId, adventureListParent, "进入副本: " + adventureId, new Color(0.18f, 0.18f, 0.24f, 1f)).GetComponent<Button>();
                var capturedAdventureId = adventureId;
                button.onClick.AddListener(() => EnterAdventure(capturedAdventureId));
            }
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

        private static Transform CreateListContainer(string name, Transform parent)
        {
            var containerGo = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            containerGo.transform.SetParent(parent, false);
            var layout = containerGo.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            containerGo.AddComponent<LayoutElement>().preferredHeight = 180f;
            return containerGo.transform;
        }

        private static GameObject CreateText(string name, Transform parent, string text, int fontSize, Color color, TextAnchor anchor, float preferredHeight)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<Text>();
            label.text = text;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = anchor;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            go.GetComponent<LayoutElement>().preferredHeight = preferredHeight;
            return go;
        }

        private static GameObject CreateButton(string name, Transform parent, string labelText, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            go.GetComponent<LayoutElement>().preferredHeight = 40f;

            var label = CreateText("Label", go.transform, labelText, 16, Color.white, TextAnchor.MiddleCenter, 40f);
            var labelRt = label.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.sizeDelta = Vector2.zero;
            return go;
        }

        private static string GetSettlementTypeLabel(SettlementType type)
        {
            switch (type)
            {
                case SettlementType.City:
                    return "城池";
                case SettlementType.Sect:
                    return "宗门";
                case SettlementType.Cave:
                    return "洞府";
                case SettlementType.Market:
                    return "坊市";
                case SettlementType.Special:
                    return "特殊地点";
                default:
                    return type.ToString();
            }
        }

        private static void ClearChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }
        }
    }
}
