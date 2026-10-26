using System;
using System.Collections.Generic;

namespace TianZhang.Spatial
{
    /// <summary>One source's frozen undirected edge; the same final metric applies in both directions.</summary>
    public sealed class SpatialMetricOverride
    {
        public SpatialMetricOverride(string sourceInstanceId, HexCoord from, HexCoord to, int metricDistanceUnits)
        {
            if (string.IsNullOrWhiteSpace(sourceInstanceId))
                throw new ArgumentException("spatial_metric_source_missing", nameof(sourceInstanceId));
            if (!from.TryGetDirectionTo(to, out _))
                throw new ArgumentException("spatial_metric_edge_not_adjacent", nameof(to));
            if (metricDistanceUnits != 1 && metricDistanceUnits != 4)
                throw new ArgumentOutOfRangeException(nameof(metricDistanceUnits), "spatial_metric_scale_invalid");
            SourceInstanceId = sourceInstanceId;
            From = from;
            To = to;
            MetricDistanceUnits = metricDistanceUnits;
        }

        public string SourceInstanceId { get; }
        public HexCoord From { get; }
        public HexCoord To { get; }
        public int MetricDistanceUnits { get; }
    }

    /// <summary>Immutable projection only. Ability ownership, duration and conflict outcomes belong to the caller.</summary>
    internal sealed class SpatialMetricOverrideSet
    {
        internal static readonly SpatialMetricOverrideSet Empty = new SpatialMetricOverrideSet();
        private readonly Dictionary<SpatialDirectedEdge, SpatialMetricOverride> byEdge =
            new Dictionary<SpatialDirectedEdge, SpatialMetricOverride>();
        internal IReadOnlyList<SpatialMetricOverride> Rules { get; }

        private SpatialMetricOverrideSet() => Rules = Array.AsReadOnly(Array.Empty<SpatialMetricOverride>());

        internal SpatialMetricOverrideSet(IReadOnlyList<SpatialMetricOverride> overrides,
            IReadOnlyDictionary<HexCoord, SpatialCellRules> cells,
            IReadOnlyDictionary<SpatialDirectedEdge, SpatialEdgeRules> edges, int unitsPerRange)
        {
            if (overrides == null) throw new ArgumentNullException(nameof(overrides));
            if (overrides.Count > 0 && unitsPerRange != 2)
                throw new ArgumentException("spatial_metric_precision_invalid", nameof(overrides));
            var groups = new Dictionary<string, List<SpatialMetricOverride>>(StringComparer.Ordinal);
            var frozen = new List<SpatialMetricOverride>(overrides.Count);
            foreach (SpatialMetricOverride item in overrides)
            {
                if (item == null) throw new ArgumentException("spatial_metric_override_missing", nameof(overrides));
                var forward = new SpatialDirectedEdge(item.From, item.To);
                var reverse = new SpatialDirectedEdge(item.To, item.From);
                if (!cells.ContainsKey(item.From) || !cells.ContainsKey(item.To))
                    throw new ArgumentException("spatial_metric_cell_missing", nameof(overrides));
                if (!edges.ContainsKey(forward) || !edges.ContainsKey(reverse))
                    throw new ArgumentException("spatial_metric_bidirectional_edge_required", nameof(overrides));
                // A higher layer must first resolve any overlap. This projection never chooses a winner.
                if (byEdge.ContainsKey(forward))
                    throw new ArgumentException("spatial_metric_override_conflict", nameof(overrides));
                if (!groups.TryGetValue(item.SourceInstanceId, out var group))
                {
                    group = new List<SpatialMetricOverride>(2);
                    groups.Add(item.SourceInstanceId, group);
                }
                if (group.Count == 2)
                    throw new ArgumentException("spatial_metric_capacity_exceeded", nameof(overrides));
                if (group.Count == 1)
                {
                    SpatialMetricOverride first = group[0];
                    if (first.MetricDistanceUnits != item.MetricDistanceUnits)
                        throw new ArgumentException("spatial_metric_mixed_scale", nameof(overrides));
                    if (first.From != item.From && first.From != item.To &&
                        first.To != item.From && first.To != item.To)
                        throw new ArgumentException("spatial_metric_edges_disconnected", nameof(overrides));
                }
                group.Add(item);
                frozen.Add(item);
                byEdge.Add(forward, item);
                byEdge.Add(reverse, item);
            }
            Rules = frozen.AsReadOnly();
        }

        internal SpatialEdgeRules ResolveEdge(SpatialDirectedEdge key, SpatialEdgeRules original)
        {
            if (!byEdge.TryGetValue(key, out var item)) return original;
            return new SpatialEdgeRules(item.MetricDistanceUnits, original.AllowsMovement, original.AllowsEffects,
                original.EffectBlockerMask, original.ClearanceLimit, original.EdgeKind,
                original.MovementBurdenUnits, original.ConnectionId, original.AllowsFlight, original.FlightMovementBurdenUnits);
        }
    }
}
