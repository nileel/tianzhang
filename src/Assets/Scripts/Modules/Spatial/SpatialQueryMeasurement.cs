#if UNITY_EDITOR
using System;
using System.Diagnostics;

namespace TianZhang.Spatial
{
    /// <summary>Opt-in editor measurements; no counters or timings in player builds.</summary>
    public readonly struct SpatialQueryMeasurement : IDisposable
    {
        public static Action<string, double> Completed;
        private readonly Action<string, double> observer;
        private readonly string name;
        private readonly long started;

        public SpatialQueryMeasurement(string name)
        {
            observer = Completed;
            this.name = name;
            started = observer == null ? 0 : Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            if (observer != null)
                observer(name, (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency);
        }
    }
}
#endif
