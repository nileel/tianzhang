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

        public void Configure(ICombatCommandHandler commandHandler, CombatActionBarView bar)
        {
            handler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
            actionBar = bar;
            Bind(actionBar?.BasicAttackButton, () => { if (CanAct) handler.RequestBasicAttack(actorId, targetId); });
            Bind(actionBar?.ArtButton, () => RequestArt(0));
            Bind(actionBar?.SecondArtButton, () => RequestArt(1));
            Bind(actionBar?.SecondArtTargetButton, SelectNextSecondArtTarget);
            Bind(actionBar?.DivineButton, () => { if (CanAct) handler.RequestDivine(actorId, targetId, divineProfileId); });
            Bind(actionBar?.GuardButton, () => { if (CanAct) handler.RequestGuard(actorId); });
            Bind(actionBar?.WaitButton, () =>
            {
                if (!acceptsCommands) return;
                if (snapshot != null && snapshot.CanEndTurn) handler.RequestEndTurn(actorId);
                else handler.RequestWait(actorId);
            });
        }

        public void SetContext(
            string nextActorId,
            string nextTargetId,
            string nextArtProfileId,
            string nextDivineProfileId,
            bool acceptsCommands)
        {
            snapshot = null;
            firstArtTargetId = secondArtTargetId = null;
            firstArtSelectionProfileId = secondArtSelectionProfileId = null;
            this.acceptsCommands = acceptsCommands;
            actorId = nextActorId;
            targetId = nextTargetId;
            artProfileId = nextArtProfileId;
            divineProfileId = nextDivineProfileId;
            actionBar?.Present(acceptsCommands, artProfileId, divineProfileId);
        }

        public void SetContext(CombatHudSnapshot nextSnapshot)
        {
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
        }

        public void ClearContext() => SetContext(null, null, null, null, false);

        private bool CanAct => acceptsCommands && (snapshot == null || !snapshot.CanEndTurn);

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
            if (snapshot == null)
            {
                if (index == 0 && !string.IsNullOrWhiteSpace(artProfileId))
                    handler.RequestArt(actorId, targetId, artProfileId);
                return;
            }
            CombatArtActionSnapshot art = GetArt(index);
            if (art == null) return;
            string selected = index == 0 ? firstArtTargetId : secondArtTargetId;
            foreach (CombatArtTargetSnapshot target in art.Targets)
            {
                if (target != null && target.CanExecute && string.Equals(target.Id, selected, StringComparison.Ordinal))
                {
                    handler.RequestArt(actorId, target.Id, art.ProfileId);
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
        }

        private static void Bind(Button button, Action action)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action());
        }
    }
}
