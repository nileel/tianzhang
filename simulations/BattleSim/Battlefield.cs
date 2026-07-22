using System;
using System.Collections.Generic;
using System.Linq;

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
    public bool AllowsEffects { get; }

    public HexEdgeRules(int MetricDistanceUnits, bool AllowsMovement = true, bool AllowsEffects = true)
    {
        if (MetricDistanceUnits < 1)
            throw new ArgumentOutOfRangeException(nameof(MetricDistanceUnits), "Metric distance must be at least one fixed-point unit.");

        this.MetricDistanceUnits = MetricDistanceUnits;
        this.AllowsMovement = AllowsMovement;
        this.AllowsEffects = AllowsEffects;
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
    readonly Dictionary<HexCoord, SurfaceState> surfaces = new();
    readonly Dictionary<(HexCoord Coord, PhenomenonChannel Channel), List<PhenomenonState>> phenomena = new();
    readonly GameData.EnvironmentRulesConfig environmentRules;
    readonly IReadOnlyList<GameData.PhenomenonPairFixture> phenomenonPairs;

    public int MetricUnitsPerRange => environmentRules.UnitsPerRange;

    public HexBattlefield(
        IReadOnlyDictionary<HexCoord, HexCellRules> cells = null,
        IReadOnlyDictionary<DirectedHexEdge, HexEdgeRules> edgeRules = null,
        IReadOnlyDictionary<DirectionalCellCover, CoverTier> cellCover = null,
        IReadOnlyDictionary<DirectedHexEdge, CoverTier> edgeCover = null,
        GameData.EnvironmentRulesConfig environmentRules = null,
        IReadOnlyList<GameData.PhenomenonPairFixture> phenomenonPairs = null)
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
    }

    public EdgeInspection InspectEdge(HexCoord from, HexCoord to, SpatialQueryKind kind)
    {
        if (!from.TryGetDirectionTo(to, out _))
            throw new ArgumentException("Spatial queries can inspect only adjacent hex edges.", nameof(to));

        var rules = GetEdgeRules(from, to);
        bool movement = kind is SpatialQueryKind.Movement or SpatialQueryKind.ForcedMovement;
        if (movement && !rules.AllowsMovement)
            return new EdgeInspection(false, rules.MetricDistanceUnits, "directed_edge_blocks_movement");
        if (!movement && !rules.AllowsEffects)
            return new EdgeInspection(false, rules.MetricDistanceUnits, "directed_edge_blocks_effect");

        var targetRules = GetRules(to);
        if (movement && targetRules.IsEntityObstacle)
            return new EdgeInspection(false, rules.MetricDistanceUnits, "entity_obstacle");
        if (movement && targetRules.BlocksMovement)
            return new EdgeInspection(false, rules.MetricDistanceUnits, "movement_blocked");

        return new EdgeInspection(true, rules.MetricDistanceUnits, "");
    }

    public MetricDistanceResult QueryMetricDistance(
        HexCoord start,
        HexCoord target,
        SpatialQueryKind kind,
        int? maxRange = null)
    {
        int rangeLimit = maxRange ?? environmentRules.MaxQueryRange;
        if (rangeLimit < 0 || rangeLimit > environmentRules.MaxQueryRange)
            throw new ArgumentOutOfRangeException(nameof(maxRange), "Metric query range exceeds the configured bound.");
        if (start == target)
            return new MetricDistanceResult(true, 0, "");

        int distanceLimit = checked(rangeLimit * environmentRules.UnitsPerRange);
        if (edgeRules.Count == 0 &&
            (cells.Count == 0 || kind is SpatialQueryKind.Attack or SpatialQueryKind.Area or SpatialQueryKind.Sight))
        {
            int uniformDistance = checked(start.DistanceTo(target) * environmentRules.StandardEdgeUnits);
            return uniformDistance <= distanceLimit
                ? new MetricDistanceResult(true, uniformDistance, "")
                : new MetricDistanceResult(false, -1, "no_legal_path_within_query_limit");
        }

        var costs = new Dictionary<HexCoord, int> { [start] = 0 };
        var frontier = new PriorityQueue<HexCoord, (int Cost, int Q, int R)>();
        frontier.Enqueue(start, (0, start.Q, start.R));

        while (frontier.TryDequeue(out var current, out var priority))
        {
            if (costs[current] != priority.Cost)
                continue;
            if (priority.Cost > distanceLimit)
                break;
            if (current == target)
                return new MetricDistanceResult(true, priority.Cost, "");

            foreach (var neighbor in current.Neighbors())
            {
                var edge = InspectEdge(current, neighbor, kind);
                if (!edge.IsLegal)
                    continue;

                int nextCost = priority.Cost + edge.MetricDistanceUnits;
                if (nextCost > distanceLimit || (costs.TryGetValue(neighbor, out int knownCost) && knownCost <= nextCost))
                    continue;

                costs[neighbor] = nextCost;
                frontier.Enqueue(neighbor, (nextCost, neighbor.Q, neighbor.R));
            }
        }

        return new MetricDistanceResult(false, -1, "no_legal_path_within_query_limit");
    }

    public IReadOnlyDictionary<HexCoord, int> FindReachable(
        HexCoord start,
        int movementBudget,
        IReadOnlyCollection<HexCoord> occupied = null)
    {
        if (movementBudget < 0)
            throw new ArgumentOutOfRangeException(nameof(movementBudget), "Movement budget cannot be negative.");

        var blocked = occupied == null ? null : new HashSet<HexCoord>(occupied);
        blocked?.Remove(start);
        int budgetUnits = checked(movementBudget * environmentRules.UnitsPerRange);
        var costs = new Dictionary<HexCoord, int> { [start] = 0 };
        var frontier = new PriorityQueue<HexCoord, (int Cost, int Q, int R)>();
        frontier.Enqueue(start, (0, start.Q, start.R));

        while (frontier.TryDequeue(out var current, out var priority))
        {
            if (costs[current] != priority.Cost)
                continue;

            foreach (var neighbor in current.Neighbors())
            {
                if (blocked?.Contains(neighbor) == true)
                    continue;

                var edge = InspectEdge(current, neighbor, SpatialQueryKind.Movement);
                if (!edge.IsLegal)
                    continue;

                int terrainBurden = (GetRules(neighbor).MovementCost - 1) * environmentRules.UnitsPerRange;
                int nextCost = priority.Cost + edge.MetricDistanceUnits + terrainBurden;
                if (nextCost > budgetUnits || (costs.TryGetValue(neighbor, out int knownCost) && knownCost <= nextCost))
                    continue;

                costs[neighbor] = nextCost;
                frontier.Enqueue(neighbor, (nextCost, neighbor.Q, neighbor.R));
            }
        }

        return costs;
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

        int minUnits = minRange * environmentRules.UnitsPerRange;
        int maxUnits = maxRange * environmentRules.UnitsPerRange;
        var blocked = occupied ?? new[] { target };
        var candidates = FindReachable(start, movementBudget, blocked);
        return candidates
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
    }

    public ForcedMovementResult ResolveForcedMovementDetailed(
        HexCoord start,
        HexDirection direction,
        int distanceBudget,
        HexCoord? occupied = null)
    {
        if (distanceBudget < 0)
            throw new ArgumentOutOfRangeException(nameof(distanceBudget), "Forced movement distance cannot be negative.");

        int budgetUnits = checked(distanceBudget * environmentRules.UnitsPerRange);
        int consumed = 0;
        var current = start;
        while (true)
        {
            var next = current.Step(direction);
            if (occupied.HasValue && next == occupied.Value)
                return new ForcedMovementResult(current, consumed, "occupied_landing");

            var edge = InspectEdge(current, next, SpatialQueryKind.ForcedMovement);
            if (!edge.IsLegal)
                return new ForcedMovementResult(current, consumed, edge.Reason);
            if (consumed + edge.MetricDistanceUnits > budgetUnits)
                return new ForcedMovementResult(current, consumed, "distance_budget_exhausted");

            consumed += edge.MetricDistanceUnits;
            current = next;
        }
    }

    public HexCoord ResolveForcedMovement(HexCoord start, HexDirection direction, int distanceBudget, HexCoord? occupied = null) =>
        ResolveForcedMovementDetailed(start, direction, distanceBudget, occupied).Position;

    public LineOfSightResult QueryLineOfSight(HexCoord source, HexCoord target)
    {
        var metricDistance = QueryMetricDistance(source, target, SpatialQueryKind.Sight);
        if (!metricDistance.IsReachable)
            return new LineOfSightResult(false, metricDistance.FailureReason);

        var line = TraceLine(source, target);
        for (int i = 1; i < line.Count; i++)
        {
            var edge = InspectEdge(line[i - 1], line[i], SpatialQueryKind.Sight);
            if (!edge.IsLegal)
                return new LineOfSightResult(false, edge.Reason);

            if (i >= line.Count - 1)
                continue;

            var rules = GetRules(line[i]);
            if (rules.IsEntityObstacle)
                return new LineOfSightResult(false, "entity_obstacle");
            if (rules.BlocksSight)
                return new LineOfSightResult(false, "sight_blocked");
        }

        return new LineOfSightResult(true, "");
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

    HexCellRules GetRules(HexCoord coord) => cells.TryGetValue(coord, out var rules) ? rules : OpenCell;

    HexEdgeRules GetEdgeRules(HexCoord from, HexCoord to) =>
        edgeRules.TryGetValue(new DirectedHexEdge(from, to), out var rules)
            ? rules
            : new HexEdgeRules(environmentRules.StandardEdgeUnits);

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
