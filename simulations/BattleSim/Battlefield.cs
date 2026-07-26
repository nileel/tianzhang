using System;
using System.Collections.Generic;
using System.Linq;
using SharedSpatial = TianZhang.Core.SpatialRules;

namespace BattleSim;

enum HexDirection
{
    East,
    NorthEast,
    NorthWest,
    West,
    SouthWest,
    SouthEast,
}

enum SpatialQueryKind
{
    Movement,
    Attack,
    ForcedMovement,
    Area,
    Sight,
}

enum CoverTier
{
    None,
    Light,
    Heavy,
}

enum PhenomenonChannel
{
    Airflow,
    Visibility,
    Temperature,
    Precipitation,
    SuspendedHazard,
    CloudDischarge,
}

enum EnvironmentCyclePhase
{
    AirflowMovement,
    TemperatureChangesPrecipitation,
    PrecipitationWashAndSurface,
    VisibilityAndSuspendedHazard,
    CloudDischarge,
    DurationCleanup,
}

readonly record struct HexCoord(int Q, int R)
{
    static readonly HexCoord[] Directions =
    {
        new(1, 0),
        new(1, -1),
        new(0, -1),
        new(-1, 0),
        new(-1, 1),
        new(0, 1),
    };

    public int DistanceTo(HexCoord other)
    {
        int dq = other.Q - Q;
        int dr = other.R - R;
        return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2;
    }

    public IEnumerable<HexCoord> Neighbors()
    {
        foreach (var direction in Directions)
            yield return new HexCoord(Q + direction.Q, R + direction.R);
    }

    public HexCoord Step(HexDirection direction)
    {
        int index = (int)direction;
        if (index < 0 || index >= Directions.Length)
            throw new ArgumentOutOfRangeException(nameof(direction), "Hex direction is invalid.");

        var offset = Directions[index];
        return new HexCoord(Q + offset.Q, R + offset.R);
    }

    public bool TryGetDirectionTo(HexCoord neighbor, out HexDirection direction)
    {
        for (int i = 0; i < Directions.Length; i++)
        {
            var offset = Directions[i];
            if (neighbor.Q == Q + offset.Q && neighbor.R == R + offset.R)
            {
                direction = (HexDirection)i;
                return true;
            }
        }

        direction = default;
        return false;
    }

    public static HexDirection Opposite(HexDirection direction) => direction switch
    {
        HexDirection.East => HexDirection.West,
        HexDirection.NorthEast => HexDirection.SouthWest,
        HexDirection.NorthWest => HexDirection.SouthEast,
        HexDirection.West => HexDirection.East,
        HexDirection.SouthWest => HexDirection.NorthEast,
        HexDirection.SouthEast => HexDirection.NorthWest,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), "Hex direction is invalid."),
    };
}

readonly record struct DirectedHexEdge(HexCoord From, HexCoord To);
readonly record struct DirectionalCellCover(HexCoord Target, HexDirection IncomingDirection);

readonly record struct HexCellRules
{
    public int MovementCost { get; }
    public bool BlocksMovement { get; }
    public bool BlocksSight { get; }
    public bool IsEntityObstacle { get; }

    public HexCellRules(
        int MovementCost = 1,
        bool BlocksMovement = false,
        bool BlocksSight = false,
        bool IsEntityObstacle = false)
    {
        if (MovementCost < 1)
            throw new ArgumentOutOfRangeException(nameof(MovementCost), "Movement cost must be at least 1.");

        this.MovementCost = MovementCost;
        this.BlocksMovement = BlocksMovement || IsEntityObstacle;
        this.BlocksSight = BlocksSight || IsEntityObstacle;
        this.IsEntityObstacle = IsEntityObstacle;
    }
}

