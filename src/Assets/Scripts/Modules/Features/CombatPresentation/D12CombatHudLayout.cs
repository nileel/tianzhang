using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>V3 CSS rem and content-width layout, isolated from the exploration Canvas scale.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class D12CombatHudLayout : MonoBehaviour
    {
        [SerializeField] private RectTransform hud;
        [SerializeField] private RectTransform card;
        [SerializeField] private RectTransform portrait;
        [SerializeField] private RectTransform vitals;
        [SerializeField] private RectTransform statuses;
        [SerializeField] private RectTransform hotbar;
        [SerializeField] private RectTransform viewport;
        [SerializeField] private RectTransform row;
        [SerializeField] private RectTransform endButton;
        [SerializeField] private RectTransform timeline;
        [SerializeField] private RectTransform ordinaryQuote;
        [SerializeField] private RectTransform jindanQuote;
        [SerializeField] private JindanDemoView jindan;
        internal Image ResourcePortrait => portrait.GetComponent<Image>();
        private Canvas canvas;
        private Button[] slots;
        private int lastWidth, lastHeight, lastCount = -1;
        private float lastOptions = -1;
        private Text quoteText;
        private float lastCostWidth = -1;
        private Text quoteTitle;
        private float lastTitleWidth = -1;

        private void Awake()
        {
            canvas = GetComponentInParent<Canvas>();
            slots = row.GetComponentsInChildren<Button>(true);
            quoteText = ordinaryQuote.Find("CompactQuote").GetComponent<Text>();
            quoteTitle = ordinaryQuote.Find("ActionPreviewHeading").GetComponent<Text>();
        }
        private void OnEnable()
        {
            lastWidth = 0;
            Canvas.willRenderCanvases += ApplyCanvasScale;
        }
        private void OnDisable() => Canvas.willRenderCanvases -= ApplyCanvasScale;
        private void ApplyCanvasScale()
        {
            // CanvasScaler updates in preWillRenderCanvases, after LateUpdate.
            // Use that frame's final scale so resizing cannot cache an old conversion.
            float scale = Mathf.Clamp(Screen.width * .0104f, 18, 20) / 20;
            hud.localScale = Vector3.one * (scale / canvas.scaleFactor);
        }
        private void LateUpdate()
        {
            int count = slots.Count(button => button.gameObject.activeSelf);
            float options = jindan.OptionsHeight;
            float costWidth = Mathf.Max(128, quoteText.preferredWidth + 4);
            float titleWidth = Mathf.Max(44, quoteTitle.preferredWidth + 2);
            if (Screen.width == lastWidth && Screen.height == lastHeight && count == lastCount && options == lastOptions && costWidth == lastCostWidth && titleWidth == lastTitleWidth) return;
            lastWidth = Screen.width; lastHeight = Screen.height; lastCount = count; lastOptions = options;
            lastCostWidth = costWidth;
            lastTitleWidth = titleWidth;
            float scale = Mathf.Clamp(Screen.width * .0104f, 18, 20) / 20;
            hud.sizeDelta = new Vector2(Screen.width, Screen.height) / scale;
            bool small = Screen.width <= 1450;
            bool ordinary = !jindan.HasAbilities;
            float cardWidth = Mathf.Min(small ? 460 : 440, hud.rect.width * (small ? .29f : .28f));
            card.sizeDelta = new Vector2(cardWidth, small ? 132.6f : 137);
            portrait.sizeDelta = small ? new Vector2(80, 96) : new Vector2(96, 116);
            vitals.offsetMin = new Vector2(small ? 96 : 112, 10);
            vitals.offsetMax = new Vector2(-14, -10);
            float numberWidth = small ? 104 : 112;
            foreach (string resource in new[] { "Health", "Spirit", "ActionPoint" })
            {
                var meter = (RectTransform)vitals.Find("Combat" + resource + "Bar");
                meter.sizeDelta = new Vector2(cardWidth - (small ? 96 : 112) - 14 - 48 - 8 - numberWidth, 5.6f);
                ((RectTransform)vitals.Find("Combat" + resource + "Value")).sizeDelta = new Vector2(numberWidth, 24);
            }
            statuses.anchorMin = statuses.anchorMax = new Vector2(.02f, small ? .78f : .8f);
            timeline.anchorMin = timeline.anchorMax = new Vector2(.5f, small ? .94f : .95f);
            float slotWidth = ordinary ? 92 : small ? 76 : 83;
            float gap = small ? 8 : 11;
            float rowWidth = count * slotWidth + Mathf.Max(0, count - 1) * gap;
            float endGap = ordinary ? 40 : small ? 20 : 26;
            var layout = row.GetComponent<HorizontalLayoutGroup>(); layout.spacing = gap;
            foreach (Button button in slots)
            {
                button.GetComponent<LayoutElement>().preferredWidth = slotWidth;
                foreach (string name in new[] { "IconFrame", "SelectedFrame", "FocusFrame" })
                    ((RectTransform)button.transform.Find(name)).sizeDelta = Vector2.one * ((small ? 60 : 64) + 8);
            }
            hotbar.anchoredPosition = new Vector2(0, 32 / scale);
            hotbar.sizeDelta = new Vector2(52 + rowWidth + endGap + 144, 140);
            viewport.sizeDelta = new Vector2(rowWidth, 114);
            endButton.anchoredPosition = new Vector2(-26, 23);
            ordinaryQuote.anchoredPosition = new Vector2(0, 32 / scale + 182);
            float titleExtra = titleWidth - 44;
            float extra = costWidth - 128 + titleExtra;
            ordinaryQuote.sizeDelta = new Vector2((small ? 438 : 460) + extra, 78);
            quoteTitle.rectTransform.sizeDelta = new Vector2(titleWidth, 54);
            quoteText.rectTransform.sizeDelta = new Vector2(costWidth, 58);
            quoteText.rectTransform.anchoredPosition = new Vector2((small ? 80 : 90) + titleExtra, 10);
            ((RectTransform)ordinaryQuote.Find("PreviewActions/ConfirmActionButton")).anchoredPosition = new Vector2((small ? 223 : 240) + extra, 16);
            ((RectTransform)ordinaryQuote.Find("PreviewActions/CancelActionButton")).anchoredPosition = new Vector2((small ? 357 : 380) + extra, 16);
            ((RectTransform)ordinaryQuote.Find("BoardActionPrompt")).anchoredPosition = new Vector2((small ? 223 : 240) + extra, 16);
            // The option grids hang below their root; keep them just above the shared quote.
            jindanQuote.anchoredPosition = ordinaryQuote.anchoredPosition + Vector2.up * (ordinaryQuote.rect.height + options + 8);
            LayoutRebuilder.MarkLayoutForRebuild(row);
        }
    }
}
