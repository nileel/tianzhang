using System;
using System.Collections.Generic;

namespace TianZhang.Spatial
{
    public sealed class SpatialQueryBoard
    {
        private readonly Dictionary<HexCoord, SpatialCellRules> cells;
        private readonly Dictionary<SpatialDirectedEdge, SpatialEdgeRules> edges;
        private readonly SpatialQueryLimits limits;

        public SpatialQueryBoard(
            IReadOnlyDictionary<HexCoord, SpatialCellRules> cells,
            IReadOnlyDictionary<SpatialDirectedEdge, SpatialEdgeRules> edges,
            SpatialQueryLimits limits)
        {
            if (cells == null)
                throw new ArgumentNullException(nameof(cells));
            if (edges == null)
                throw new ArgumentNullException(nameof(edges));
            this.cells = new Dictionary<HexCoord, SpatialCellRules>(cells);
            this.edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>(edges);
            this.limits = limits;
        }

        public int UnitsPerRange => limits.UnitsPerRange;
        public int MaxQueryRange => limits.MaxQueryRange;
        public IEnumerable<HexCoord> Cells => cells.Keys;

        public SpatialEdgeInspection InspectEdge(
            HexCoord from,
            HexCoord to,
            SpatialQueryKind kind,
            ulong activeEffectBlockers = ulong.MaxValue)
        {
            if (!from.TryGetDirectionTo(to, out _))
                throw new ArgumentException("Spatial queries can inspect only adjacent hex edges.", nameof(to));
            if (!cells.TryGetValue(from, out var fromRules) || !cells.TryGetValue(to, out var toRules))
                return new SpatialEdgeInspection(false, 0, SpatialQueryReasons.MissingCell);
            if (fromRules.HeightLevel != toRules.HeightLevel)
                return new SpatialEdgeInspection(false, 0, SpatialQueryReasons.HeightRuleUnconfigured);

            var key = new SpatialDirectedEdge(from, to);
            if (!edges.TryGetValue(key, out var edgeRules))
            {
                var reverse = new SpatialDirectedEdge(to, from);
                string reason = edges.ContainsKey(reverse)
                    ? SpatialQueryReasons.ReverseDirectedEdgeNotPermitted
                    : SpatialQueryReasons.MissingDirectedEdge;
                return new SpatialEdgeInspection(false, 0, reason);
            }

            bool movement = kind == SpatialQueryKind.Movement || kind == SpatialQueryKind.ForcedMovement;
            if (movement && !edgeRules.AllowsMovement)
                return new SpatialEdgeInspection(false, edgeRules.MetricDistanceUnits, SpatialQueryReasons.DirectedEdgeBlocksMovement);
            if (!movement && (!edgeRules.AllowsEffects || (edgeRules.EffectBlockerMask & activeEffectBlockers) != 0))
                return new SpatialEdgeInspection(false, edgeRules.MetricDistanceUnits, SpatialQueryReasons.DirectedEdgeBlocksEffects);
            if (toRules.IsEntityObstacle)
                return new SpatialEdgeInspection(false, edgeRules.MetricDistanceUnits, SpatialQueryReasons.EntityObstacle);
            if (movement && toRules.BlocksMovement)
                return new SpatialEdgeInspection(false, edgeRules.MetricDistanceUnits, SpatialQueryReasons.MovementBlocked);

            return new SpatialEdgeInspection(true, edgeRules.MetricDistanceUnits, SpatialQueryReasons.Ok);
        }

