using UnityEngine;
using UnityEngine.UI;
using TianZhang.Game;

namespace TianZhang.Adventure
{
    public enum AdventureSceneState
    {
        Loading,
        Exploration,
        Combat,
        Returning,
    }

    public class AdventureSceneController : MonoBehaviour
    {
        public AdventureSceneState CurrentState { get; private set; } = AdventureSceneState.Loading;
        public string CurrentAdventureId => GameSession.Instance?.CurrentAdventureId ?? "prototype_adventure";

        private Text adventureIdText;
        private Text sourceText;
        private Button returnToSourceButton;

        private void Start()
        {
            BuildAdventureUi();
            RefreshAdventureUi();
            MarkExplorationReady();
            Debug.Log("[AdventureScene] started");
        }

        public void MarkExplorationReady()
        {
            if (CurrentState != AdventureSceneState.Returning)
                CurrentState = AdventureSceneState.Exploration;
        }

        public void BeginEncounter()
        {
            if (CurrentState != AdventureSceneState.Returning)
                CurrentState = AdventureSceneState.Combat;
        }

        public void CompleteEncounter()
        {
            if (CurrentState == AdventureSceneState.Combat)
                CurrentState = AdventureSceneState.Exploration;
        }

        public void MarkReturning()
        {
            CurrentState = AdventureSceneState.Returning;
        }

        public void ReturnToSource()
        {
            MarkReturning();
            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.ReturnToPreviousScene();
        }

        public void ReturnToWorld()
        {
            MarkReturning();
            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.EnterWorld(
                    GameSession.Instance?.CurrentWorldNodeId ?? "jiangzuo_hub");
        }

        public string BuildSourceDescription()
        {
            var target = GameSession.Instance?.LastReturnTarget ?? default;
            if (target.SceneName == "SettlementScene")
                return "来源据点: " + (string.IsNullOrEmpty(target.SettlementId) ? "未记录" : target.SettlementId);

            if (target.SceneName == "WorldScene")
                return "来源主世界节点: " + (string.IsNullOrEmpty(target.WorldNodeId) ? "未记录" : target.WorldNodeId);

            return "来源: 未记录";
        }

        private void BuildAdventureUi()
        {
            if (GameObject.Find("AdventurePanel") != null)
            {
                adventureIdText = GameObject.Find("AdventureIdText")?.GetComponent<Text>();
                sourceText = GameObject.Find("AdventureSourceText")?.GetComponent<Text>();
                returnToSourceButton = GameObject.Find("ReturnToSourceButton")?.GetComponent<Button>();
                return;
            }

            var canvas = EnsureUICanvas();

            var panelGo = new GameObject("AdventurePanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panelGo.transform.SetParent(canvas.transform, false);
            var panelRt = panelGo.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0f, 1f);
            panelRt.anchorMax = new Vector2(0f, 1f);
            panelRt.pivot = new Vector2(0f, 1f);
            panelRt.anchoredPosition = new Vector2(24f, -24f);
            panelRt.sizeDelta = new Vector2(380f, 210f);
            panelGo.GetComponent<Image>().color = new Color(0.04f, 0.06f, 0.08f, 0.9f);
            var layout = panelGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 18, 18);
            layout.spacing = 10f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            CreateText("AdventureTitle", panelGo.transform, "副本", 26, Color.white, TextAnchor.MiddleCenter, 38f);
            adventureIdText = CreateText("AdventureIdText", panelGo.transform, "", 18, Color.yellow, TextAnchor.MiddleCenter, 32f).GetComponent<Text>();
            sourceText = CreateText("AdventureSourceText", panelGo.transform, "", 15, new Color(0.85f, 0.85f, 0.78f), TextAnchor.MiddleCenter, 42f).GetComponent<Text>();

            returnToSourceButton = CreateButton("ReturnToSourceButton", panelGo.transform, "返回来源", new Color(0.28f, 0.34f, 0.42f, 1f)).GetComponent<Button>();
            returnToSourceButton.onClick.AddListener(ReturnToSource);
        }

        private void RefreshAdventureUi()
        {
            if (adventureIdText != null)
                adventureIdText.text = "当前副本: " + CurrentAdventureId;

            if (sourceText != null)
                sourceText.text = BuildSourceDescription();

            if (returnToSourceButton != null)
                returnToSourceButton.interactable = SceneFlowManager.Instance != null;
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
            go.GetComponent<LayoutElement>().preferredHeight = 42f;

            var label = CreateText("Label", go.transform, labelText, 16, Color.white, TextAnchor.MiddleCenter, 42f);
            var labelRt = label.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.sizeDelta = Vector2.zero;
            return go;
        }
    }
}
