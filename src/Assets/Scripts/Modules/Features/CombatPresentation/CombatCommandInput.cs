using System;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.CombatPresentation
{
    public sealed class CombatCommandInput : MonoBehaviour
    {
        [SerializeField] private CombatActionBarView actionBar;
        private ICombatCommandHandler handler;
        private ICombatActionPreviewHandler previewHandler;
        private CombatActionPreviewView previewView;
        private CombatTimelineView timeline;
        public CombatActionPreviewSnapshot Preview { get; private set; }
        public CombatActionCandidate Candidate => Preview?.Candidate;

        public void ConfigurePreview(ICombatActionPreviewHandler previews, CombatActionPreviewView view, CombatTimelineView axis)
        {
            previewHandler = previews ?? throw new ArgumentNullException(nameof(previews));
            previewView = view;
            timeline = axis;
            Bind(view.ConfirmButton, ConfirmCandidate);
            Bind(view.CancelButton, CancelCandidate);
        }

        public void CancelCandidate()
        {
            Preview = null;
            previewView?.Present(null);
            timeline?.PresentPrediction(null);
        }

        public void ConfirmCandidate()
        {
            if (!isActiveAndEnabled || Preview == null || !Preview.CanConfirm) return;
            CombatActionCandidate selected = Candidate;
            CancelCandidate();
            previewHandler.ConfirmAction(selected);
        }

        private void Choose(CombatActionKind kind, string target = null, string profile = null)
        {
            if (!acceptsCommands || snapshot == null || previewHandler == null) return;
            movementView?.Cancel();
            Preview = previewHandler.PreviewAction(new CombatActionCandidate(kind, actorId, target, profile,
                snapshot.CommandContextVersion));
            previewView?.Present(Preview);
            timeline?.PresentPrediction(Preview);
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
            Bind(actionBar?.BasicAttackButton, () => { if (CanAct) Choose(CombatActionKind.BasicAttack, targetId); });
            Bind(actionBar?.ArtButton, () => RequestArt(0));
            Bind(actionBar?.SecondArtButton, () => RequestArt(1));
            Bind(actionBar?.SecondArtTargetButton, SelectNextSecondArtTarget);
            Bind(actionBar?.DivineButton, () => { if (CanAct) Choose(CombatActionKind.Divine, targetId, divineProfileId); });
            Bind(actionBar?.GuardButton, () => { if (CanAct) Choose(CombatActionKind.Guard); });
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

        private bool CanAct => acceptsCommands;

        private CombatArtActionSnapshot GetArt(int index) =>
            snapshot != null && snapshot.ArtActions.Count > index ? snapshot.ArtActions[index] : null;

        private static string KeepTarget(CombatArtActionSnapshot art, string priorProfileId, string priorTargetId)
        {
            if (art == null) return null;
            if (string.Equals(art.ProfileId, priorProfileId, StringComparison.Ordinal))
            {
                foreach (CombatArtTargetSnapshot target in art.Targets)
                {
                    if (target != null && string.Equals(target.Id, priorTargetId, StringComparison.Ordinal))
                        return target.Id;
                }
            }
            return art.Targets.Count > 0 ? art.Targets[0].Id : null;
        }

        private void RequestArt(int index)
        {
            if (!CanAct) return;
            if (snapshot == null) return;
            CombatArtActionSnapshot art = GetArt(index);
            if (art == null) return;
            string selected = index == 0 ? firstArtTargetId : secondArtTargetId;
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
            if (!CanAct || art == null || art.Targets.Count < 2) return;
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
                Choose(CombatActionKind.Art, secondArtTargetId, art.ProfileId);
        }

        private void Bind(Button button, Action action)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => { if (isActiveAndEnabled && acceptsCommands) action(); });
        }
    }
}
