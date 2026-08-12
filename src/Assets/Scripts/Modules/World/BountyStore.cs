using System;
using System.Collections.Generic;

namespace TianZhang.World
{
    public enum BountyStatus
    {
        Available,
        Accepted,
        ObjectiveCompleted,
        Claimed,
    }

    public sealed class BountyState
    {
        public BountyState(string bountyId, BountyStatus status, int progress)
        {
            if (string.IsNullOrWhiteSpace(bountyId)) throw new ArgumentException("Bounty ID is required.", nameof(bountyId));
            if (!Enum.IsDefined(typeof(BountyStatus), status)) throw new ArgumentOutOfRangeException(nameof(status));
            if (progress < 0) throw new ArgumentOutOfRangeException(nameof(progress));
            BountyId = bountyId;
            Status = status;
            Progress = progress;
        }

        public string BountyId { get; }
        public BountyStatus Status { get; }
        public int Progress { get; }
    }

    public sealed class BountyStoreSnapshot
    {
        public BountyStoreSnapshot(IEnumerable<BountyState> states)
        {
            States = states == null ? new BountyState[0] : new List<BountyState>(states).ToArray();
        }

        public BountyState[] States { get; }
    }

    public sealed class BountyStore
    {
        private Dictionary<string, BountyState> states =
            new Dictionary<string, BountyState>(StringComparer.Ordinal);

        public bool TryGet(string bountyId, out BountyState state)
        {
            return states.TryGetValue(bountyId, out state);
        }

        internal void Set(BountyState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Status == BountyStatus.Available)
                states.Remove(state.BountyId);
            else
                states[state.BountyId] = state;
        }

        internal void Replace(BountyStoreSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var replacement = new Dictionary<string, BountyState>(StringComparer.Ordinal);
            foreach (BountyState state in snapshot.States)
            {
                if (state == null || state.Status == BountyStatus.Available || replacement.ContainsKey(state.BountyId))
                    throw new InvalidOperationException("Invalid bounty snapshot.");
                replacement.Add(state.BountyId, state);
            }
            states = replacement;
        }

        public BountyStoreSnapshot Capture()
        {
            var entries = new List<BountyState>(states.Values);
            entries.Sort((left, right) => string.CompareOrdinal(left.BountyId, right.BountyId));
            return new BountyStoreSnapshot(entries);
        }

        public void Restore(BountyStoreSnapshot snapshot)
        {
            Replace(snapshot);
        }
    }
}
