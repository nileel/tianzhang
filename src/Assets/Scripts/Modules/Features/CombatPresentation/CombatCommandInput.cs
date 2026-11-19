using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TianZhang.Features.CombatPresentation
{
    public sealed class CombatCommandInput : MonoBehaviour
    {
        [SerializeField] private CombatActionBarView actionBar;
        [SerializeField] private CombatTargetInput targetInput;
        private ICombatCommandHandler handler;
        private ICombatActionPreviewHandler previewHandler;
        private CombatActionPreviewView previewView;
        private CombatTimelineView timeline;
        private Static3DCombatUnitPresentationProvider unitProvider;
        public void ConfigureTargetPreview(Static3DCombatUnitPresentationProvider provider) => unitProvider = provider;
        public CombatActionPreviewSnapshot Preview { get; private set; }
        public CombatActionCandidate Candidate => Preview?.Candidate;
        public bool IsSelectingAttack { get; private set; }
        private bool attackLocked;
        private bool showingInvalidation;
        public bool IsTargetLocked => attackLocked || movementView?.IsLocked == true;
        public bool HasPendingSelection => Preview != null || movementView?.IsSelecting == true || showingInvalidation;
        private CombatUnitPresentationHex? pointedCell;

        public void ConfigurePreview(ICombatActionPreviewHandler previews, CombatActionPreviewView view, CombatTimelineView axis,
            Action onConfirm = null, Action onCancel = null)
        {
            previewHandler = previews ?? throw new ArgumentNullException(nameof(previews));
            previewView = view;
            timeline = axis;
            view.ConfirmButton.onClick.RemoveAllListeners();
            view.ConfirmButton.onClick.AddListener(() => (onConfirm ?? ConfirmCandidate)());
            view.CancelButton.onClick.RemoveAllListeners();
            view.CancelButton.onClick.AddListener(() => (onCancel ?? CancelSelectionLayer)());
            Bind(view.SwapSlotButton, SelectNextSwapSlot);
            Bind(view.SwapCandidateButton, SelectNextSwapCandidate);
        }

        public void CancelCandidate()
        {
            IsSelectingAttack = false;
            attackLocked = false;
            showingInvalidation = false;
            selectingArt = null;
            pointedCell = null;
            Preview = null;
            previewView?.Present(null);
            timeline?.PresentPrediction(null);
            movementView?.PresentAttackArea(null);
            unitProvider?.PresentTargetPreview(null);
        }

        public void ConfirmCandidate()
        {
            if (movementView?.IsSelecting == true) { movementView.Confirm(); return; }
            if (IsSelectingAttack && !attackLocked) return;
            CommitCandidate();
        }

        public void CancelSelectionLayer()
        {
            if (movementView?.IsSelecting == true) { movementView.CancelSelectionLayer(); return; }
            if (attackLocked)
            {
                attackLocked = false;
                PresentPointerPrompt("指向预览，左键锁定", actionName: selectingArt?.DisplayName ?? "普攻",
                    minimumRange: selectingArt?.MinimumRange ?? 0, maximumRange: selectingArt?.MaximumRange ?? 0);
                return;
            }
            CancelCandidate();
        }

        private void CommitCandidate()
        {
            if (!isActiveAndEnabled || !CanAct || Preview == null || !Preview.CanConfirm) return;
            CombatActionCandidate selected = Candidate;
            CancelCandidate();
            previewHandler.ConfirmAction(selected);
        }

        private void Choose(CombatActionKind kind, string target = null, string profile = null, int slot = -1, int? direction = null)
        {
            if (!CanAct || snapshot == null || previewHandler == null) return;
            CancelCandidate();
            movementView?.Cancel();
            Preview = previewHandler.PreviewAction(new CombatActionCandidate(kind, actorId, target, profile,
                snapshot.CommandContextVersion, slotIndex: slot, attackDirection: direction));
            previewView?.Present(Preview, CurrentActionPoints, snapshot);
            timeline?.PresentPrediction(Preview);
            movementView?.PresentAttackArea(Preview?.AttackArea);
        }

        private void BeginSelection(CombatActionKind kind, CombatArtActionSnapshot art = null)
        {
            if (!CanAct || previewHandler == null || movementView == null || targetInput == null) return;
            CancelCandidate();
            movementView.Cancel();
            IsSelectingAttack = true;
            selectingArt = art;
            var selection = new CombatActionCandidate(kind, actorId, null, art?.ProfileId, snapshot.CommandContextVersion);
            PresentPointerPrompt(art == null
                    ? "指向预览，左键锁定，右键或 Esc 取消"
                    : !string.IsNullOrEmpty(art.EntryQuote?.Reason) ? art.EntryQuote.Reason : "左键锁定可施放地块，右键或 Esc 取消",
                selection, art?.DisplayName ?? "普攻", art?.MinimumRange ?? 0, art?.MaximumRange ?? 0);
        }

        private void PresentPointerPrompt(string reason, CombatActionCandidate selection = null, string actionName = null,
            int minimumRange = 0, int maximumRange = 0)
        {
            selection ??= Candidate;
            if (actionName == null) actionName = Preview?.ActionName ?? "攻击";
            pointedCell = null;
            CombatActionPreviewSnapshot price = selectingArt?.EntryQuote ?? snapshot.FindPreferredAction(selection.Kind, selection.ProfileId);
            bool fixedPrice = price != null && (selectingArt == null || selectingArt.Targets.All(target =>
                target.SpiritCost == price.SpiritCost && (target.Quote == null || target.Quote.ActionPointCostUnits == price.ActionPointCostUnits)));
            Preview = new CombatActionPreviewSnapshot(new CombatActionCandidate(selection.Kind,
                actorId, null, selection.ProfileId, snapshot.CommandContextVersion), actionName, "当前角色", "待选目标", false,
                reason, price?.SpiritCost ?? 0, false, 0, 0, 0, price?.ActionPointCostUnits ?? 0,
                minimumRange: minimumRange, maximumRange: maximumRange);
            previewView?.Present(Preview, CurrentActionPoints, snapshot, selectingTarget: true, priceKnown: fixedPrice);
            timeline?.PresentPrediction(null);
            PresentSelectionHighlights();
        }

        private void PresentSelectionHighlights()
        {
            if (movementView == null) return;
            IReadOnlyList<CombatUnitPresentationHex> selectableCells = selectingArt?.CastableCells ??
                snapshot.Actions.Where(action => action.Candidate.Kind == CombatActionKind.BasicAttack && action.AttackArea != null)
                    .SelectMany(action => action.AttackArea.Cells).Where(cell => cell.IsLegal)
                    .Select(cell => cell.Position).Distinct().ToArray();
            CombatAttackAreaSnapshot area = Preview?.AttackArea;
            if (Candidate?.TargetCell != null && area != null &&
                !area.Cells.Any(cell => cell.IsLegal && cell.Position.Equals(Candidate.TargetCell.Value))) area = null;
            movementView.PresentAttackArea(area, selectableCells: selectableCells);
            unitProvider?.PresentTargetPreview(Preview?.CanConfirm == true && area != null ? area.Targets : null);
        }

        private int CurrentActionPoints => snapshot?.Movement?.RemainingPoints ?? -1;

        private void LateUpdate()
        {
            Mouse mouse = Mouse.current;
            if (HasPendingSelection && (mouse?.rightButton.wasPressedThisFrame == true || Keyboard.current?.escapeKey.wasPressedThisFrame == true))
            { CancelSelectionLayer(); return; }
            if (!IsSelectingAttack) return;
            if (!CanAct || snapshot?.Movement == null) { CancelCandidate(); return; }
            if (attackLocked) return;
            if (mouse == null || !TryPickAttackCell(mouse.position.ReadValue(), out var cell))
            {
                if (pointedCell.HasValue)
                    PresentPointerPrompt(selectingArt == null
                        ? "请指向棋盘内的地块，左键锁定"
                        : "请指向可施放地块，左键锁定", null, selectingArt?.DisplayName,
                        selectingArt?.MinimumRange ?? 0, selectingArt?.MaximumRange ?? 0);
                return;
            }
            // Freeze the authoritative quote on the locking click, even if the pointer has not moved.
            if (!pointedCell.HasValue || !pointedCell.Value.Equals(cell) || mouse.leftButton.wasPressedThisFrame)
            {
                pointedCell = cell;
                bool basic = selectingArt == null;
                CombatUnitPresentationHex origin = snapshot.Movement.Origin;
                int q = cell.Q - origin.Q, r = cell.R - origin.R;
                if (basic && q == 0 && r == 0)
                {
                    PresentPointerPrompt("请选择其他地块，不能攻击自身", new CombatActionCandidate(
                        CombatActionKind.BasicAttack, actorId, null, null, snapshot.CommandContextVersion), "普攻");
                    return;
                }
                // Same axial six-direction order as the board; choose the nearest facing to the pointed cell.
                float angle = Mathf.Atan2(-0.8660254f * r, q + .5f * r) * Mathf.Rad2Deg;
                int direction = (Mathf.FloorToInt((angle + 30f) / 60f) % 6 + 6) % 6;
                bool directional = basic && snapshot.FindAction(CombatActionKind.BasicAttack)?.AttackArea != null;
                Preview = previewHandler.PreviewAction(new CombatActionCandidate(
                    basic ? CombatActionKind.BasicAttack : CombatActionKind.Art, actorId, null,
                    basic ? null : selectingArt.ProfileId, snapshot.CommandContextVersion,
                    attackDirection: directional ? direction : (int?)null, targetCell: cell));
                attackLocked = mouse.leftButton.wasPressedThisFrame && Preview?.CanConfirm == true;
                previewView?.Present(Preview, CurrentActionPoints, snapshot, selectingTarget: !attackLocked);
                timeline?.PresentPrediction(Preview);
                PresentSelectionHighlights();
            }
        }

        private void OnDisable() => ClearContext();

        private bool TryPickAttackCell(Vector2 screenPoint, out CombatUnitPresentationHex cell)
        {
            cell = default;
            if (movementView.UiOwnsPointer(screenPoint)) return false;
            if (targetInput.TryPickBodyPoint(screenPoint, out Vector3 bodyPoint))
                return movementView.TryGetCellAtWorld(bodyPoint, out cell);
            return movementView.TryPickCell(screenPoint, out cell);
        }
        private CombatMovementView movementView;
        private string actorId;
        private string targetId;
        private string artProfileId;
        private string divineProfileId;
        private bool acceptsCommands;
        private CombatHudSnapshot snapshot;
        private CombatArtActionSnapshot selectingArt;
        public event Action SelectionStarted;

        public void Configure(ICombatCommandHandler commandHandler, CombatActionBarView bar, CombatMovementView movement = null)
        {
            handler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
            actionBar = bar;
            movementView = movement;
            if (movementView != null) movementView.SelectionStarted = () => { CancelCandidate(); SelectionStarted?.Invoke(); };
            Bind(actionBar?.MoveButton, () => movementView?.BeginSelection());
            Bind(actionBar?.BasicAttackButton, () => BeginSelection(CombatActionKind.BasicAttack));
            Bind(actionBar?.ArtButton, () => RequestArt(0));
            Bind(actionBar?.SecondArtButton, () => RequestArt(1));
            Bind(actionBar?.ThirdArtButton, () => RequestArt(2));
            Bind(actionBar?.FourthArtButton, () => RequestArt(3));
            Bind(actionBar?.FifthArtButton, () => RequestArt(4));
            Bind(actionBar?.SecondDivineButton, () =>
            {
                string profile = snapshot?.DivineProfileIds.Count > 1 ? snapshot.DivineProfileIds[1] : null;
                var choice = profile == null ? null : snapshot.FindPreferredAction(CombatActionKind.Divine, profile)?.Candidate;
                if (choice != null) Choose(CombatActionKind.Divine, choice.TargetId, choice.ProfileId);
            });
            Bind(actionBar?.DivineButton, () =>
            {
                var choice = snapshot?.FindPreferredAction(CombatActionKind.Divine, divineProfileId)?.Candidate;
                if (choice != null) Choose(CombatActionKind.Divine, choice.TargetId, choice.ProfileId);
            });
            Bind(actionBar?.GuardButton, () => { if (CanAct) Choose(CombatActionKind.Guard); });
            Bind(actionBar?.SwapSpellButton, BeginSwapSpell);
            Bind(actionBar?.UseItemButton, () =>
            {
                if (!CanAct) return;
                var choice = snapshot.FindPreferredAction(CombatActionKind.UseItem)?.Candidate;
                if (choice == null) return;
                CancelCandidate();
                movementView?.Cancel();
                Preview = previewHandler.PreviewAction(choice);
                previewView?.Present(Preview, CurrentActionPoints);
                timeline?.PresentPrediction(Preview);
            });
            Bind(actionBar?.SwapEquipmentButton, () =>
            {
                if (!CanAct) return;
                var choice = snapshot.FindPreferredAction(CombatActionKind.SwapEquipment)?.Candidate;
                if (choice == null) return;
                CancelCandidate();
                movementView?.Cancel();
                Preview = previewHandler.PreviewAction(choice);
                previewView?.Present(Preview, CurrentActionPoints);
                timeline?.PresentPrediction(Preview);
            });
            Bind(actionBar?.WaitButton, () =>
            {
                if (!acceptsCommands) return;
                if (snapshot != null && snapshot.CanEndTurn) Choose(CombatActionKind.EndTurn);
                else Choose(CombatActionKind.Wait);
            });
        }

        public void SetContext(
            string nextActorId,
            string nextTargetId,
            string nextArtProfileId,
            string nextDivineProfileId,
            bool acceptsCommands)
        {
            CancelCandidate();
            snapshot = null;
            this.acceptsCommands = acceptsCommands;
            actorId = nextActorId;
            targetId = nextTargetId;
            artProfileId = nextArtProfileId;
            divineProfileId = nextDivineProfileId;
            actionBar?.Present(acceptsCommands, artProfileId, divineProfileId);
            movementView?.Present(null);
        }

        public void SetContext(CombatHudSnapshot nextSnapshot)
        {
            bool invalidated = nextSnapshot != null && (showingInvalidation || HasPendingSelection && snapshot != null &&
                snapshot.CommandContextVersion != nextSnapshot.CommandContextVersion);
            CancelCandidate();
            snapshot = nextSnapshot;
            acceptsCommands = snapshot != null && snapshot.AcceptsCommands;
            actorId = snapshot?.Player?.Id;
            targetId = snapshot?.Enemy?.Id;
            divineProfileId = snapshot != null && snapshot.DivineProfileIds.Count > 0 ? snapshot.DivineProfileIds[0] : null;
            actionBar?.Present(snapshot);
            movementView?.Present(nextSnapshot);
            if (invalidated) ShowInvalidated();
        }

        public void ShowInvalidated()
        {
            showingInvalidation = true;
            previewView?.PresentInvalidated();
        }

        public void ClearContext()
        {
            // Clearing authority on exit must not redraw a Canvas that may already be destroyed.
            acceptsCommands = false;
            snapshot = null;
            CancelCandidate();
            movementView?.Present(null);
        }

        private bool CanAct => acceptsCommands && snapshot != null && !snapshot.IsPresenting;

        private void BeginSwapSpell()
        {
            if (!CanAct) return;
            var options = snapshot.Actions.Where(action => action.Candidate.Kind == CombatActionKind.SwapSpell).ToArray();
            CombatActionCandidate choice = (options.FirstOrDefault(action => action.CanConfirm) ??
                options.FirstOrDefault(action => !snapshot.ArtProfileIds.Contains(action.Candidate.ProfileId)) ??
                options.FirstOrDefault())?.Candidate;
            if (choice != null) Choose(CombatActionKind.SwapSpell, profile: choice.ProfileId, slot: choice.SlotIndex);
        }

        private void SelectNextSwapSlot()
        {
            if (!CanAct || Candidate?.Kind != CombatActionKind.SwapSpell) return;
            int[] slots = snapshot.Actions.Where(action => action.Candidate.Kind == CombatActionKind.SwapSpell)
                .Select(action => action.Candidate.SlotIndex).Distinct().ToArray();
            if (slots.Length == 0) return;
            int next = (Array.IndexOf(slots, Candidate.SlotIndex) + 1) % slots.Length;
            Choose(CombatActionKind.SwapSpell, profile: Candidate.ProfileId, slot: slots[next]);
        }

        private void SelectNextSwapCandidate()
        {
            if (!CanAct || Candidate?.Kind != CombatActionKind.SwapSpell) return;
            string[] profiles = snapshot.Actions.Where(action => action.Candidate.Kind == CombatActionKind.SwapSpell &&
                    !snapshot.ArtProfileIds.Contains(action.Candidate.ProfileId))
                .Select(action => action.Candidate.ProfileId).Distinct().ToArray();
            if (profiles.Length == 0) return;
            int next = (Array.IndexOf(profiles, Candidate.ProfileId) + 1) % profiles.Length;
            Choose(CombatActionKind.SwapSpell, profile: profiles[next], slot: Candidate.SlotIndex);
        }

        private CombatArtActionSnapshot GetArt(int index) =>
            snapshot != null && snapshot.ArtActions.Count > index ? snapshot.ArtActions[index] : null;

        private void RequestArt(int index)
        {
            if (!CanAct || snapshot == null) return;
            CombatArtActionSnapshot art = GetArt(index);
            if (art?.EntryQuote?.CanOpen != true) return;
            // The skill button only opens targeting; every legal target still needs its own pointed click.
            BeginSelection(CombatActionKind.Art, art);
        }

        private void Bind(Button button, Action action)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                if (isActiveAndEnabled && acceptsCommands && button.gameObject.activeInHierarchy && button.IsInteractable())
                { SelectionStarted?.Invoke(); action(); }
            });
        }
    }
}