        public SpatialMetricDistanceResult QueryMetricDistance(
            HexCoord start,
            HexCoord target,
            SpatialQueryKind kind,
            int? maxRange = null,
            ulong activeEffectBlockers = ulong.MaxValue,
            Func<HexCoord, bool> canTraverse = null)
        {
            int rangeLimit = maxRange ?? limits.MaxQueryRange;
            if (rangeLimit < 0 || rangeLimit > limits.MaxQueryRange)
                throw new ArgumentOutOfRangeException(nameof(maxRange));
            if (!cells.TryGetValue(start, out var startRules) || !cells.TryGetValue(target, out var targetRules))
                return new SpatialMetricDistanceResult(false, -1, SpatialQueryReasons.MissingCell);
            if (canTraverse?.Invoke(start) == false || canTraverse?.Invoke(target) == false)
                return new SpatialMetricDistanceResult(false, -1, SpatialQueryReasons.MissingCell);
            if (startRules.HeightLevel != targetRules.HeightLevel)
                return new SpatialMetricDistanceResult(false, -1, SpatialQueryReasons.HeightRuleUnconfigured);
            if (start == target)
                return new SpatialMetricDistanceResult(true, 0, SpatialQueryReasons.Ok);

            int distanceLimit = checked(rangeLimit * limits.UnitsPerRange);
            var traversal = Traverse(start, kind, distanceLimit, null, activeEffectBlockers, target, canTraverse);
            return traversal.Costs.TryGetValue(target, out int distance)
                ? new SpatialMetricDistanceResult(true, distance, SpatialQueryReasons.Ok)
                : new SpatialMetricDistanceResult(false, -1, traversal.FirstRejection ?? SpatialQueryReasons.NoLegalPath);
        }

        public SpatialRangeResult QueryRange(
            HexCoord center,
            int minRange,
            int maxRange,
            SpatialQueryKind kind,
            bool requireLineOfSight,
            ulong activeEffectBlockers = ulong.MaxValue)
        {
            if (minRange < 0 || maxRange < minRange || maxRange > limits.MaxQueryRange)
                throw new ArgumentOutOfRangeException(nameof(minRange));

            int minimumUnits = checked(minRange * limits.UnitsPerRange);
            int maximumUnits = checked(maxRange * limits.UnitsPerRange);
            var entries = new Dictionary<HexCoord, SpatialRangeEntry>();
            foreach (var coord in cells.Keys)
                entries[coord] = QueryRangeEntryUnits(
                    center,
                    coord,
                    minimumUnits,
                    maximumUnits,
                    kind,
                    requireLineOfSight,
                    activeEffectBlockers);
            return new SpatialRangeResult(entries);
        }

        public SpatialRangeEntry QueryRangeEntry(
            HexCoord center,
            HexCoord target,
            int minRange,
            int maxRange,
            SpatialQueryKind kind,
            bool requireLineOfSight,
            ulong activeEffectBlockers = ulong.MaxValue)
        {
            if (minRange < 0 || maxRange < minRange || maxRange > limits.MaxQueryRange)
                throw new ArgumentOutOfRangeException(nameof(minRange));
            return QueryRangeEntryUnits(
                center,
                target,
                checked(minRange * limits.UnitsPerRange),
                checked(maxRange * limits.UnitsPerRange),
                kind,
                requireLineOfSight,
                activeEffectBlockers);
        }

        private SpatialRangeEntry QueryRangeEntryUnits(
            HexCoord center,
            HexCoord target,
            int minimumUnits,
            int maximumUnits,
            SpatialQueryKind kind,
            bool requireLineOfSight,
            ulong activeEffectBlockers)
        {
            var distance = QueryMetricDistance(
                center,
                target,
                kind,
                limits.MaxQueryRange,
                activeEffectBlockers);
            if (!distance.IsReachable)
                return new SpatialRangeEntry(target, false, -1, false, distance.Reason);
            if (distance.DistanceUnits < minimumUnits)
                return new SpatialRangeEntry(
                    target, false, distance.DistanceUnits, false, SpatialQueryReasons.BelowMinimumRange);
            if (distance.DistanceUnits > maximumUnits)
                return new SpatialRangeEntry(
                    target, false, distance.DistanceUnits, false, SpatialQueryReasons.AboveMaximumRange);

            var sight = requireLineOfSight
                ? QueryLineOfSight(center, target, activeEffectBlockers)
                : new SpatialLineOfSightResult(true, SpatialQueryReasons.Ok);
            return new SpatialRangeEntry(
                target,
                sight.HasLineOfSight,
                distance.DistanceUnits,
                sight.HasLineOfSight,
                sight.Reason);
        }

