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
            ulong activeEffectBlockers = ulong.MaxValue,
            int bodyScaleRank = 1)
        {
            if (bodyScaleRank < 1) throw new ArgumentOutOfRangeException(nameof(bodyScaleRank));
            if (!from.TryGetDirectionTo(to, out _))
                throw new ArgumentException("Spatial queries can inspect only adjacent hex edges.", nameof(to));
            if (!cells.TryGetValue(from, out var fromRules) || !cells.TryGetValue(to, out var toRules))
                return new SpatialEdgeInspection(false, 0, SpatialQueryReasons.MissingCell);

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
            if (movement)
            {
                // Height difference itself is not a rejection: the explicit edge type decides
                // whether a connection exists. The kind checks keep a mislabelled flat edge from
                // silently bridging a height step.
                switch (edgeRules.EdgeKind)
                {
                    case SpatialEdgeKind.OpenCliff:
                        return Reject(edgeRules, SpatialQueryReasons.OpenCliffBlocksGround);
                    case SpatialEdgeKind.EntityWall:
                        return Reject(edgeRules, SpatialQueryReasons.EntityWallBlocksMovement);
                    case SpatialEdgeKind.OneWayDrop:
                        if (edgeRules.AllowsMovement && toRules.HeightLevel >= fromRules.HeightLevel)
                            return Reject(edgeRules, SpatialQueryReasons.OneWayDropDirectionInvalid);
                        break;
                    case SpatialEdgeKind.Flat:
                        if (fromRules.HeightLevel != toRules.HeightLevel)
                            return Reject(edgeRules, SpatialQueryReasons.EdgeHeightMismatch);
                        break;
                    case SpatialEdgeKind.Slope:
                    case SpatialEdgeKind.Stairs:
                        if (Math.Abs((long)toRules.HeightLevel - fromRules.HeightLevel) > 1)
                            return Reject(edgeRules, SpatialQueryReasons.EdgeHeightMismatch);
                        break;
                    case SpatialEdgeKind.Climb:
                    case SpatialEdgeKind.Bridge:
                        return Reject(edgeRules, SpatialQueryReasons.EdgeKindNotImplemented);
                }
                if (!edgeRules.AllowsMovement)
                    return Reject(edgeRules, SpatialQueryReasons.DirectedEdgeBlocksMovement);
                if (edgeRules.ClearanceLimit > 0 && bodyScaleRank > edgeRules.ClearanceLimit)
                    return Reject(edgeRules, SpatialQueryReasons.ClearanceInsufficient);
            }
            else
            {
                // Effects use the same explicit edge. A wall blocks ordinary action paths;
                // an open cliff or one-way drop keeps the effect path.
                if (edgeRules.EdgeKind == SpatialEdgeKind.EntityWall)
                    return Reject(edgeRules, SpatialQueryReasons.EntityWallBlocksEffects);
                if (!edgeRules.AllowsEffects || (edgeRules.EffectBlockerMask & activeEffectBlockers) != 0)
                    return Reject(edgeRules, SpatialQueryReasons.DirectedEdgeBlocksEffects);
            }
            if (toRules.IsEntityObstacle)
                return Reject(edgeRules, SpatialQueryReasons.EntityObstacle);
            if (movement && toRules.BlocksMovement)
                return Reject(edgeRules, SpatialQueryReasons.MovementBlocked);

            return new SpatialEdgeInspection(true, edgeRules.MetricDistanceUnits, SpatialQueryReasons.Ok,
                edgeRules.MovementBurdenUnits);
        }

        private static SpatialEdgeInspection Reject(SpatialEdgeRules rules, string reason) =>
            new SpatialEdgeInspection(false, rules.MetricDistanceUnits, reason, rules.MovementBurdenUnits);

        /// <summary>Occupancy belongs to the caller; no cell is silently removed from occupied.</summary>
        public string InspectPlacement(SpatialPose pose, IReadOnlyCollection<HexCoord> occupied = null)
        {
            if (!IsValidPose(pose)) return SpatialQueryReasons.InvalidPose;
            foreach (HexCoord coord in pose.OccupiedCells)
            {
                if (!cells.TryGetValue(coord, out var rules)) return SpatialQueryReasons.MissingCell;
                if (rules.IsEntityObstacle) return SpatialQueryReasons.EntityObstacle;
                if (rules.BlocksMovement) return SpatialQueryReasons.MovementBlocked;
                if (Contains(occupied, coord)) return SpatialQueryReasons.Occupied;
            }
            if (pose.Footprint == SpatialFootprintKind.Single) return SpatialQueryReasons.Ok;
            HexCoord rear = pose.Anchor.Step((pose.Facing + 3) % 6);
            if (!edges.TryGetValue(new SpatialDirectedEdge(pose.Anchor, rear), out var forward) ||
                !edges.TryGetValue(new SpatialDirectedEdge(rear, pose.Anchor), out var reverse))
                return SpatialQueryReasons.MissingDirectedEdge;
            bool sameHeight = cells[pose.Anchor].HeightLevel == cells[rear].HeightLevel;
            if (sameHeight)
            {
                if (!forward.AllowsMovement && !reverse.AllowsMovement)
                    return SpatialQueryReasons.DirectedEdgeBlocksMovement;
            }
            else
            {
                // 战场空间与环境规则 §八.4: a cross-height body must stand on one explicit
                // ramp/stair connection with the same named connection and edge kind in both
                // directions, both movement-enabled. This spatial owner currently serves GROUND,
                // altitudeBand=0 units; other movement modes are added with their own prototypes.
                if (Math.Abs((long)cells[pose.Anchor].HeightLevel - cells[rear].HeightLevel) != 1)
                    return SpatialQueryReasons.PairCrossHeightConnectionInvalid;
                if (forward.EdgeKind != reverse.EdgeKind ||
                    (forward.EdgeKind != SpatialEdgeKind.Slope && forward.EdgeKind != SpatialEdgeKind.Stairs) ||
                    string.IsNullOrWhiteSpace(forward.ConnectionId) ||
                    !string.Equals(forward.ConnectionId, reverse.ConnectionId, StringComparison.Ordinal))
                    return SpatialQueryReasons.PairCrossHeightConnectionInvalid;
                if (!forward.AllowsMovement || !reverse.AllowsMovement)
                    return SpatialQueryReasons.DirectedEdgeBlocksMovement;
            }
            if (forward.ClearanceLimit > 0 && pose.BodyScaleRank > forward.ClearanceLimit ||
                reverse.ClearanceLimit > 0 && pose.BodyScaleRank > reverse.ClearanceLimit)
                return SpatialQueryReasons.ClearanceInsufficient;
            return SpatialQueryReasons.Ok;
        }

        /// <summary>Choose the nearest visible effect-path pair before applying either range bound.</summary>
        public SpatialContactResult QueryContact(SpatialPose source, SpatialPose target, int min, int max)
        {
            if (min < 0 || max < min || max > limits.MaxQueryRange)
                throw new ArgumentOutOfRangeException(nameof(min));
            if (!IsValidPose(source) || !IsValidPose(target))
                return new SpatialContactResult(source.Anchor, target.Anchor, -1, false, false,
                    SpatialQueryReasons.InvalidPose, limits.UnitsPerRange);
            var sources = new List<HexCoord>(source.OccupiedCells);
            var targets = new List<HexCoord>(target.OccupiedCells);
            sources.Sort(CompareCells); targets.Sort(CompareCells);
            bool found = false;
            int bestDistance = int.MaxValue;
            HexCoord bestSource = source.Anchor, bestTarget = target.Anchor;
            string rejection = null;
            foreach (HexCoord from in sources)
            foreach (HexCoord to in targets)
            {
                var distance = QueryMetricDistance(from, to, SpatialQueryKind.Attack);
                if (!distance.IsReachable) { rejection ??= distance.Reason; continue; }
                var sight = QueryLineOfSight(from, to);
                if (!sight.HasLineOfSight) { rejection ??= sight.Reason; continue; }
                if (found && distance.DistanceUnits >= bestDistance) continue;
                found = true; bestDistance = distance.DistanceUnits; bestSource = from; bestTarget = to;
            }
            if (!found)
                return new SpatialContactResult(bestSource, bestTarget, -1, false, false,
                    rejection ?? SpatialQueryReasons.NoLegalPath, limits.UnitsPerRange);
            string reason = bestDistance < checked(min * limits.UnitsPerRange) ? SpatialQueryReasons.BelowMinimumRange :
                bestDistance > checked(max * limits.UnitsPerRange) ? SpatialQueryReasons.AboveMaximumRange : SpatialQueryReasons.Ok;
            return new SpatialContactResult(bestSource, bestTarget, bestDistance,
                reason.Length == 0, true, reason, limits.UnitsPerRange);
        }

        private static bool IsValidPose(SpatialPose pose) => pose.BodyScaleRank > 0 && pose.Facing >= 0 &&
            pose.Facing < 6 && (pose.Footprint == SpatialFootprintKind.Single || pose.Footprint == SpatialFootprintKind.AdjacentPair);

        private static bool Contains(IReadOnlyCollection<HexCoord> occupied, HexCoord cell)
        {
            if (occupied == null) return false;
            if (occupied is HashSet<HexCoord> set) return set.Contains(cell);
            foreach (HexCoord candidate in occupied) if (candidate == cell) return true;
            return false;
        }

        private static int CompareCells(HexCoord left, HexCoord right)
        {
            int q = left.Q.CompareTo(right.Q);
            return q == 0 ? left.R.CompareTo(right.R) : q;
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
            if (!cells.ContainsKey(start) || !cells.ContainsKey(target))
                return new SpatialMetricDistanceResult(false, -1, SpatialQueryReasons.MissingCell);
            if (canTraverse?.Invoke(start) == false || canTraverse?.Invoke(target) == false)
                return new SpatialMetricDistanceResult(false, -1, SpatialQueryReasons.MissingCell);
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
            SpatialPose start,
            HexCoord target,
            int actionPointBudgetUnits,
            int movementEfficiencyMilliUnits,
            IReadOnlyCollection<HexCoord> occupied = null,
            int? targetFacing = null)
        {
            ValidateMovementInputs(actionPointBudgetUnits, movementEfficiencyMilliUnits);
            if (targetFacing.HasValue && (targetFacing.Value < 0 || targetFacing.Value >= 6))
                throw new ArgumentOutOfRangeException(nameof(targetFacing));
            if (movementEfficiencyMilliUnits == 0)
                return UnpricedMovementPath(SpatialQueryReasons.MovementEfficiencyZero);
            if (start.Anchor == target)
                return UnpricedMovementPath(SpatialQueryReasons.DestinationUnchanged);
            var traversal = TraversePoses(start, actionPointBudgetUnits, movementEfficiencyMilliUnits,
                occupied, target, targetFacing);
            if (!traversal.Destination.HasValue)
                return UnpricedMovementPath(traversal.FirstRejection ?? SpatialQueryReasons.NoLegalPath);
            var steps = new List<SpatialMovementStep>();
            var current = traversal.Destination.Value;
            while (current != start)
            {
                SpatialMovementStep step = traversal.Previous[current];
                steps.Add(step);
                current = step.From;
            }
            steps.Reverse();
            return new SpatialMovementPathResult(steps, limits.UnitsPerRange,
                traversal.Costs[traversal.Destination.Value], SpatialQueryReasons.Ok);
        }

        /// <summary>
        /// 与 QueryMovementPath 同一边遍历、同一 AP 边权的可达集合；预算与报价均为 AP 最小单位，
        /// 不先反算整格预算裁掉压缩边。v_milli=0 拒绝自主移动，返回空集合而不进入除法。
        /// </summary>
        public IReadOnlyDictionary<SpatialPose, int> FindReachableWithinActionPoints(
            SpatialPose start,
            int actionPointBudgetUnits,
            int movementEfficiencyMilliUnits,
            IReadOnlyCollection<HexCoord> occupied = null)
        {
            ValidateMovementInputs(actionPointBudgetUnits, movementEfficiencyMilliUnits);
            if (movementEfficiencyMilliUnits == 0)
                return new Dictionary<SpatialPose, int>();
            return TraversePoses(start, actionPointBudgetUnits, movementEfficiencyMilliUnits, occupied, null, null).Costs;
        }

        private SpatialMovementPathResult UnpricedMovementPath(string reason) =>
            new SpatialMovementPathResult(null, limits.UnitsPerRange, 0, reason);

        private static void ValidateMovementInputs(int actionPointBudgetUnits, int movementEfficiencyMilliUnits)
        {
            if (actionPointBudgetUnits < 0) throw new ArgumentOutOfRangeException(nameof(actionPointBudgetUnits));
            if (movementEfficiencyMilliUnits < 0) throw new ArgumentOutOfRangeException(nameof(movementEfficiencyMilliUnits));
        }

        private PoseTraversalResult TraversePoses(SpatialPose start, int budget, int efficiency,
            IReadOnlyCollection<HexCoord> occupied, HexCoord? target, int? targetFacing)
        {
            var result = new PoseTraversalResult();
            var blocked = occupied == null ? null : new HashSet<HexCoord>(occupied);
            string initial = InspectPlacement(start, blocked);
            if (initial.Length != 0) { result.FirstRejection = initial; return result; }
            var weight = new ActionPointEdgeWeight(limits.UnitsPerRange, efficiency);
            var frontier = new List<PoseFrontierEntry> { new PoseFrontierEntry(start, 0) };
            result.Costs.Add(start, 0);
            while (frontier.Count > 0)
            {
                int best = 0;
                for (int index = 1; index < frontier.Count; index++)
                    if (frontier[index].CompareTo(frontier[best]) < 0) best = index;
                PoseFrontierEntry current = frontier[best];
                frontier.RemoveAt(best);
                if (result.Costs[current.Pose] != current.Cost) continue;
                if (target.HasValue && current.Pose.Anchor == target.Value &&
                    (!targetFacing.HasValue || current.Pose.Facing == targetFacing.Value))
                { result.Destination = current.Pose; break; }
                for (int direction = 0; direction < 6; direction++)
                {
                    if (!TryMovementStep(current.Pose, direction, blocked, out var step, out string reason))
                    {
                        // Missing outer cells and occupied alternatives do not hide a meaningful rule rejection.
                        if (result.FirstRejection == null && (reason == SpatialQueryReasons.ClearanceInsufficient ||
                            reason == SpatialQueryReasons.HeightRuleUnconfigured ||
                            reason == SpatialQueryReasons.EdgeHeightMismatch ||
                            reason == SpatialQueryReasons.PairCrossHeightConnectionInvalid ||
                            reason == SpatialQueryReasons.EntityObstacle))
                            result.FirstRejection = reason;
                        continue;
                    }
                    if (!weight.TryGetEdgeCostUnits(step.Edge.MetricDistanceUnits, step.Edge.MovementBurdenUnits, out int edgeCost) ||
                        (long)current.Cost + edgeCost > int.MaxValue)
                    { result.FirstRejection ??= SpatialQueryReasons.ActionPointCostOverflow; continue; }
                    int nextCost = current.Cost + edgeCost;
                    if (nextCost > budget || result.Costs.TryGetValue(step.To, out int known) && known <= nextCost) continue;
                    result.Costs[step.To] = nextCost;
                    result.Previous[step.To] = step;
                    frontier.Add(new PoseFrontierEntry(step.To, nextCost));
                }
            }
            return result;
        }

        private bool TryMovementStep(SpatialPose from, int direction, HashSet<HexCoord> blocked,
            out SpatialMovementStep step, out string reason)
        {
            step = default;
            int delta = (direction - from.Facing + 6) % 6;
            int turnSign = delta <= 3 ? 1 : -1;
            if (!TryTurns(from, direction, turnSign, blocked, out var turns, out reason))
            {
                if (delta != 3 || !TryTurns(from, direction, -1, blocked, out turns, out reason)) return false;
            }
            SpatialPose rotated = from.WithFacing(direction);
            SpatialPose to = rotated.WithAnchor(from.Anchor.Step(direction));
            reason = InspectPlacement(to, blocked);
            if (reason.Length != 0) return false;
            SpatialEdgeInspection frontEdge = InspectEdge(from.Anchor, to.Anchor, SpatialQueryKind.Movement,
                bodyScaleRank: from.BodyScaleRank);
            if (!frontEdge.IsLegal) { reason = frontEdge.Reason; return false; }
            if (from.Footprint == SpatialFootprintKind.AdjacentPair)
            {
                HexCoord rear = rotated.Anchor.Step((direction + 3) % 6);
                var rearEdge = InspectEdge(rear, from.Anchor, SpatialQueryKind.Movement, bodyScaleRank: from.BodyScaleRank);
                if (!rearEdge.IsLegal) { reason = rearEdge.Reason; return false; }
            }
            long burdenUnits = (long)frontEdge.MovementBurdenUnits + cells[to.Anchor].MovementBurdenUnits;
            if (burdenUnits > int.MaxValue) { reason = SpatialQueryReasons.ActionPointCostOverflow; return false; }
            step = new SpatialMovementStep(from, to, turns,
                new SpatialMovementEdgeCost(from.Anchor, to.Anchor, frontEdge.MetricDistanceUnits, (int)burdenUnits));
            reason = SpatialQueryReasons.Ok;
            return true;
        }

        private bool TryTurns(SpatialPose from, int direction, int sign, HashSet<HexCoord> blocked,
            out IReadOnlyList<int> turns, out string reason)
        {
            var facings = new List<int>();
            turns = facings;
            SpatialPose current = from;
            while (current.Facing != direction)
            {
                SpatialPose next = current.WithFacing((current.Facing + sign + 6) % 6);
                if (from.Footprint == SpatialFootprintKind.AdjacentPair)
                {
                    HexCoord oldRear = current.Anchor.Step((current.Facing + 3) % 6);
                    HexCoord newRear = next.Anchor.Step((next.Facing + 3) % 6);
                    var sweep = InspectEdge(oldRear, newRear, SpatialQueryKind.Movement, bodyScaleRank: from.BodyScaleRank);
                    if (!sweep.IsLegal) { reason = sweep.Reason; return false; }
                    reason = InspectPlacement(next, blocked);
                    if (reason.Length != 0) return false;
                }
                facings.Add(next.Facing);
                current = next;
            }
            reason = SpatialQueryReasons.Ok;
            return true;
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
                             edge.Reason == SpatialQueryReasons.EdgeHeightMismatch ||
                             edge.Reason == SpatialQueryReasons.EntityObstacle))
                            firstRejection = edge.Reason;
                        continue;
                    }

                    long burdenUnits = (long)edge.MovementBurdenUnits + cells[neighbor].MovementBurdenUnits;
                    if (burdenUnits > int.MaxValue) continue;
                    int movementBurden = kind == SpatialQueryKind.Movement ? (int)burdenUnits : 0;
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

        private readonly struct PoseFrontierEntry : IComparable<PoseFrontierEntry>
        {
            public PoseFrontierEntry(SpatialPose pose, int cost) { Pose = pose; Cost = cost; }
            public SpatialPose Pose { get; }
            public int Cost { get; }
            public int CompareTo(PoseFrontierEntry other)
            {
                int compare = Cost.CompareTo(other.Cost);
                if (compare != 0) return compare;
                compare = CompareCells(Pose.Anchor, other.Pose.Anchor);
                return compare != 0 ? compare : Pose.Facing.CompareTo(other.Pose.Facing);
            }
        }

        private sealed class PoseTraversalResult
        {
            public readonly Dictionary<SpatialPose, int> Costs = new Dictionary<SpatialPose, int>();
            public readonly Dictionary<SpatialPose, SpatialMovementStep> Previous = new Dictionary<SpatialPose, SpatialMovementStep>();
            public SpatialPose? Destination;
            public string FirstRejection;
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
