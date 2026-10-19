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
        [SerializeField] private Button basicAttackButton;
        [SerializeField] private Button artButton;
        [SerializeField] private Button divineButton;
        [SerializeField] private Button guardButton;
        [SerializeField] private Button waitButton;
        [SerializeField] private Text artLabel;
        [SerializeField] private Text divineLabel;
        [SerializeField] private Button secondArtButton;
        [SerializeField] private Text secondArtLabel;
        [SerializeField] private Text actionStatusLabel;
        [SerializeField] private Text waitLabel;
        private Func<string, string> displayReason = value => value;
        [SerializeField] private Button moveButton;
        [SerializeField] private Button confirmMoveButton;
        [SerializeField] private Button cancelMoveButton;
        [SerializeField] private Text movementLabel;
        [SerializeField] private Button swapSpellButton;
        public Button SwapSpellButton => swapSpellButton;
        [SerializeField] private Button swapEquipmentButton;
        public Button SwapEquipmentButton => swapEquipmentButton;
        [SerializeField] private Button useItemButton;
        public Button UseItemButton => useItemButton;
        public Button MoveButton => moveButton;
        public Button ConfirmMoveButton => confirmMoveButton;
        public Button CancelMoveButton => cancelMoveButton;

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
        public Button GuardButton => guardButton;
        public Button WaitButton => waitButton;

        public void Present(bool visible, string artProfileId, string divineProfileId)
        {
            root?.SetActive(visible);
            SetEnabled(secondArtButton, false);
            SetEnabled(swapSpellButton, false);
            SetEnabled(swapEquipmentButton, false);
            SetEnabled(useItemButton, false);
            if (secondArtLabel != null) secondArtLabel.text = "术法";
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
                swapSpellButton.GetComponentInChildren<Text>().text = "换术\n剩余 " + (swap?.CombatSwapsRemaining ?? 0) + " 次";
            string equipmentReason = PresentAction(snapshot?.FindPreferredAction(CombatActionKind.SwapEquipment), swapEquipmentButton, canAct);
            string itemReason = PresentAction(snapshot?.FindPreferredAction(CombatActionKind.UseItem), useItemButton, canAct);
            string basicReason = PresentAction(snapshot?.FindPreferredAction(CombatActionKind.BasicAttack), basicAttackButton, canAct);
            string guardReason = PresentAction(snapshot?.FindAction(CombatActionKind.Guard), guardButton, canAct);
            string waitReason = PresentAction(snapshot?.FindAction(snapshot.CanEndTurn ? CombatActionKind.EndTurn : CombatActionKind.Wait),
                waitButton, canAct);
            string divineId = snapshot != null && snapshot.DivineProfileIds.Count > 0 ? snapshot.DivineProfileIds[0] : null;
            if (divineButton != null) divineButton.gameObject.SetActive(!string.IsNullOrWhiteSpace(divineId));
            string divineReason = PresentAction(snapshot?.FindPreferredAction(CombatActionKind.Divine, divineId), divineButton, canAct);

            CombatArtActionSnapshot first = snapshot != null && snapshot.ArtActions.Count > 0 ? snapshot.ArtActions[0] : null;
            CombatArtActionSnapshot second = snapshot != null && snapshot.ArtActions.Count > 1 ? snapshot.ArtActions[1] : null;
            string firstReason = PresentArt(first, artButton, artLabel, canAct);
            string secondReason = PresentArt(second, secondArtButton, secondArtLabel, canAct);
            if (actionStatusLabel != null)
            {
                // The formal 192x76 status area also shows unarmed/equipment rejection reasons.
                // 20pt overflowed at both target resolutions; keep the complete text at 16pt.
                actionStatusLabel.fontSize = 16;
                actionStatusLabel.text = snapshot == null ? string.Empty :
                    (snapshot.IsPresenting ? "演出中，请稍候" : !visible ? snapshot.TurnText :
                    string.Join("；", Array.FindAll(new[] { basicReason, guardReason, waitReason, divineReason, firstReason, secondReason, swapReason, equipmentReason, itemReason },
                        reason => !string.IsNullOrEmpty(reason)).Distinct()));
            }
        }

        private string PresentArt(CombatArtActionSnapshot art, Button button, Text label, bool canAct)
        {
            CombatArtTargetSnapshot target = art?.Targets.FirstOrDefault(option => option.CanExecute) ??
                art?.Targets.FirstOrDefault();
            SetEnabled(button, canAct && target?.CanExecute == true);
            if (label != null)
            {
                label.text = art == null ? "术法" : art.DisplayName + "\n" +
                    (target?.Quote == null ? string.Empty : CombatActionPreviewView.FormatActionPoints(target.Quote.ActionPointCostUnits) +
                    "AP / " + target.Quote.SpiritCost + "MP") +
                    (target?.Quote?.RemainingCooldownTicks > 0 ? " · CD" + target.Quote.RemainingCooldownTicks : string.Empty);
            }
            if (art == null) return string.Empty;
            if (target == null) return art.DisplayName + "：无可选目标";
            return target.CanExecute || string.IsNullOrEmpty(target.RejectionReason)
                ? string.Empty
                : displayReason(target.RejectionReason);
        }

        private string PresentAction(CombatActionPreviewSnapshot quote, Button button, bool canAct)
        {
            SetEnabled(button, canAct && quote != null && quote.CanConfirm);
            if (quote == null) return string.Empty;
            Text label = button == null ? null : button.GetComponentInChildren<Text>();
            if (label != null)
            {
                string cost = CombatActionPreviewView.FormatActionPoints(quote.ActionPointCostUnits) + "AP" +
                    (quote.SpiritCost > 0 ? " / " + quote.SpiritCost + "MP" : string.Empty);
                label.text = quote.Candidate.Kind switch
                {
                    CombatActionKind.SwapEquipment when quote.EquipmentOwned.HasValue => quote.ActionName + "\n" +
                        (quote.EquipmentOwned.Value ? "×1 " : "×0 ") + cost,
                    CombatActionKind.UseItem => quote.ActionName + " ×" + quote.ItemAvailableQuantity + "\n" + cost,
                    CombatActionKind.EndTurn or CombatActionKind.Wait => quote.ActionName,
                    CombatActionKind.Guard => quote.ActionName + cost,
                    _ => quote.ActionName + "\n" + cost,
                };
                // Guard (82x36), item (90x60), and equipment (82x60) overflow at 20pt.
                // The equipment ownership/cost line measured 83px and wrapped to 87px high.
                if (quote.Candidate.Kind is CombatActionKind.Guard or CombatActionKind.UseItem or CombatActionKind.SwapEquipment)
                    label.fontSize = 18;
            }
            return quote.CanConfirm ? string.Empty : quote.Reason;
        }

        public void PresentMovement(CombatHudSnapshot snapshot, bool selecting, CombatMovementOption selected, CombatMovementOption preview = null)
        {
            bool canInput = snapshot != null && snapshot.AcceptsCommands && !snapshot.IsPresenting;
            SetEnabled(moveButton, canInput && snapshot.Movement != null && snapshot.Movement.Options.Count > 0);
            SetEnabled(confirmMoveButton, canInput && selecting && selected != null);
            SetEnabled(cancelMoveButton, canInput && selecting);
            if (confirmMoveButton != null) confirmMoveButton.gameObject.SetActive(selecting);
            if (cancelMoveButton != null) cancelMoveButton.gameObject.SetActive(selecting);
            CombatMovementOption shown = preview ?? selected;
            if (movementLabel != null) movementLabel.text = snapshot?.Movement == null ? string.Empty :
                "累计 CT +" + snapshot.PendingCtPenalty +
                (shown == null ? (selecting ? "\n选择目的地" : string.Empty) : "\n消耗 " +
                CombatActionPreviewView.FormatActionPoints(shown.Cost) + " AP" + (selected == null ? " · 点击选定" : " · 确认后移动"));
        }

        private static void SetEnabled(Button button, bool enabled)
        {
            if (button == null) return;
            button.interactable = enabled;
            if (button.image != null) button.image.color = new Color(1, 1, 1, enabled ? 1 : 0.65f);
            Text label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = enabled ? new Color32(244, 234, 212, 255) : new Color32(177, 191, 187, 255);
        }
    }
}
