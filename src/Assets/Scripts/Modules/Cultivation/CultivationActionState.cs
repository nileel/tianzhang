using System.Collections.Generic;

namespace TianZhang.Cultivation
{
    /// <summary>Current cultivation action and its committed world-cycle identifiers.</summary>
    public sealed class CultivationActionState
    {
        private readonly List<string> committedCycleIds = new List<string>();
        public CultivationActionState(string actionStateId, int status) { ActionStateId = actionStateId ?? string.Empty; Status = status; }
        public string ActionStateId { get; private set; } public int Status { get; private set; }
        public IReadOnlyList<string> CommittedCycleIds { get { return committedCycleIds.AsReadOnly(); } }
        public bool TryCommitCycle(string cycleId)
        {
            if (string.IsNullOrWhiteSpace(cycleId) || committedCycleIds.Contains(cycleId)) return false;
            committedCycleIds.Add(cycleId); return true;
        }
        public void SetStatus(int status) { Status = status; }
        public CultivationActionStateSnapshot Capture() { return new CultivationActionStateSnapshot(ActionStateId, Status, committedCycleIds); }
        public void Restore(CultivationActionStateSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            ActionStateId = snapshot.ActionStateId; Status = snapshot.Status; committedCycleIds.Clear(); committedCycleIds.AddRange(snapshot.CommittedCycleIds);
        }
    }
    public sealed class CultivationActionStateSnapshot
    {
        public CultivationActionStateSnapshot(string actionStateId, int status, IEnumerable<string> committedCycleIds)
        { ActionStateId = actionStateId ?? string.Empty; Status = status; CommittedCycleIds = committedCycleIds == null ? new string[0] : new List<string>(committedCycleIds).ToArray(); }
        public string ActionStateId { get; } public int Status { get; } public string[] CommittedCycleIds { get; }
    }
}
