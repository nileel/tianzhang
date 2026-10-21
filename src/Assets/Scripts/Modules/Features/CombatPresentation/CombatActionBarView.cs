using System;
using System.Linq;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.UI;

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
            bool visible = snapshot != null && snapshot.AcceptsCommands && !snapshot.IsPresenting;
            bool canAct = visible;
            root?.SetActive(snapshot != null);
            CombatActionPreviewSnapshot swap = snapshot?.FindPreferredAction(CombatActionKind.SwapSpell);
            SetEnabled(swapSpellButton, canAct && swap?.CanConfirm == true);
            string swapReason = swap?.CanConfirm == false ? swap.Reason : string.Empty;
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

            CombatArtActionSnapshot first = snapshot != null && snapshot.ArtActions.Count > 0 ? snapshot.ArtActions[0] : null;
            CombatArtActionSnapshot second = snapshot != null && snapshot.ArtActions.Count > 1 ? snapshot.ArtActions[1] : null;
            CombatArtActionSnapshot third = snapshot != null && snapshot.ArtActions.Count > 2 ? snapshot.ArtActions[2] : null;
            string firstReason = PresentArt(first, artButton, artLabel, canAct);
            string secondReason = PresentArt(second, secondArtButton, secondArtLabel, canAct);
            string thirdReason = PresentArt(third, thirdArtButton, thirdArtLabel, canAct);
            if (actionStatusLabel != null)
            {
                actionStatusLabel.fontSize = 22;
                actionStatusLabel.text = snapshot == null ? string.Empty :
                    (snapshot.IsPresenting ? "演出中，请稍候" : !visible ? snapshot.TurnText :
                    string.Join("；", Array.FindAll(new[] { basicReason, guardReason, waitReason, divineReason, firstReason, secondReason, thirdReason, swapReason, equipmentReason, itemReason },
                        reason => !string.IsNullOrEmpty(reason)).Distinct()));
                actionStatusLabel.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(actionStatusLabel.text));
            }
        }

        private string PresentArt(CombatArtActionSnapshot art, Button button, Text label, bool canAct)
        {
            CombatArtTargetSnapshot target = art?.Targets.FirstOrDefault(option => option.CanExecute) ??
                art?.Targets.FirstOrDefault();
            SetEnabled(button, canAct && target?.CanExecute == true);
            SetIcon(button, art?.ProfileId switch
            {
                "spell_guanshiji" => guanshijiIcon,
                "spell_julingshu" => julingshuIcon,
                "spell_feixingzhi" => feixingzhiIcon,
                _ => null,
            });
            PresentCooldown(button, target?.Quote?.RemainingCooldownTicks ?? 0);
            if (label != null)
                label.text = art == null ? "术法" : CompactName(art.DisplayName) + "\n" +
                    (target?.Quote == null ? string.Empty : CombatActionPreviewView.FormatActionPoints(target.Quote.ActionPointCostUnits) +
                    " AP\n" + target.Quote.SpiritCost + " MP");
            if (art == null) return string.Empty;
            if (target == null) return art.DisplayName + "：无可选目标";
            return target.CanExecute || string.IsNullOrEmpty(target.RejectionReason)
                ? string.Empty
                : displayReason(target.RejectionReason);
        }

        private string PresentAction(CombatActionPreviewSnapshot quote, Button button, bool canAct)
        {
            SetEnabled(button, canAct && quote != null && quote.CanConfirm);
            PresentCooldown(button, quote?.RemainingCooldownTicks ?? 0);
            if (quote == null) return string.Empty;
            if (quote.Candidate.Kind == CombatActionKind.BasicAttack)
                SetIcon(button, quote.BasicAttackSourceKind == "unarmed_body" ? unarmedIcon : attackIcon);
            Text label = button == null ? null : button.GetComponentInChildren<Text>();
            if (label != null)
            {
                string cost = CombatActionPreviewView.FormatActionPoints(quote.ActionPointCostUnits) + " AP" +
                    (quote.SpiritCost > 0 ? "\n" + quote.SpiritCost + " MP" : string.Empty);
                label.text = quote.Candidate.Kind switch
                {
                    CombatActionKind.SwapEquipment when quote.EquipmentOwned.HasValue => CompactName(quote.ActionName) + "\n" + cost + "\n" +
                        (quote.EquipmentOwned.Value ? "已持有" : "未持有"),
                    CombatActionKind.UseItem => CompactName(quote.ActionName) + " ×" + quote.ItemAvailableQuantity + "\n" + cost,
                    CombatActionKind.EndTurn or CombatActionKind.Wait => quote.ActionName,
                    CombatActionKind.Guard => quote.ActionName + "\n" + cost,
                    _ => CompactName(quote.ActionName) + "\n" + cost,
                };
            }
            return quote.CanConfirm ? string.Empty : quote.Reason;
        }

        public void PresentMovement(CombatHudSnapshot snapshot, bool selecting, CombatMovementOption preview = null)
        {
            bool canInput = snapshot != null && snapshot.AcceptsCommands && !snapshot.IsPresenting;
            SetEnabled(moveButton, canInput && snapshot.Movement != null && snapshot.Movement.Options.Count > 0);
            if (movementLabel != null) movementLabel.text = snapshot?.Movement == null ? string.Empty :
                (!canInput ? "暂不可移动" : selecting ? (preview == null ? "点击地格" :
                    "消耗 " + CombatActionPreviewView.FormatActionPoints(preview.Cost) + " AP") : "按路径报价") +
                "\nCT +" + snapshot.PendingCtPenalty;
        }

        private static string CompactName(string value) => value != null && value.Length > 10
            ? value.Substring(0, 9) + "…" : value;

        private static void SetIcon(Button button, Sprite sprite)
        {
            Image icon = button == null ? null : button.transform.Find("ActionIcon")?.GetComponent<Image>();
            if (icon != null && sprite != null) icon.sprite = sprite;
        }

        private static void PresentCooldown(Button button, int ticks)
        {
            if (button == null) return;
            Text label = button.transform.Find("Cooldown")?.GetComponent<Text>();
            if (label != null) label.text = ticks > 0 ? "CD " + ticks : string.Empty;
            button.transform.Find("CooldownShade")?.gameObject.SetActive(ticks > 0);
            button.transform.Find("ActionIcon")?.gameObject.SetActive(ticks <= 0);
        }

        private static void SetEnabled(Button button, bool enabled)
        {
            if (button == null) return;
            button.interactable = enabled;
            if (button.image != null) button.image.color = Color.white;
            Image icon = button.transform.Find("ActionIcon")?.GetComponent<Image>();
            if (icon != null) icon.color = new Color(1, 1, 1, enabled ? 1 : .5f);
            Text label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = enabled ? new Color32(239, 233, 211, 255) : new Color32(164, 181, 179, 255);
        }
    }
}
