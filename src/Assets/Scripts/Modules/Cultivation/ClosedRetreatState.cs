namespace TianZhang.Cultivation
{
    /// <summary>Closed-retreat plan and latest explicit stop reason.</summary>
    public sealed class ClosedRetreatState
    {
        public ClosedRetreatState(string retreatId, bool active, string lastStopReason)
        { RetreatId = retreatId ?? string.Empty; Active = active; LastStopReason = lastStopReason ?? string.Empty; }
        public string RetreatId { get; private set; } public bool Active { get; private set; } public string LastStopReason { get; private set; }
        public void Start(string retreatId) { RetreatId = retreatId ?? string.Empty; Active = true; LastStopReason = string.Empty; }
        public void Stop(string reason) { Active = false; LastStopReason = reason ?? string.Empty; }
        public ClosedRetreatStateSnapshot Capture() { return new ClosedRetreatStateSnapshot(RetreatId, Active, LastStopReason); }
        public void Restore(ClosedRetreatStateSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            RetreatId = snapshot.RetreatId; Active = snapshot.Active; LastStopReason = snapshot.LastStopReason;
        }
    }
    public sealed class ClosedRetreatStateSnapshot
    {
        public ClosedRetreatStateSnapshot(string retreatId, bool active, string lastStopReason)
        { RetreatId = retreatId ?? string.Empty; Active = active; LastStopReason = lastStopReason ?? string.Empty; }
        public string RetreatId { get; } public bool Active { get; } public string LastStopReason { get; }
    }
}
