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
        private CombatHudSnapshot snapshot;
        private CombatMovementView movementView;
        private string actorId;
        private string targetId;
        private string artProfileId;
        private string divineProfileId;

        public void Configure(ICombatCommandHandler commandHandler, CombatActionBarView bar, CombatMovementView movement = null)
        {
            handler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
            actionBar = bar;
            movementView = movement;
            Bind(actionBar?.MoveButton, () => movementView?.BeginSelection());
            Bind(actionBar?.ConfirmMoveButton, () => movementView?.Confirm());
            Bind(actionBar?.CancelMoveButton, () => movementView?.Cancel());
            Bind(actionBar?.BasicAttackButton, () => handler.RequestBasicAttack(actorId, targetId));
            Bind(actionBar?.ArtButton, () => handler.RequestArt(actorId, targetId, artProfileId));
            Bind(actionBar?.DivineButton, () => handler.RequestDivine(actorId, targetId, divineProfileId));
            Bind(actionBar?.GuardButton, () => handler.RequestGuard(actorId));
            Bind(actionBar?.WaitButton, () =>
            {
                if (snapshot != null && snapshot.CanEndTurn) handler.RequestEndTurn(actorId);
                else handler.RequestWait(actorId);
            });
        }

        public void SetContext(CombatHudSnapshot value)
        {
            snapshot = value;
            actorId = value?.Player?.Id;
            targetId = value?.Enemy?.Id;
            artProfileId = value != null && value.ArtProfileIds.Count > 0 ? value.ArtProfileIds[0] : null;
            divineProfileId = value != null && value.DivineProfileIds.Count > 0 ? value.DivineProfileIds[0] : null;
            actionBar?.Present(value, artProfileId, divineProfileId);
            movementView?.Present(value);
        }

        public void ClearContext() => SetContext(null);

        public void SetContext(
            string nextActorId,
            string nextTargetId,
            string nextArtProfileId,
            string nextDivineProfileId,
            bool acceptsCommands)
        {
            actorId = nextActorId;
            targetId = nextTargetId;
            artProfileId = nextArtProfileId;
            divineProfileId = nextDivineProfileId;
            actionBar?.Present(acceptsCommands, artProfileId, divineProfileId);
        }

        private static void Bind(Button button, Action action)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action());
        }
    }
}
