using System;
using System.Collections.Generic;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    /// <summary>
    /// U-COMBAT-AP-MOVE-01：Spatial 合法路径的原始边度量无损传入同源 AP 报价，整路与分段费用完全一致。
    /// 夹具对应 开发管理/统一行动力公共合同.txt §十 APC-F02（同路分段）与 APC-F03（原始负担／极端），
    /// 为技术输入，不是生产数值或平衡结论。
    /// </summary>
    public sealed class CombatActionPointMovementTests
    {
        private static readonly HexCoord Origin = new HexCoord(0, 0);

        [Test]
        public void PlainEdgesArePricedPerEdgeAndNeverRoundedAgainAtThePathEnd()
        {
            SpatialQueryBoard board = CreateLineBoard(edgeCount: 3);
            var target = new HexCoord(3, 0);

            SpatialMovementPathResult whole = board.QueryMovementPath(
                Origin,
                target,
                actionPointBudgetUnits: 100000,
                movementEfficiencyMilliUnits: 3000);

            Assert.AreEqual(SpatialQueryReasons.Ok, whole.Reason);
            Assert.AreEqual(1000, whole.UnitsPerRange);
            Assert.AreEqual(3, whole.Path.Count);
            Assert.AreEqual(3, whole.Edges.Count);
            for (int index = 0; index < whole.Edges.Count; index++)
                Assert.AreEqual(1000, whole.Edges[index].DistanceUnits);
            Assert.AreEqual(33333, SingleEdgeCostUnits(board, Origin, new HexCoord(1, 0), 3000));
            Assert.AreEqual(99999, whole.ActionPointCostUnits, "Three edges of 33333 units equal 99.999 AP.");
            Assert.AreNotEqual(100000, whole.ActionPointCostUnits,
                "Rounded once at the path end the same route would cost 100000 units, so each edge is priced on its own.");

            SpatialMovementPathResult withinBudget = board.QueryMovementPath(
                Origin, target, actionPointBudgetUnits: 99999, movementEfficiencyMilliUnits: 3000);
            Assert.AreEqual(SpatialQueryReasons.Ok, withinBudget.Reason);
            Assert.AreEqual(99999, withinBudget.ActionPointCostUnits);

            SpatialMovementPathResult shortBudget = board.QueryMovementPath(
                Origin, target, actionPointBudgetUnits: 99998, movementEfficiencyMilliUnits: 3000);
            Assert.AreEqual(SpatialQueryReasons.NoLegalPath, shortBudget.Reason);
            Assert.AreEqual(0, shortBudget.ActionPointCostUnits);
            Assert.IsEmpty(shortBudget.Path);
            Assert.IsEmpty(shortBudget.Edges);
        }

        [Test]
        public void WholePathCostEqualsSplitSegmentsWithoutRoundingReset()
        {
            SpatialQueryBoard board = CreateLineBoard(edgeCount: 3);
            var mid = new HexCoord(2, 0);
            var target = new HexCoord(3, 0);

            SpatialMovementPathResult whole = board.QueryMovementPath(Origin, target, 100000, 3000);
            SpatialMovementPathResult firstSegment = board.QueryMovementPath(Origin, mid, 100000, 3000);
            SpatialMovementPathResult secondSegment = board.QueryMovementPath(mid, target, 100000, 3000);

            Assert.AreEqual(SpatialQueryReasons.Ok, firstSegment.Reason);
            Assert.AreEqual(SpatialQueryReasons.Ok, secondSegment.Reason);
            Assert.AreEqual(66666, firstSegment.ActionPointCostUnits);
            Assert.AreEqual(33333, secondSegment.ActionPointCostUnits);
            Assert.AreEqual(
                whole.ActionPointCostUnits,
                firstSegment.ActionPointCostUnits + secondSegment.ActionPointCostUnits,
                "A split move prices the same edges, so no per-command rounding is reset between segments.");

            Assert.AreEqual(whole.Edges[0].From, firstSegment.Edges[0].From);
            Assert.AreEqual(whole.Edges[1].To, firstSegment.Edges[1].To);
            Assert.AreEqual(whole.Edges[2].From, secondSegment.Edges[0].From);
            Assert.AreEqual(whole.Edges[2].To, secondSegment.Edges[0].To);

            SpatialMovementPathResult requoted = board.QueryMovementPath(mid, target, 100000, 3000);
            Assert.AreEqual(secondSegment.ActionPointCostUnits, requoted.ActionPointCostUnits);
        }

        [Test]
        public void CompressedExpandedAndBurdenedEdgesUseRawDistanceUnits()
        {
            var board = new SpatialQueryBoard(
                CellsWithBurden(count: 3, burdenAt: index => index == 3 ? 500 : 0),
                LineEdges(edgeCount: 3, metricAt: index => index switch { 0 => 500, 1 => 2000, _ => 1000 }),
                new SpatialQueryLimits(1000, 16));
            var first = new HexCoord(1, 0);
            var second = new HexCoord(2, 0);
            var third = new HexCoord(3, 0);

            SpatialMovementPathResult burdened = board.QueryMovementPath(Origin, third, 100000, 5000);

            Assert.AreEqual(SpatialQueryReasons.Ok, burdened.Reason);
            Assert.AreEqual(10000, SingleEdgeCostUnits(board, Origin, first, 5000), "Compressed edge d_e=500.");
            Assert.AreEqual(40000, SingleEdgeCostUnits(board, first, second, 5000), "Expanded edge d_e=2000.");
            Assert.AreEqual(30000, SingleEdgeCostUnits(board, second, third, 5000), "Plain 1000 edge into a 500 burden cell.");
            Assert.AreEqual(80000, burdened.ActionPointCostUnits);

            Assert.AreEqual(500, burdened.Edges[0].MetricDistanceUnits);
            Assert.AreEqual(0, burdened.Edges[0].MovementBurdenUnits);
            Assert.AreEqual(2000, burdened.Edges[1].MetricDistanceUnits);
            Assert.AreEqual(0, burdened.Edges[1].MovementBurdenUnits);
            Assert.AreEqual(1000, burdened.Edges[2].MetricDistanceUnits);
            Assert.AreEqual(500, burdened.Edges[2].MovementBurdenUnits);
            Assert.AreEqual(1500, burdened.Edges[2].DistanceUnits);
            Assert.AreEqual(second, burdened.Edges[2].From);
            Assert.AreEqual(third, burdened.Edges[2].To);
            Assert.AreEqual(third, burdened.Path[burdened.Path.Count - 1]);
        }

        [Test]
        public void ShortestPositiveEdgeKeepsOneMinimumUnit()
        {
            var board = new SpatialQueryBoard(
                CellsWithBurden(count: 1),
                LineEdges(edgeCount: 1, metricAt: _ => 1),
                new SpatialQueryLimits(1000, 16));

            SpatialMovementPathResult quote = board.QueryMovementPath(
                Origin,
                new HexCoord(1, 0),
                actionPointBudgetUnits: 1,
                movementEfficiencyMilliUnits: int.MaxValue);

            Assert.AreEqual(SpatialQueryReasons.Ok, quote.Reason);
            Assert.AreEqual(1, quote.ActionPointCostUnits, "A positive-cost legal edge is never free.");
            Assert.AreEqual(1, quote.Edges[0].DistanceUnits);
        }

        [Test]
        public void ArithmeticOverflowIsRejectedWithAStableReason()
        {
            var hugeMetric = new SpatialQueryBoard(
                CellsWithBurden(count: 1),
                LineEdges(edgeCount: 1, metricAt: _ => int.MaxValue),
                new SpatialQueryLimits(1, 16));
            var hugeBurden = new SpatialQueryBoard(
                CellsWithBurden(count: 1, burdenAt: _ => int.MaxValue),
                LineEdges(edgeCount: 1, metricAt: _ => 1),
                new SpatialQueryLimits(1, 16));

            SpatialMovementPathResult metricQuote = hugeMetric.QueryMovementPath(
                Origin, new HexCoord(1, 0), int.MaxValue, movementEfficiencyMilliUnits: 1);
            SpatialMovementPathResult burdenQuote = hugeBurden.QueryMovementPath(
                Origin, new HexCoord(1, 0), int.MaxValue, movementEfficiencyMilliUnits: 1);

            Assert.AreEqual(SpatialQueryReasons.ActionPointCostOverflow, metricQuote.Reason);
            Assert.AreEqual(SpatialQueryReasons.ActionPointCostOverflow, burdenQuote.Reason,
                "A d_e that cannot be represented as int is a stable data error, not a negative or clamped price.");
            Assert.AreEqual(0, metricQuote.ActionPointCostUnits);
            Assert.AreEqual(0, burdenQuote.ActionPointCostUnits);
            Assert.IsEmpty(metricQuote.Path);
            Assert.IsEmpty(burdenQuote.Path);
            Assert.IsFalse(hugeMetric.FindReachableWithinActionPoints(
                Origin, int.MaxValue, 1).ContainsKey(new HexCoord(1, 0)));
        }

        [Test]
        public void ZeroMovementEfficiencyRefusesVoluntaryMovementWithoutDividing()
        {
            SpatialQueryBoard board = CreateLineBoard(edgeCount: 3);
            var target = new HexCoord(3, 0);

            SpatialMovementPathResult quote = board.QueryMovementPath(
                Origin, target, actionPointBudgetUnits: 100000, movementEfficiencyMilliUnits: 0);

            Assert.AreEqual(SpatialQueryReasons.MovementEfficiencyZero, quote.Reason);
            Assert.AreEqual(0, quote.ActionPointCostUnits);
            Assert.IsEmpty(quote.Path);
            Assert.IsEmpty(quote.Edges);
            Assert.IsEmpty(board.FindReachableWithinActionPoints(Origin, 100000, 0));
        }

        [Test]
        public void NegativeMovementEfficiencyAndNegativeBudgetAreDataErrors()
        {
            SpatialQueryBoard board = CreateLineBoard(edgeCount: 1);

            Assert.Throws<ArgumentOutOfRangeException>(() => board.QueryMovementPath(
                Origin, new HexCoord(1, 0), 100000, movementEfficiencyMilliUnits: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => board.QueryMovementPath(
                Origin, new HexCoord(1, 0), actionPointBudgetUnits: -1, movementEfficiencyMilliUnits: 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => board.FindReachableWithinActionPoints(
                Origin, 100000, movementEfficiencyMilliUnits: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => board.FindReachableWithinActionPoints(
                Origin, actionPointBudgetUnits: -1, movementEfficiencyMilliUnits: 1000));
        }

        [Test]
        public void UnchangedDestinationIsRejectedInsteadOfQuotedAsFreeMovement()
        {
            SpatialQueryBoard board = CreateLineBoard(edgeCount: 1);

            SpatialMovementPathResult quote = board.QueryMovementPath(
                Origin, Origin, actionPointBudgetUnits: 100000, movementEfficiencyMilliUnits: 3000);

            Assert.AreEqual(SpatialQueryReasons.DestinationUnchanged, quote.Reason);
            Assert.AreEqual(0, quote.ActionPointCostUnits);
            Assert.IsEmpty(quote.Path);
        }

        [Test]
        public void BudgetOnlyGatesReachabilityAndNeverRepricesEdges()
        {
            SpatialQueryBoard board = CreateLineBoard(edgeCount: 3);
            var target = new HexCoord(3, 0);

            SpatialMovementPathResult exact = board.QueryMovementPath(Origin, target, 99999, 3000);
            SpatialMovementPathResult grown = board.QueryMovementPath(Origin, target, 1200000, 3000);

            Assert.AreEqual(SpatialQueryReasons.Ok, exact.Reason);
            Assert.AreEqual(SpatialQueryReasons.Ok, grown.Reason);
            Assert.AreEqual(exact.ActionPointCostUnits, grown.ActionPointCostUnits,
                "The AP upper bound never enters the movement price numerator.");
        }

        [Test]
        public void MovementEfficiencyChangeRepricesTheNextQuote()
        {
            SpatialQueryBoard board = CreateLineBoard(edgeCount: 3);
            var target = new HexCoord(3, 0);

            SpatialMovementPathResult slow = board.QueryMovementPath(Origin, target, 100000, 3000);
            SpatialMovementPathResult fast = board.QueryMovementPath(Origin, target, 100000, 5000);

            Assert.AreEqual(99999, slow.ActionPointCostUnits);
            Assert.AreEqual(60000, fast.ActionPointCostUnits);
            Assert.AreEqual(20000, SingleEdgeCostUnits(board, Origin, new HexCoord(1, 0), 5000));
        }

        [Test]
        public void OccupiedAndBlockedCellsStopThePricedTraversal()
        {
            SpatialQueryBoard board = CreateLineBoard(edgeCount: 3);
            var mid = new HexCoord(2, 0);
            var target = new HexCoord(3, 0);

            SpatialMovementPathResult occupiedMid = board.QueryMovementPath(
                Origin, target, 100000, 3000, new[] { mid });
            SpatialMovementPathResult occupiedTarget = board.QueryMovementPath(
                Origin, target, 100000, 3000, new[] { target });

            Assert.AreEqual(SpatialQueryReasons.NoLegalPath, occupiedMid.Reason,
                "An occupied cell blocks the only legal line instead of becoming a detour.");
            Assert.AreEqual(SpatialQueryReasons.NoLegalPath, occupiedTarget.Reason);
            Assert.IsFalse(board.FindReachableWithinActionPoints(
                Origin, 100000, 3000, new[] { mid }).ContainsKey(target));

            var cells = CellsWithBurden(count: 3);
            cells[mid] = new SpatialCellRules(0, false, false, true, 0);
            var obstructed = new SpatialQueryBoard(
                cells,
                LineEdges(edgeCount: 3, metricAt: _ => 1000),
                new SpatialQueryLimits(1000, 16));

            SpatialMovementPathResult entityObstacle = obstructed.QueryMovementPath(Origin, target, 100000, 3000);
            Assert.AreEqual(SpatialQueryReasons.EntityObstacle, entityObstacle.Reason);
        }

        [Test]
        public void ForcedMovementKeepsItsMetricBudgetAndIsNotActionPointPriced()
        {
            SpatialQueryBoard board = CreateLineBoard(edgeCount: 3);
            SpatialMovementPathResult voluntary = board.QueryMovementPath(
                Origin, new HexCoord(2, 0), 100000, 3000);

            SpatialForcedMovementResult forced = board.ResolveForcedMovement(
                Origin, direction: 0, distanceBudget: 2);

            Assert.AreEqual(new HexCoord(2, 0), forced.Position);
            Assert.AreEqual(2000, forced.ConsumedDistanceUnits);
            Assert.AreEqual(SpatialQueryReasons.DistanceBudgetExhausted, forced.Reason);
            Assert.AreEqual(66666, voluntary.ActionPointCostUnits);
            Assert.AreNotEqual(voluntary.ActionPointCostUnits, forced.ConsumedDistanceUnits,
                "Forced movement stays on the raw metric budget and never charges the target's action points.");
        }

        [Test]
        public void CombatBoundaryMovementResultCarriesRawEdgesUnitsPerRangeAndActionPointCost()
        {
            SpatialQueryBoard board = CreateLineBoard(edgeCount: 3);
            var target = new HexCoord(3, 0);
            SpatialMovementPathResult quote = board.QueryMovementPath(Origin, target, 100000, 3000);

            var boundary = new CombatMovementQueryResult(
                true,
                SpatialQueryReasons.Ok,
                quote.Path,
                movementCost: 3,
                quote.Edges,
                quote.UnitsPerRange,
                quote.ActionPointCostUnits);

            Assert.IsTrue(boundary.IsReachable);
            Assert.AreEqual(99999, boundary.ActionPointCostUnits);
            Assert.AreEqual(1000, boundary.UnitsPerRange);
            Assert.AreEqual(quote.Path.Count, boundary.MovementEdges.Count);
            for (int index = 0; index < quote.Edges.Count; index++)
            {
                Assert.AreEqual(quote.Edges[index].From, boundary.MovementEdges[index].From);
                Assert.AreEqual(quote.Edges[index].To, boundary.MovementEdges[index].To);
                Assert.AreEqual(quote.Edges[index].MetricDistanceUnits, boundary.MovementEdges[index].MetricDistanceUnits);
                Assert.AreEqual(quote.Edges[index].MovementBurdenUnits, boundary.MovementEdges[index].MovementBurdenUnits);
            }

            var unpriced = new CombatMovementQueryResult(true, string.Empty, quote.Path, 3);
            Assert.IsEmpty(unpriced.MovementEdges, "A boundary without an AP payload stays explicitly empty.");
            Assert.AreEqual(0, unpriced.UnitsPerRange);
            Assert.AreEqual(0, unpriced.ActionPointCostUnits);
        }

        private static int SingleEdgeCostUnits(
            SpatialQueryBoard board,
            HexCoord from,
            HexCoord to,
            int movementEfficiencyMilliUnits)
        {
            SpatialMovementPathResult single = board.QueryMovementPath(
                from, to, actionPointBudgetUnits: int.MaxValue, movementEfficiencyMilliUnits: movementEfficiencyMilliUnits);
            Assert.AreEqual(SpatialQueryReasons.Ok, single.Reason);
            Assert.AreEqual(1, single.Edges.Count);
            return single.ActionPointCostUnits;
        }

        private static SpatialQueryBoard CreateLineBoard(int edgeCount, int unitsPerRange = 1000) =>
            new SpatialQueryBoard(
                CellsWithBurden(edgeCount),
                LineEdges(edgeCount, metricAt: _ => 1000),
                new SpatialQueryLimits(unitsPerRange, 16));

        private static Dictionary<HexCoord, SpatialCellRules> CellsWithBurden(
            int count,
            Func<int, int> burdenAt = null)
        {
            var cells = new Dictionary<HexCoord, SpatialCellRules>();
            for (int index = 0; index <= count; index++)
            {
                cells[new HexCoord(index, 0)] = new SpatialCellRules(
                    0,
                    false,
                    false,
                    false,
                    burdenAt?.Invoke(index) ?? 0);
            }
            return cells;
        }

        private static Dictionary<SpatialDirectedEdge, SpatialEdgeRules> LineEdges(
            int edgeCount,
            Func<int, int> metricAt)
        {
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            for (int index = 0; index < edgeCount; index++)
            {
                var from = new HexCoord(index, 0);
                var to = new HexCoord(index + 1, 0);
                edges[new SpatialDirectedEdge(from, to)] = new SpatialEdgeRules(
                    metricAt?.Invoke(index) ?? 1000,
                    allowsMovement: true,
                    allowsEffects: true);
            }
            return edges;
        }
    }
}