        public IReadOnlyDictionary<HexCoord, int> FindReachable(
            HexCoord start,
            int movementBudget,
            IReadOnlyCollection<HexCoord> occupied = null)
        {
            if (movementBudget < 0)
                throw new ArgumentOutOfRangeException(nameof(movementBudget));
            var blocked = occupied == null ? null : new HashSet<HexCoord>(occupied);
            blocked?.Remove(start);
            return Traverse(
                start,
                SpatialQueryKind.Movement,
                checked(movementBudget * limits.UnitsPerRange),
                blocked,
                ulong.MaxValue,
                null,
                null).Costs;
        }

        public IReadOnlyList<HexCoord> FindPath(
            HexCoord start,
            HexCoord target,
            int movementBudget,
            IReadOnlyCollection<HexCoord> occupied = null)
        {
            if (movementBudget < 0)
                throw new ArgumentOutOfRangeException(nameof(movementBudget));
            var blocked = occupied == null ? null : new HashSet<HexCoord>(occupied);
            blocked?.Remove(start);
            var traversal = Traverse(
                start,
                SpatialQueryKind.Movement,
                checked(movementBudget * limits.UnitsPerRange),
                blocked,
                ulong.MaxValue,
                target,
                null);
            if (!traversal.Costs.ContainsKey(target))
                return Array.Empty<HexCoord>();

            var path = new List<HexCoord>();
            var current = target;
            while (current != start)
            {
                path.Add(current);
                current = traversal.Previous[current];
            }
            path.Reverse();
            return path;
        }

        public SpatialLineOfSightResult QueryLineOfSight(
            HexCoord source,
            HexCoord target,
            ulong activeEffectBlockers = ulong.MaxValue)
        {
            var metric = QueryMetricDistance(
                source,
                target,
                SpatialQueryKind.Sight,
                limits.MaxQueryRange,
                activeEffectBlockers);
            if (!metric.IsReachable)
                return new SpatialLineOfSightResult(false, metric.Reason);

            var line = TraceLine(source, target);
            for (int index = 1; index < line.Count; index++)
            {
                var edge = InspectEdge(line[index - 1], line[index], SpatialQueryKind.Sight, activeEffectBlockers);
                if (!edge.IsLegal)
                    return new SpatialLineOfSightResult(false, edge.Reason);
                if (index == line.Count - 1)
                    continue;
                var rules = cells[line[index]];
                if (rules.IsEntityObstacle)
                    return new SpatialLineOfSightResult(false, SpatialQueryReasons.EntityObstacle);
                if (rules.BlocksSight)
                    return new SpatialLineOfSightResult(false, SpatialQueryReasons.SightBlocked);
            }
            return new SpatialLineOfSightResult(true, SpatialQueryReasons.Ok);
        }

        public SpatialForcedMovementResult ResolveForcedMovement(
            HexCoord start,
            int direction,
            int distanceBudget,
            IReadOnlyCollection<HexCoord> occupied = null)
        {
            if (distanceBudget < 0)
                throw new ArgumentOutOfRangeException(nameof(distanceBudget));
            var blocked = occupied == null ? null : new HashSet<HexCoord>(occupied);
            blocked?.Remove(start);
            int budgetUnits = checked(distanceBudget * limits.UnitsPerRange);
            int consumedUnits = 0;
            var current = start;
            while (true)
            {
                var next = current.Step(direction);
                if (blocked?.Contains(next) == true)
                    return new SpatialForcedMovementResult(current, consumedUnits, SpatialQueryReasons.Occupied);
                var edge = InspectEdge(current, next, SpatialQueryKind.ForcedMovement);
                if (!edge.IsLegal)
                    return new SpatialForcedMovementResult(current, consumedUnits, edge.Reason);
                if (consumedUnits + edge.MetricDistanceUnits > budgetUnits)
                    return new SpatialForcedMovementResult(
                        current, consumedUnits, SpatialQueryReasons.DistanceBudgetExhausted);
                consumedUnits += edge.MetricDistanceUnits;
                current = next;
            }
        }

