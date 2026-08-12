using System;
using System.Collections.Generic;
using TianZhang.Entity;

namespace TianZhang.Features.CharacterCreation
{
    public sealed class PlayerSlotSummary
    {
        public PlayerSlotSummary(string slotId, string displayName, bool canLoad, string failureReason)
        {
            SlotId = slotId;
            DisplayName = displayName;
            CanLoad = canLoad;
            FailureReason = failureReason;
        }

        public string SlotId { get; }
        public string DisplayName { get; }
        public bool CanLoad { get; }
        public string FailureReason { get; }
    }

    public sealed class PlayerEntryResult
    {
        private PlayerEntryResult(bool succeeded, string failureReason)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
        }

        public bool Succeeded { get; }
        public string FailureReason { get; }
        public static PlayerEntryResult Success() => new PlayerEntryResult(true, null);
        public static PlayerEntryResult Failed(string reason) =>
            new PlayerEntryResult(false, string.IsNullOrWhiteSpace(reason) ? "player_entry_failed" : reason);
    }

    public interface IPlayerEntryHost
    {
        IReadOnlyList<PlayerSlotSummary> ListSlots();
        PlayerEntryResult CreateNewPlayer(string slotId, CharacterData profile, string startNodeId);
        PlayerEntryResult LoadPlayer(string slotId);
    }
}
