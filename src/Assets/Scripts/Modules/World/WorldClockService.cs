namespace TianZhang.World
{
    /// <summary>Only owner of the persistent world-day counter.</summary>
    public sealed class WorldClockService
    {
        public WorldClockService(int day) { Day = day < 0 ? 0 : day; }
        public int Day { get; private set; }
        public int AdvanceDay() { Day++; return Day; }
        public WorldClockSnapshot Capture() { return new WorldClockSnapshot(Day); }
        public void Restore(WorldClockSnapshot snapshot) { if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot)); Day = snapshot.Day < 0 ? 0 : snapshot.Day; }
    }
    public sealed class WorldClockSnapshot { public WorldClockSnapshot(int day) { Day = day; } public int Day { get; } }
}
