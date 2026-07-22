global using TianZhang.Core.SpatialRules;

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
    readonly SpatialQueryBoard spatialBoard;

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
        spatialBoard = BuildSpatialBoard(this.cells, this.edgeRules, this.environmentRules);
    }

    public static HexBattlefield CreateStandardFixture(
        IReadOnlyDictionary<HexCoord, HexCellRules> cellOverrides = null,
        GameData.EnvironmentRulesConfig environmentRules = null)
    {
        var rules = environmentRules ?? GameData.EnvironmentRules;
        ValidateEnvironmentRules(rules);
        int radius = rules.MaxQueryRange;
        var fixtureCells = new Dictionary<HexCoord, HexCellRules>();
        for (int q = -radius; q <= radius; q++)
        {
            int minR = Math.Max(-radius, -q - radius);
            int maxR = Math.Min(radius, -q + radius);
            for (int r = minR; r <= maxR; r++)
                fixtureCells[new HexCoord(q, r)] = OpenCell;
        }

        if (cellOverrides != null)
        {
            foreach (var entry in cellOverrides)
                fixtureCells[entry.Key] = Validate(entry.Value);
        }

        var fixtureEdges = new Dictionary<DirectedHexEdge, HexEdgeRules>();
        foreach (var from in fixtureCells.Keys)
        {
            foreach (var to in from.Neighbors())
            {
                if (!fixtureCells.ContainsKey(to))
                    continue;
                fixtureEdges[new DirectedHexEdge(from, to)] = new HexEdgeRules(rules.StandardEdgeUnits);
            }
        }

        return new HexBattlefield(fixtureCells, fixtureEdges, environmentRules: rules);
    }

    public EdgeInspection InspectEdge(HexCoord from, HexCoord to, SpatialQueryKind kind)
    {
        var result = spatialBoard.InspectEdge(ToSpatial(from), ToSpatial(to), kind);
        return new EdgeInspection(result.IsLegal, result.MetricDistanceUnits, result.Reason);
    }

    public MetricDistanceResult QueryMetricDistance(
        HexCoord start,
        HexCoord target,
        SpatialQueryKind kind,
        int? maxRange = null)
    {
        var result = spatialBoard.QueryMetricDistance(ToSpatial(start), ToSpatial(target), kind, maxRange);
        return new MetricDistanceResult(result.IsReachable, result.DistanceUnits, result.Reason);
    }

    public IReadOnlyDictionary<HexCoord, int> FindReachable(
        HexCoord start,
        int movementBudget,
        IReadOnlyCollection<HexCoord> occupied = null)
    {
        IReadOnlyCollection<SpatialHexCoord> blocked = occupied == null
            ? null
            : occupied.Select(ToSpatial).ToArray();
        return spatialBoard.FindReachable(ToSpatial(start), movementBudget, blocked)
            .ToDictionary(entry => ToHex(entry.Key), entry => entry.Value);
    }

    public bool IsTargetInRange(
        HexCoord center,
        HexCoord target,
        int minRange,
        int maxRange,
        SpatialQueryKind kind,
        bool requireLineOfSight,
        out string reason)
    {
        var range = spatialBoard.QueryRange(
            ToSpatial(center), minRange, maxRange, kind, requireLineOfSight);
        if (!range.TryGet(ToSpatial(target), out var entry))
        {
            reason = SpatialQueryReasons.MissingCell;
            return false;
        }

        reason = entry.Reason;
        return entry.IsInRange;
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

        var blocked = occupied ?? new[] { target };
        var candidates = FindReachable(start, movementBudget, blocked);
        return candidates
            .Select(entry =>
            {
                bool canAttack = IsTargetInRange(
                    entry.Key, target, minRange, maxRange,
                    SpatialQueryKind.Attack, requireLineOfSight: true, out _);
                var distance = QueryMetricDistance(entry.Key, target, SpatialQueryKind.Attack);
                int distanceUnits = distance.IsReachable ? distance.DistanceUnits : int.MaxValue / 4;
                int minUnits = minRange * environmentRules.UnitsPerRange;
                int maxUnits = maxRange * environmentRules.UnitsPerRange;
                int rangeGap = distanceUnits < minUnits ? minUnits - distanceUnits : Math.Max(0, distanceUnits - maxUnits);
                return new
                {
                    Position = entry.Key,
                    AttackPenalty = canAttack ? 0 : 1,
                    RangeGap = rangeGap,
                    MovementCost = entry.Value,
                    Distance = distanceUnits,
                };
            })
            .OrderBy(candidate => candidate.AttackPenalty)
            .ThenBy(candidate => candidate.RangeGap)
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
        IReadOnlyCollection<SpatialHexCoord> blocked = occupied.HasValue
            ? new[] { ToSpatial(occupied.Value) }
            : null;
        var result = spatialBoard.ResolveForcedMovement(
            ToSpatial(start), (int)direction, distanceBudget, blocked);
        return new ForcedMovementResult(
            ToHex(result.Position), result.ConsumedDistanceUnits, result.Reason);
    }

    public HexCoord ResolveForcedMovement(HexCoord start, HexDirection direction, int distanceBudget, HexCoord? occupied = null) =>
        ResolveForcedMovementDetailed(start, direction, distanceBudget, occupied).Position;

    public LineOfSightResult QueryLineOfSight(HexCoord source, HexCoord target)
    {
        var result = spatialBoard.QueryLineOfSight(ToSpatial(source), ToSpatial(target));
        return new LineOfSightResult(result.HasLineOfSight, result.Reason);
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

    static SpatialQueryBoard BuildSpatialBoard(
        IReadOnlyDictionary<HexCoord, HexCellRules> configuredCells,
        IReadOnlyDictionary<DirectedHexEdge, HexEdgeRules> configuredEdges,
        GameData.EnvironmentRulesConfig rules)
    {
        var sharedCells = configuredCells.ToDictionary(
            entry => ToSpatial(entry.Key),
            entry => new SpatialCellRules(
                heightLevel: 0,
                entry.Value.BlocksMovement,
                entry.Value.BlocksSight,
                entry.Value.IsEntityObstacle,
                movementBurdenUnits: checked((entry.Value.MovementCost - 1) * rules.UnitsPerRange)));
        var sharedEdges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
        foreach (var entry in configuredEdges)
        {
            var from = ToSpatial(entry.Key.From);
            var to = ToSpatial(entry.Key.To);
            if (!sharedCells.ContainsKey(from))
                sharedCells.Add(from, new SpatialCellRules(0, false, false, false, 0));
            if (!sharedCells.ContainsKey(to))
                sharedCells.Add(to, new SpatialCellRules(0, false, false, false, 0));
            sharedEdges.Add(
                new SpatialDirectedEdge(from, to),
                new SpatialEdgeRules(
                    entry.Value.MetricDistanceUnits,
                    entry.Value.AllowsMovement,
                    entry.Value.AllowsEffects));
        }

        return new SpatialQueryBoard(
            sharedCells,
            sharedEdges,
            new SpatialQueryLimits(rules.UnitsPerRange, rules.MaxQueryRange));
    }

    static SpatialHexCoord ToSpatial(HexCoord coord) => new(coord.Q, coord.R);

    static HexCoord ToHex(SpatialHexCoord coord) => new(coord.Q, coord.R);

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
