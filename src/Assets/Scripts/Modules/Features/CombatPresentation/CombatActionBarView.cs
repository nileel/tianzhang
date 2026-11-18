using System;
using System.Linq;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace TianZhang.Features.CombatPresentation
{
    public sealed class CombatActionBarView : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Sprite attackIcon;
        [SerializeField] private Sprite unarmedIcon;
        [SerializeField] private Sprite guanshijiIcon;
        [SerializeField] private Sprite julingshuIcon;
        [SerializeField] private Sprite feixingzhiIcon;
        [SerializeField] private Sprite defaultAbilityIcon;
        [SerializeField] private Button basicAttackButton;
        [SerializeField] private Button artButton;
        [SerializeField] private Button divineButton;
        [SerializeField] private Button guardButton;
        [SerializeField] private Button waitButton;
        [SerializeField] private Text artLabel;
        [SerializeField] private Text divineLabel;
        [SerializeField] private Button secondArtButton;
        [SerializeField] private Text secondArtLabel;
        [SerializeField] private Button thirdArtButton;
        [SerializeField] private Text thirdArtLabel;
        [SerializeField] private Button fourthArtButton;
        [SerializeField] private Button fifthArtButton;
        [SerializeField] private Button secondDivineButton;
        [SerializeField] private Transform slotContainer;
        [SerializeField] private Button bagButton;
        [SerializeField] private GameObject bagRoot;
        [SerializeField] private Text slotDetails;
        [SerializeField] private CombatCommandInput commandInput;
        [SerializeField] private CombatMovementView movementView;
        [SerializeField] private CombatActionPreviewView previewView;
        private CombatHudSnapshot current;
        private Button jindanSelection;
        private Button[] slots;
        private string[] shortcuts;
        private bool inputPaused;
        public Button FourthArtButton => fourthArtButton;
        public Button FifthArtButton => fifthArtButton;
        public Button SecondDivineButton => secondDivineButton;
        public Button SelectedButton { get; private set; }
        public void SelectJindan(Button button) => jindanSelection = button;
        public void PauseInput(bool paused) => inputPaused = paused;

        private void Awake()
        {
            if (slotContainer != null) slots = slotContainer.GetComponentsInChildren<Button>(true);
            if (bagButton != null) bagButton.onClick.AddListener(() => bagRoot.SetActive(!bagRoot.activeSelf));
        }
        [SerializeField] private Text actionStatusLabel;
        [SerializeField] private Text waitLabel;
        private Func<string, string> displayReason = value => value;
        [SerializeField] private Button moveButton;
        [SerializeField] private Text movementLabel;
        [SerializeField] private Button swapSpellButton;
        public Button SwapSpellButton => swapSpellButton;
        [SerializeField] private Button swapEquipmentButton;
        public Button SwapEquipmentButton => swapEquipmentButton;
        [SerializeField] private Button useItemButton;
        public Button UseItemButton => useItemButton;
        public Button MoveButton => moveButton;

        private Func<string, string> displayProfile = value => value;

        public void ConfigureText(Func<string, string> profileDisplay, Func<string, string> reasonDisplay = null)
        {
            displayProfile = profileDisplay ?? throw new ArgumentNullException(nameof(profileDisplay));
            displayReason = reasonDisplay ?? (value => value);
        }

        public Button BasicAttackButton => basicAttackButton;
        public Button ArtButton => artButton;
        public Button DivineButton => divineButton;
        public Button SecondArtButton => secondArtButton;
        public Button ThirdArtButton => thirdArtButton;
        public Button GuardButton => guardButton;
        public Button WaitButton => waitButton;

        public void Present(bool visible, string artProfileId, string divineProfileId)
        {
            root?.SetActive(visible);
            SetEnabled(secondArtButton, false);
            SetEnabled(thirdArtButton, false);
            SetEnabled(swapSpellButton, false);
            SetEnabled(swapEquipmentButton, false);
            SetEnabled(useItemButton, false);
            if (secondArtLabel != null) secondArtLabel.text = "术法";
            if (thirdArtLabel != null) thirdArtLabel.text = "术法";
            if (actionStatusLabel != null) actionStatusLabel.text = string.Empty;
            if (waitLabel != null) waitLabel.text = "待机";
            SetEnabled(basicAttackButton, visible);
            SetEnabled(guardButton, visible);
            SetEnabled(waitButton, visible);
            SetEnabled(artButton, visible && !string.IsNullOrWhiteSpace(artProfileId));
            SetEnabled(divineButton, visible && !string.IsNullOrWhiteSpace(divineProfileId));
            if (artLabel != null) artLabel.text = string.IsNullOrWhiteSpace(artProfileId) ? "术法" : displayProfile(artProfileId);
            if (divineLabel != null) divineLabel.text = string.IsNullOrWhiteSpace(divineProfileId) ? "神通" : displayProfile(divineProfileId);
        }

        public void Present(CombatHudSnapshot snapshot)
        {
            current = snapshot;
            bool visible = snapshot != null && snapshot.AcceptsCommands && !snapshot.IsPresenting;
            bool canAct = visible;
            root?.SetActive(snapshot != null);
            CombatActionPreviewSnapshot swap = snapshot?.FindPreferredAction(CombatActionKind.SwapSpell);
            SetEnabled(swapSpellButton, canAct && swap != null);
            if (swapSpellButton != null)
                swapSpellButton.GetComponentInChildren<Text>().text = "换术\n" +
                    (swap == null ? string.Empty : CombatActionPreviewView.FormatActionPoints(swap.ActionPointCostUnits) + " AP\n") +
                    "剩余 " + (swap?.CombatSwapsRemaining ?? 0) + " 次";
            PresentCooldown(swapSpellButton, swap?.RemainingCooldownTicks ?? 0);
            string equipmentReason = PresentAction(snapshot?.FindPreferredAction(CombatActionKind.SwapEquipment), swapEquipmentButton, canAct);
            string itemReason = PresentAction(snapshot?.FindPreferredAction(CombatActionKind.UseItem), useItemButton, canAct);
            string basicReason = PresentAction(snapshot?.FindPreferredAction(CombatActionKind.BasicAttack), basicAttackButton, canAct);
            string guardReason = PresentAction(snapshot?.FindAction(CombatActionKind.Guard), guardButton, canAct);
            string waitReason = PresentAction(snapshot?.FindAction(snapshot?.CanEndTurn == true ? CombatActionKind.EndTurn : CombatActionKind.Wait),
                waitButton, canAct);
            string divineId = snapshot != null && snapshot.DivineProfileIds.Count > 0 ? snapshot.DivineProfileIds[0] : null;
            if (divineButton != null) divineButton.gameObject.SetActive(!string.IsNullOrWhiteSpace(divineId));
            string divineReason = PresentAction(snapshot?.FindPreferredAction(CombatActionKind.Divine, divineId), divineButton, canAct);
            string secondDivine = snapshot?.DivineProfileIds.Count > 1 ? snapshot.DivineProfileIds[1] : null;
            if (secondDivineButton != null) secondDivineButton.gameObject.SetActive(secondDivine != null);
            PresentAction(secondDivine == null ? null : snapshot.FindPreferredAction(CombatActionKind.Divine, secondDivine), secondDivineButton, canAct);

            CombatArtActionSnapshot first = snapshot != null && snapshot.ArtActions.Count > 0 ? snapshot.ArtActions[0] : null;
            CombatArtActionSnapshot second = snapshot != null && snapshot.ArtActions.Count > 1 ? snapshot.ArtActions[1] : null;
            CombatArtActionSnapshot third = snapshot != null && snapshot.ArtActions.Count > 2 ? snapshot.ArtActions[2] : null;
            string firstReason = PresentArt(first, artButton, artLabel, canAct);
            string secondReason = PresentArt(second, secondArtButton, secondArtLabel, canAct);
            string thirdReason = PresentArt(third, thirdArtButton, thirdArtLabel, canAct);
            PresentArt(snapshot?.ArtActions.Count > 3 ? snapshot.ArtActions[3] : null, fourthArtButton,
                fourthArtButton == null ? null : fourthArtButton.GetComponentInChildren<Text>(), canAct);
            PresentArt(snapshot?.ArtActions.Count > 4 ? snapshot.ArtActions[4] : null, fifthArtButton,
                fifthArtButton == null ? null : fifthArtButton.GetComponentInChildren<Text>(), canAct);
            if (actionStatusLabel != null)
            {
                actionStatusLabel.fontSize = 16;
                actionStatusLabel.text = snapshot == null ? string.Empty :
                    (snapshot.IsPresenting ? "演出中，请稍候" : !visible ? snapshot.TurnText :
                    string.Empty);
                actionStatusLabel.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(actionStatusLabel.text));
            }
        }

        private string PresentArt(CombatArtActionSnapshot art, Button button, Text label, bool canAct)
        {
            if (button != null) button.gameObject.SetActive(art != null);
            CombatActionPreviewSnapshot quote = art?.EntryQuote;
            SetEnabled(button, canAct && quote?.CanOpen == true);
            SetIcon(button, art?.ProfileId switch
            {
                "spell_guanshiji" => guanshijiIcon,
                "spell_julingshu" => julingshuIcon,
                "spell_feixingzhi" => feixingzhiIcon,
                _ => null,
            });
            PresentCooldown(button, quote?.RemainingCooldownTicks ?? 0);
            if (label != null)
                label.text = art == null ? "术法" : Caption(CompactName(art.DisplayName),
                    (quote?.CanOpen == false ? "不可用" : quote == null ? string.Empty :
                    CombatActionPreviewView.FormatActionPoints(quote.ActionPointCostUnits) + " AP"));
            if (art == null) return string.Empty;
            return quote?.CanOpen == true ? string.Empty : quote?.Reason ?? string.Empty;
        }

        private string PresentAction(CombatActionPreviewSnapshot quote, Button button, bool canAct)
        {
            SetEnabled(button, canAct && quote != null &&
                (quote.Candidate.Kind == CombatActionKind.BasicAttack ? quote.CanOpen : quote.CanConfirm));
            PresentCooldown(button, quote?.RemainingCooldownTicks ?? 0);
            if (quote == null) return string.Empty;
            if (quote.Candidate.Kind == CombatActionKind.BasicAttack)
                SetIcon(button, quote.BasicAttackSourceKind == "unarmed_body" ? unarmedIcon : attackIcon);
            Text label = button == null ? null : button.GetComponentInChildren<Text>();
            if (label != null)
            {
                string cost = CombatActionPreviewView.FormatActionPoints(quote.ActionPointCostUnits) + " AP";
                label.text = quote.Candidate.Kind switch
                {
                    CombatActionKind.SwapEquipment when quote.EquipmentOwned.HasValue => CompactName(quote.ActionName) + "\n" + cost + "\n" +
                        (quote.EquipmentOwned.Value ? "已持有" : "未持有"),
                    CombatActionKind.UseItem => CompactName(quote.ActionName) + " ×" + quote.ItemAvailableQuantity + "\n" + cost,
                    CombatActionKind.EndTurn or CombatActionKind.Wait => quote.ActionName + "\n<size=13>主动结束</size>",
                    CombatActionKind.Guard => Caption(quote.ActionName, cost),
                    CombatActionKind.BasicAttack => Caption("普攻", !quote.CanOpen ? "不可用" : cost),
                    _ => Caption(CompactName(quote.ActionName), !quote.CanOpen ? "不可用" : cost),
                };
            }
            return quote.CanConfirm || quote.Candidate.Kind == CombatActionKind.BasicAttack && quote.CanOpen ? string.Empty : quote.Reason;
        }

        public void PresentMovement(CombatHudSnapshot snapshot, bool selecting, CombatMovementOption preview = null)
        {
            bool canInput = snapshot != null && snapshot.AcceptsCommands && !snapshot.IsPresenting;
            SetEnabled(moveButton, canInput && snapshot.Movement != null && snapshot.Movement.Options.Count > 0);
            if (movementLabel != null) movementLabel.text = snapshot?.Movement == null ? string.Empty :
                (!canInput ? "暂不可用" : selecting ? (preview == null ? "选择地格" :
                    CombatActionPreviewView.FormatActionPoints(preview.Cost) + " AP") : "路径报价");
        }

        private static string CompactName(string value) => value != null && value.Length > 5
            ? value.Substring(0, 4) + "…" : value;
        public static string Caption(string title, string sub) => title + "\n<size=14><color=#bdcbc5>" + sub + "</color></size>";

        private void LateUpdate()
        {
            if (slots == null || current == null || root?.activeInHierarchy != true) return;
            Button selected = jindanSelection;
            if (movementView?.IsSelecting == true) selected = moveButton;
            else if (commandInput?.Candidate != null)
            {
                var candidate = commandInput.Candidate;
                selected = candidate.Kind == CombatActionKind.BasicAttack ? basicAttackButton :
                    candidate.Kind == CombatActionKind.Guard ? guardButton :
                    candidate.Kind is CombatActionKind.EndTurn or CombatActionKind.Wait ? waitButton : null;
                if (candidate.Kind == CombatActionKind.Art)
                {
                    var artButtons = new[] { artButton, secondArtButton, thirdArtButton, fourthArtButton, fifthArtButton };
                    for (int i = 0; i < current.ArtActions.Count && i < artButtons.Length; i++)
                        if (current.ArtActions[i].ProfileId == candidate.ProfileId) selected = artButtons[i];
                }
            }
            SelectedButton = selected;
            var active = slots.Where(b => b.gameObject.activeInHierarchy).ToArray();
            shortcuts = new string[active.Length];
            int number = 1;
            for (int i = 0; i < active.Length; i++)
            {
                Button b = active[i];
                string key = b == moveButton ? "M" : b == guardButton ? "G" : b == bagButton ? "I" : b.name == "close_tab" ? "X" :
                    number <= 9 ? (number++).ToString() : "F" + (number++ - 9);
                shortcuts[i] = key;
                Text hint = b.transform.Find("Shortcut")?.GetComponent<Text>();
                if (hint != null) hint.text = key;
                b.transform.Find("SelectedFrame")?.gameObject.SetActive(b == selected);
                b.transform.Find("SelectedUnderline")?.gameObject.SetActive(b == selected);
                b.transform.Find("FocusFrame")?.gameObject.SetActive(EventSystem.current?.currentSelectedGameObject == b.gameObject);
            }
            PresentSlotDetails(active);
            var keyboard = Keyboard.current;
            if (keyboard == null || inputPaused) return;
            if (keyboard.tabKey.wasPressedThisFrame)
            {
                var focusable = active.Concat(new[] { waitButton }).Where(b => b != null && b.IsInteractable()).ToArray();
                if (focusable.Length > 0)
                {
                    int index = Array.FindIndex(focusable, b => EventSystem.current?.currentSelectedGameObject == b.gameObject);
                    int step = keyboard.shiftKey.isPressed ? -1 : 1;
                    EventSystem.current?.SetSelectedGameObject(focusable[(index + step + focusable.Length) % focusable.Length].gameObject);
                }
            }
            if (!current.AcceptsCommands || current.IsPresenting) return;
            for (int i = 0; i < active.Length; i++)
                if (Enum.TryParse(shortcuts[i].Length == 1 && char.IsDigit(shortcuts[i][0]) ? "Digit" + shortcuts[i] : shortcuts[i], out Key key) &&
                    keyboard[key].wasPressedThisFrame && active[i].IsInteractable()) active[i].onClick.Invoke();
        }

        private void PresentSlotDetails(Button[] active)
        {
            if (slotDetails == null) return;
            if (SelectedButton != null || previewView?.IsVisible == true || movementView?.IsSelecting == true)
            { slotDetails.transform.parent.gameObject.SetActive(false); return; }
            Vector2 pointer = Mouse.current?.position.ReadValue() ?? new Vector2(-1, -1);
            Button hovered = active.FirstOrDefault(b => RectTransformUtility.RectangleContainsScreenPoint((RectTransform)b.transform, pointer));
            Button focus = active.FirstOrDefault(b => EventSystem.current?.currentSelectedGameObject == b.gameObject);
            Button target = hovered ?? focus;
            var artButtons = new[] { artButton, secondArtButton, thirdArtButton, fourthArtButton, fifthArtButton };
            int index = Array.IndexOf(artButtons, target);
            var quote = index >= 0 && index < current.ArtActions.Count ? current.ArtActions[index].EntryQuote :
                target == basicAttackButton ? current.FindPreferredAction(CombatActionKind.BasicAttack) :
                target == guardButton ? current.FindAction(CombatActionKind.Guard) : null;
            slotDetails.text = quote == null ? string.Empty : quote.ActionName + " · " +
                CombatActionPreviewView.FormatActionPoints(quote.ActionPointCostUnits) + " AP / " + quote.SpiritCost +
                " MP · CD " + quote.RemainingCooldownTicks + (string.IsNullOrEmpty(quote.Reason) ? "" : " · " + quote.Reason);
            slotDetails.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(slotDetails.text));
        }

        private void SetIcon(Button button, Sprite sprite)
        {
            Image icon = button == null ? null : button.transform.Find("ActionIcon")?.GetComponent<Image>();
            if (icon != null) icon.sprite = sprite ?? defaultAbilityIcon;
        }

        internal static void PresentCooldown(Button button, int ticks)
        {
            if (button == null) return;
            Text label = button.transform.Find("Cooldown")?.GetComponent<Text>();
            if (label != null) label.text = ticks > 0 ? ticks + " tick" : string.Empty;
            button.transform.Find("CooldownShade")?.gameObject.SetActive(ticks > 0);
            button.transform.Find("ActionIcon")?.gameObject.SetActive(ticks <= 0);
        }

        private static void SetEnabled(Button button, bool enabled)
        {
            if (button == null) return;
            button.interactable = enabled;
            Image icon = button.transform.Find("ActionIcon")?.GetComponent<Image>();
            if (icon != null) { Color tint = icon.color; tint.a = enabled ? 1 : .45f; icon.color = tint; }
            Text label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = enabled ? new Color32(239, 233, 211, 255) : new Color32(164, 181, 179, 255);
        }
    }
}
