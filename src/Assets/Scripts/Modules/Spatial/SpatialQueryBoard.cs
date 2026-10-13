using System;
using System.Collections.Generic;

namespace TianZhang.Spatial
{
    public sealed class SpatialQueryBoard
    {
        /// <summary>AP 精度 S：1 点行动力等于该数量的 AP 最小单位（统一行动力公共合同 §二）。</summary>
        public const int ActionPointUnitsPerPoint = 1000;

        /// <summary>AP_ref：移动定价固定分子（统一行动力公共合同 §四）；AP 上限不进入分子。</summary>
        public const int MovementPriceReferenceActionPoints = 100;

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

        /// <summary>Queries just the two generated cells using the existing attack metric and sight rules.</summary>
        public IReadOnlyList<SpatialAreaCell> QueryTwoCellArea(
            HexCoord origin, SpatialTwoCellShape shape, int direction)
        {
            SpatialTwoCellArea area = origin.GetTwoCellArea(shape, direction);
            int maximumRange = shape == SpatialTwoCellShape.ForwardPair ? 2 : 1;
            SpatialRangeEntry first = QueryRangeEntry(origin, area.First, 1, maximumRange, SpatialQueryKind.Attack, true);
            SpatialRangeEntry second = QueryRangeEntry(origin, area.Second, 1, maximumRange, SpatialQueryKind.Attack, true);
            return Array.AsReadOnly(new[]
            {
                new SpatialAreaCell(first.Coord, first.IsInRange, first.Reason),
                new SpatialAreaCell(second.Coord, second.IsInRange, second.Reason),
            });
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

        /// <summary>
        /// 一条合法移动路径的定点行动力报价（统一行动力公共合同 §四）。与 FindReachableWithinActionPoints
        /// 使用同一个合法边遍历和同一 AP 边权：每条正费合法边 max(1, floor(AP_ref × S × d_e × 1000 / (K × v_milli)))，
        /// 逐边取整后求和，不在路径末尾再次取整。d_e 已含边长与进入目标格的移动负担。
        /// v_milli=0 拒绝自主移动且不进入除法；负值为数据错误；中间量用 checked long，费用不可表示为 int 或超预算时稳定拒绝。
        /// </summary>
        public SpatialMovementPathResult QueryMovementPath(
            HexCoord start,
            HexCoord target,
            int actionPointBudgetUnits,
            int movementEfficiencyMilliUnits,
            IReadOnlyCollection<HexCoord> occupied = null)
        {
            if (actionPointBudgetUnits < 0)
                throw new ArgumentOutOfRangeException(nameof(actionPointBudgetUnits));
            if (movementEfficiencyMilliUnits < 0)
                throw new ArgumentOutOfRangeException(nameof(movementEfficiencyMilliUnits));
            if (movementEfficiencyMilliUnits == 0)
                return UnpricedMovementPath(SpatialQueryReasons.MovementEfficiencyZero);
            if (start == target)
                return UnpricedMovementPath(SpatialQueryReasons.DestinationUnchanged);

            var blocked = occupied == null ? null : new HashSet<HexCoord>(occupied);
            blocked?.Remove(start);
            var traversal = Traverse(
                start,
                SpatialQueryKind.Movement,
                actionPointBudgetUnits,
                blocked,
                ulong.MaxValue,
                target,
                null,
                new ActionPointEdgeWeight(limits.UnitsPerRange, movementEfficiencyMilliUnits),
                true);
            if (!traversal.Costs.ContainsKey(target))
            {
                return UnpricedMovementPath(
                    traversal.FirstRejection ?? SpatialQueryReasons.NoLegalPath);
            }

            var path = new List<HexCoord>();
            var edges = new List<SpatialMovementEdgeCost>();
            var current = target;
            while (current != start)
            {
                path.Add(current);
                edges.Add(traversal.MovementEdges[current]);
                current = traversal.Previous[current];
            }
            path.Reverse();
            edges.Reverse();
            return new SpatialMovementPathResult(
                path,
                edges,
                limits.UnitsPerRange,
                traversal.Costs[target],
                SpatialQueryReasons.Ok);
        }

        /// <summary>
        /// 与 QueryMovementPath 同一边遍历、同一 AP 边权的可达集合；预算与报价均为 AP 最小单位，
        /// 不先反算整格预算裁掉压缩边。v_milli=0 拒绝自主移动，返回空集合而不进入除法。
        /// </summary>
        public IReadOnlyDictionary<HexCoord, int> FindReachableWithinActionPoints(
            HexCoord start,
            int actionPointBudgetUnits,
            int movementEfficiencyMilliUnits,
            IReadOnlyCollection<HexCoord> occupied = null)
        {
            if (actionPointBudgetUnits < 0)
                throw new ArgumentOutOfRangeException(nameof(actionPointBudgetUnits));
            if (movementEfficiencyMilliUnits < 0)
                throw new ArgumentOutOfRangeException(nameof(movementEfficiencyMilliUnits));
            if (movementEfficiencyMilliUnits == 0)
                return new Dictionary<HexCoord, int>();

            var blocked = occupied == null ? null : new HashSet<HexCoord>(occupied);
            blocked?.Remove(start);
            return Traverse(
                start,
                SpatialQueryKind.Movement,
                actionPointBudgetUnits,
                blocked,
                ulong.MaxValue,
                null,
                null,
                new ActionPointEdgeWeight(limits.UnitsPerRange, movementEfficiencyMilliUnits)).Costs;
        }

        private SpatialMovementPathResult UnpricedMovementPath(string reason) =>
            new SpatialMovementPathResult(null, null, limits.UnitsPerRange, 0, reason);

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
            Func<HexCoord, bool> canTraverse,
            ActionPointEdgeWeight? actionPointWeight = null,
            bool recordMovementEdges = false)
        {
            var costs = new Dictionary<HexCoord, int>();
            var previous = new Dictionary<HexCoord, HexCoord>();
            var movementEdges = recordMovementEdges
                ? new Dictionary<HexCoord, SpatialMovementEdgeCost>()
                : null;
            if (!cells.ContainsKey(start) || blocked?.Contains(start) == true || canTraverse?.Invoke(start) == false)
                return new TraversalResult(costs, previous, movementEdges, SpatialQueryReasons.MissingCell);

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
                    int nextCost;
                    if (actionPointWeight.HasValue)
                    {
                        if (!actionPointWeight.Value.TryGetEdgeCostUnits(
                                edge.MetricDistanceUnits,
                                movementBurden,
                                out int edgeCostUnits))
                        {
                            if (firstRejection == null)
                                firstRejection = SpatialQueryReasons.ActionPointCostOverflow;
                            continue;
                        }
                        long pricedCost = (long)currentCost + edgeCostUnits;
                        if (pricedCost > int.MaxValue)
                        {
                            if (firstRejection == null)
                                firstRejection = SpatialQueryReasons.ActionPointCostOverflow;
                            continue;
                        }
                        nextCost = (int)pricedCost;
                    }
                    else
                    {
                        nextCost = checked(currentCost + edge.MetricDistanceUnits + movementBurden);
                    }
                    if (nextCost > budgetUnits ||
                        (costs.TryGetValue(neighbor, out int knownCost) && knownCost <= nextCost))
                        continue;
                    costs[neighbor] = nextCost;
                    previous[neighbor] = currentEntry.Coord;
                    if (movementEdges != null)
                    {
                        movementEdges[neighbor] = new SpatialMovementEdgeCost(
                            currentEntry.Coord,
                            neighbor,
                            edge.MetricDistanceUnits,
                            movementBurden);
                    }
                    frontier.Add(new FrontierEntry(neighbor, nextCost));
                }
            }
            return new TraversalResult(costs, previous, movementEdges, firstRejection);
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

