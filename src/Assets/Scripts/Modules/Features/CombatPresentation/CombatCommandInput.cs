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

        public void Configure(ICombatCommandHandler commandHandler, CombatActionBarView bar)
        {
            handler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
            actionBar = bar;
            Bind(actionBar?.BasicAttackButton, () => handler.RequestBasicAttack(actorId, targetId));
            Bind(actionBar?.ArtButton, () => handler.RequestArt(actorId, targetId, artProfileId));
            Bind(actionBar?.DivineButton, () => handler.RequestDivine(actorId, targetId, divineProfileId));
            Bind(actionBar?.GuardButton, () => handler.RequestGuard(actorId));
            Bind(actionBar?.WaitButton, () => handler.RequestWait(actorId));
        }

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
