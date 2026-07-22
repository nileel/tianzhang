using System;
using System.Collections.Generic;

namespace TianZhang.Core.SpatialRules
{
    public sealed class SpatialQueryBoard
    {
        private readonly Dictionary<SpatialHexCoord, SpatialCellRules> cells;
        private readonly Dictionary<SpatialDirectedEdge, SpatialEdgeRules> edges;
        private readonly SpatialQueryLimits limits;

        public SpatialQueryBoard(
            IReadOnlyDictionary<SpatialHexCoord, SpatialCellRules> cells,
            IReadOnlyDictionary<SpatialDirectedEdge, SpatialEdgeRules> edges,
            SpatialQueryLimits limits)
        {
            if (cells == null)
                throw new ArgumentNullException(nameof(cells));
            if (edges == null)
                throw new ArgumentNullException(nameof(edges));

            this.cells = new Dictionary<SpatialHexCoord, SpatialCellRules>(cells);
            this.edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>(edges);
            this.limits = limits;
        }

        public int UnitsPerRange => limits.UnitsPerRange;
        public int MaxQueryRange => limits.MaxQueryRange;

        public SpatialEdgeInspection InspectEdge(
            SpatialHexCoord from,
            SpatialHexCoord to,
            SpatialQueryKind kind)
        {
            var key = new SpatialDirectedEdge(from, to);
            if (!edges.TryGetValue(key, out var edge))
                return new SpatialEdgeInspection(false, 0, SpatialQueryReasons.MissingDirectedEdge);
            if (!cells.ContainsKey(from) || !cells.TryGetValue(to, out var target))
                return new SpatialEdgeInspection(false, edge.MetricDistanceUnits, SpatialQueryReasons.MissingCell);

            bool movement = kind == SpatialQueryKind.Movement || kind == SpatialQueryKind.ForcedMovement;
            if (movement && !edge.AllowsMovement)
                return new SpatialEdgeInspection(false, edge.MetricDistanceUnits, SpatialQueryReasons.DirectedEdgeBlocksMovement);
            if (!movement && !edge.AllowsEffects)
                return new SpatialEdgeInspection(false, edge.MetricDistanceUnits, SpatialQueryReasons.DirectedEdgeBlocksEffects);
            if (target.IsEntityObstacle)
                return new SpatialEdgeInspection(false, edge.MetricDistanceUnits, SpatialQueryReasons.EntityObstacle);
            if (movement && target.BlocksMovement)
                return new SpatialEdgeInspection(false, edge.MetricDistanceUnits, SpatialQueryReasons.MovementBlocked);

            return new SpatialEdgeInspection(true, edge.MetricDistanceUnits, SpatialQueryReasons.Ok);
        }

        public SpatialMetricDistanceResult QueryMetricDistance(
            SpatialHexCoord start,
            SpatialHexCoord target,
            SpatialQueryKind kind,
            int? maxRange = null)
        {
            int rangeLimit = maxRange ?? limits.MaxQueryRange;
            if (rangeLimit < 0 || rangeLimit > limits.MaxQueryRange)
                throw new ArgumentOutOfRangeException(nameof(maxRange));
            if (!cells.ContainsKey(start) || !cells.ContainsKey(target))
                return new SpatialMetricDistanceResult(false, -1, SpatialQueryReasons.MissingCell);
            if (start == target)
                return new SpatialMetricDistanceResult(true, 0, SpatialQueryReasons.Ok);

            int distanceLimit = checked(rangeLimit * limits.UnitsPerRange);
            var costs = RunDijkstra(start, kind, distanceLimit, null, includeMovementBurden: false);
            return costs.TryGetValue(target, out int distance)
                ? new SpatialMetricDistanceResult(true, distance, SpatialQueryReasons.Ok)
                : new SpatialMetricDistanceResult(false, -1, SpatialQueryReasons.NoLegalPath);
        }

