using System;
using System.Collections.Generic;

namespace TianZhang.Spatial
{
    public enum SpatialFootprintKind { Single, AdjacentPair }

    /// <summary>Fixed footprint and clearance facts at one anchor and six-way facing.</summary>
    public readonly struct SpatialPose : IEquatable<SpatialPose>
    {
        public SpatialPose(HexCoord anchor, int facing = 0,
            SpatialFootprintKind footprint = SpatialFootprintKind.Single, int bodyScaleRank = 1)
        {
            if (facing < 0 || facing >= 6) throw new ArgumentOutOfRangeException(nameof(facing));
            if (footprint != SpatialFootprintKind.Single && footprint != SpatialFootprintKind.AdjacentPair)
                throw new ArgumentOutOfRangeException(nameof(footprint));
            if (bodyScaleRank < 1) throw new ArgumentOutOfRangeException(nameof(bodyScaleRank));
            Anchor = anchor; Facing = facing; Footprint = footprint; BodyScaleRank = bodyScaleRank;
        }

        public HexCoord Anchor { get; }
        public int Facing { get; }
        public SpatialFootprintKind Footprint { get; }
        public int BodyScaleRank { get; }
        public IReadOnlyList<HexCoord> OccupiedCells => Array.AsReadOnly(Footprint == SpatialFootprintKind.AdjacentPair
            ? new[] { Anchor, Anchor.Step((Facing + 3) % 6) } : new[] { Anchor });
        public bool Contains(HexCoord cell) => cell == Anchor ||
            Footprint == SpatialFootprintKind.AdjacentPair && cell == Anchor.Step((Facing + 3) % 6);
        public SpatialPose WithAnchor(HexCoord anchor) => new SpatialPose(anchor, Facing, Footprint, BodyScaleRank);
        public SpatialPose WithFacing(int facing) => new SpatialPose(Anchor, facing, Footprint, BodyScaleRank);
        public bool Equals(SpatialPose other) => Anchor == other.Anchor && Facing == other.Facing &&
            Footprint == other.Footprint && BodyScaleRank == other.BodyScaleRank;
        public override bool Equals(object obj) => obj is SpatialPose other && Equals(other);
        public override int GetHashCode() => unchecked(((Anchor.GetHashCode() * 397 ^ Facing) * 397 ^
            (int)Footprint) * 397 ^ BodyScaleRank);
        public static bool operator ==(SpatialPose left, SpatialPose right) => left.Equals(right);
        public static bool operator !=(SpatialPose left, SpatialPose right) => !left.Equals(right);
        public override string ToString() => $"{Anchor}/{Facing}/{Footprint}/{BodyScaleRank}";
    }

    public readonly struct SpatialContactResult
    {
        public SpatialContactResult(HexCoord sourceCell, HexCoord targetCell, int distanceUnits,
            bool isInRange, bool hasLineOfSight, string reason, int unitsPerRange)
        {
            SourceCell = sourceCell; TargetCell = targetCell; DistanceUnits = distanceUnits;
            IsInRange = isInRange; HasLineOfSight = hasLineOfSight;
            Reason = reason ?? SpatialQueryReasons.Ok; UnitsPerRange = unitsPerRange;
        }
        public HexCoord SourceCell { get; }
        public HexCoord TargetCell { get; }
        public int DistanceUnits { get; }
        public bool IsInRange { get; }
        public bool HasLineOfSight { get; }
        public string Reason { get; }
        public int UnitsPerRange { get; }
    }

    public enum SpatialTwoCellShape
    {
        ForwardPair,
        AdjacentPair,
    }

    /// <summary>Geometry only; neither cell is replaced when a spatial query rejects it.</summary>
    public readonly struct SpatialTwoCellArea
    {
        internal SpatialTwoCellArea(HexCoord first, HexCoord second)
        {
            First = first;
            Second = second;
        }

        public HexCoord First { get; }
        public HexCoord Second { get; }
    }

