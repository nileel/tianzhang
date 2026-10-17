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
            if (kind is CombatActionKind.Art or CombatActionKind.Divine && Preview?.CanConfirm == true)
            {
                CommitCandidate();
                return;
            }
            previewView?.Present(Preview);
            timeline?.PresentPrediction(Preview);
            movementView?.PresentAttackArea(Preview?.AttackArea);
        }

        private void BeginAttackSelection(CombatArtActionSnapshot art = null)
        {
            if (!CanAct || previewHandler == null || movementView == null || targetInput == null) return;
            CancelCandidate();
            movementView.Cancel();
            IsSelectingAttack = true;
            PresentPointerPrompt("鼠标指向地块预览，左键施行；右键或 Esc 取消",
                new CombatActionCandidate(art == null ? CombatActionKind.BasicAttack : CombatActionKind.Art,
                    actorId, null, art?.ProfileId, snapshot.CommandContextVersion), art?.DisplayName ?? "普攻");
        }

        private void PresentPointerPrompt(string reason, CombatActionCandidate selection = null, string actionName = null)
        {
            selection ??= Candidate;
            actionName ??= Preview.ActionName;
            pointedCell = null;
            Preview = new CombatActionPreviewSnapshot(new CombatActionCandidate(selection.Kind,
                actorId, null, selection.ProfileId, snapshot.CommandContextVersion), actionName, "当前角色", "地块", false,
                reason, 0, false, 0, 0, 0);
            previewView?.Present(Preview);
            timeline?.PresentPrediction(null);
            movementView.PresentAttackArea(null);
        }

        private void LateUpdate()
        {
            if (!IsSelectingAttack) return;
            if (!CanAct) { CancelCandidate(); return; }
            Mouse mouse = Mouse.current;
            if (mouse?.rightButton.wasPressedThisFrame == true || Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            { CancelCandidate(); return; }
            if (mouse == null || !movementView.TryPickCell(mouse.position.ReadValue(), out var cell))
            { if (pointedCell.HasValue) PresentPointerPrompt("请指向棋盘内的地块，左键施行"); return; }
            // Requote on the committing click even when the pointer has not moved.
            if (!pointedCell.HasValue || !pointedCell.Value.Equals(cell) || mouse.leftButton.wasPressedThisFrame)
            {
                pointedCell = cell;
                bool art = Candidate.Kind == CombatActionKind.Art;
                string profile = Candidate.ProfileId;
                CombatUnitPresentationHex origin = snapshot.Movement.Origin;
                int q = cell.Q - origin.Q, r = cell.R - origin.R;
                if (q == 0 && r == 0) { PresentPointerPrompt("请选择其他地块，不能攻击自身"); return; }
                // Same axial six-direction order as the board; choose the nearest facing to the pointed cell.
                float angle = Mathf.Atan2(-0.8660254f * r, q + .5f * r) * Mathf.Rad2Deg;
                int direction = (Mathf.FloorToInt((angle + 30f) / 60f) % 6 + 6) % 6;
                Preview = previewHandler.PreviewAction(new CombatActionCandidate(art ? CombatActionKind.Art : CombatActionKind.BasicAttack,
                    actorId, targetInput.FindEnemyAtCell(cell), profile, snapshot.CommandContextVersion, attackDirection:
                        !art && snapshot.FindAction(CombatActionKind.BasicAttack)?.AttackArea != null ? direction : (int?)null));
                previewView?.Present(Preview);
                timeline?.PresentPrediction(Preview);
                movementView.PresentAttackArea(Preview?.AttackArea, cell, Preview?.CanConfirm == true);
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
        private string firstArtTargetId;
        private string secondArtTargetId;
        private string firstArtSelectionProfileId;
        private string secondArtSelectionProfileId;

        public void Configure(ICombatCommandHandler commandHandler, CombatActionBarView bar, CombatMovementView movement = null)
        {
            handler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
            actionBar = bar;
            movementView = movement;
            if (movementView != null) movementView.SelectionStarted = CancelCandidate;
            Bind(actionBar?.MoveButton, () => movementView?.BeginSelection());
            Bind(actionBar?.ConfirmMoveButton, () => movementView?.Confirm());
            Bind(actionBar?.CancelMoveButton, () => movementView?.Cancel());
            Bind(actionBar?.BasicAttackButton, () => BeginAttackSelection());
            Bind(actionBar?.ArtButton, () => RequestArt(0));
            Bind(actionBar?.SecondArtButton, () => RequestArt(1));
            Bind(actionBar?.SecondArtTargetButton, SelectNextSecondArtTarget);
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
            firstArtTargetId = secondArtTargetId = null;
            firstArtSelectionProfileId = secondArtSelectionProfileId = null;
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
            CombatArtActionSnapshot first = GetArt(0);
            CombatArtActionSnapshot second = GetArt(1);
            firstArtTargetId = KeepTarget(first, firstArtSelectionProfileId, firstArtTargetId);
            secondArtTargetId = KeepTarget(second, secondArtSelectionProfileId, secondArtTargetId);
            firstArtSelectionProfileId = first?.ProfileId;
            secondArtSelectionProfileId = second?.ProfileId;
            actionBar?.Present(snapshot, firstArtTargetId, secondArtTargetId);
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

        private static string KeepTarget(CombatArtActionSnapshot art, string priorProfileId, string priorTargetId)
        {
            if (art == null) return null;
            CombatArtTargetSnapshot prior = string.Equals(art.ProfileId, priorProfileId, StringComparison.Ordinal)
                ? art.Targets.FirstOrDefault(target => target != null && string.Equals(target.Id, priorTargetId, StringComparison.Ordinal))
                : null;
            if (prior != null && prior.CanExecute) return prior.Id;
            // A default target that moved out of range must not hide another legal target.
            return (art.Targets.FirstOrDefault(target => target != null && target.CanExecute) ??
                prior ?? art.Targets.FirstOrDefault())?.Id;
        }

        private void RequestArt(int index)
        {
            if (!CanAct) return;
            if (snapshot == null) return;
            CombatArtActionSnapshot art = GetArt(index);
            if (art == null) return;
            if (art.ProfileId == "spell_guanshiji")
            {
                BeginAttackSelection(art);
                return;
            }
            string selected = index == 0 ? firstArtTargetId : secondArtTargetId;
            if (!art.Targets.Any(target => target.Id == selected && target.CanExecute))
            {
                selected = art.Targets.FirstOrDefault(target => target.CanExecute)?.Id;
                if (index == 0) firstArtTargetId = selected;
                else secondArtTargetId = selected;
                actionBar?.Present(snapshot, firstArtTargetId, secondArtTargetId);
            }
            foreach (CombatArtTargetSnapshot target in art.Targets)
            {
                if (target != null && string.Equals(target.Id, selected, StringComparison.Ordinal))
                {
                    Choose(CombatActionKind.Art, target.Id, art.ProfileId);
                    return;
                }
            }
        }

        private void SelectNextSecondArtTarget()
        {
            CombatArtActionSnapshot art = GetArt(1);
            if (!CanAct || art == null || art.ProfileId == "spell_guanshiji" || art.Targets.Count < 2) return;
            int selectedIndex = 0;
            for (int index = 0; index < art.Targets.Count; index++)
            {
                if (string.Equals(art.Targets[index].Id, secondArtTargetId, StringComparison.Ordinal))
                {
                    selectedIndex = index;
                    break;
                }
            }
            secondArtTargetId = art.Targets[(selectedIndex + 1) % art.Targets.Count].Id;
            actionBar?.Present(snapshot, firstArtTargetId, secondArtTargetId);
            if (Candidate?.Kind == CombatActionKind.Art && Candidate.ProfileId == art.ProfileId)
                CancelCandidate();
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
