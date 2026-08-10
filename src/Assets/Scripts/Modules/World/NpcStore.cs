using System.Collections.Generic;

namespace TianZhang.World
{
    public sealed class NpcStore
    {
        private readonly Dictionary<string, NpcState> states = new Dictionary<string, NpcState>();
        public bool TryGet(string npcId, out NpcState state) { return states.TryGetValue(npcId, out state); }
        public bool TrySet(string npcId, string worldNodeId, string cultivationActionId)
        {
            if (string.IsNullOrWhiteSpace(npcId)) return false;
            states[npcId] = new NpcState(npcId, worldNodeId, cultivationActionId); return true;
        }
        public NpcStoreSnapshot Capture() { return new NpcStoreSnapshot(states.Values); }
        public void Restore(NpcStoreSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot)); states.Clear();
            foreach (NpcState state in snapshot.States) if (!TrySet(state.NpcId, state.WorldNodeId, state.CultivationActionId)) throw new System.InvalidOperationException("Invalid NPC snapshot.");
        }
    }
    public sealed class NpcState { public NpcState(string npcId, string worldNodeId, string cultivationActionId) { NpcId = npcId; WorldNodeId = worldNodeId ?? string.Empty; CultivationActionId = cultivationActionId ?? string.Empty; } public string NpcId { get; } public string WorldNodeId { get; } public string CultivationActionId { get; } }
    public sealed class NpcStoreSnapshot { public NpcStoreSnapshot(IEnumerable<NpcState> states) { States = states == null ? new NpcState[0] : new List<NpcState>(states).ToArray(); } public NpcState[] States { get; } }
}
