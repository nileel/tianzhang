using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    public sealed class R2IncomingMetricTests
    {
        [Test]
        public void IncomingBatchPreservesDirectedMetricsContactsAndScope()
        {
            var cells = Enumerable.Range(0, 5).Select(q => new HexCoord(q, 0)).ToDictionary(c => c, _ => new SpatialCellRules());
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            for (int i = 0; i < 4; i++)
            {
                edges.Add(new SpatialDirectedEdge(new HexCoord(i, 0), new HexCoord(i + 1, 0)), new SpatialEdgeRules(2, true, true));
                if (i != 3) edges.Add(new SpatialDirectedEdge(new HexCoord(i + 1, 0), new HexCoord(i, 0)), new SpatialEdgeRules(3 + i, true, true));
            }
            var board = new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(2, 16));
            CompareAll(board);
            CompareAll(board.WithMetricOverrides(new[] { new SpatialMetricOverride("compression", new HexCoord(0, 0), new HexCoord(1, 0), 1) }));
            using (board.BeginQueryBatch(metricDistancesToTarget: true))
            {
                Assert.AreEqual(8, board.QueryMetricDistance(new HexCoord(0, 0), new HexCoord(4, 0), SpatialQueryKind.Attack).DistanceUnits);
                var compressed = board.WithMetricOverrides(new[] { new SpatialMetricOverride("compression", new HexCoord(0, 0), new HexCoord(1, 0), 1) });
                Assert.AreEqual(7, compressed.QueryMetricDistance(new HexCoord(0, 0), new HexCoord(4, 0), SpatialQueryKind.Attack).DistanceUnits);
            }
            var blockedEdges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>(edges)
            { [new SpatialDirectedEdge(new HexCoord(1,0), new HexCoord(2,0))] = new SpatialEdgeRules(2,true,true,effectBlockerMask:1) };
            var blocked = new SpatialQueryBoard(cells, blockedEdges, new SpatialQueryLimits(2,16));
            CompareAll(blocked);
            int searches = 0;
            SpatialQueryMeasurement.Completed = (name, _) => { if (name == "metric_search") searches++; };
            try
            {
                using (board.BeginQueryBatch(metricDistancesToTarget: true))
                {
                    foreach (var source in cells.Keys)
                    foreach (var kind in new[] { SpatialQueryKind.Attack, SpatialQueryKind.Sight })
                        Assert.IsTrue(board.QueryMetricDistance(source, new HexCoord(4, 0), kind).IsReachable);
                    Assert.AreEqual(2, searches);
                    using (board.BeginQueryBatch())
                        board.QueryMetricDistance(new HexCoord(0, 0), new HexCoord(4, 0), SpatialQueryKind.Attack);
                    Assert.AreEqual(3, searches);
                    board.QueryMetricDistance(new HexCoord(0, 0), new HexCoord(4, 0), SpatialQueryKind.Attack);
                    Assert.AreEqual(3, searches, "Nested disposal must restore the incoming batch.");
                    Assert.IsFalse(board.QueryMetricDistance(new HexCoord(0, 0), new HexCoord(4, 0), SpatialQueryKind.Attack,
                        canTraverse: c => c.Q != 2).IsReachable);
                }
                int previous = searches;
                board.QueryMetricDistance(new HexCoord(0, 0), new HexCoord(4, 0), SpatialQueryKind.Attack);
                Assert.AreEqual(previous + 1, searches);
            }
            finally { SpatialQueryMeasurement.Completed = null; }
        }

        [TestCase(false)] [TestCase(true)]
        public void ReverseTraversalUsesTheOriginalDestinationBurden(bool hugeDestination)
        {
            var a = new HexCoord(0, 0); var b = new HexCoord(1, 0);
            var cells = new Dictionary<HexCoord, SpatialCellRules>
            {
                [a] = new SpatialCellRules(0, false, false, false, hugeDestination ? 0 : int.MaxValue),
                [b] = new SpatialCellRules(0, false, false, false, hugeDestination ? int.MaxValue : 0),
            };
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>
            { [new SpatialDirectedEdge(a, b)] = new SpatialEdgeRules(2, true, true, movementBurdenUnits: 1) };
            var board = new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(2, 16));
            var expected = board.QueryMetricDistance(a, b, SpatialQueryKind.Attack);
            Assert.AreEqual(!hugeDestination, expected.IsReachable);
            using (board.BeginQueryBatch(metricDistancesToTarget: true))
                Assert.AreEqual(expected, board.QueryMetricDistance(a, b, SpatialQueryKind.Attack));
        }

        [Test]
        public void IncomingMissPreservesForwardRejectionAndBoardIdentity()
        {
            var a = new HexCoord(0, 0); var wall = new HexCoord(1, 0); var b = new HexCoord(3, 0);
            var cells = new Dictionary<HexCoord, SpatialCellRules>
            { [a] = new(), [wall] = new(0, false, false, true, 0), [b] = new() };
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>
            { [new SpatialDirectedEdge(a, wall)] = new SpatialEdgeRules(2, true, true) };
            var board = new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(2, 16));
            var expected = board.QueryMetricDistance(a, b, SpatialQueryKind.Attack);
            Assert.AreEqual(SpatialQueryReasons.EntityObstacle, expected.Reason);
            using (board.BeginQueryBatch(metricDistancesToTarget: true))
            {
                Assert.AreEqual(expected, board.QueryMetricDistance(a, b, SpatialQueryKind.Attack));
                var other = board.WithCellMovementBurdens(new Dictionary<HexCoord, int> { [b] = 1 });
                Assert.AreEqual(expected, other.QueryMetricDistance(a, b, SpatialQueryKind.Attack));
            }
        }

        private static void CompareAll(SpatialQueryBoard board)
        {
            var queries = (from a in board.Cells from b in board.Cells
                from kind in new[] { SpatialQueryKind.Attack, SpatialQueryKind.Sight, SpatialQueryKind.Movement }
                from limit in new[] { 0, 1, 4, 16 } from mask in new[] { 0UL, ulong.MaxValue }
                select (a, b, kind, limit, mask)).ToArray();
            var expected = queries.Select(q => board.QueryMetricDistance(q.a, q.b, q.kind, q.limit, q.mask)).ToArray();
            var poses = board.Cells.Select(c => new SpatialPose(c)).Concat(new[] {
                new SpatialPose(new HexCoord(1, 0), 0, SpatialFootprintKind.AdjacentPair, 2),
                new SpatialPose(new HexCoord(2, 0), 0, SpatialFootprintKind.AdjacentPair, 2) }).ToArray();
            var contacts = (from a in poses from b in poses select (a, b)).ToArray();
            var expectedContacts = contacts.Select(q => board.QueryContact(q.a, q.b, 2, 4)).ToArray();
            using (board.BeginQueryBatch(metricDistancesToTarget: true))
            {
                for (int i = 0; i < queries.Length; i++)
                {
                    var q = queries[i];
                    Assert.AreEqual(expected[i], board.QueryMetricDistance(q.a, q.b, q.kind, q.limit, q.mask), q.ToString());
                }
                for (int i = 0; i < contacts.Length; i++)
                    Assert.AreEqual(expectedContacts[i], board.QueryContact(contacts[i].a, contacts[i].b, 2, 4));
            }
        }
    }
}
