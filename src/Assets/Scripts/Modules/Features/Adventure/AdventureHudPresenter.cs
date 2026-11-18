using System;
using TianZhang.Content;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace TianZhang.Features.Adventure
{
    public sealed class AdventureHudPresenter : MonoBehaviour
    {
        [SerializeField] private Transform nodeContainer;
        [SerializeField] private Text adventureText;
        [SerializeField] private Text statusText;
        [SerializeField] private Text combatLocationText;
        [SerializeField] private Font nodeFont;
        [SerializeField] private Sprite nodeNormal;
        [SerializeField] private Sprite nodeHover;
        [SerializeField] private Sprite nodeSelected;
        [SerializeField] private GameObject returnEntry;
        [SerializeField] private RectTransform explorationPanel;
        [SerializeField] private Button returnButton;
        [SerializeField] private Text returnLabel;
        [SerializeField] private Text returnReason;
        [SerializeField] private Sprite combatReturnSprite;
        private Sprite explorationReturnSprite;
        [SerializeField] private GameObject withdrawalConfirmation;
        [SerializeField] private Button confirmWithdrawalButton;
        [SerializeField] private Button continueCombatButton;

        public const string WithdrawalMessage = "撤离不计胜负，不推进本场悬赏，也不获得本场掉落。已使用的道具不返还，已确认的换装保留；本场生命与灵力变化不带回城镇。返回后仍需在城镇保存。";

        public void ConfigureWithdrawal(Action request, Action confirm, Action cancel)
        {
            explorationReturnSprite = returnButton.image.sprite;
            if (returnButton == null || withdrawalConfirmation == null || confirmWithdrawalButton == null ||
                continueCombatButton == null || returnEntry == null || explorationPanel == null || returnLabel == null || returnReason == null)
                throw new InvalidOperationException("adventure_withdrawal_ui_missing");
            returnButton.onClick.AddListener(() => request());
            confirmWithdrawalButton.onClick.AddListener(() => confirm());
            continueCombatButton.onClick.AddListener(() => cancel());
        }

        public void ApplyReturnEntryLayout(bool inCombat)
        {
            if (returnEntry == null || explorationPanel == null) return;
            var rect = (RectTransform)returnEntry.transform;
            float scale = Mathf.Clamp(Screen.width * .0104f, 18, 20) / 20;
            var canvas = returnEntry.GetComponentInParent<Canvas>();
            rect.localScale = Vector3.one * (inCombat ? scale / canvas.scaleFactor : 1);
            rect.anchorMin = rect.anchorMax = inCombat ? new Vector2(.98f, .94f) : explorationPanel.anchorMax;
            rect.pivot = inCombat ? Vector2.one : Vector2.up;
            rect.anchoredPosition = inCombat ? new Vector2(0, -68 * scale / canvas.scaleFactor) : new Vector2(24, 0);
            rect.sizeDelta = inCombat ? new Vector2(44, 40) : new Vector2(explorationPanel.rect.width, 144);
            if (returnButton != null)
            {
                var button = (RectTransform)returnButton.transform;
                button.anchorMin = button.anchorMax = button.pivot = Vector2.zero;
                button.anchoredPosition = inCombat ? Vector2.zero : new Vector2(16, 68);
                button.sizeDelta = inCombat ? new Vector2(44, 40) : new Vector2(rect.rect.width - 32, 60);
                returnButton.image.sprite = inCombat ? combatReturnSprite : explorationReturnSprite;
                returnButton.transition = inCombat ? Selectable.Transition.ColorTint : Selectable.Transition.SpriteSwap;
                returnLabel.rectTransform.offsetMin = new Vector2(inCombat ? 2 : 16, 4);
                returnLabel.rectTransform.offsetMax = new Vector2(inCombat ? -2 : -16, -4);
            }
            if (returnReason != null)
            {
                var reason = returnReason.rectTransform;
                reason.anchorMin = reason.anchorMax = reason.pivot = Vector2.zero;
                reason.anchoredPosition = inCombat ? new Vector2(-256, -32) : new Vector2(16, 12);
                reason.sizeDelta = inCombat ? new Vector2(300, 30) : new Vector2(rect.rect.width - 32, 48);
            }
        }

        public void PresentReturnEntry(bool visible, string label, bool available, string reason, bool confirming, bool inCombat)
        {
            ApplyReturnEntryLayout(inCombat);
            returnEntry?.SetActive(visible);
            if (returnLabel != null) { returnLabel.text = inCombat ? "☰" : label; returnLabel.fontSize = inCombat ? 18 : 28; }
            if (returnReason != null) returnReason.text = reason ?? string.Empty;
            if (returnButton != null) returnButton.interactable = available;
            bool showConfirmation = visible && confirming;
            if (withdrawalConfirmation != null && withdrawalConfirmation.activeSelf != showConfirmation)
            {
                withdrawalConfirmation.SetActive(showConfirmation);
                EventSystem.current?.SetSelectedGameObject(showConfirmation ? confirmWithdrawalButton.gameObject : returnButton.gameObject);
            }
        }
        private Func<string, string> resolveText = value => value;
        private Func<string, string> displayReason = value => value;

        public void ConfigureText(Func<string, string> resolve, Func<string, string> reasonDisplay)
        {
            resolveText = resolve ?? throw new ArgumentNullException(nameof(resolve));
            displayReason = reasonDisplay ?? throw new ArgumentNullException(nameof(reasonDisplay));
        }

        public void Present(AdventureSession session, Func<string, bool> selectNode, string failureReason,
            bool nodesInteractable = true)
        {
            if (session == null) return;
            if (adventureText != null) adventureText.text = resolveText(session.Map.displayNameKey);
            if (combatLocationText != null) combatLocationText.text = resolveText(session.Map.displayNameKey);
            if (statusText != null) statusText.text = string.IsNullOrWhiteSpace(failureReason)
                ? resolveText(session.Status) : displayReason(failureReason);
            if (nodeContainer == null) return;
            if (nodeContainer.childCount == 0) CreateNodes(session, selectNode);
            foreach (Transform node in nodeContainer)
                node.GetComponent<Button>().interactable = nodesInteractable;
        }

        public void PresentTerrainRehearsalLocation(string title = "阶地演武 · 地形试玩")
        {
            if (combatLocationText != null) combatLocationText.text = title;
        }

        private void CreateNodes(AdventureSession session, Func<string, bool> selectNode)
        {
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
                label.text = resolveText("adventure_node_" + node.nodeId) + " (" + node.q + "," + node.r + ")";
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
