using System;
using System.Collections.Generic;
using NUnit.Framework;
using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    public sealed class D5SpatialScaleTests
    {
        private static readonly HexCoord A = new HexCoord(5, 14);
        private static readonly HexCoord B = new HexCoord(6, 14);
        private static readonly HexCoord C = new HexCoord(7, 14);
        private static readonly HexCoord D = new HexCoord(8, 14);
        private const string Source = "demo_20261024_space_yang_scale_guardian";

        [Test]
        public void TwoCompressedEdgesReachTwoHexTargetWithOneRangeInEitherDirection()
        {
            var original = Line();
            var board = original.WithMetricOverrides(new[] { Scale(A, B, 1), Scale(B, C, 1) });
            Assert.AreEqual(2, A.TopologicalDistanceTo(C));
            Assert.IsFalse(original.QueryContact(new SpatialPose(A), new SpatialPose(C), 1, 1).IsInRange);
            foreach (var pair in new[] { (A, C), (C, A) })
            {
                var contact = board.QueryContact(new SpatialPose(pair.Item1), new SpatialPose(pair.Item2), 1, 1);
                Assert.IsTrue(contact.IsInRange, contact.Reason);
                Assert.AreEqual(2, contact.DistanceUnits, "K=2: two half-range edges total one range.");
                Assert.IsTrue(board.QueryRange(pair.Item1, 1, 1, SpatialQueryKind.Attack, true)
                    .Entries[pair.Item2].IsInRange);
                Assert.IsTrue(board.QueryRange(pair.Item1, 1, 1, SpatialQueryKind.Sight, true)
                    .Entries[pair.Item2].IsInRange);
            }
            Assert.AreEqual(C, board.ResolveForcedMovement(A, 0, 1).Position);
            Assert.AreEqual(4, original.QueryMetricDistance(A, C, SpatialQueryKind.Attack).DistanceUnits);
        }

        [Test]
        public void ExpandedAdjacentTargetIsOutsideOneRangeIncludingAlternatePaths()
        {
            var detour = new HexCoord(6, 15);
            var cells = Cells(A, B, C, detour);
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            AddEdge(edges, A, B); AddEdge(edges, B, C);
            AddEdge(edges, B, detour); AddEdge(edges, detour, C);
            var board = new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(2, 16))
                .WithMetricOverrides(new[] { Scale(B, C, 4) });
            Assert.AreEqual(1, B.TopologicalDistanceTo(C));
            Assert.AreEqual(4, board.QueryMetricDistance(B, C, SpatialQueryKind.Attack).DistanceUnits);
            Assert.AreEqual(SpatialQueryReasons.AboveMaximumRange,
                board.QueryContact(new SpatialPose(B), new SpatialPose(C), 1, 1).Reason);
            Assert.IsFalse(board.QueryRange(B, 0, 1, SpatialQueryKind.Sight, true).Entries[C].IsInRange);
            Assert.AreEqual(B, board.ResolveForcedMovement(B, 0, 1).Position);
        }

        [Test]
        public void MovementUsesMetricPlusUnchangedEdgeAndDestinationBurdens()
        {
            var cells = Cells(A, B);
            cells[B] = new SpatialCellRules(0, false, false, false, 2);
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            AddEdge(edges, A, B, new SpatialEdgeRules(2, true, true, movementBurdenUnits: 1));
            var original = new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(2, 16));
            var compressed = original.WithMetricOverrides(new[] { Scale(A, B, 1) });
            var expanded = original.WithMetricOverrides(new[] { Scale(A, B, 4) });
            var start = new SpatialPose(A);
            Assert.AreEqual(62500, original.QueryMovementPath(start, B, 100000, 4000).ActionPointCostUnits);
            var path = compressed.QueryMovementPath(start, B, 50000, 4000);
            Assert.AreEqual(1, path.Steps.Count);
            Assert.AreEqual(50000, path.ActionPointCostUnits);
            Assert.AreEqual(50000, compressed.FindReachableWithinActionPoints(start, 50000, 4000)[path.Steps[0].To]);
            Assert.AreEqual(0, compressed.QueryMovementPath(start, B, 49999, 4000).Steps.Count);
            Assert.AreEqual(87500, expanded.QueryMovementPath(start, B, 100000, 4000).ActionPointCostUnits);
            Assert.AreEqual(1, compressed.QueryMetricDistance(A, B, SpatialQueryKind.Attack).DistanceUnits,
                "Movement burdens must not become attack distance.");
            Assert.AreEqual(1, compressed.InspectEdge(A, B, SpatialQueryKind.Movement).MovementBurdenUnits);
            Assert.IsTrue(compressed.TryGetCell(B, out var cell));
            Assert.AreEqual(2, cell.MovementBurdenUnits);
        }

        [Test]
        public void ReplacementAndRemovalReadBaseRulesAndKeepOtherActiveSource()
        {
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            AddEdge(edges, A, B, new SpatialEdgeRules(3, true, true));
            AddEdge(edges, B, C, new SpatialEdgeRules(5, true, true));
            var original = new SpatialQueryBoard(Cells(A, B, C), edges, new SpatialQueryLimits(2, 16));
            var other = new SpatialMetricOverride("other-surviving-source", B, C, 4);
            var active = original.WithMetricOverrides(new[] { Scale(A, B, 1), other });
            var remaining = active.WithMetricOverrides(new[] { other });
            Assert.AreEqual(3, remaining.InspectEdge(A, B, SpatialQueryKind.Attack).MetricDistanceUnits);
            Assert.AreEqual(4, remaining.InspectEdge(B, C, SpatialQueryKind.Attack).MetricDistanceUnits);
            var removed = remaining.WithMetricOverrides(Array.Empty<SpatialMetricOverride>());
            Assert.AreEqual(3, removed.InspectEdge(A, B, SpatialQueryKind.Attack).MetricDistanceUnits);
            Assert.AreEqual(5, removed.InspectEdge(B, C, SpatialQueryKind.Attack).MetricDistanceUnits);
            Assert.AreEqual(1, active.InspectEdge(A, B, SpatialQueryKind.Attack).MetricDistanceUnits);
            Assert.AreEqual(2, active.MetricOverrides.Count);
            Assert.AreEqual("other-surviving-source", remaining.MetricOverrides[0].SourceInstanceId);
        }

        [Test]
        public void WallProjectionRebuildAndObjectTargetQueriesKeepScaleAndBlockingSeparate()
        {
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            AddEdge(edges, A, B, new SpatialEdgeRules(3, false, false,
                edgeKind: SpatialEdgeKind.EntityWall, connectionId: "wall-link"));
            var original = new SpatialQueryBoard(Cells(A, B), edges, new SpatialQueryLimits(2, 16));
            var wall = new SpatialSceneObjectRules("wall", A, B, true, true, 0,
                replacesWallConnectionId: "wall-link");
            var rubble = new SpatialSceneObjectRules("wall", A, B, false, false, 0,
                replacesWallConnectionId: "wall-link");
            var scaled = original.WithSceneObjects(new[] { wall }).WithMetricOverrides(new[] { Scale(A, B, 1) });
            Assert.AreEqual(SpatialQueryReasons.SceneObjectBlocksMovement,
                scaled.InspectEdge(A, B, SpatialQueryKind.Movement).Reason);
            Assert.IsFalse(scaled.QueryLineOfSight(A, B).HasLineOfSight);
            var policy = new SpatialAttackPolicy(SpatialMovementMode.Ground, SpatialMovementMode.Ground, 0);
            Assert.IsTrue(scaled.QuerySceneObjectContact(new SpatialPose(A), B, "wall", 0, 1, policy).IsInRange,
                "Ignoring the target's own blocker must not lose the active metric.");
            var broken = scaled.WithSceneObjects(new[] { rubble });
            Assert.AreEqual(1, broken.InspectEdge(A, B, SpatialQueryKind.Movement).MetricDistanceUnits);
            Assert.IsTrue(broken.QueryLineOfSight(A, B).HasLineOfSight);
            Assert.AreEqual(3, broken.WithMetricOverrides(Array.Empty<SpatialMetricOverride>())
                .InspectEdge(A, B, SpatialQueryKind.Movement).MetricDistanceUnits);
            var removed = scaled.WithMetricOverrides(Array.Empty<SpatialMetricOverride>());
            Assert.AreEqual(SpatialQueryReasons.SceneObjectBlocksMovement,
                removed.InspectEdge(A, B, SpatialQueryKind.Movement).Reason);
            Assert.AreEqual(3, removed.InspectEdge(A, B, SpatialQueryKind.Attack).MetricDistanceUnits);
        }

        [Test]
        public void HeightClearanceAndEffectBlockersRemainAuthoritative()
        {
            var cells = Cells(A, B);
            cells[B] = new SpatialCellRules(1, false, false, false, 0);
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            AddEdge(edges, A, B, new SpatialEdgeRules(2, true, true, 1, 1, SpatialEdgeKind.Stairs,
                connectionId: "stairs"));
            var board = new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(2, 16))
                .WithMetricOverrides(new[] { Scale(A, B, 1) });
            Assert.IsTrue(board.InspectEdge(A, B, SpatialQueryKind.Movement).IsLegal);
            Assert.AreEqual(SpatialQueryReasons.ClearanceInsufficient,
                board.InspectEdge(A, B, SpatialQueryKind.Movement, bodyScaleRank: 2).Reason);
            Assert.AreEqual(SpatialQueryReasons.DirectedEdgeBlocksEffects,
                board.InspectEdge(A, B, SpatialQueryKind.Attack).Reason);
            Assert.IsTrue(board.InspectEdge(A, B, SpatialQueryKind.Attack, 0).IsLegal);
            Assert.IsTrue(board.TryGetCell(B, out var cell));
            Assert.AreEqual(1, cell.HeightLevel);
            Assert.AreEqual(SpatialQueryReasons.VerticalReachExceeded,
                board.QueryContact(new SpatialPose(A), new SpatialPose(B), 0, 1,
                    new SpatialAttackPolicy(SpatialMovementMode.Ground, SpatialMovementMode.Ground, 0)).Reason);
        }

        [TestCase("conflicting_source")]
        [TestCase("duplicate_reverse")]
        [TestCase("missing_edge")]
        [TestCase("missing_reverse")]
        [TestCase("missing_cell")]
        [TestCase("disconnected")]
        [TestCase("mixed_scale")]
        [TestCase("capacity")]
        [TestCase("null")]
        [TestCase("precision")]
        public void InvalidSetRejectsAtomicallyWithoutReplacingPriorProjection(string failure)
        {
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            AddEdge(edges, A, B); AddEdge(edges, B, C); AddEdge(edges, C, D);
            if (failure == "missing_edge")
            { edges.Remove(new SpatialDirectedEdge(C, D)); edges.Remove(new SpatialDirectedEdge(D, C)); }
            if (failure == "missing_reverse") edges.Remove(new SpatialDirectedEdge(D, C));
            var cells = Cells(A, B, C, D);
            if (failure == "missing_cell") cells.Remove(D);
            var original = new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(failure == "precision" ? 1 : 2, 16));
            var board = failure == "precision" ? original : original.WithMetricOverrides(new[] { Scale(A, B, 4) });
            var request = new List<SpatialMetricOverride> { Scale(A, B, 1) };
            switch (failure)
            {
                case "conflicting_source": request.Add(new SpatialMetricOverride("other", B, A, 4)); break;
                case "duplicate_reverse": request.Add(Scale(B, A, 1)); break;
                case "mixed_scale": request.Add(Scale(B, C, 4)); break;
                case "capacity": request.Add(Scale(B, C, 1)); request.Add(Scale(C, D, 1)); break;
                case "null": request.Add(null); break;
                case "precision": break;
                default: request.Add(Scale(C, D, 1)); break;
            }
            Assert.Throws<ArgumentException>(() => board.WithMetricOverrides(request));
            Assert.AreEqual(failure == "precision" ? 2 : 4,
                board.InspectEdge(A, B, SpatialQueryKind.Attack).MetricDistanceUnits);
            Assert.AreEqual(2, original.InspectEdge(A, B, SpatialQueryKind.Attack).MetricDistanceUnits);
        }

        [Test]
        public void PublishedProjectionDoesNotAliasTheCallerCollection()
        {
            var input = new List<SpatialMetricOverride> { Scale(A, B, 1) };
            var board = Line().WithMetricOverrides(input);
            input.Clear();
            Assert.AreEqual(1, board.MetricOverrides.Count);
            Assert.AreEqual(Source, board.MetricOverrides[0].SourceInstanceId);
            Assert.AreEqual(A, board.MetricOverrides[0].From);
            Assert.AreEqual(B, board.MetricOverrides[0].To);
            Assert.AreEqual(1, board.InspectEdge(B, A, SpatialQueryKind.Attack).MetricDistanceUnits);
            Assert.Throws<ArgumentNullException>(() => board.WithMetricOverrides(null));
            Assert.Throws<ArgumentException>(() => new SpatialMetricOverride("", A, B, 1));
            Assert.Throws<ArgumentException>(() => Scale(A, C, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Scale(A, B, 2));
        }

        private static SpatialMetricOverride Scale(HexCoord from, HexCoord to, int units) =>
            new SpatialMetricOverride(Source, from, to, units);

        private static SpatialQueryBoard Line()
        {
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            AddEdge(edges, A, B); AddEdge(edges, B, C);
            return new SpatialQueryBoard(Cells(A, B, C), edges, new SpatialQueryLimits(2, 16));
        }

        private static Dictionary<HexCoord, SpatialCellRules> Cells(params HexCoord[] coords)
        {
            var result = new Dictionary<HexCoord, SpatialCellRules>();
            foreach (var coord in coords) result.Add(coord, new SpatialCellRules(0, false, false, false, 0));
            return result;
        }

        private static void AddEdge(Dictionary<SpatialDirectedEdge, SpatialEdgeRules> edges,
            HexCoord from, HexCoord to, SpatialEdgeRules? rules = null)
        {
            var edge = rules ?? new SpatialEdgeRules(2, true, true);
            edges.Add(new SpatialDirectedEdge(from, to), edge);
            edges.Add(new SpatialDirectedEdge(to, from), edge);
        }
    }
}
