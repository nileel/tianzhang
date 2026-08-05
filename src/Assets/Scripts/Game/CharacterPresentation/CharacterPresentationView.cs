using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Game.CharacterPresentation
{
    public enum CharacterPresentationSurface
    {
        Profile,
        Dialogue,
    }

    /// <summary>
    /// Runtime owner for the separated profile and dialogue portrait layers.
    /// Approved 1664x936 artwork bounds are mapped to the project's 1920x1080 UI reference canvas.
    /// </summary>
    public sealed class CharacterPresentationView : MonoBehaviour
    {
        private const float ReferenceAspect = 16f / 9f;
        private const float CharacterBreathScale = 0.01f;
        private const float CharacterDriftPixels = 4f;

        private CharacterPresentationDefinition definition;
        private CharacterPresentationSurface surface;
        private RectTransform characterRect;
        private Vector2 baseCharacterPosition;
        private Vector3 baseCharacterScale;
        private Image profileFxImage;
        private CanvasGroup rootCanvasGroup;
        private CanvasGroup sealCanvasGroup;
        private float enabledAt;
        private bool built;

        public CharacterPresentationDefinition Definition => definition;
        public CharacterPresentationSurface Surface => surface;
        public Image CharacterImage { get; private set; }
        public Image BackgroundImage { get; private set; }
        public Image ProfileFxImage => profileFxImage;
        public Image NameArtImage { get; private set; }
        public Image DaoTitleArtImage { get; private set; }
        public Image SealImage { get; private set; }
        public RectMask2D DialogueMask { get; private set; }

        public bool Initialize(
            CharacterPresentationDefinition source,
            CharacterPresentationSurface requestedSurface,
            out string reason)
        {
            if (source == null)
            {
                reason = "Character presentation source is missing.";
                return false;
            }

            if (!(transform is RectTransform))
            {
                reason = "Character presentation view must be created on a RectTransform.";
                return false;
            }

            if (!source.TryValidate(out reason))
                return false;

            definition = source;
            surface = requestedSurface;
            Rebuild();
            reason = null;
            return true;
        }

        private void OnEnable()
        {
            enabledAt = Time.unscaledTime;
            if (definition != null && !built)
                Rebuild();
        }

        private void Update()
        {
            if (!built || characterRect == null)
                return;

            float elapsed = Time.unscaledTime - enabledAt;
            float wave = Mathf.Sin(elapsed * 0.7f);
            float scale = 1f + CharacterBreathScale * wave;
            characterRect.localScale = baseCharacterScale * scale;
            characterRect.anchoredPosition = baseCharacterPosition
                + Vector2.up * (CharacterDriftPixels * wave);

            if (profileFxImage != null)
            {
                Color color = profileFxImage.color;
                color.a = 0.42f + 0.04f * Mathf.Sin(elapsed * 0.32f);
                profileFxImage.color = color;
            }

            if (rootCanvasGroup != null)
                rootCanvasGroup.alpha = Mathf.Clamp01(elapsed / 0.35f);

            if (sealCanvasGroup != null && SealImage != null)
            {
                float stamp = Mathf.Clamp01((elapsed - 0.5f) / 0.25f);
                sealCanvasGroup.alpha = stamp;
                SealImage.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.18f, 1f, stamp);
            }
        }

        private void Rebuild()
        {
            ClearChildren();
            ResetRuntimeReferences();

            var ownRect = (RectTransform)transform;
            Stretch(ownRect);

            rootCanvasGroup = GetComponent<CanvasGroup>();
            if (rootCanvasGroup == null)
                rootCanvasGroup = gameObject.AddComponent<CanvasGroup>();
            rootCanvasGroup.alpha = Application.isPlaying ? 0f : 1f;

            if (surface == CharacterPresentationSurface.Profile)
                BuildProfile();
            else
                BuildDialogue();

            if (characterRect != null)
            {
                baseCharacterPosition = characterRect.anchoredPosition;
                baseCharacterScale = characterRect.localScale;
            }

            enabledAt = Time.unscaledTime;
            built = true;
        }

        private void BuildProfile()
        {
            RectTransform backdropRoot = CreateRect("ProfileBackdrop", transform);
            Center(backdropRoot, new Vector2(1920f, 1080f));
            var backdropFitter = backdropRoot.gameObject.AddComponent<AspectRatioFitter>();
            backdropFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            backdropFitter.aspectRatio = ReferenceAspect;

            BackgroundImage = CreateImage("ProfileBackground", backdropRoot, definition.profileBackground16x9);
            Stretch(BackgroundImage.rectTransform);

            if (definition.profileFx != null)
            {
                profileFxImage = CreateImage("ProfileFx", backdropRoot, definition.profileFx);
                Stretch(profileFxImage.rectTransform);
                profileFxImage.color = new Color(1f, 1f, 1f, 0.42f);
                profileFxImage.raycastTarget = false;
            }

            RectTransform contentRoot = CreateRect("ProfileSafeContent", transform);
            Center(contentRoot, new Vector2(1920f, 1080f));
            var contentFitter = contentRoot.gameObject.AddComponent<AspectRatioFitter>();
            contentFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            contentFitter.aspectRatio = ReferenceAspect;

            CharacterImage = CreateImage("CharacterFullBody", contentRoot, definition.characterFullBody);
            Place(CharacterImage.rectTransform, new Vector2(0.662f, 0.5f), new Vector2(505f, 1010f), new Vector2(0f, -7f));
            CharacterImage.preserveAspect = true;
            CharacterImage.raycastTarget = false;
            characterRect = CharacterImage.rectTransform;

            if (definition.nameArt != null)
            {
                NameArtImage = CreateImage("NameArt", contentRoot, definition.nameArt);
                Place(NameArtImage.rectTransform, new Vector2(0.236f, 0.5f), new Vector2(186f, 438f), new Vector2(0f, 261f));
                NameArtImage.preserveAspect = true;
                NameArtImage.raycastTarget = false;
            }

            if (definition.daoTitleArt != null)
            {
                DaoTitleArtImage = CreateImage("DaoTitleArt", contentRoot, definition.daoTitleArt);
                Place(DaoTitleArtImage.rectTransform, new Vector2(0.241f, 0.5f), new Vector2(66f, 228f), new Vector2(0f, -64f));
                DaoTitleArtImage.preserveAspect = true;
                DaoTitleArtImage.raycastTarget = false;
            }

            if (definition.seal != null)
            {
                SealImage = CreateImage("Seal", contentRoot, definition.seal);
                Place(SealImage.rectTransform, new Vector2(0.244f, 0.5f), new Vector2(78f, 90f), new Vector2(0f, -225f));
                SealImage.preserveAspect = true;
                SealImage.raycastTarget = false;
                sealCanvasGroup = SealImage.gameObject.AddComponent<CanvasGroup>();
                sealCanvasGroup.alpha = Application.isPlaying ? 0f : 1f;
            }
        }

        private void BuildDialogue()
        {
            Image baseColor = CreateImage("DialogueBackdrop", transform, null);
            Stretch(baseColor.rectTransform);
            baseColor.color = new Color(0.075f, 0.065f, 0.055f, 1f);

            RectTransform portraitViewport = CreateRect("DialoguePortraitViewport", transform);
            portraitViewport.anchorMin = new Vector2(0.45f, 0f);
            portraitViewport.anchorMax = Vector2.one;
            portraitViewport.offsetMin = Vector2.zero;
            portraitViewport.offsetMax = Vector2.zero;
            DialogueMask = portraitViewport.gameObject.AddComponent<RectMask2D>();

            CharacterImage = CreateImage("DialoguePortraitFromFullBody", portraitViewport, definition.DialoguePortrait);
            Place(CharacterImage.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(720f, 1440f), new Vector2(-40f, -320f));
            CharacterImage.preserveAspect = true;
            CharacterImage.raycastTarget = false;
            characterRect = CharacterImage.rectTransform;

            Image dialoguePanel = CreateImage("DialoguePanel", transform, null);
            dialoguePanel.rectTransform.anchorMin = Vector2.zero;
            dialoguePanel.rectTransform.anchorMax = new Vector2(1f, 0.28f);
            dialoguePanel.rectTransform.offsetMin = Vector2.zero;
            dialoguePanel.rectTransform.offsetMax = Vector2.zero;
            dialoguePanel.color = new Color(0.93f, 0.91f, 0.86f, 0.96f);

            if (definition.seal != null)
            {
                SealImage = CreateImage("DialogueSeal", dialoguePanel.transform, definition.seal);
                Place(SealImage.rectTransform, new Vector2(0.08f, 0.5f), new Vector2(62f, 72f), Vector2.zero);
                SealImage.preserveAspect = true;
                SealImage.raycastTarget = false;
            }
        }

        private void ClearChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }
        }

        private void ResetRuntimeReferences()
        {
            built = false;
            characterRect = null;
            profileFxImage = null;
            sealCanvasGroup = null;
            CharacterImage = null;
            BackgroundImage = null;
            NameArtImage = null;
            DaoTitleArtImage = null;
            SealImage = null;
            DialogueMask = null;
        }

        private static Image CreateImage(string name, Transform parent, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Center(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 position)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }
    }
}
