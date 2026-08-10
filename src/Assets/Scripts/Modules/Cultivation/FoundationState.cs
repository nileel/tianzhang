namespace TianZhang.Cultivation
{
    /// <summary>Foundation progress only. Mansion and Jindan concerns live in their own state owners.</summary>
    public sealed class FoundationState
    {
        public FoundationState(int phase, float continuousProgress, int totalMansionCapacity)
        {
            Phase = phase < 0 ? 0 : phase;
            ContinuousProgress = continuousProgress < 0f ? 0f : continuousProgress;
            TotalMansionCapacity = totalMansionCapacity < 0 ? 0 : totalMansionCapacity;
        }
        public int Phase { get; private set; }
        public float ContinuousProgress { get; private set; }
        public int TotalMansionCapacity { get; private set; }
        public void Advance(float progress) { if (progress > 0f) ContinuousProgress += progress; }
        public void SetPhase(int phase) { Phase = phase < 0 ? 0 : phase; }
        public void SetTotalMansionCapacity(int capacity) { TotalMansionCapacity = capacity < 0 ? 0 : capacity; }
        public FoundationStateSnapshot Capture() { return new FoundationStateSnapshot(Phase, ContinuousProgress, TotalMansionCapacity); }
        public void Restore(FoundationStateSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            Phase = snapshot.Phase; ContinuousProgress = snapshot.ContinuousProgress; TotalMansionCapacity = snapshot.TotalMansionCapacity;
        }
    }
    public sealed class FoundationStateSnapshot
    {
        public FoundationStateSnapshot(int phase, float continuousProgress, int totalMansionCapacity)
        { Phase = phase; ContinuousProgress = continuousProgress; TotalMansionCapacity = totalMansionCapacity; }
        public int Phase { get; } public float ContinuousProgress { get; } public int TotalMansionCapacity { get; }
    }
}
