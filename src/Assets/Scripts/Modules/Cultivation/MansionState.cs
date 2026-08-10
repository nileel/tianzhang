namespace TianZhang.Cultivation
{
    /// <summary>One Purple Mansion's opening and capacity state.</summary>
    public sealed class MansionState
    {
        public MansionState(string mansionId, int buildState, int capacity)
        { MansionId = mansionId ?? string.Empty; BuildState = buildState; Capacity = capacity < 0 ? 0 : capacity; }
        public string MansionId { get; private set; } public int BuildState { get; private set; } public int Capacity { get; private set; }
        public void SetBuildState(int state) { BuildState = state; }
        public void SetCapacity(int capacity) { Capacity = capacity < 0 ? 0 : capacity; }
        public MansionStateSnapshot Capture() { return new MansionStateSnapshot(MansionId, BuildState, Capacity); }
        public void Restore(MansionStateSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            MansionId = snapshot.MansionId; BuildState = snapshot.BuildState; Capacity = snapshot.Capacity;
        }
    }
    public sealed class MansionStateSnapshot
    {
        public MansionStateSnapshot(string mansionId, int buildState, int capacity)
        { MansionId = mansionId ?? string.Empty; BuildState = buildState; Capacity = capacity; }
        public string MansionId { get; } public int BuildState { get; } public int Capacity { get; }
    }
}