        /// <summary>
        /// 统一行动力公共合同 §四的定点 AP 边权：每条正费合法边的 u = max(1, floor(AP_ref × S × d_e × 1000 / (K × v_milli)))。
        /// 中间量用 checked long；d_e 不可表示为 int 或费用超过 int 时返回 false，由调用方稳定拒绝。
        /// </summary>
        private readonly struct ActionPointEdgeWeight
        {
            public ActionPointEdgeWeight(int unitsPerRange, int movementEfficiencyMilliUnits)
            {
                if (unitsPerRange < 1)
                    throw new ArgumentOutOfRangeException(nameof(unitsPerRange));
                if (movementEfficiencyMilliUnits < 1)
                    throw new ArgumentOutOfRangeException(nameof(movementEfficiencyMilliUnits));
                UnitsPerRange = unitsPerRange;
                MovementEfficiencyMilliUnits = movementEfficiencyMilliUnits;
            }

            public int UnitsPerRange { get; }
            public int MovementEfficiencyMilliUnits { get; }

            public bool TryGetEdgeCostUnits(
                int metricDistanceUnits,
                int movementBurdenUnits,
                out int costUnits)
            {
                costUnits = 0;
                long distanceUnits = checked((long)metricDistanceUnits + movementBurdenUnits);
                if (distanceUnits > int.MaxValue)
                    return false;
                long numerator = checked(
                    (long)MovementPriceReferenceActionPoints *
                    ActionPointUnitsPerPoint *
                    distanceUnits *
                    1000L);
                long denominator = checked((long)UnitsPerRange * MovementEfficiencyMilliUnits);
                long cost = numerator / denominator;
                if (cost > int.MaxValue)
                    return false;
                costUnits = cost < 1 ? 1 : (int)cost;
                return true;
            }
        }

        private sealed class TraversalResult
        {
            public TraversalResult(
                Dictionary<HexCoord, int> costs,
                Dictionary<HexCoord, HexCoord> previous,
                Dictionary<HexCoord, SpatialMovementEdgeCost> movementEdges,
                string firstRejection)
            {
                Costs = costs;
                Previous = previous;
                MovementEdges = movementEdges;
                FirstRejection = firstRejection;
            }

            public Dictionary<HexCoord, int> Costs { get; }
            public Dictionary<HexCoord, HexCoord> Previous { get; }
            public Dictionary<HexCoord, SpatialMovementEdgeCost> MovementEdges { get; }
            public string FirstRejection { get; }
        }
    }
}