        public SpatialRangeResult QueryRange(
            SpatialHexCoord center,
            int minRange,
            int maxRange,
            SpatialQueryKind kind,
            bool requireLineOfSight)
        {
            if (minRange < 0 || maxRange < minRange || maxRange > limits.MaxQueryRange)
                throw new ArgumentOutOfRangeException(nameof(minRange));
            if (!cells.TryGetValue(center, out var centerRules))
                throw new ArgumentException("Range center is not configured.", nameof(center));

            int minUnits = checked(minRange * limits.UnitsPerRange);
            int maxUnits = checked(maxRange * limits.UnitsPerRange);
            var distances = RunDijkstra(
                center,
                kind,
                checked(limits.MaxQueryRange * limits.UnitsPerRange),
                null,
                includeMovementBurden: false);
            var result = new Dictionary<SpatialHexCoord, SpatialRangeEntry>();
            foreach (var candidate in cells)
            {
                if (candidate.Value.HeightLevel != centerRules.HeightLevel)
                {
                    result[candidate.Key] = new SpatialRangeEntry(
                        candidate.Key, false, -1, false, SpatialQueryReasons.HeightRuleUnconfigured);
                    continue;
                }

                if (!distances.TryGetValue(candidate.Key, out int distanceUnits))
                {
                    result[candidate.Key] = new SpatialRangeEntry(
                        candidate.Key, false, -1, false, SpatialQueryReasons.NoLegalPath);
                    continue;
                }
                if (distanceUnits < minUnits)
                {
                    result[candidate.Key] = new SpatialRangeEntry(
                        candidate.Key, false, distanceUnits, false, SpatialQueryReasons.BelowMinimumRange);
                    continue;
                }
                if (distanceUnits > maxUnits)
                {
                    result[candidate.Key] = new SpatialRangeEntry(
                        candidate.Key, false, distanceUnits, false, SpatialQueryReasons.AboveMaximumRange);
                    continue;
                }

                var sight = requireLineOfSight
                    ? QueryLineOfSight(center, candidate.Key)
                    : new SpatialLineOfSightResult(true, SpatialQueryReasons.Ok);
                result[candidate.Key] = new SpatialRangeEntry(
                    candidate.Key,
                    sight.HasLineOfSight,
                    distanceUnits,
                    sight.HasLineOfSight,
                    sight.Reason);
            }

            return new SpatialRangeResult(result);
        }

        public SpatialLineOfSightResult QueryLineOfSight(
            SpatialHexCoord source,
            SpatialHexCoord target)
        {
            if (!cells.TryGetValue(source, out var sourceRules) || !cells.TryGetValue(target, out var targetRules))
                return new SpatialLineOfSightResult(false, SpatialQueryReasons.MissingCell);
            if (sourceRules.HeightLevel != targetRules.HeightLevel)
                return new SpatialLineOfSightResult(false, SpatialQueryReasons.HeightRuleUnconfigured);
            if (source == target)
                return new SpatialLineOfSightResult(true, SpatialQueryReasons.Ok);

            var line = TraceLine(source, target);
            for (int index = 1; index < line.Count; index++)
            {
                var edge = InspectEdge(line[index - 1], line[index], SpatialQueryKind.Sight);
                if (!edge.IsLegal)
                    return new SpatialLineOfSightResult(false, edge.Reason);

                if (index >= line.Count - 1)
                    continue;

                var rules = cells[line[index]];
                if (rules.IsEntityObstacle)
                    return new SpatialLineOfSightResult(false, SpatialQueryReasons.EntityObstacle);
                if (rules.BlocksSight)
                    return new SpatialLineOfSightResult(false, SpatialQueryReasons.SightBlocked);
                if (rules.HeightLevel != sourceRules.HeightLevel)
                    return new SpatialLineOfSightResult(false, SpatialQueryReasons.HeightRuleUnconfigured);
            }

            return new SpatialLineOfSightResult(true, SpatialQueryReasons.Ok);
        }

        public IReadOnlyDictionary<SpatialHexCoord, int> FindReachable(
            SpatialHexCoord start,
            int movementBudget,
            IReadOnlyCollection<SpatialHexCoord> occupied)
        {
            if (movementBudget < 0)
                throw new ArgumentOutOfRangeException(nameof(movementBudget));
            if (!cells.ContainsKey(start))
                return new Dictionary<SpatialHexCoord, int>();

            var blocked = occupied == null
                ? new HashSet<SpatialHexCoord>()
                : new HashSet<SpatialHexCoord>(occupied);
            blocked.Remove(start);
            return RunDijkstra(
                start,
                SpatialQueryKind.Movement,
                checked(movementBudget * limits.UnitsPerRange),
                blocked,
                includeMovementBurden: true);
        }

        public SpatialForcedMovementResult ResolveForcedMovement(
            SpatialHexCoord start,
            int direction,
            int distanceBudget,
            IReadOnlyCollection<SpatialHexCoord> occupied)
        {
            if (distanceBudget < 0)
                throw new ArgumentOutOfRangeException(nameof(distanceBudget));

            var blocked = occupied == null
                ? new HashSet<SpatialHexCoord>()
                : new HashSet<SpatialHexCoord>(occupied);
            int budgetUnits = checked(distanceBudget * limits.UnitsPerRange);
            int consumed = 0;
            var current = start;
            while (true)
            {
                var next = current.Step(direction);
                if (blocked.Contains(next))
                    return new SpatialForcedMovementResult(current, consumed, SpatialQueryReasons.Occupied);

                var edge = InspectEdge(current, next, SpatialQueryKind.ForcedMovement);
                if (!edge.IsLegal)
                    return new SpatialForcedMovementResult(current, consumed, edge.Reason);
                if (consumed + edge.MetricDistanceUnits > budgetUnits)
                    return new SpatialForcedMovementResult(current, consumed, SpatialQueryReasons.DistanceBudgetExhausted);

                consumed += edge.MetricDistanceUnits;
                current = next;
            }
        }

