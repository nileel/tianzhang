using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    public sealed class R1SpatialSearchTests
    {
        [TestCase(false, int.MaxValue)]
        [TestCase(true, int.MaxValue)]
        [TestCase(false, 3)]
        [TestCase(true, 3)]
        public void CompleteSearchReusesExactCostsAndPosePredecessors(bool pair, int maximumEdges)
        {
            var board = Grid();
            var start = new SpatialPose(new HexCoord(0, 0), 0,
                pair ? SpatialFootprintKind.AdjacentPair : SpatialFootprintKind.Single, pair ? 2 : 1);
            var policy = new SpatialMovementPolicy(maximumEdges: maximumEdges);
            var occupied = new[] { new HexCoord(2, 1) };
            const int budget = 100000, efficiency = 7000;
            var expected = board.FindReachableWithinActionPoints(start, budget, efficiency, occupied, policy);
            var paths = expected.Keys.Where(p => p.Anchor != start.Anchor).ToDictionary(p => p,
                p => board.QueryMovementPath(start, p.Anchor, budget, efficiency, occupied, p.Facing, policy));
            int searches = 0;
            SpatialQueryMeasurement.Completed = (name, _) => { if (name == "movement_search") searches++; };
            try
            {
                using (board.BeginQueryBatch())
                {
                    var actual = board.FindReachableWithinActionPoints(start, budget, efficiency, occupied, policy);
                    CollectionAssert.AreEquivalent(expected, actual);
                    foreach (var pairOfPaths in paths)
                    {
                        var pose = pairOfPaths.Key;
                        var route = board.QueryMovementPath(start, pose.Anchor, budget, efficiency, occupied, pose.Facing, policy);
                        Assert.AreEqual(expected[pose], route.ActionPointCostUnits);
                        Assert.AreEqual(Describe(pairOfPaths.Value), Describe(route));
                        Assert.LessOrEqual(route.Steps.Count, maximumEdges);
                    }
                    Assert.AreEqual(1, searches, "All endpoints must share one complete search.");
                }
                var destination = paths.Keys.First();
                board.QueryMovementPath(start, destination.Anchor, budget, efficiency, occupied, destination.Facing, policy);
                Assert.AreEqual(2, searches, "A batch cannot survive its read-only call site.");
            }
            finally { SpatialQueryMeasurement.Completed = null; }
        }

        [Test]
        public void ReuseChecksOccupancyPolicyBudgetSpeedPoseAndBoard()
        {
            var board = Grid();
            var start = new SpatialPose(new HexCoord(0, 0));
            var target = new HexCoord(1, 0);
            var occupied = new List<HexCoord>();
            var queries = new (SpatialPose start, int budget, int speed, SpatialMovementPolicy policy)[]
            {
                (start, 100000, 7000, new SpatialMovementPolicy()),
                (start, 100000, 7000, new SpatialMovementPolicy(2)),
                (start, 2000, 7000, SpatialMovementPolicy.Normal),
                (start, 100000, 14000, SpatialMovementPolicy.Normal),
                (start.WithFacing(2), 100000, 7000, SpatialMovementPolicy.Normal),
                (start, 100000, 7000, new SpatialMovementPolicy(maximumEdges: 0)),
                (start, 100000, 7000, new SpatialMovementPolicy(blockedReason: "test_blocked")),
                (start, 100000, 7000, new SpatialMovementPolicy(sealedEdges: new[] {new SpatialDirectedEdge(start.Anchor, target)})),
            };
            var expected = queries.Select(q => board.QueryMovementPath(q.start, target, q.budget, q.speed, occupied, policy: q.policy)).ToArray();
            using (board.BeginQueryBatch())
            {
                board.FindReachableWithinActionPoints(start, 100000, 7000, occupied);
                for (int i = 0; i < queries.Length; i++)
                {
                    var q = queries[i];
                    Assert.AreEqual(Describe(expected[i]), Describe(board.QueryMovementPath(q.start, target, q.budget, q.speed, occupied, policy: q.policy)));
                }
                occupied.Add(target);
                Assert.IsEmpty(board.QueryMovementPath(start, target, 100000, 7000, occupied).Path);
                var changed = board.WithCellMovementBurdens(new Dictionary<HexCoord, int> { [target] = 1000 });
                Assert.Greater(changed.QueryMovementPath(start, target, 100000, 7000).ActionPointCostUnits, expected[0].ActionPointCostUnits);
            }
        }

        [Test]
        public void RangeBatchPreservesEveryDistanceSightAndRejection()
        {
            var board = Grid();
            var source = new HexCoord(0, 0);
            var targets = board.Cells.ToArray();
            var expected = targets.ToDictionary(p => p, p => board.QueryRangeEntry(source, p, 1, 2, SpatialQueryKind.Attack, true));
            int searches = 0;
            SpatialQueryMeasurement.Completed = (name, _) => { if (name == "metric_search") searches++; };
            try
            {
                var range = board.QueryRange(source, 1, 2, SpatialQueryKind.Attack, true);
                foreach (var target in targets)
                {
                    Assert.IsTrue(range.TryGet(target, out var actual));
                    Assert.AreEqual(expected[target], actual);
                }
                Assert.AreEqual(2, searches, "One attack search and one sight search, rather than one per endpoint.");
            }
            finally { SpatialQueryMeasurement.Completed = null; }
        }

        [Test]
        public void CompleteMetricSearchSkipsUnrelatedEdgesBeyondIntegerBudget()
        {
            var start=new HexCoord(0,0);var target=new HexCoord(1,0);
            var a=new HexCoord(0,1);var b=new HexCoord(0,2);
            var cells=new[] {start,target,a,b}.ToDictionary(p=>p,p=>new SpatialCellRules());
            var edges=new Dictionary<SpatialDirectedEdge,SpatialEdgeRules>
            {
                [new SpatialDirectedEdge(start,target)]=new SpatialEdgeRules(1,true,true),
                [new SpatialDirectedEdge(start,a)]=new SpatialEdgeRules(2,true,true),
                [new SpatialDirectedEdge(a,b)]=new SpatialEdgeRules(int.MaxValue,true,true),
            };
            var board=new SpatialQueryBoard(cells,edges,new SpatialQueryLimits(1000,16));
            var expected=board.QueryMetricDistance(start,target,SpatialQueryKind.Attack);
            Assert.IsTrue(expected.IsReachable);
            using(board.BeginQueryBatch())
                Assert.AreEqual(expected,board.QueryMetricDistance(start,target,SpatialQueryKind.Attack));
        }

        [Test]
        public void MetricBatchNeverReusesDelegateTraversalOrDifferentLimits()
        {
            var board = Grid(); var source = new HexCoord(0, 0); var target = new HexCoord(2, 0);
            var full = board.QueryMetricDistance(source, target, SpatialQueryKind.Attack);
            using (board.BeginQueryBatch())
            {
                Assert.AreEqual(full, board.QueryMetricDistance(source, target, SpatialQueryKind.Attack));
                Assert.IsFalse(board.QueryMetricDistance(source, target, SpatialQueryKind.Attack, 0).IsReachable);
                Assert.IsFalse(board.QueryMetricDistance(source, target, SpatialQueryKind.Attack, canTraverse: p => p != target).IsReachable);
                Assert.AreEqual(full, board.QueryMetricDistance(source, target, SpatialQueryKind.Attack));
            }
        }

        private static string Describe(SpatialMovementPathResult path) => path.Reason + ":" + path.ActionPointCostUnits + ":" +
            string.Join(";", path.Steps.Select(s => s.From.Anchor + "/" + s.From.Facing + ">" + s.To.Anchor + "/" + s.To.Facing +
                "/" + string.Join(",", s.TurnFacings) + "/" + s.Edge.MetricDistanceUnits + "/" + s.Edge.MovementBurdenUnits));

        private static SpatialQueryBoard Grid()
        {
            var cells = new Dictionary<HexCoord, SpatialCellRules>();
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            for (int q = -2; q <= 3; q++) for (int r = -2; r <= 3; r++)
                cells.Add(new HexCoord(q, r), new SpatialCellRules(0, false, q == 1 && r == 1, false, (q + r + 4) % 3 * 100));
            foreach (var cell in cells.Keys)
                for (int direction = 0; direction < 6; direction++)
                {
                    var next = cell.Step(direction);
                    if (cells.ContainsKey(next)) edges.Add(new SpatialDirectedEdge(cell, next),
                        new SpatialEdgeRules(250 + (direction % 3) * 375, true, true));
                }
            return new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(1000, 16));
        }
    }
}
