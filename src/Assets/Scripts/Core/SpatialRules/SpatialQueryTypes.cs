using System;
using System.Collections.Generic;

namespace TianZhang.Core.SpatialRules
{
    public enum SpatialQueryKind
    {
        Movement,
        Attack,
        ForcedMovement,
        Area,
        Sight,
    }

    public static class SpatialQueryReasons
    {
        public const string Ok = "";
        public const string MissingCell = "cell_not_configured";
        public const string MissingDirectedEdge = "directed_edge_not_configured";
        public const string DirectedEdgeBlocksMovement = "directed_edge_blocks_movement";
        public const string DirectedEdgeBlocksEffects = "directed_edge_blocks_effects";
        public const string EntityObstacle = "entity_obstacle";
        public const string MovementBlocked = "movement_blocked";
        public const string SightBlocked = "sight_blocked";
        public const string HeightRuleUnconfigured = "height_rule_unconfigured";
        public const string NoLegalPath = "no_legal_path_within_query_limit";
        public const string BelowMinimumRange = "below_min_range";
        public const string AboveMaximumRange = "above_max_range";
        public const string Occupied = "occupied";
        public const string DistanceBudgetExhausted = "distance_budget_exhausted";
        public const string QueryBoardNotConfigured = "spatial_query_not_configured";
    }

    public readonly struct SpatialCellRules
    {
        public int HeightLevel { get; }
        public bool BlocksMovement { get; }
        public bool BlocksSight { get; }
        public bool IsEntityObstacle { get; }

        public SpatialCellRules(
            int heightLevel,
            bool blocksMovement,
            bool blocksSight,
            bool isEntityObstacle)
        {
            HeightLevel = heightLevel;
            BlocksMovement = blocksMovement || isEntityObstacle;
            BlocksSight = blocksSight || isEntityObstacle;
            IsEntityObstacle = isEntityObstacle;
        }
    }

    public readonly struct SpatialEdgeRules
    {
        public int MetricDistanceUnits { get; }
        public bool AllowsMovement { get; }
        public bool AllowsEffects { get; }

        public SpatialEdgeRules(int metricDistanceUnits, bool allowsMovement, bool allowsEffects)
        {
            if (metricDistanceUnits < 1)
                throw new ArgumentOutOfRangeException(nameof(metricDistanceUnits));

            MetricDistanceUnits = metricDistanceUnits;
            AllowsMovement = allowsMovement;
            AllowsEffects = allowsEffects;
        }
    }

    public readonly struct SpatialQueryLimits
    {
        public int UnitsPerRange { get; }
        public int MaxQueryRange { get; }

        public SpatialQueryLimits(int unitsPerRange, int maxQueryRange)
        {
            if (unitsPerRange < 1)
                throw new ArgumentOutOfRangeException(nameof(unitsPerRange));
            if (maxQueryRange < 1)
                throw new ArgumentOutOfRangeException(nameof(maxQueryRange));

            UnitsPerRange = unitsPerRange;
            MaxQueryRange = maxQueryRange;
        }
    }

    public readonly struct SpatialEdgeInspection
    {
        public bool IsLegal { get; }
        public int MetricDistanceUnits { get; }
        public string Reason { get; }

        public SpatialEdgeInspection(bool isLegal, int metricDistanceUnits, string reason)
        {
            IsLegal = isLegal;
            MetricDistanceUnits = metricDistanceUnits;
            Reason = reason;
        }
    }

    public readonly struct SpatialMetricDistanceResult
    {
        public bool IsReachable { get; }
        public int DistanceUnits { get; }
        public string Reason { get; }

        public SpatialMetricDistanceResult(bool isReachable, int distanceUnits, string reason)
        {
            IsReachable = isReachable;
            DistanceUnits = distanceUnits;
            Reason = reason;
        }
    }

    public readonly struct SpatialLineOfSightResult
    {
        public bool HasLineOfSight { get; }
        public string Reason { get; }

        public SpatialLineOfSightResult(bool hasLineOfSight, string reason)
        {
            HasLineOfSight = hasLineOfSight;
            Reason = reason;
        }
    }

    public readonly struct SpatialRangeEntry
    {
        public SpatialHexCoord Coord { get; }
        public bool IsInRange { get; }
        public int DistanceUnits { get; }
        public bool HasLineOfSight { get; }
        public string Reason { get; }

        public SpatialRangeEntry(
            SpatialHexCoord coord,
            bool isInRange,
            int distanceUnits,
            bool hasLineOfSight,
            string reason)
        {
            Coord = coord;
            IsInRange = isInRange;
            DistanceUnits = distanceUnits;
            HasLineOfSight = hasLineOfSight;
            Reason = reason;
        }
    }

    public sealed class SpatialRangeResult
    {
        private readonly IReadOnlyDictionary<SpatialHexCoord, SpatialRangeEntry> entries;

        internal SpatialRangeResult(IReadOnlyDictionary<SpatialHexCoord, SpatialRangeEntry> entries)
        {
            this.entries = entries ?? throw new ArgumentNullException(nameof(entries));
        }

        public IReadOnlyDictionary<SpatialHexCoord, SpatialRangeEntry> Entries => entries;

        public bool TryGet(SpatialHexCoord coord, out SpatialRangeEntry entry) =>
            entries.TryGetValue(coord, out entry);
    }

    public readonly struct SpatialForcedMovementResult
    {
        public SpatialHexCoord Position { get; }
        public int ConsumedDistanceUnits { get; }
        public string Reason { get; }

        public SpatialForcedMovementResult(
            SpatialHexCoord position,
            int consumedDistanceUnits,
            string reason)
        {
            Position = position;
            ConsumedDistanceUnits = consumedDistanceUnits;
            Reason = reason;
        }
    }
}
