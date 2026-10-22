using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    public sealed class F4SpatialFootprintTests
    {
        private static readonly HexCoord Origin = new HexCoord(0, 0);
        private static SpatialPose Pair(int facing = 0) => new SpatialPose(Origin, facing, SpatialFootprintKind.AdjacentPair, 2);

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void SixFacingsOccupyAnchorAndOppositeNeighbor(int facing)
        {
            var pose = Pair(facing).WithAnchor(new HexCoord(2, -1));
            CollectionAssert.AreEqual(new[] { pose.Anchor, pose.Anchor.Step((facing + 3) % 6) }, pose.OccupiedCells);
            Assert.IsTrue(pose.Contains(pose.OccupiedCells[1]));
            Assert.IsFalse(pose.Contains(pose.Anchor.Step(facing)));
            Assert.AreEqual(pose, new SpatialPose(pose.Anchor, facing, SpatialFootprintKind.AdjacentPair, 2));
        }

        [Test]
        public void InvalidPoseAndClearanceInputsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialPose(Origin, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialPose(Origin, 6));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialPose(Origin, footprint: (SpatialFootprintKind)7));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialPose(Origin, bodyScaleRank: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialEdgeRules(1000, true, true, clearanceLimit: -1));
            Assert.AreEqual(SpatialQueryReasons.InvalidPose, new Fixture().Board().InspectPlacement(default));
        }

        [TestCase(1, 0, true)] [TestCase(1, 1, true)] [TestCase(1, 2, true)]
        [TestCase(2, 0, true)] [TestCase(2, 1, false)] [TestCase(2, 2, true)]
        public void ClearanceUsesIndependentRankAndNeverChangesAttackRange(int rank, int limit, bool allowed)
        {
            var fixture = new Fixture();
            HexCoord target = Origin.Step(0);
            fixture.Edge(Origin, target, limit: limit);
            var board = fixture.Board();
            var source = new SpatialPose(Origin, bodyScaleRank: rank);
            var move = board.QueryMovementPath(source, target, 100000, 1000);
            Assert.AreEqual(allowed, move.Steps.Count == 1, move.Reason);
            if (!allowed) Assert.AreEqual(SpatialQueryReasons.ClearanceInsufficient, move.Reason);
            Assert.IsTrue(board.QueryContact(source, new SpatialPose(target), 1, 1).IsInRange);
        }

        [Test]
        public void InitialBodyCannotBridgeNarrowClosedOrMissingInternalEdge()
        {
            var fixture = new Fixture();
            HexCoord rear = Origin.Step(3);
            fixture.Edge(rear, Origin, limit: 1);
            Assert.AreEqual(SpatialQueryReasons.ClearanceInsufficient, fixture.Board().InspectPlacement(Pair()));
            fixture.Edge(rear, Origin, movement: false);
            fixture.Edge(Origin, rear, movement: false);
            Assert.AreEqual(SpatialQueryReasons.DirectedEdgeBlocksMovement, fixture.Board().InspectPlacement(Pair()));
            fixture.Edges.Remove(new SpatialDirectedEdge(rear, Origin));
            Assert.AreEqual(SpatialQueryReasons.MissingDirectedEdge, fixture.Board().InspectPlacement(Pair()));
        }

        [Test]
        public void PlacementChecksRearBoundaryHeightObstacleAndCallerOccupancy()
        {
            var fixture = new Fixture();
            HexCoord rear = Origin.Step(3);
            Assert.AreEqual(SpatialQueryReasons.Occupied, fixture.Board().InspectPlacement(Pair(), new[] { rear }));
            Assert.AreEqual(SpatialQueryReasons.Occupied,
                fixture.Board().QueryMovementPath(Pair(), Origin.Step(0), 100000, 1000, new[] { Origin }).Reason,
                "The spatial service must not erase an occupant supplied at its own anchor.");
            fixture.Cells[rear] = new SpatialCellRules(1, false, false, false, 0);
            Assert.AreEqual(SpatialQueryReasons.HeightRuleUnconfigured, fixture.Board().InspectPlacement(Pair()));
            fixture.Cells[rear] = new SpatialCellRules(0, false, false, true, 0);
            Assert.AreEqual(SpatialQueryReasons.EntityObstacle, fixture.Board().InspectPlacement(Pair()));
            fixture.Cells.Remove(rear);
            Assert.AreEqual(SpatialQueryReasons.MissingCell, fixture.Board().InspectPlacement(Pair()));
        }

        [Test]
        public void SingleDirectionTravelAllowsStaticBodyButNeverReverseMovement()
        {
            var fixture = new Fixture();
            HexCoord rear = Origin.Step(3), target = Origin.Step(0);
            fixture.Edge(Origin, rear, movement: false);
            fixture.Edge(target, Origin, movement: false);
            var board = fixture.Board();
            Assert.AreEqual(SpatialQueryReasons.Ok, board.InspectPlacement(Pair()));
            var forward = board.QueryMovementPath(Pair(), target, 100000, 1000);
            Assert.AreEqual(1, forward.Steps.Count, forward.Reason);
            Assert.IsEmpty(board.QueryMovementPath(forward.Steps[0].To, Origin, 100000, 1000).Steps);
        }

        [TestCase("sweep")] [TestCase("intermediate_body")] [TestCase("occupied")]
        public void TurnCannotSkipItsRearSweepOrIntermediatePlacement(string blockedPart)
        {
            var fixture = new Fixture();
            HexCoord rear = Origin.Step(3), turnedRear = Origin.Step(4);
            if (blockedPart == "sweep") fixture.Edge(rear, turnedRear, limit: 1);
            if (blockedPart == "intermediate_body") fixture.Edge(Origin, turnedRear, limit: 1);
            var occupied = blockedPart == "occupied" ? new[] { turnedRear } : Array.Empty<HexCoord>();
            var move = fixture.Board().QueryMovementPath(Pair(), Origin.Step(2), 100000, 1000, occupied);
            Assert.IsEmpty(move.Steps, "A 120-degree turn must visit the intervening 60-degree pose.");
        }

        [TestCase(false, false)] [TestCase(true, false)] [TestCase(true, true)]
        public void HalfTurnChecksBothShortestSequencesInStableOrder(bool blockPositive, bool blockNegative)
        {
            var fixture = new Fixture();
            HexCoord rear = Origin.Step(3);
            if (blockPositive) fixture.Edge(rear, Origin.Step(4), limit: 1);
            if (blockNegative) fixture.Edge(rear, Origin.Step(2), limit: 1);
            var result = fixture.Board().QueryMovementPath(Pair(), Origin.Step(3), 100000, 1000);
            if (blockPositive && blockNegative) { Assert.IsEmpty(result.Steps); return; }
            Assert.AreEqual(1, result.Steps.Count, result.Reason);
            CollectionAssert.AreEqual(blockPositive ? new[] { 5, 4, 3 } : new[] { 1, 2, 3 }, result.Steps[0].TurnFacings);
        }

        [Test]
        public void SixtyDegreeTurnNeverUsesTheOtherThreeHundredDegrees()
        {
            var fixture = new Fixture();
            fixture.Edge(Origin.Step(3), Origin.Step(4), limit: 1);
            Assert.IsEmpty(fixture.Board().QueryMovementPath(Pair(), Origin.Step(1), 100000, 1000).Steps);
        }

        [Test]
        public void BothTranslatedEdgesAreRequiredButOnlyTheAnchorEdgeIsPriced()
        {
            var fixture = new Fixture();
            HexCoord rear = Origin.Step(3), target = Origin.Step(0);
            fixture.Edge(rear, Origin, metric: 9000);
            fixture.Edge(Origin, target, metric: 2000);
            fixture.Cells[Origin] = new SpatialCellRules(0, false, false, false, 7000);
            fixture.Cells[target] = new SpatialCellRules(0, false, false, false, 500);
            var result = fixture.Board().QueryMovementPath(Pair(), target, 50000, 5000);
            Assert.AreEqual(1, result.Steps.Count, result.Reason);
            Assert.AreEqual(50000, result.ActionPointCostUnits);
            Assert.AreEqual(2000, result.Edges[0].MetricDistanceUnits);
            Assert.AreEqual(500, result.Edges[0].MovementBurdenUnits);
            fixture.Edge(rear, Origin, movement: false);
            Assert.IsEmpty(fixture.Board().QueryMovementPath(Pair(), target, 50000, 5000, targetFacing: 0).Steps,
                "The rear cannot cross a blocked directed edge even when the front edge is clear.");
        }

        [Test]
        public void ReachabilityRetainsDifferentFacingsAtTheSameAnchorAndQuotesTheirOwnSteps()
        {
            var board = new Fixture().Board();
            HexCoord target = Origin.Step(0);
            var reachable = board.FindReachableWithinActionPoints(Pair(), 300000, 1000);
            Assert.IsTrue(reachable.ContainsKey(Pair().WithAnchor(target)));
            Assert.IsTrue(reachable.ContainsKey(Pair(1).WithAnchor(target)));
            foreach (int facing in new[] { 0, 1 })
            {
                var quote = board.QueryMovementPath(Pair(), target, 300000, 1000, targetFacing: facing);
                Assert.IsNotEmpty(quote.Steps, quote.Reason);
                SpatialPose end = quote.Steps[quote.Steps.Count - 1].To;
                Assert.AreEqual(facing, end.Facing);
                Assert.AreEqual(reachable[end], quote.ActionPointCostUnits);
                CollectionAssert.AreEqual(quote.Steps.Select(step => step.To.Anchor), quote.Path);
                CollectionAssert.AreEqual(quote.Steps.Select(step => step.Edge), quote.Edges);
            }
        }

        [Test]
        public void BodyCannotMoveThroughAnotherRearAndNoBudgetCannotTurnInPlace()
        {
            var board = new Fixture().Board();
            HexCoord target = Origin.Step(0);
            var other = new SpatialPose(target.Step(0), 0, SpatialFootprintKind.AdjacentPair, 2);
            Assert.IsEmpty(board.QueryMovementPath(Pair(), target, 100000, 1000, other.OccupiedCells).Steps);
            Assert.IsEmpty(board.QueryMovementPath(Pair(), target, 0, 1000).Steps);
            Assert.AreEqual(SpatialQueryReasons.DestinationUnchanged,
                board.QueryMovementPath(Pair(), Origin, 100000, 1000, targetFacing: 3).Reason);
        }

        [Test]
        public void NearestBodyCellCannotBeSkippedToEvadeMinimumRange()
        {
            var target = new SpatialPose(new HexCoord(2, 0), 0, SpatialFootprintKind.AdjacentPair, 2);
            var contact = new Fixture().Board().QueryContact(new SpatialPose(Origin), target, 2, 3);
            Assert.IsFalse(contact.IsInRange);
            Assert.IsTrue(contact.HasLineOfSight);
            Assert.AreEqual(SpatialQueryReasons.BelowMinimumRange, contact.Reason);
            Assert.AreEqual(new HexCoord(1, 0), contact.TargetCell);
            Assert.AreEqual(1000, contact.DistanceUnits);
        }

        [Test]
        public void ContactDistanceAndSightComeFromTheSamePair()
        {
            var fixture = new Fixture();
            fixture.Edge(Origin, new HexCoord(1, 0), effects: false);
            fixture.Edge(Origin, new HexCoord(1, -1), metric: 2000);
            var target = new SpatialPose(new HexCoord(1, -1), 2, SpatialFootprintKind.AdjacentPair, 2);
            var result = fixture.Board().QueryContact(new SpatialPose(Origin), target, 2, 2);
            Assert.IsTrue(result.IsInRange, result.Reason);
            Assert.AreEqual(target.Anchor, result.TargetCell);
            Assert.AreEqual(Origin, result.SourceCell);
            Assert.AreEqual(2000, result.DistanceUnits);
        }

        [Test]
        public void ContactTiesUseCoordinatesRatherThanAnchorOrFootprintOrder()
        {
            var board = new Fixture().Board();
            var target = new SpatialPose(new HexCoord(1, 0), 1, SpatialFootprintKind.AdjacentPair, 2);
            var result = board.QueryContact(new SpatialPose(Origin), target, 1, 1);
            Assert.AreEqual(new HexCoord(0, 1), result.TargetCell);
            var reversed = new SpatialPose(new HexCoord(0, 1), 4, SpatialFootprintKind.AdjacentPair, 2);
            Assert.AreEqual(result.TargetCell, board.QueryContact(new SpatialPose(Origin), reversed, 1, 1).TargetCell);
        }

        [Test]
        public void ContactUsesWeightedRangeAndRejectsCompleteOcclusion()
        {
            var fixture = new Fixture();
            HexCoord target = Origin.Step(0);
            fixture.Edge(Origin, target, metric: 2000);
            var result = fixture.Board().QueryContact(new SpatialPose(Origin), new SpatialPose(target), 1, 1);
            Assert.AreEqual(SpatialQueryReasons.AboveMaximumRange, result.Reason);
            Assert.AreEqual(2000, result.DistanceUnits);
            fixture.Edge(Origin, target, effects: false);
            result = fixture.Board().QueryContact(new SpatialPose(Origin), new SpatialPose(target), 0, 16);
            Assert.IsFalse(result.IsInRange);
            Assert.IsFalse(result.HasLineOfSight);
        }

        [Test]
        public void StepEqualityIncludesTheChosenTurnSequenceAndCopiesItsInput()
        {
            var turns = new[] { 1, 2, 3 };
            var edge = new SpatialMovementEdgeCost(Origin, Origin.Step(3), 1000, 0);
            var positive = new SpatialMovementStep(Pair(), Pair(3).WithAnchor(Origin.Step(3)), turns, edge);
            var same = new SpatialMovementStep(positive.From, positive.To, new[] { 1, 2, 3 }, edge);
            var negative = new SpatialMovementStep(positive.From, positive.To, new[] { 5, 4, 3 }, edge);
            turns[0] = 5;
            Assert.AreEqual(positive, same);
            Assert.AreEqual(positive.GetHashCode(), same.GetHashCode());
            Assert.AreNotEqual(positive, negative);
            Assert.AreEqual(1, positive.TurnFacings[0]);
        }

        [TestCase(3, 0, 0)] [TestCase(3, -3, 1)] [TestCase(0, -3, 2)]
        [TestCase(-3, 0, 3)] [TestCase(-3, 3, 4)] [TestCase(0, 3, 5)]
        [TestCase(1, 1, 0)] [TestCase(-1, -1, 2)] [TestCase(0, 0, -1)]
        public void ContactDirectionUsesStableSixWaySectorsAtAnyDistance(int q, int r, int expected)
        {
            Assert.AreEqual(expected, Origin.DirectionToward(new HexCoord(q, r)));
            if (Origin.TopologicalDistanceTo(new HexCoord(q, r)) != 1)
                Assert.AreEqual(-1, Origin.DirectionTo(new HexCoord(q, r)));
        }

        private sealed class Fixture
        {
            public readonly Dictionary<HexCoord, SpatialCellRules> Cells = new Dictionary<HexCoord, SpatialCellRules>();
            public readonly Dictionary<SpatialDirectedEdge, SpatialEdgeRules> Edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            public Fixture()
            {
                for (int q = -4; q <= 4; q++)
                for (int r = -4; r <= 4; r++)
                {
                    var cell = new HexCoord(q, r);
                    if (cell.TopologicalDistanceTo(Origin) <= 4) Cells.Add(cell, new SpatialCellRules(0, false, false, false, 0));
                }
                foreach (HexCoord cell in Cells.Keys)
                foreach (HexCoord neighbor in cell.Neighbors())
                    if (Cells.ContainsKey(neighbor)) Edge(cell, neighbor);
            }
            public void Edge(HexCoord from, HexCoord to, int limit = 0, bool movement = true, bool effects = true, int metric = 1000) =>
                Edges[new SpatialDirectedEdge(from, to)] = new SpatialEdgeRules(metric, movement, effects, clearanceLimit: limit);
            public SpatialQueryBoard Board() => new SpatialQueryBoard(Cells, Edges, new SpatialQueryLimits(1000, 16));
        }
    }
}
