using System;
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
        public CombatActionPreviewSnapshot Preview { get; private set; }
        public CombatActionCandidate Candidate => Preview?.Candidate;
        public bool IsSelectingAttack { get; private set; }
        private CombatUnitPresentationHex? pointedCell;

        public void ConfigurePreview(ICombatActionPreviewHandler previews, CombatActionPreviewView view, CombatTimelineView axis)
        {
            previewHandler = previews ?? throw new ArgumentNullException(nameof(previews));
            previewView = view;
            timeline = axis;
            Bind(view.ConfirmButton, ConfirmCandidate);
            Bind(view.CancelButton, CancelCandidate);
            Bind(view.SwapSlotButton, SelectNextSwapSlot);
            Bind(view.SwapCandidateButton, SelectNextSwapCandidate);
        }

        public void CancelCandidate()
        {
            IsSelectingAttack = false;
            selectingArt = null;
            pointedCell = null;
            Preview = null;
            previewView?.Present(null);
            timeline?.PresentPrediction(null);
            movementView?.PresentAttackArea(null);
        }

        public void ConfirmCandidate()
        {
            if (IsSelectingAttack) return;
            CommitCandidate();
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
            if (kind == CombatActionKind.Divine && Preview?.CanConfirm == true)
            {
                CommitCandidate();
                return;
            }
            previewView?.Present(Preview);
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
                    ? "鼠标指向地块预览，左键施行；右键或 Esc 取消"
                    : "左键点击施法范围内角色；右键或 Esc 取消",
                selection, art?.DisplayName ?? "普攻", art?.MinimumRange ?? 0, art?.MaximumRange ?? 0);
        }

        private void PresentPointerPrompt(string reason, CombatActionCandidate selection = null, string actionName = null,
            int minimumRange = 0, int maximumRange = 0)
        {
            selection ??= Candidate;
            if (actionName == null) actionName = Preview?.ActionName ?? "攻击";
            pointedCell = null;
            Preview = new CombatActionPreviewSnapshot(new CombatActionCandidate(selection.Kind,
                actorId, null, selection.ProfileId, snapshot.CommandContextVersion), actionName, "当前角色", "待选目标", false,
                reason, 0, false, 0, 0, 0, minimumRange: minimumRange, maximumRange: maximumRange);
            previewView?.Present(Preview);
            timeline?.PresentPrediction(null);
            PresentSelectionHighlights(null, null, false);
        }

        private void PresentSelectionHighlights(CombatAttackAreaSnapshot area, CombatUnitPresentationHex? cell, bool legal)
        {
            if (movementView == null) return;
            if (selectingArt != null && snapshot?.Movement != null)
            {
                CombatUnitPresentationHex origin = snapshot.Movement.Origin;
                movementView.PresentCastingPreview(origin, selectingArt.MinimumRange, selectingArt.MaximumRange, area, cell, legal);
            }
            else movementView.PresentAttackArea(area, cell, legal);
        }

        private void LateUpdate()
        {
            if (!IsSelectingAttack) return;
            if (!CanAct || snapshot?.Movement == null) { CancelCandidate(); return; }
            Mouse mouse = Mouse.current;
            if (mouse?.rightButton.wasPressedThisFrame == true || Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            { CancelCandidate(); return; }
            if (mouse == null || !movementView.TryPickCell(mouse.position.ReadValue(), out var cell))
            {
                if (pointedCell.HasValue)
                    PresentPointerPrompt(selectingArt == null
                        ? "请指向棋盘内的地块，左键施行"
                        : "请指向施法范围内的角色，左键施行", null, selectingArt?.DisplayName,
                        selectingArt?.MinimumRange ?? 0, selectingArt?.MaximumRange ?? 0);
                return;
            }
            // Requote on the committing click even when the pointer has not moved.
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
                string target = targetInput.FindUnitAtCell(cell);
                Preview = previewHandler.PreviewAction(new CombatActionCandidate(
                    basic ? CombatActionKind.BasicAttack : CombatActionKind.Art, actorId, target,
                    basic ? null : selectingArt.ProfileId, snapshot.CommandContextVersion,
                    attackDirection: directional ? direction : (int?)null));
                previewView?.Present(Preview);
                timeline?.PresentPrediction(Preview);
                CombatUnitPresentationHex? effectCell = basic || target != null ? cell : (CombatUnitPresentationHex?)null;
                PresentSelectionHighlights(Preview?.AttackArea, effectCell, Preview?.CanConfirm == true);
            }
            if (mouse.leftButton.wasPressedThisFrame && Preview?.CanConfirm == true)
            {
                CommitCandidate();
            }
        }

        private void OnDisable() => ClearContext();
        private CombatMovementView movementView;
        private string actorId;
        private string targetId;
        private string artProfileId;
        private string divineProfileId;
        private bool acceptsCommands;
        private CombatHudSnapshot snapshot;
        private CombatArtActionSnapshot selectingArt;

        public void Configure(ICombatCommandHandler commandHandler, CombatActionBarView bar, CombatMovementView movement = null)
        {
            handler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
            actionBar = bar;
            movementView = movement;
            if (movementView != null) movementView.SelectionStarted = CancelCandidate;
            Bind(actionBar?.MoveButton, () => movementView?.BeginSelection());
            Bind(actionBar?.BasicAttackButton, () => BeginSelection(CombatActionKind.BasicAttack));
            Bind(actionBar?.ArtButton, () => RequestArt(0));
            Bind(actionBar?.SecondArtButton, () => RequestArt(1));
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
                previewView?.Present(Preview);
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
                previewView?.Present(Preview);
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
            CancelCandidate();
            snapshot = nextSnapshot;
            acceptsCommands = snapshot != null && snapshot.AcceptsCommands;
            actorId = snapshot?.Player?.Id;
            targetId = snapshot?.Enemy?.Id;
            divineProfileId = snapshot != null && snapshot.DivineProfileIds.Count > 0 ? snapshot.DivineProfileIds[0] : null;
            actionBar?.Present(snapshot);
            movementView?.Present(nextSnapshot);
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
            CombatActionCandidate choice = (options.FirstOrDefault(action => action.CanConfirm) ?? options.FirstOrDefault())?.Candidate;
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
            string[] profiles = snapshot.Actions.Where(action => action.Candidate.Kind == CombatActionKind.SwapSpell)
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
            if (art == null || !art.Targets.Any(target => target != null && target.CanExecute)) return;
            // The skill button only opens targeting; every legal target still needs its own pointed click.
            BeginSelection(CombatActionKind.Art, art);
        }

        private void Bind(Button button, Action action)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                if (isActiveAndEnabled && acceptsCommands && button.gameObject.activeInHierarchy && button.IsInteractable()) action();
            });
        }
    }
}
