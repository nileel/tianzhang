namespace TianZhang.Cultivation
{
    /// <summary>Player-facing projection of the frozen foundation/purple-mansion root state.</summary>
    public sealed class FoundationState
    {
        public FoundationState(
            int stageCode,
            float continuousProgress,
            int selfMansionCapacity,
            int currentMansionCarryingCapacity,
            int completionMansionCapacity)
        {
            StageCode = stageCode < 0 ? 0 : stageCode;
            ContinuousProgress = continuousProgress < 0f ? 0f : continuousProgress;
            SelfMansionCapacity = selfMansionCapacity < 0 ? 0 : selfMansionCapacity;
            CurrentMansionCarryingCapacity = currentMansionCarryingCapacity < 0 ? 0 : currentMansionCarryingCapacity;
            CompletionMansionCapacity = completionMansionCapacity < 0 ? 0 : completionMansionCapacity;
        }
        public int StageCode { get; private set; }
        public float ContinuousProgress { get; private set; }
        public int SelfMansionCapacity { get; private set; }
        public int CurrentMansionCarryingCapacity { get; private set; }
        public int CompletionMansionCapacity { get; private set; }
        public void Advance(float progress) { if (progress > 0f) ContinuousProgress += progress; }
        public void SetStageCode(int stageCode) { StageCode = stageCode < 0 ? 0 : stageCode; }
        public void SetCurrentMansionCarryingCapacity(int capacity) { CurrentMansionCarryingCapacity = capacity < 0 ? 0 : capacity; }
        public FoundationStateSnapshot Capture()
        {
            return new FoundationStateSnapshot(
                StageCode,
                ContinuousProgress,
                SelfMansionCapacity,
                CurrentMansionCarryingCapacity,
                CompletionMansionCapacity);
        }
        public void Restore(FoundationStateSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            StageCode = snapshot.StageCode;
            ContinuousProgress = snapshot.ContinuousProgress;
            SelfMansionCapacity = snapshot.SelfMansionCapacity;
            CurrentMansionCarryingCapacity = snapshot.CurrentMansionCarryingCapacity;
            CompletionMansionCapacity = snapshot.CompletionMansionCapacity;
        }
    }
    public sealed class FoundationStateSnapshot
    {
        public FoundationStateSnapshot(
            int stageCode,
            float continuousProgress,
            int selfMansionCapacity,
            int currentMansionCarryingCapacity,
            int completionMansionCapacity)
        {
            StageCode = stageCode;
            ContinuousProgress = continuousProgress;
            SelfMansionCapacity = selfMansionCapacity;
            CurrentMansionCarryingCapacity = currentMansionCarryingCapacity;
            CompletionMansionCapacity = completionMansionCapacity;
        }
        public int StageCode { get; }
        public float ContinuousProgress { get; }
        public int SelfMansionCapacity { get; }
        public int CurrentMansionCarryingCapacity { get; }
        public int CompletionMansionCapacity { get; }
    }
}
