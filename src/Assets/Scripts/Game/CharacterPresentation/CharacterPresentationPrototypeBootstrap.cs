using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace TianZhang.Game.CharacterPresentation
{
    /// <summary>
    /// Isolated prototype caller. Formal scenes do not reference this bootstrap.
    /// </summary>
    public sealed class CharacterPresentationPrototypeBootstrap : MonoBehaviour
    {
        public CharacterPresentationDefinition definition;

        public CharacterPresentationView ProfileView { get; private set; }
        public CharacterPresentationView DialogueView { get; private set; }
        public Canvas PresentationCanvas { get; private set; }

        private void Start()
        {
            BuildPresentation();
        }

        public bool BuildPresentation()
        {
            if (PresentationCanvas != null)
                return true;

            if (definition == null)
            {
                Debug.LogError("Character presentation definition is missing.", this);
                return false;
            }

            if (!definition.TryValidate(out string reason))
            {
                Debug.LogError(reason, this);
                return false;
            }

            EnsureEventSystem();

            GameObject canvasGo = new GameObject(
                "CharacterPresentationCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            PresentationCanvas = canvasGo.GetComponent<Canvas>();
            PresentationCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            PresentationCanvas.sortingOrder = 120;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform profileRoot = CreateSurfaceRoot("ProfileSurface", canvasGo.transform);
            ProfileView = profileRoot.gameObject.AddComponent<CharacterPresentationView>();
            if (!ProfileView.Initialize(definition, CharacterPresentationSurface.Profile, out reason))
            {
                Debug.LogError(reason, this);
                return false;
            }

            RectTransform dialogueRoot = CreateSurfaceRoot("DialogueSurface", canvasGo.transform);
            DialogueView = dialogueRoot.gameObject.AddComponent<CharacterPresentationView>();
            if (!DialogueView.Initialize(definition, CharacterPresentationSurface.Dialogue, out reason))
            {
                Debug.LogError(reason, this);
                return false;
            }

            CreateModeControls(canvasGo.transform);
            ShowProfile();
            return true;
        }

        public void ShowProfile()
        {
            if (ProfileView != null)
                ProfileView.gameObject.SetActive(true);
            if (DialogueView != null)
                DialogueView.gameObject.SetActive(false);
        }

        public void ShowDialogue()
        {
            if (ProfileView != null)
                ProfileView.gameObject.SetActive(false);
            if (DialogueView != null)
                DialogueView.gameObject.SetActive(true);
        }

        private void CreateModeControls(Transform parent)
        {
            RectTransform controls = new GameObject("PrototypeModeControls", typeof(RectTransform))
                .GetComponent<RectTransform>();
            controls.SetParent(parent, false);
            controls.anchorMin = new Vector2(1f, 1f);
            controls.anchorMax = new Vector2(1f, 1f);
            controls.pivot = new Vector2(1f, 1f);
            controls.anchoredPosition = new Vector2(-24f, -24f);
            controls.sizeDelta = new Vector2(320f, 46f);

            Button profileButton = CreateButton("ProfileModeButton", controls, "PROFILE", new Vector2(-82f, 0f));
            profileButton.onClick.AddListener(ShowProfile);
            Button dialogueButton = CreateButton("DialogueModeButton", controls, "DIALOGUE", new Vector2(82f, 0f));
            dialogueButton.onClick.AddListener(ShowDialogue);
        }

        private static Button CreateButton(string name, Transform parent, string label, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(152f, 42f);
            go.GetComponent<Image>().color = new Color(0.08f, 0.07f, 0.06f, 0.72f);

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            RectTransform textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            Text text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = label;
            text.fontSize = 16;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.9f, 0.82f, 0.64f, 1f);
            text.raycastTarget = false;
            return go.GetComponent<Button>();
        }

        private static RectTransform CreateSurfaceRoot(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
                return;

            var eventSystemGo = new GameObject(
                "CharacterPresentationEventSystem",
                typeof(EventSystem),
                typeof(InputSystemUIInputModule));
            eventSystemGo.transform.SetParent(transform, false);
        }
    }
}
