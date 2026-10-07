using System;
using System.Collections.Generic;

namespace TianZhang.Spatial
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
        public const string ReverseDirectedEdgeNotPermitted = "reverse_directed_edge_not_permitted";
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
        public const string DestinationUnchanged = "destination_unchanged";
        public const string MovementEfficiencyZero = "movement_efficiency_zero";
        public const string ActionPointCostOverflow = "action_point_cost_overflow";
    }

    public readonly struct SpatialCellRules
    {
        public SpatialCellRules(
            int heightLevel,
            bool blocksMovement,
            bool blocksSight,
            bool isEntityObstacle,
            int movementBurdenUnits)
        {
            if (movementBurdenUnits < 0)
                throw new ArgumentOutOfRangeException(nameof(movementBurdenUnits));
            HeightLevel = heightLevel;
            BlocksMovement = blocksMovement || isEntityObstacle;
            BlocksSight = blocksSight || isEntityObstacle;
            IsEntityObstacle = isEntityObstacle;
            MovementBurdenUnits = movementBurdenUnits;
        }

        public int HeightLevel { get; }
        public bool BlocksMovement { get; }
        public bool BlocksSight { get; }
        public bool IsEntityObstacle { get; }
        public int MovementBurdenUnits { get; }
    }

    public readonly struct SpatialEdgeRules
    {
        public SpatialEdgeRules(
            int metricDistanceUnits,
            bool allowsMovement,
            bool allowsEffects,
            ulong effectBlockerMask = 0)
        {
            if (metricDistanceUnits < 1)
                throw new ArgumentOutOfRangeException(nameof(metricDistanceUnits));
            MetricDistanceUnits = metricDistanceUnits;
            AllowsMovement = allowsMovement;
            AllowsEffects = allowsEffects;
            EffectBlockerMask = effectBlockerMask;
        }

        public int MetricDistanceUnits { get; }
        public bool AllowsMovement { get; }
        public bool AllowsEffects { get; }
        public ulong EffectBlockerMask { get; }
    }

    public readonly struct SpatialQueryLimits
    {
        public SpatialQueryLimits(int unitsPerRange, int maxQueryRange)
        {
            if (unitsPerRange < 1)
                throw new ArgumentOutOfRangeException(nameof(unitsPerRange));
            if (maxQueryRange < 1)
                throw new ArgumentOutOfRangeException(nameof(maxQueryRange));
            UnitsPerRange = unitsPerRange;
            MaxQueryRange = maxQueryRange;
        }

        public int UnitsPerRange { get; }
        public int MaxQueryRange { get; }
    }

    public readonly struct SpatialEdgeInspection
    {
        public SpatialEdgeInspection(bool isLegal, int metricDistanceUnits, string reason)
        {
            IsLegal = isLegal;
            MetricDistanceUnits = metricDistanceUnits;
            Reason = reason ?? SpatialQueryReasons.Ok;
        }

        public bool IsLegal { get; }
        public int MetricDistanceUnits { get; }
        public string Reason { get; }
    }

    public readonly struct SpatialMetricDistanceResult
    {
        public SpatialMetricDistanceResult(bool isReachable, int distanceUnits, string reason)
        {
            IsReachable = isReachable;
            DistanceUnits = distanceUnits;
            Reason = reason ?? SpatialQueryReasons.Ok;
        }

        public bool IsReachable { get; }
        public int DistanceUnits { get; }
        public string Reason { get; }
    }

    public readonly struct SpatialLineOfSightResult
    {
        public SpatialLineOfSightResult(bool hasLineOfSight, string reason)
        {
            HasLineOfSight = hasLineOfSight;
            Reason = reason ?? SpatialQueryReasons.Ok;
        }

        public bool HasLineOfSight { get; }
        public string Reason { get; }
    }

    public readonly struct SpatialRangeEntry
    {
        public SpatialRangeEntry(
            HexCoord coord,
            bool isInRange,
            int distanceUnits,
            bool hasLineOfSight,
            string reason)
        {
            Coord = coord;
            IsInRange = isInRange;
            DistanceUnits = distanceUnits;
            HasLineOfSight = hasLineOfSight;
            Reason = reason ?? SpatialQueryReasons.Ok;
        }

        public HexCoord Coord { get; }
        public bool IsInRange { get; }
        public int DistanceUnits { get; }
        public bool HasLineOfSight { get; }
        public string Reason { get; }
    }

    public sealed class SpatialRangeResult
    {
        internal SpatialRangeResult(IReadOnlyDictionary<HexCoord, SpatialRangeEntry> entries)
        {
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        }

        public IReadOnlyDictionary<HexCoord, SpatialRangeEntry> Entries { get; }

        public bool TryGet(HexCoord coord, out SpatialRangeEntry entry) =>
            Entries.TryGetValue(coord, out entry);
    }

    public readonly struct SpatialForcedMovementResult
    {
        public SpatialForcedMovementResult(HexCoord position, int consumedDistanceUnits, string reason)
        {
            Position = position;
            ConsumedDistanceUnits = consumedDistanceUnits;
            Reason = reason ?? SpatialQueryReasons.Ok;
        }

        public HexCoord Position { get; }
        public int ConsumedDistanceUnits { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// 一条合法有向移动边的原始度量（统一行动力公共合同 §四）：MetricDistanceUnits 为边长，
    /// MovementBurdenUnits 为进入目标格的移动负担；两者分别保留，不作为攻击射程使用。
    /// </summary>
    public readonly struct SpatialMovementEdgeCost
    {
        public SpatialMovementEdgeCost(
            HexCoord from,
            HexCoord to,
            int metricDistanceUnits,
            int movementBurdenUnits)
        {
            if (!from.TryGetDirectionTo(to, out _))
                throw new ArgumentException("Movement edges must connect topological neighbors.", nameof(to));
            if (metricDistanceUnits < 1)
                throw new ArgumentOutOfRangeException(nameof(metricDistanceUnits));
            if (movementBurdenUnits < 0)
                throw new ArgumentOutOfRangeException(nameof(movementBurdenUnits));
            From = from;
            To = to;
            MetricDistanceUnits = metricDistanceUnits;
            MovementBurdenUnits = movementBurdenUnits;
        }

        public HexCoord From { get; }
        public HexCoord To { get; }
        public int MetricDistanceUnits { get; }
        public int MovementBurdenUnits { get; }

        /// <summary>d_e = checked(MetricDistanceUnits + MovementBurdenUnits)。</summary>
        public int DistanceUnits => checked(MetricDistanceUnits + MovementBurdenUnits);
    }

    /// <summary>
    /// 一条合法移动路径的定点行动力报价（统一行动力公共合同 §四）。Path 不含起点，按现有 FindPath 顺序
    /// 包含经过格与终点；Edges 与 Path 一一对应；ActionPointCostUnits 为 AP 最小单位。
    /// Reason 为 Ok 才表示路径可达；不可达时 Path 与 Edges 为空、ActionPointCostUnits 为 0。
    /// </summary>
    public readonly struct SpatialMovementPathResult
    {
        public SpatialMovementPathResult(
            IReadOnlyList<HexCoord> path,
            IReadOnlyList<SpatialMovementEdgeCost> edges,
            int unitsPerRange,
            int actionPointCostUnits,
            string reason)
        {
            Path = path ?? Array.Empty<HexCoord>();
            Edges = edges ?? Array.Empty<SpatialMovementEdgeCost>();
            UnitsPerRange = unitsPerRange;
            ActionPointCostUnits = actionPointCostUnits;
            Reason = reason ?? SpatialQueryReasons.Ok;
        }

        public IReadOnlyList<HexCoord> Path { get; }
        public IReadOnlyList<SpatialMovementEdgeCost> Edges { get; }
        public int UnitsPerRange { get; }
        public int ActionPointCostUnits { get; }
        public string Reason { get; }
    }
}
