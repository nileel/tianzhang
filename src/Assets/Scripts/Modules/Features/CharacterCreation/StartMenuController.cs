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
            view?.Configure(OpenNewPlayer, LoadPlayer, DeletePlayer);
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

        /// <summary>
        /// Removes one confirmed slot. A failure keeps the existing list and the stored file intact,
        /// and only a confirmed success re-enumerates so the removed row disappears immediately.
        /// </summary>
        public void DeletePlayer(string slotId)
        {
            PlayerEntryResult result = host.DeletePlayer(slotId);
            if (!result.Succeeded)
            {
                view?.ShowFailure(DescribeDeleteFailure(result.FailureReason));
                return;
            }
            RefreshSlots();
        }

        private static string DescribeDeleteFailure(string reason)
        {
            switch (reason)
            {
                case "InvalidSlotId": return "存档编号无效，未删除任何存档。";
                case "SlotNotFound": return "存档不存在，可能已被删除。";
                case "DeleteFailed": return "删除存档失败，存档仍保留。";
                default: return "删除存档失败：" + reason;
            }
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
