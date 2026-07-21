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
}

readonly record struct HexCellRules
{
    public int MovementCost { get; }
    public bool BlocksMovement { get; }
    public bool BlocksSight { get; }

    public HexCellRules(int MovementCost = 1, bool BlocksMovement = false, bool BlocksSight = false)
    {
        if (MovementCost < 1)
            throw new ArgumentOutOfRangeException(nameof(MovementCost), "Movement cost must be at least 1.");

        this.MovementCost = MovementCost;
        this.BlocksMovement = BlocksMovement;
        this.BlocksSight = BlocksSight;
    }
}

sealed class HexBattlefield
{
    static readonly HexCellRules OpenCell = new(1, false, false);
    readonly Dictionary<HexCoord, HexCellRules> cells;

    public HexBattlefield(IReadOnlyDictionary<HexCoord, HexCellRules> cells = null)
    {
        this.cells = cells == null
            ? new Dictionary<HexCoord, HexCellRules>()
            : cells.ToDictionary(entry => entry.Key, entry => Validate(entry.Value));
    }

    public IReadOnlyDictionary<HexCoord, int> FindReachable(HexCoord start, int movementBudget, HexCoord? occupied = null)
    {
        if (movementBudget < 0)
            throw new ArgumentOutOfRangeException(nameof(movementBudget), "Movement budget cannot be negative.");

        var costs = new Dictionary<HexCoord, int> { [start] = 0 };
        var frontier = new PriorityQueue<HexCoord, (int Cost, int Q, int R)>();
        frontier.Enqueue(start, (0, start.Q, start.R));

        while (frontier.TryDequeue(out var current, out var priority))
        {
            if (costs[current] != priority.Cost)
                continue;

            foreach (var neighbor in current.Neighbors())
            {
                var rules = GetRules(neighbor);
                if (rules.BlocksMovement || (occupied.HasValue && neighbor == occupied.Value))
                    continue;

                int nextCost = priority.Cost + rules.MovementCost;
                if (nextCost > movementBudget || (costs.TryGetValue(neighbor, out int knownCost) && knownCost <= nextCost))
                    continue;

                costs[neighbor] = nextCost;
                frontier.Enqueue(neighbor, (nextCost, neighbor.Q, neighbor.R));
            }
        }

        return costs;
    }

    public HexCoord FindAttackPosition(HexCoord start, HexCoord target, int movementBudget, int minRange, int maxRange)
    {
        if (minRange < 0 || maxRange < minRange)
            throw new ArgumentOutOfRangeException(nameof(minRange), "Attack range is invalid.");

        var candidates = FindReachable(start, movementBudget, target);
        return candidates
            .Select(entry =>
            {
                int distance = entry.Key.DistanceTo(target);
                bool hasSight = HasLineOfSight(entry.Key, target);
                bool canAttack = distance >= minRange && distance <= maxRange && hasSight;
                int rangeGap = distance < minRange ? minRange - distance : Math.Max(0, distance - maxRange);
                return new
                {
                    Position = entry.Key,
                    AttackPenalty = canAttack ? 0 : 1,
                    RangeGap = rangeGap,
                    SightPenalty = hasSight ? 0 : 1,
                    MovementCost = entry.Value,
                    Distance = distance,
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

    public HexCoord ResolveForcedMovement(HexCoord start, HexDirection direction, int distanceBudget, HexCoord? occupied = null)
    {
        if (distanceBudget < 0)
            throw new ArgumentOutOfRangeException(nameof(distanceBudget), "Forced movement distance cannot be negative.");

        var current = start;
        for (int distance = 0; distance < distanceBudget; distance++)
        {
            var next = current.Step(direction);
            if (GetRules(next).BlocksMovement || (occupied.HasValue && next == occupied.Value))
                break;
            current = next;
        }

        return current;
    }

    public bool HasLineOfSight(HexCoord source, HexCoord target)
    {
        int distance = source.DistanceTo(target);
        for (int step = 1; step < distance; step++)
        {
            double t = step / (double)distance;
            if (GetRules(RoundCube(
                    Lerp(source.Q, target.Q, t),
                    Lerp(-source.Q - source.R, -target.Q - target.R, t),
                    Lerp(source.R, target.R, t))).BlocksSight)
                return false;
        }

        return true;
    }

    HexCellRules GetRules(HexCoord coord) => cells.TryGetValue(coord, out var rules) ? rules : OpenCell;

    static HexCellRules Validate(HexCellRules rules)
    {
        if (rules.MovementCost < 1)
            throw new ArgumentOutOfRangeException(nameof(rules), "Movement cost must be at least 1.");
        return rules;
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