        private TraversalResult Traverse(
            HexCoord start,
            SpatialQueryKind kind,
            int budgetUnits,
            HashSet<HexCoord> blocked,
            ulong activeEffectBlockers,
            HexCoord? stopAt,
            Func<HexCoord, bool> canTraverse)
        {
            var costs = new Dictionary<HexCoord, int>();
            var previous = new Dictionary<HexCoord, HexCoord>();
            if (!cells.ContainsKey(start) || blocked?.Contains(start) == true || canTraverse?.Invoke(start) == false)
                return new TraversalResult(costs, previous, SpatialQueryReasons.MissingCell);

            var frontier = new List<FrontierEntry> { new FrontierEntry(start, 0) };
            costs[start] = 0;
            string firstRejection = null;
            while (frontier.Count > 0)
            {
                var currentEntry = DequeueMinimum(frontier);
                if (!costs.TryGetValue(currentEntry.Coord, out int currentCost) || currentCost != currentEntry.Cost)
                    continue;
                if (stopAt.HasValue && currentEntry.Coord == stopAt.Value)
                    break;

                foreach (var neighbor in currentEntry.Coord.Neighbors())
                {
                    if (blocked?.Contains(neighbor) == true || canTraverse?.Invoke(neighbor) == false)
                        continue;
                    var edge = InspectEdge(currentEntry.Coord, neighbor, kind, activeEffectBlockers);
                    if (!edge.IsLegal)
                    {
                        if (firstRejection == null &&
                            (edge.Reason == SpatialQueryReasons.HeightRuleUnconfigured ||
                             edge.Reason == SpatialQueryReasons.EntityObstacle))
                            firstRejection = edge.Reason;
                        continue;
                    }

                    int movementBurden = kind == SpatialQueryKind.Movement
                        ? cells[neighbor].MovementBurdenUnits
                        : 0;
                    int nextCost = checked(currentCost + edge.MetricDistanceUnits + movementBurden);
                    if (nextCost > budgetUnits ||
                        (costs.TryGetValue(neighbor, out int knownCost) && knownCost <= nextCost))
                        continue;
                    costs[neighbor] = nextCost;
                    previous[neighbor] = currentEntry.Coord;
                    frontier.Add(new FrontierEntry(neighbor, nextCost));
                }
            }
            return new TraversalResult(costs, previous, firstRejection);
        }

        private static FrontierEntry DequeueMinimum(List<FrontierEntry> frontier)
        {
            int bestIndex = 0;
            for (int index = 1; index < frontier.Count; index++)
            {
                if (frontier[index].CompareTo(frontier[bestIndex]) < 0)
                    bestIndex = index;
            }
            var result = frontier[bestIndex];
            frontier.RemoveAt(bestIndex);
            return result;
        }

        private static IReadOnlyList<HexCoord> TraceLine(HexCoord source, HexCoord target)
        {
            int distance = source.TopologicalDistanceTo(target);
            var line = new List<HexCoord>(distance + 1);
            if (distance == 0)
            {
                line.Add(source);
                return line;
            }
            for (int step = 0; step <= distance; step++)
            {
                double amount = step / (double)distance;
                line.Add(RoundCube(
                    Lerp(source.Q, target.Q, amount),
                    Lerp(-source.Q - source.R, -target.Q - target.R, amount),
                    Lerp(source.R, target.R, amount)));
            }
            return line;
        }

        private static double Lerp(int start, int end, double amount) => start + (end - start) * amount;

        private static HexCoord RoundCube(double x, double y, double z)
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

        private readonly struct FrontierEntry : IComparable<FrontierEntry>
        {
            public FrontierEntry(HexCoord coord, int cost)
            {
                Coord = coord;
                Cost = cost;
            }

            public HexCoord Coord { get; }
            public int Cost { get; }

            public int CompareTo(FrontierEntry other)
            {
                int cost = Cost.CompareTo(other.Cost);
                if (cost != 0) return cost;
                int q = Coord.Q.CompareTo(other.Coord.Q);
                return q != 0 ? q : Coord.R.CompareTo(other.Coord.R);
            }
        }

        private sealed class TraversalResult
        {
            public TraversalResult(
                Dictionary<HexCoord, int> costs,
                Dictionary<HexCoord, HexCoord> previous,
                string firstRejection)
            {
                Costs = costs;
                Previous = previous;
                FirstRejection = firstRejection;
            }

            public Dictionary<HexCoord, int> Costs { get; }
            public Dictionary<HexCoord, HexCoord> Previous { get; }
            public string FirstRejection { get; }
        }
    }
}
