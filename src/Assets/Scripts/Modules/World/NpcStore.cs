using System;
using System.Collections.Generic;
using TianZhang.Entity;

namespace TianZhang.World
{
    public sealed class NpcStore
    {
        private Dictionary<string, NpcState> states =
            new Dictionary<string, NpcState>(StringComparer.Ordinal);

        public bool TryGet(string npcId, out NpcState state)
        {
            return states.TryGetValue(npcId, out state);
        }

        public bool TrySet(
            string npcId,
            string worldNodeId,
            string cultivationActionId,
            FoundationPurpleMansionSaveData cultivationState = null)
        {
            if (string.IsNullOrWhiteSpace(npcId)) return false;
            states[npcId] = new NpcState(npcId, worldNodeId, cultivationActionId, cultivationState);
            return true;
        }

        public NpcStoreSnapshot Capture()
        {
            var entries = new List<NpcState>(states.Values);
            entries.Sort((left, right) => string.CompareOrdinal(left.NpcId, right.NpcId));
            return new NpcStoreSnapshot(entries);
        }

        public void Restore(NpcStoreSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var replacement = new Dictionary<string, NpcState>(StringComparer.Ordinal);
            foreach (NpcState state in snapshot.States)
            {
                if (state == null || string.IsNullOrWhiteSpace(state.NpcId) || replacement.ContainsKey(state.NpcId))
                    throw new InvalidOperationException("Invalid NPC snapshot.");
                replacement.Add(state.NpcId, new NpcState(
                    state.NpcId,
                    state.WorldNodeId,
                    state.CultivationActionId,
                    state.CultivationState));
            }
            states = replacement;
        }
    }

    public sealed class NpcState
    {
        private readonly FoundationPurpleMansionSaveData cultivationState;

        public NpcState(string npcId, string worldNodeId, string cultivationActionId)
            : this(npcId, worldNodeId, cultivationActionId, null)
        {
        }

        public NpcState(
            string npcId,
            string worldNodeId,
            string cultivationActionId,
            FoundationPurpleMansionSaveData cultivationState)
        {
            NpcId = npcId;
            WorldNodeId = worldNodeId ?? string.Empty;
            CultivationActionId = cultivationActionId ?? string.Empty;
            this.cultivationState = Clone(cultivationState);
        }

        public string NpcId { get; }
        public string WorldNodeId { get; }
        public string CultivationActionId { get; }
        public FoundationPurpleMansionSaveData CultivationState { get { return Clone(cultivationState); } }

        private static FoundationPurpleMansionSaveData Clone(FoundationPurpleMansionSaveData source)
        {
            if (source == null) return null;
            FoundationPurpleMansionRuntimeState state;
            string reason;
            if (!FoundationPurpleMansionRuntimeState.TryRestore(source, out state, out reason))
                throw new ArgumentException(reason, nameof(source));
            return state.CaptureSaveData();
        }
    }

    public sealed class NpcStoreSnapshot
    {
        public NpcStoreSnapshot(IEnumerable<NpcState> states)
        {
            States = states == null ? new NpcState[0] : new List<NpcState>(states).ToArray();
        }

        public NpcState[] States { get; }
    }
}
