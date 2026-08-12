using System;
using System.Collections.Generic;
using TianZhang.Entity;
using UnityEngine;

namespace TianZhang.Features.CharacterCreation
{
    public sealed class StartMenuController : MonoBehaviour
    {
        [SerializeField] private StartMenuView view;
        [SerializeField] private CharacterCreationController characterCreation;
        private IPlayerEntryHost host;

        public void Configure(
            IPlayerEntryHost playerEntryHost,
            StartMenuView startMenuView,
            CharacterCreationController creationController)
        {
            host = playerEntryHost ?? throw new ArgumentNullException(nameof(playerEntryHost));
            view = startMenuView;
            characterCreation = creationController;
        }

        private void Start()
        {
            if (host == null) return;
            view?.Configure(OpenNewPlayer, LoadPlayer);
            RefreshSlots();
        }

        public void RefreshSlots()
        {
            IReadOnlyList<PlayerSlotSummary> slots = host.ListSlots();
            view?.ShowSlots(slots);
        }

        public void OpenNewPlayer()
        {
            characterCreation?.Open();
        }

        public void LoadPlayer(string slotId)
        {
            PlayerEntryResult result = host.LoadPlayer(slotId);
            if (!result.Succeeded) view?.ShowFailure(result.FailureReason);
        }

        public void CompleteNewPlayer(string slotId, CharacterData profile, string startNodeId)
        {
            PlayerEntryResult result = host.CreateNewPlayer(slotId, profile, startNodeId);
            if (!result.Succeeded)
            {
                characterCreation?.ShowFailure(result.FailureReason);
                return;
            }
            RefreshSlots();
        }
    }
}
