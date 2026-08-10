using System.Collections.Generic;

namespace TianZhang.World
{
    public sealed class BountyStore
    {
        private readonly Dictionary<string, BountyState> states = new Dictionary<string, BountyState>();
        public bool TryGet(string bountyId, out BountyState state) { return states.TryGetValue(bountyId, out state); }
        public bool TrySet(string bountyId, int status, string acceptedBy)
        {
            if (string.IsNullOrWhiteSpace(bountyId)) return false;
            states[bountyId] = new BountyState(bountyId, status, acceptedBy); return true;
        }
        public BountyStoreSnapshot Capture() { return new BountyStoreSnapshot(states.Values); }
        public void Restore(BountyStoreSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot)); states.Clear();
            foreach (BountyState state in snapshot.States) if (!TrySet(state.BountyId, state.Status, state.AcceptedBy)) throw new System.InvalidOperationException("Invalid bounty snapshot.");
        }
    }
    public sealed class BountyState { public BountyState(string bountyId, int status, string acceptedBy) { BountyId = bountyId; Status = status; AcceptedBy = acceptedBy ?? string.Empty; } public string BountyId { get; } public int Status { get; } public string AcceptedBy { get; } }
    public sealed class BountyStoreSnapshot { public BountyStoreSnapshot(IEnumerable<BountyState> states) { States = states == null ? new BountyState[0] : new List<BountyState>(states).ToArray(); } public BountyState[] States { get; } }
}