readonly record struct HexEdgeRules
{
    public int MetricDistanceUnits { get; }
    public bool AllowsMovement { get; }
    public GameData.AreaEffectBlocker EffectBlockers { get; }

    public HexEdgeRules(
        int MetricDistanceUnits,
        bool AllowsMovement = true,
        GameData.AreaEffectBlocker EffectBlockers = GameData.AreaEffectBlocker.None)
    {
        if (MetricDistanceUnits < 1)
            throw new ArgumentOutOfRangeException(nameof(MetricDistanceUnits), "Metric distance must be at least one fixed-point unit.");
        if ((EffectBlockers & ~GameData.AreaEffectBlocker.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(EffectBlockers), "Effect blockers are invalid.");

        this.MetricDistanceUnits = MetricDistanceUnits;
        this.AllowsMovement = AllowsMovement;
        this.EffectBlockers = EffectBlockers;
    }
}

sealed record SurfaceState(
    string SurfaceType,
    int? DurationCycles,
    string Source,
    string Faction,
    string SourceRef);

sealed record PhenomenonState(
    string PhenomenonType,
    PhenomenonChannel Channel,
    int StrengthTier,
    int DurationCycles,
    int MinHeightLevel = 0,
    int MaxHeightLevel = 0,
    HexDirection? Direction = null);

readonly record struct EdgeInspection(bool IsLegal, int MetricDistanceUnits, string Reason);
readonly record struct MetricDistanceResult(bool IsReachable, int DistanceUnits, string FailureReason);
readonly record struct RangeQueryResult(bool IsInRange, int DistanceUnits, bool HasLineOfSight, string Reason);
readonly record struct ForcedMovementResult(HexCoord Position, int ConsumedDistanceUnits, string StopReason);
readonly record struct LineOfSightResult(bool HasLineOfSight, string Reason);
readonly record struct CoverResult(CoverTier Tier, string Reason);
sealed record PhenomenonApplyResult(bool Applied, string Reason, PhenomenonState FinalState);

sealed class HexBattlefield
{
    static readonly HexCellRules OpenCell = new(1, false, false);
    readonly Dictionary<HexCoord, HexCellRules> cells;
    readonly Dictionary<DirectedHexEdge, HexEdgeRules> edgeRules;
    readonly Dictionary<DirectionalCellCover, CoverTier> cellCover;
    readonly Dictionary<DirectedHexEdge, CoverTier> edgeCover;
    readonly HashSet<HexCoord> validCells;
    readonly Dictionary<HexCoord, SurfaceState> surfaces = new();
    readonly Dictionary<(HexCoord Coord, PhenomenonChannel Channel), List<PhenomenonState>> phenomena = new();
    readonly GameData.EnvironmentRulesConfig environmentRules;
    readonly IReadOnlyList<GameData.PhenomenonPairFixture> phenomenonPairs;
    readonly SharedSpatial.SpatialQueryBoard spatialBoard;
    readonly Dictionary<(HexCoord Source, HexCoord Target, int MinRange, int MaxRange, SpatialQueryKind Kind, bool RequireLineOfSight), RangeQueryResult> rangeQueryCache = new();
    readonly Dictionary<(HexCoord Start, HexCoord Target, int MovementBudget, int MinRange, int MaxRange), HexCoord> attackPositionCache = new();

    public int MetricUnitsPerRange => environmentRules.UnitsPerRange;

    public HexBattlefield(
        IReadOnlyDictionary<HexCoord, HexCellRules> cells = null,
        IReadOnlyDictionary<DirectedHexEdge, HexEdgeRules> edgeRules = null,
        IReadOnlyDictionary<DirectionalCellCover, CoverTier> cellCover = null,
        IReadOnlyDictionary<DirectedHexEdge, CoverTier> edgeCover = null,
        GameData.EnvironmentRulesConfig environmentRules = null,
        IReadOnlyList<GameData.PhenomenonPairFixture> phenomenonPairs = null,
        IReadOnlySet<HexCoord> validCells = null)
    {
        this.environmentRules = environmentRules ?? GameData.EnvironmentRules;
        ValidateEnvironmentRules(this.environmentRules);
        this.cells = cells == null
            ? new Dictionary<HexCoord, HexCellRules>()
            : cells.ToDictionary(entry => entry.Key, entry => Validate(entry.Value));
        this.edgeRules = edgeRules == null
            ? new Dictionary<DirectedHexEdge, HexEdgeRules>()
            : edgeRules.ToDictionary(entry => Validate(entry.Key), entry => Validate(entry.Value));
        this.cellCover = cellCover == null
            ? new Dictionary<DirectionalCellCover, CoverTier>()
            : cellCover.ToDictionary(entry => entry.Key, entry => ValidateCover(entry.Value));
        this.edgeCover = edgeCover == null
            ? new Dictionary<DirectedHexEdge, CoverTier>()
            : edgeCover.ToDictionary(entry => Validate(entry.Key), entry => ValidateCover(entry.Value));
        this.phenomenonPairs = phenomenonPairs ?? GameData.EnvironmentPhenomenonPairFixtures;
        this.validCells = validCells == null ? null : new HashSet<HexCoord>(validCells);
        spatialBoard = BuildSpatialBoard();
    }

    public bool IsValidCell(HexCoord coord) => validCells == null || validCells.Contains(coord);

    internal static HexBattlefield CreateTechnicalFixture() => new();

    public EdgeInspection InspectEdge(HexCoord from, HexCoord to, SpatialQueryKind kind) =>
        InspectEdge(from, to, kind, GameData.AreaEffectBlocker.All);

    EdgeInspection InspectEdge(
        HexCoord from,
        HexCoord to,
        SpatialQueryKind kind,
        GameData.AreaEffectBlocker activeEffectBlockers)
    {
        if (!IsValidCell(from) || !IsValidCell(to))
            return new EdgeInspection(false, 0, "target_cell_invalid_or_out_of_bounds");
        var result = spatialBoard.InspectEdge(
            ToSpatial(from),
            ToSpatial(to),
            ToSpatial(kind),
            (ulong)activeEffectBlockers);
        return new EdgeInspection(result.IsLegal, result.MetricDistanceUnits, NormalizeSpatialReason(result.Reason));
    }

    public MetricDistanceResult QueryMetricDistance(
        HexCoord start,
        HexCoord target,
        SpatialQueryKind kind,
        int? maxRange = null)
        => QueryMetricDistanceCore(
            start,
            target,
            kind,
            maxRange,
            GameData.AreaEffectBlocker.All,
            canTraverse: null);

    public MetricDistanceResult QueryAreaEffectDistance(
        HexCoord start,
        HexCoord target,
        GameData.AreaEffectBlocker declaredBlockers,
        int? maxRange = null)
    {
        if ((declaredBlockers & ~GameData.AreaEffectBlocker.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(declaredBlockers), "Area effect blockers are invalid.");
        return QueryMetricDistanceCore(
            start,
            target,
            SpatialQueryKind.Area,
            maxRange,
            declaredBlockers,
            canTraverse: null);
    }

    MetricDistanceResult QueryMetricDistanceCore(
        HexCoord start,
        HexCoord target,
        SpatialQueryKind kind,
        int? maxRange,
        GameData.AreaEffectBlocker activeEffectBlockers,
        Func<HexCoord, bool> canTraverse)
    {
        int rangeLimit = maxRange ?? environmentRules.MaxQueryRange;
        if (rangeLimit < 0 || rangeLimit > environmentRules.MaxQueryRange)
            throw new ArgumentOutOfRangeException(nameof(maxRange), "Metric query range exceeds the configured bound.");
        if (!IsValidCell(start) || !IsValidCell(target) ||
            canTraverse?.Invoke(start) == false ||
            canTraverse?.Invoke(target) == false)
            return new MetricDistanceResult(false, -1, "target_cell_invalid_or_out_of_bounds");
        var result = spatialBoard.QueryMetricDistance(
            ToSpatial(start),
            ToSpatial(target),
            ToSpatial(kind),
            rangeLimit,
            (ulong)activeEffectBlockers,
            canTraverse == null
                ? null
                : coord => canTraverse(FromSpatial(coord)));
        return new MetricDistanceResult(result.IsReachable, result.DistanceUnits, NormalizeSpatialReason(result.Reason));
    }

    public IReadOnlyDictionary<HexCoord, int> FindReachable(
        HexCoord start,
        int movementBudget,
        IReadOnlyCollection<HexCoord> occupied = null)
    {
        var occupiedSpatial = occupied == null
            ? null
            : occupied.Select(ToSpatial).ToArray();
        return spatialBoard
            .FindReachable(ToSpatial(start), movementBudget, occupiedSpatial)
            .ToDictionary(entry => FromSpatial(entry.Key), entry => entry.Value);
    }

    public RangeQueryResult QueryRange(
        HexCoord source,
        HexCoord target,
        int minRange,
        int maxRange,
        SpatialQueryKind kind,
        bool requireLineOfSight)
    {
        var cacheKey = (source, target, minRange, maxRange, kind, requireLineOfSight);
        if (rangeQueryCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var result = spatialBoard.QueryRangeEntry(
            ToSpatial(source),
            ToSpatial(target),
            minRange,
            maxRange,
            ToSpatial(kind),
            requireLineOfSight);
        var queryResult = new RangeQueryResult(
            result.IsInRange,
            result.DistanceUnits,
            result.HasLineOfSight,
            NormalizeSpatialReason(result.Reason));
        rangeQueryCache.Add(cacheKey, queryResult);
        return queryResult;
    }

    public HexCoord FindAttackPosition(
        HexCoord start,
        HexCoord target,
        int movementBudget,
        int minRange,
        int maxRange,
        IReadOnlyCollection<HexCoord> occupied = null)
    {
        if (minRange < 0 || maxRange < minRange)
            throw new ArgumentOutOfRangeException(nameof(minRange), "Attack range is invalid.");

        var cacheKey = (start, target, movementBudget, minRange, maxRange);
        if (occupied == null && attackPositionCache.TryGetValue(cacheKey, out var cached))
            return cached;

        int minUnits = minRange * environmentRules.UnitsPerRange;
        int maxUnits = maxRange * environmentRules.UnitsPerRange;
        var blocked = occupied ?? new[] { target };
        var candidates = FindReachable(start, movementBudget, blocked);
        var position = candidates
            .Select(entry =>
            {
                var distance = QueryMetricDistance(entry.Key, target, SpatialQueryKind.Attack);
                bool hasSight = QueryLineOfSight(entry.Key, target).HasLineOfSight;
                bool canAttack = distance.IsReachable && distance.DistanceUnits >= minUnits && distance.DistanceUnits <= maxUnits && hasSight;
                int distanceUnits = distance.IsReachable ? distance.DistanceUnits : int.MaxValue / 4;
                int rangeGap = distanceUnits < minUnits
                    ? minUnits - distanceUnits
                    : Math.Max(0, distanceUnits - maxUnits);
                return new
                {
                    Position = entry.Key,
                    AttackPenalty = canAttack ? 0 : 1,
                    RangeGap = rangeGap,
                    SightPenalty = hasSight ? 0 : 1,
                    MovementCost = entry.Value,
                    Distance = distanceUnits,
                };
            })
            .OrderBy(candidate => candidate.AttackPenalty)
            .ThenBy(candidate => candidate.RangeGap)
            .ThenBy(candidate => candidate.SightPenalty)
            .ThenBy(candidate => candidate.MovementCost)
            .ThenBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Position.Q)
            .ThenBy(candidate => candidate.Position.R)
            .First()
            .Position;
        if (occupied == null)
            attackPositionCache.Add(cacheKey, position);
        return position;
    }

    public GameData.AreaTargetingResult ResolveAreaTargeting(
        GameData.AreaTargetingConfig config,
        HexCoord caster,
        int casterTeam,
        int casterIndex,
        HexCoord targetCell,
        int effectiveRangeModifier,
        IReadOnlyList<GameData.AreaTargetCandidate> candidates)
    {
        ValidateAreaTargetingConfig(config);
        if (candidates == null)
            throw new ArgumentNullException(nameof(candidates));

        int effectiveMinRange = checked(config.MinCastRange + effectiveRangeModifier);
        int effectiveMaxRange = checked(config.MaxCastRange + effectiveRangeModifier);
        if (effectiveMinRange < 0 || effectiveMaxRange < effectiveMinRange)
            throw new ArgumentOutOfRangeException(
                nameof(effectiveRangeModifier),
                "Effective cast range must retain a non-negative legal interval.");

        HexCoord center = config.CenterKind == GameData.AreaCenterKind.Caster ? caster : targetCell;
        if (!IsValidCell(center))
            return new GameData.AreaTargetingResult(null, Array.Empty<int>(), "target_cell_invalid_or_out_of_bounds");

        var unblockedCastDistance = QueryAreaEffectDistance(
            caster,
            center,
            GameData.AreaEffectBlocker.None,
            effectiveMaxRange);
        int minDistanceUnits = checked(effectiveMinRange * environmentRules.UnitsPerRange);
        int maxDistanceUnits = checked(effectiveMaxRange * environmentRules.UnitsPerRange);
        if (!unblockedCastDistance.IsReachable ||
            unblockedCastDistance.DistanceUnits < minDistanceUnits ||
            unblockedCastDistance.DistanceUnits > maxDistanceUnits)
        {
            return new GameData.AreaTargetingResult(center, Array.Empty<int>(), "cast_distance_out_of_range");
        }

        var castPropagation = QueryAreaEffectDistance(caster, center, config.EffectBlockers, effectiveMaxRange);
        if (!castPropagation.IsReachable)
            return new GameData.AreaTargetingResult(center, Array.Empty<int>(), "declared_effect_blocker");

        var hits = new List<int>();
        var propagationEnvelope = config.Shape with { InnerRadius = 0 };
        bool propagationBlocked = false;
        bool stateRejected = false;
        bool factionRejected = false;
        foreach (var candidate in candidates.OrderBy(candidate => candidate.Index))
        {
            if (!IsWithinAreaShape(center, candidate.Position, config.Shape))
                continue;

            var propagation = QueryMetricDistanceCore(
                center,
                candidate.Position,
                SpatialQueryKind.Area,
                maxRange: null,
                config.EffectBlockers,
                coord => coord == center || IsWithinAreaShape(center, coord, propagationEnvelope));
            if (!propagation.IsReachable)
            {
                propagationBlocked = true;
                continue;
            }

            if (!IsTargetStateAllowed(config.AllowedStates, candidate.IsAlive))
            {
                stateRejected = true;
                continue;
            }

            var faction = GetTargetFaction(casterTeam, casterIndex, candidate);
            if ((config.AllowedFactions & faction) == 0)
            {
                factionRejected = true;
                continue;
            }

            hits.Add(candidate.Index);
        }

        if (hits.Count > 0)
            return new GameData.AreaTargetingResult(center, hits, "");
        if (propagationBlocked)
            return new GameData.AreaTargetingResult(center, Array.Empty<int>(), "declared_effect_blocker");
        if (stateRejected)
            return new GameData.AreaTargetingResult(center, Array.Empty<int>(), "target_state_or_corpse_ineligible");
        if (factionRejected)
            return new GameData.AreaTargetingResult(center, Array.Empty<int>(), "target_faction_ineligible");
        return new GameData.AreaTargetingResult(center, Array.Empty<int>(), "no_legal_target");
    }

    static void ValidateAreaTargetingConfig(GameData.AreaTargetingConfig config)
    {
        if (config == null || string.IsNullOrWhiteSpace(config.Name) || config.Shape == null ||
            !Enum.IsDefined(config.CenterKind) ||
            config.MinCastRange < 0 || config.MaxCastRange < config.MinCastRange ||
            (config.EffectBlockers & ~GameData.AreaEffectBlocker.All) != 0 ||
            (config.AllowedFactions &
                ~(GameData.AreaTargetFaction.Enemy |
                  GameData.AreaTargetFaction.Ally |
                  GameData.AreaTargetFaction.Self)) != 0 ||
            (config.AllowedStates &
                ~(GameData.AreaTargetState.Alive |
                  GameData.AreaTargetState.Corpse)) != 0)
        {
            throw new ArgumentException("Area targeting configuration is invalid.", nameof(config));
        }

        var shape = config.Shape;
        if (shape.InnerRadius < 0 || !Enum.IsDefined(shape.Facing))
            throw new ArgumentException("Area shape configuration is invalid.", nameof(config));
        switch (shape.Kind)
        {
            case GameData.AreaShapeKind.Circle when shape.Radius >= 0 && shape.Length == 0 &&
                                                    shape.FanHalfAngleSteps == 0 && shape.InnerRadius <= shape.Radius:
                return;
            case GameData.AreaShapeKind.Line when shape.Radius == 0 && shape.Length > 0 &&
                                                  shape.FanHalfAngleSteps == 0 && shape.InnerRadius < shape.Length:
                return;
            case GameData.AreaShapeKind.Fan when shape.Radius == 0 && shape.Length > 0 &&
                                                 shape.FanHalfAngleSteps is >= 0 and <= 1 && shape.InnerRadius < shape.Length:
                return;
            default:
                throw new ArgumentException("Area shape configuration is invalid.", nameof(config));
        }
    }

    static bool IsTargetStateAllowed(GameData.AreaTargetState allowedStates, bool isAlive) =>
        isAlive
            ? (allowedStates & GameData.AreaTargetState.Alive) != 0
            : (allowedStates & GameData.AreaTargetState.Corpse) != 0;

    static GameData.AreaTargetFaction GetTargetFaction(
        int casterTeam,
        int casterIndex,
        GameData.AreaTargetCandidate candidate)
    {
        if (candidate.Index == casterIndex)
            return GameData.AreaTargetFaction.Self;
        return candidate.Team == casterTeam
            ? GameData.AreaTargetFaction.Ally
            : GameData.AreaTargetFaction.Enemy;
    }

    static bool IsWithinAreaShape(HexCoord center, HexCoord target, GameData.AreaShapeConfig shape)
    {
        int distance = center.DistanceTo(target);
        if (shape.InnerRadius > 0 && distance <= shape.InnerRadius)
            return false;

        return shape.Kind switch
        {
            GameData.AreaShapeKind.Circle => distance <= shape.Radius,
            GameData.AreaShapeKind.Line => IsOnLine(center, target, shape.Facing, shape.Length),
            GameData.AreaShapeKind.Fan => IsInFan(center, target, shape.Facing, shape.Length, shape.FanHalfAngleSteps),
            _ => false,
        };
    }

    static bool IsOnLine(HexCoord center, HexCoord target, HexDirection facing, int length)
    {
        var current = center;
        for (int step = 1; step <= length; step++)
        {
            current = current.Step(facing);
            if (current == target)
                return true;
        }
        return false;
    }

    static bool IsInFan(HexCoord center, HexCoord target, HexDirection facing, int length, int halfAngleSteps)
    {
        if (center.DistanceTo(target) > length)
            return false;
        if (target == center)
            return true;

        var offset = new HexCoord(target.Q - center.Q, target.R - center.R);
        for (int step = 0; step < (int)facing; step++)
            offset = new HexCoord(-offset.R, offset.Q + offset.R);

        return halfAngleSteps == 0
            ? offset.R == 0 && offset.Q > 0
            : offset.Q >= 0 && offset.Q + offset.R >= 0;
    }

    public ForcedMovementResult ResolveForcedMovementDetailed(
        HexCoord start,
        HexDirection direction,
        int distanceBudget,
        HexCoord? occupied = null)
    {
        var occupiedSpatial = occupied.HasValue
            ? new[] { ToSpatial(occupied.Value) }
            : null;
        var result = spatialBoard.ResolveForcedMovement(
            ToSpatial(start),
            (int)direction,
            distanceBudget,
            occupiedSpatial);
        string reason = result.Reason == SharedSpatial.SpatialQueryReasons.Occupied
            ? "occupied_landing"
            : NormalizeSpatialReason(result.Reason);
        return new ForcedMovementResult(FromSpatial(result.Position), result.ConsumedDistanceUnits, reason);
    }

    public HexCoord ResolveForcedMovement(HexCoord start, HexDirection direction, int distanceBudget, HexCoord? occupied = null) =>
        ResolveForcedMovementDetailed(start, direction, distanceBudget, occupied).Position;

    public LineOfSightResult QueryLineOfSight(HexCoord source, HexCoord target)
    {
        var result = spatialBoard.QueryLineOfSight(ToSpatial(source), ToSpatial(target));
        return new LineOfSightResult(result.HasLineOfSight, NormalizeSpatialReason(result.Reason));
    }

    public bool HasLineOfSight(HexCoord source, HexCoord target) => QueryLineOfSight(source, target).HasLineOfSight;

    public CoverResult QueryCover(HexCoord source, HexCoord target, bool isAdjacentMelee = false)
    {
        if (source == target)
            return new CoverResult(CoverTier.None, "same_cell");

        var line = TraceLine(source, target);
        var previous = line[^2];
        if (!target.TryGetDirectionTo(previous, out var incomingDirection))
            throw new InvalidOperationException("Cover query could not determine the incoming hex direction.");

        var candidates = new List<(CoverTier Tier, string Reason)>();
        if (!(isAdjacentMelee && source.DistanceTo(target) == 1) &&
            cellCover.TryGetValue(new DirectionalCellCover(target, incomingDirection), out var cellTier))
            candidates.Add((cellTier, $"cell:{cellTier}"));
        if (edgeCover.TryGetValue(new DirectedHexEdge(previous, target), out var edgeTier))
            candidates.Add((edgeTier, $"edge:{edgeTier}"));

        if (candidates.Count == 0)
            return new CoverResult(CoverTier.None, "no_cover");

        var winner = candidates
            .OrderByDescending(candidate => GameData.EnvironmentCoverTierRanks[candidate.Tier])
            .ThenBy(candidate => candidate.Reason, StringComparer.Ordinal)
            .First();
        return new CoverResult(winner.Tier, winner.Reason);
    }

    public SurfaceState SetSurface(HexCoord coord, SurfaceState surface)
    {
        if (surface == null)
            throw new ArgumentNullException(nameof(surface));
        if (string.IsNullOrWhiteSpace(surface.SurfaceType))
            throw new ArgumentException("Surface type is required.", nameof(surface));
        if (surface.DurationCycles.HasValue && surface.DurationCycles.Value < 1)
            throw new ArgumentOutOfRangeException(nameof(surface), "Surface duration must be positive when present.");

        surfaces[coord] = surface;
        return surface;
    }

    public SurfaceState GetSurface(HexCoord coord) => surfaces.GetValueOrDefault(coord);

    public int SurfaceCountAt(HexCoord coord) => surfaces.ContainsKey(coord) ? 1 : 0;

    public PhenomenonApplyResult ApplyPhenomenon(HexCoord coord, PhenomenonState incoming)
    {
        ValidatePhenomenon(incoming);
        var key = (coord, incoming.Channel);
        if (!phenomena.TryGetValue(key, out var states))
        {
            states = new List<PhenomenonState>();
            phenomena[key] = states;
        }

        var overlaps = states
            .Where(existing => RangesOverlap(
                existing.MinHeightLevel,
                existing.MaxHeightLevel,
                incoming.MinHeightLevel,
                incoming.MaxHeightLevel))
            .ToArray();
        if (overlaps.Length == 0)
        {
            states.Add(incoming);
            return new PhenomenonApplyResult(true, "created", incoming);
        }
        if (overlaps.Length > 1)
            return new PhenomenonApplyResult(false, "ambiguous_height_overlap", overlaps[0]);

        var current = overlaps[0];
        PhenomenonState finalState;
        if (current.PhenomenonType == incoming.PhenomenonType)
        {
            if (current.Direction == incoming.Direction)
            {
                finalState = current with
                {
                    StrengthTier = Math.Min(
                        environmentRules.MaxPhenomenonStrengthTier,
                        current.StrengthTier + incoming.StrengthTier),
                    DurationCycles = Math.Max(current.DurationCycles, incoming.DurationCycles),
                    MinHeightLevel = Math.Min(current.MinHeightLevel, incoming.MinHeightLevel),
                    MaxHeightLevel = Math.Max(current.MaxHeightLevel, incoming.MaxHeightLevel),
                };
            }
            else if (current.Direction.HasValue && incoming.Direction.HasValue &&
                     HexCoord.Opposite(current.Direction.Value) == incoming.Direction.Value)
            {
                int remainingStrength = current.StrengthTier - incoming.StrengthTier;
                if (remainingStrength == 0)
                {
                    states.Remove(current);
                    if (states.Count == 0)
                        phenomena.Remove(key);
                    return new PhenomenonApplyResult(true, "opposite_directions_cancel", null);
                }

                var stronger = remainingStrength > 0 ? current : incoming;
                finalState = stronger with
                {
                    StrengthTier = Math.Abs(remainingStrength),
                    DurationCycles = Math.Max(current.DurationCycles, incoming.DurationCycles),
                };
            }
            else
            {
                return new PhenomenonApplyResult(false, "unresolved_direction_pair", current);
            }
        }
        else
        {
            var pair = phenomenonPairs.FirstOrDefault(candidate => candidate.Matches(current, incoming));
            if (pair == null)
                return new PhenomenonApplyResult(false, "missing_pair", current);
            if (pair.Cancels)
            {
                states.Remove(current);
                if (states.Count == 0)
                    phenomena.Remove(key);
                return new PhenomenonApplyResult(true, "paired_cancel", null);
            }

            finalState = new PhenomenonState(
                pair.ResultType,
                incoming.Channel,
                pair.ResultStrengthTier,
                pair.ResultDurationCycles,
                Math.Min(current.MinHeightLevel, incoming.MinHeightLevel),
                Math.Max(current.MaxHeightLevel, incoming.MaxHeightLevel),
                pair.ResultDirection);
        }

        states[states.IndexOf(current)] = finalState;
        return new PhenomenonApplyResult(true, "resolved", finalState);
    }

    public IReadOnlyDictionary<PhenomenonChannel, PhenomenonState> GetPhenomena(HexCoord coord, int heightLevel)
    {
        var result = new Dictionary<PhenomenonChannel, PhenomenonState>();
        foreach (var channel in Enum.GetValues<PhenomenonChannel>())
        {
            if (!phenomena.TryGetValue((coord, channel), out var states))
                continue;

            var effective = states
                .Where(state => heightLevel >= state.MinHeightLevel && heightLevel <= state.MaxHeightLevel)
                .ToArray();
            if (effective.Length > 1)
                throw new InvalidOperationException($"Phenomenon channel {channel} has multiple final results at height {heightLevel}.");
            if (effective.Length == 1)
                result[channel] = effective[0];
        }

        return result;
    }

    public IReadOnlyList<EnvironmentCyclePhase> AdvanceEnvironmentCycle()
    {
        var phases = GameData.EnvironmentCycleOrder.ToArray();
        foreach (var key in phenomena.Keys.ToArray())
        {
            var states = phenomena[key];
            for (int i = states.Count - 1; i >= 0; i--)
            {
                int remaining = states[i].DurationCycles - 1;
                if (remaining <= 0)
                    states.RemoveAt(i);
                else
                    states[i] = states[i] with { DurationCycles = remaining };
            }

            if (states.Count == 0)
                phenomena.Remove(key);
        }

        return phases;
    }

    SharedSpatial.SpatialQueryBoard BuildSpatialBoard()
    {
        var configuredCoords = validCells == null
            ? CreateTechnicalFixtureCoords(environmentRules.MaxQueryRange)
            : new HashSet<HexCoord>(validCells);
        foreach (var coord in cells.Keys)
            configuredCoords.Add(coord);
        foreach (var edge in edgeRules.Keys)
        {
            configuredCoords.Add(edge.From);
            configuredCoords.Add(edge.To);
        }

        var spatialCells = new Dictionary<SharedSpatial.SpatialHexCoord, SharedSpatial.SpatialCellRules>();
        foreach (var coord in configuredCoords)
        {
            var rules = cells.TryGetValue(coord, out var configured) ? configured : OpenCell;
            spatialCells[ToSpatial(coord)] = new SharedSpatial.SpatialCellRules(
                heightLevel: 0,
                blocksMovement: rules.BlocksMovement,
                blocksSight: rules.BlocksSight,
                isEntityObstacle: rules.IsEntityObstacle,
                movementBurdenUnits: checked((rules.MovementCost - 1) * environmentRules.UnitsPerRange));
        }

        var spatialEdges = new Dictionary<SharedSpatial.SpatialDirectedEdge, SharedSpatial.SpatialEdgeRules>();
        if (edgeRules.Count > 0)
        {
            foreach (var entry in edgeRules)
            {
                spatialEdges.Add(
                    new SharedSpatial.SpatialDirectedEdge(ToSpatial(entry.Key.From), ToSpatial(entry.Key.To)),
                    new SharedSpatial.SpatialEdgeRules(
                        entry.Value.MetricDistanceUnits,
                        entry.Value.AllowsMovement,
                        allowsEffects: true,
                        effectBlockerMask: (ulong)entry.Value.EffectBlockers));
            }
        }
        else
        {
            foreach (var from in configuredCoords)
            {
                foreach (var to in from.Neighbors())
                {
                    if (!configuredCoords.Contains(to))
                        continue;
                    spatialEdges.Add(
                        new SharedSpatial.SpatialDirectedEdge(ToSpatial(from), ToSpatial(to)),
                        new SharedSpatial.SpatialEdgeRules(
                            environmentRules.StandardEdgeUnits,
                            allowsMovement: true,
                            allowsEffects: true));
                }
            }
        }

        return new SharedSpatial.SpatialQueryBoard(
            spatialCells,
            spatialEdges,
            new SharedSpatial.SpatialQueryLimits(
                environmentRules.UnitsPerRange,
                environmentRules.MaxQueryRange));
    }

    static HashSet<HexCoord> CreateTechnicalFixtureCoords(int radius)
    {
        var result = new HashSet<HexCoord>();
        for (int q = -radius; q <= radius; q++)
        {
            int minimumR = Math.Max(-radius, -q - radius);
            int maximumR = Math.Min(radius, -q + radius);
            for (int r = minimumR; r <= maximumR; r++)
                result.Add(new HexCoord(q, r));
        }
        return result;
    }

    static SharedSpatial.SpatialHexCoord ToSpatial(HexCoord coord) =>
        new(coord.Q, coord.R);

    static HexCoord FromSpatial(SharedSpatial.SpatialHexCoord coord) =>
        new(coord.Q, coord.R);

    static SharedSpatial.SpatialQueryKind ToSpatial(SpatialQueryKind kind) => kind switch
    {
        SpatialQueryKind.Movement => SharedSpatial.SpatialQueryKind.Movement,
        SpatialQueryKind.Attack => SharedSpatial.SpatialQueryKind.Attack,
        SpatialQueryKind.ForcedMovement => SharedSpatial.SpatialQueryKind.ForcedMovement,
        SpatialQueryKind.Area => SharedSpatial.SpatialQueryKind.Area,
        SpatialQueryKind.Sight => SharedSpatial.SpatialQueryKind.Sight,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    static string NormalizeSpatialReason(string reason) => reason switch
    {
        SharedSpatial.SpatialQueryReasons.MissingCell => "target_cell_invalid_or_out_of_bounds",
        SharedSpatial.SpatialQueryReasons.DirectedEdgeBlocksEffects => "directed_edge_blocks_effect",
        _ => reason,
    };

    static HexCellRules Validate(HexCellRules rules)
    {
        if (rules.MovementCost < 1)
            throw new ArgumentOutOfRangeException(nameof(rules), "Movement cost must be at least 1.");
        return rules;
    }

    static HexEdgeRules Validate(HexEdgeRules rules)
    {
        if (rules.MetricDistanceUnits < 1)
            throw new ArgumentOutOfRangeException(nameof(rules), "Metric distance must be positive.");
        return rules;
    }

    static DirectedHexEdge Validate(DirectedHexEdge edge)
    {
        if (!edge.From.TryGetDirectionTo(edge.To, out _))
            throw new ArgumentException("Configured directed edges must connect topological neighbors.", nameof(edge));
        return edge;
    }

    static CoverTier ValidateCover(CoverTier tier)
    {
        if (!GameData.EnvironmentCoverTierRanks.ContainsKey(tier))
            throw new ArgumentOutOfRangeException(nameof(tier), "Cover tier is not registered in GameData.");
        return tier;
    }

    static void ValidateEnvironmentRules(GameData.EnvironmentRulesConfig rules)
    {
        if (rules == null || rules.UnitsPerRange < 1 || rules.CompressedEdgeUnits < 1 ||
            rules.StandardEdgeUnits < 1 || rules.ExpandedEdgeUnits < 1 ||
            rules.MaxQueryRange < 1 || rules.MaxPhenomenonStrengthTier < 1)
            throw new InvalidOperationException("Environment rules configuration is invalid.");
    }

    void ValidatePhenomenon(PhenomenonState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        if (string.IsNullOrWhiteSpace(state.PhenomenonType))
            throw new ArgumentException("Phenomenon type is required.", nameof(state));
        if (state.StrengthTier < 1 || state.StrengthTier > environmentRules.MaxPhenomenonStrengthTier)
            throw new ArgumentOutOfRangeException(nameof(state), "Phenomenon strength is outside the configured tiers.");
        if (state.DurationCycles < 1)
            throw new ArgumentOutOfRangeException(nameof(state), "Phenomenon duration must be positive.");
        if (state.MinHeightLevel > state.MaxHeightLevel)
            throw new ArgumentException("Phenomenon height range is invalid.", nameof(state));
    }

    static bool RangesOverlap(int minA, int maxA, int minB, int maxB) => minA <= maxB && minB <= maxA;

    static IReadOnlyList<HexCoord> TraceLine(HexCoord source, HexCoord target)
    {
        int distance = source.DistanceTo(target);
        var result = new List<HexCoord>(distance + 1);
        if (distance == 0)
        {
            result.Add(source);
            return result;
        }

        for (int step = 0; step <= distance; step++)
        {
            double t = step / (double)distance;
            result.Add(RoundCube(
                Lerp(source.Q, target.Q, t),
                Lerp(-source.Q - source.R, -target.Q - target.R, t),
                Lerp(source.R, target.R, t)));
        }

        return result;
    }

    static double Lerp(int start, int end, double t) => start + (end - start) * t;

    static HexCoord RoundCube(double x, double y, double z)
    {
        int roundedX = (int)Math.Round(x, MidpointRounding.AwayFromZero);
        int roundedY = (int)Math.Round(y, MidpointRounding.AwayFromZero);
        int roundedZ = (int)Math.Round(z, MidpointRounding.AwayFromZero);
        double xDifference = Math.Abs(roundedX - x);
        double yDifference = Math.Abs(roundedY - y);
        double zDifference = Math.Abs(roundedZ - z);

        if (xDifference > yDifference && xDifference > zDifference)
            roundedX = -roundedY - roundedZ;
        else if (yDifference > zDifference)
            roundedY = -roundedX - roundedZ;
        else
            roundedZ = -roundedX - roundedY;

        return new HexCoord(roundedX, roundedZ);
    }
}