        private Dictionary<SpatialHexCoord, int> RunDijkstra(
            SpatialHexCoord start,
            SpatialQueryKind kind,
            int distanceLimit,
            HashSet<SpatialHexCoord> occupied,
            bool includeMovementBurden)
        {
            var costs = new Dictionary<SpatialHexCoord, int> { [start] = 0 };
            var settled = new HashSet<SpatialHexCoord>();
            var unsettled = new MinQueue();
            unsettled.Enqueue(start, 0);
            while (unsettled.TryDequeue(out var current, out int currentCost))
            {
                if (settled.Contains(current) || !costs.TryGetValue(current, out int bestCost) || bestCost != currentCost)
                    continue;
                settled.Add(current);
                if (currentCost > distanceLimit)
                    break;

                foreach (var neighbor in current.Neighbors())
                {
                    if (occupied != null && occupied.Contains(neighbor))
                        continue;
                    if (!cells.TryGetValue(current, out var currentRules) ||
                        !cells.TryGetValue(neighbor, out var neighborRules) ||
                        currentRules.HeightLevel != neighborRules.HeightLevel)
                        continue;

                    var edge = InspectEdge(current, neighbor, kind);
                    if (!edge.IsLegal)
                        continue;

                    int nextCost = checked(
                        currentCost +
                        edge.MetricDistanceUnits +
                        (includeMovementBurden ? neighborRules.MovementBurdenUnits : 0));
                    if (nextCost > distanceLimit)
                        continue;
                    if (costs.TryGetValue(neighbor, out int known) && known <= nextCost)
                        continue;

                    costs[neighbor] = nextCost;
                    unsettled.Enqueue(neighbor, nextCost);
                }
            }

            return costs;
        }

        private sealed class MinQueue
        {
            private readonly List<QueueEntry> entries = new List<QueueEntry>();

            public void Enqueue(SpatialHexCoord coord, int cost)
            {
                entries.Add(new QueueEntry(coord, cost));
                int index = entries.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (Compare(entries[parent], entries[index]) <= 0)
                        break;
                    Swap(parent, index);
                    index = parent;
                }
            }

            public bool TryDequeue(out SpatialHexCoord coord, out int cost)
            {
                if (entries.Count == 0)
                {
                    coord = default;
                    cost = 0;
                    return false;
                }

                var first = entries[0];
                int lastIndex = entries.Count - 1;
                entries[0] = entries[lastIndex];
                entries.RemoveAt(lastIndex);
                int index = 0;
                while (true)
                {
                    int left = index * 2 + 1;
                    if (left >= entries.Count)
                        break;
                    int right = left + 1;
                    int smallest = right < entries.Count && Compare(entries[right], entries[left]) < 0
                        ? right
                        : left;
                    if (Compare(entries[index], entries[smallest]) <= 0)
                        break;
                    Swap(index, smallest);
                    index = smallest;
                }

                coord = first.Coord;
                cost = first.Cost;
                return true;
            }

            private void Swap(int left, int right)
            {
                var value = entries[left];
                entries[left] = entries[right];
                entries[right] = value;
            }

            private static int Compare(QueueEntry left, QueueEntry right)
            {
                int cost = left.Cost.CompareTo(right.Cost);
                return cost != 0 ? cost : SpatialQueryBoard.Compare(left.Coord, right.Coord);
            }
        }

        private readonly struct QueueEntry
        {
            public QueueEntry(SpatialHexCoord coord, int cost)
            {
                Coord = coord;
                Cost = cost;
            }

            public SpatialHexCoord Coord { get; }
            public int Cost { get; }
        }

        private static int Compare(SpatialHexCoord left, SpatialHexCoord right)
        {
            int q = left.Q.CompareTo(right.Q);
            return q != 0 ? q : left.R.CompareTo(right.R);
        }

        private static List<SpatialHexCoord> TraceLine(SpatialHexCoord start, SpatialHexCoord end)
        {
            int distance = start.TopologicalDistanceTo(end);
            var line = new List<SpatialHexCoord>(distance + 1);
            if (distance == 0)
            {
                line.Add(start);
                return line;
            }

            for (int step = 0; step <= distance; step++)
            {
                double t = step / (double)distance;
                line.Add(RoundCube(
                    Lerp(start.Q, end.Q, t),
                    Lerp(start.R, end.R, t),
                    Lerp(start.S, end.S, t)));
            }

            return line;
        }

        private static double Lerp(int start, int end, double t) => start + (end - start) * t;

        private static SpatialHexCoord RoundCube(double q, double r, double s)
        {
            int roundedQ = (int)Math.Round(q);
            int roundedR = (int)Math.Round(r);
            int roundedS = (int)Math.Round(s);
            double deltaQ = Math.Abs(roundedQ - q);
            double deltaR = Math.Abs(roundedR - r);
            double deltaS = Math.Abs(roundedS - s);
            if (deltaQ > deltaR && deltaQ > deltaS)
                roundedQ = -roundedR - roundedS;
            else if (deltaR > deltaS)
                roundedR = -roundedQ - roundedS;

            return new SpatialHexCoord(roundedQ, roundedR);
        }
    }
}
