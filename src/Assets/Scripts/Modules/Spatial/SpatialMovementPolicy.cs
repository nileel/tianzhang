using System;
using System.Collections.Generic;

namespace TianZhang.Spatial
{
    /// <summary>Caller-owned force facts. Does not change metric distance or movement efficiency.</summary>
    public sealed class SpatialMovementPolicy
    {
        public static readonly SpatialMovementPolicy Normal = new SpatialMovementPolicy();
        public SpatialMovementPolicy(int numerator = 1, int denominator = 1, bool lightLeap = false,
            int maximumEdges = int.MaxValue, string blockedReason = "", IEnumerable<SpatialDirectedEdge> sealedEdges = null)
        {
            if (numerator < 1 || denominator < 1 || maximumEdges < 0)
                throw new ArgumentOutOfRangeException(nameof(numerator));
            Numerator = numerator; Denominator = denominator; LightLeap = lightLeap;
            MaximumEdges = maximumEdges; BlockedReason = blockedReason ?? string.Empty;
            this.sealedEdges = new HashSet<SpatialDirectedEdge>(sealedEdges ?? Array.Empty<SpatialDirectedEdge>());
        }
        private readonly HashSet<SpatialDirectedEdge> sealedEdges;
        public bool BlocksEdge(HexCoord from, HexCoord to) => sealedEdges.Contains(new SpatialDirectedEdge(from, to));
        public SpatialMovementPolicy WithSealedEdges(IEnumerable<SpatialDirectedEdge> edges) =>
            new SpatialMovementPolicy(Numerator, Denominator, LightLeap, MaximumEdges, BlockedReason, edges);
        public int Numerator { get; }
        public int Denominator { get; }
        public bool LightLeap { get; }
        public int MaximumEdges { get; }
        public string BlockedReason { get; }
        public int Price(int metric, int burden, int unitsPerRange, int efficiency)
        {
            long numerator = checked(100000000L * checked((long)metric + burden) * Numerator);
            long denominator = checked((long)unitsPerRange * efficiency * Denominator);
            return checked((int)Math.Max(1L, numerator / denominator));
        }
    }
}