    public readonly struct SpatialAreaCell
    {
        public SpatialAreaCell(HexCoord coord, bool isLegal, string reason)
        {
            Coord = coord;
            IsLegal = isLegal;
            Reason = reason ?? SpatialQueryReasons.Ok;
        }

        public HexCoord Coord { get; }
        public bool IsLegal { get; }
        public string Reason { get; }
    }

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
        public const string ClearanceInsufficient = "clearance_insufficient";
        public const string InvalidPose = "spatial_pose_invalid";
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
            ulong effectBlockerMask = 0,
            int clearanceLimit = 0)
        {
            if (metricDistanceUnits < 1)
                throw new ArgumentOutOfRangeException(nameof(metricDistanceUnits));
            if (clearanceLimit < 0)
                throw new ArgumentOutOfRangeException(nameof(clearanceLimit));
            MetricDistanceUnits = metricDistanceUnits;
            AllowsMovement = allowsMovement;
            AllowsEffects = allowsEffects;
            EffectBlockerMask = effectBlockerMask;
            ClearanceLimit = clearanceLimit;
        }

        public int MetricDistanceUnits { get; }
        public bool AllowsMovement { get; }
        public bool AllowsEffects { get; }
        public ulong EffectBlockerMask { get; }
        public int ClearanceLimit { get; }
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

    /// <summary>One anchor move, including the chosen ordered 60-degree turns before it.</summary>
    public readonly struct SpatialMovementStep : IEquatable<SpatialMovementStep>
    {
        public SpatialMovementStep(SpatialPose from, SpatialPose to, IReadOnlyList<int> turnFacings,
            SpatialMovementEdgeCost edge)
        {
            From = from; To = to; Edge = edge;
            TurnFacings = new List<int>(turnFacings ?? Array.Empty<int>()).AsReadOnly();
        }
        public SpatialPose From { get; }
        public SpatialPose To { get; }
        public IReadOnlyList<int> TurnFacings { get; }
        public SpatialMovementEdgeCost Edge { get; }
        public bool Equals(SpatialMovementStep other)
        {
            if (From != other.From || To != other.To || !Edge.Equals(other.Edge)) return false;
            int count = TurnFacings?.Count ?? 0;
            if (count != (other.TurnFacings?.Count ?? 0)) return false;
            for (int index = 0; index < count; index++)
                if (TurnFacings[index] != other.TurnFacings[index]) return false;
            return true;
        }
        public override bool Equals(object obj) => obj is SpatialMovementStep other && Equals(other);
        public override int GetHashCode()
        {
            int hash = unchecked((From.GetHashCode() * 397 ^ To.GetHashCode()) * 397 ^ Edge.GetHashCode());
            if (TurnFacings != null)
                foreach (int facing in TurnFacings) hash = unchecked(hash * 397 ^ facing);
            return hash;
        }
    }

    /// <summary>Steps are authoritative; anchor path and priced edges are their read-only projections.</summary>
    public readonly struct SpatialMovementPathResult
    {
        public SpatialMovementPathResult(
            IReadOnlyList<SpatialMovementStep> steps,
            int unitsPerRange,
            int actionPointCostUnits,
            string reason)
        {
            Steps = new List<SpatialMovementStep>(steps ?? Array.Empty<SpatialMovementStep>()).AsReadOnly();
            var path = new List<HexCoord>();
            var edges = new List<SpatialMovementEdgeCost>();
            foreach (SpatialMovementStep step in Steps) { path.Add(step.To.Anchor); edges.Add(step.Edge); }
            Path = path.AsReadOnly(); Edges = edges.AsReadOnly();
            UnitsPerRange = unitsPerRange;
            ActionPointCostUnits = actionPointCostUnits;
            Reason = reason ?? SpatialQueryReasons.Ok;
        }

        public IReadOnlyList<SpatialMovementStep> Steps { get; }
        public IReadOnlyList<HexCoord> Path { get; }
        public IReadOnlyList<SpatialMovementEdgeCost> Edges { get; }
        public int UnitsPerRange { get; }
        public int ActionPointCostUnits { get; }
        public string Reason { get; }
    }
}
