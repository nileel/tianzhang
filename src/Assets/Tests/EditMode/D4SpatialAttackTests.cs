using System;
using System.Collections.Generic;
using NUnit.Framework;
using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    public sealed class D4SpatialAttackTests
    {
        private static readonly HexCoord Origin = new HexCoord(0, 0);
        private static readonly HexCoord East = new HexCoord(1, 0);
        private static readonly HexCoord EastTwo = new HexCoord(2, 0);
        private const SpatialMovementMode AllModes = SpatialMovementMode.Ground | SpatialMovementMode.Fly |
            SpatialMovementMode.Burrow | SpatialMovementMode.Swim;

        [Test]
        public void AttackChecksModeMasksAndVerticalReachWithoutChangingHorizontalDistance()
        {
            var board = Board(new Dictionary<HexCoord, int> { [EastTwo] = 2 });
            var source = new SpatialPose(Origin);
            var target = new SpatialPose(EastTwo);
            var ground = new SpatialAttackPolicy(SpatialMovementMode.Ground, SpatialMovementMode.Ground, 1);
            Assert.AreEqual(SpatialQueryReasons.VerticalReachExceeded, board.QueryContact(source, target, 1, 2, ground).Reason);
            Assert.IsTrue(board.QueryContact(source, target, 1, 2).IsInRange, "The existing policy-free query remains geometric.");
            var flyer = new SpatialPose(Origin, movementMode: SpatialMovementMode.Fly, altitudeBand: 1);
            Assert.AreEqual(SpatialQueryReasons.SourceModeNotAllowed, board.QueryContact(flyer, target, 1, 2, ground).Reason);
            var flyPolicy = new SpatialAttackPolicy(AllModes, AllModes, 1);
            var contact = board.QueryContact(flyer, target, 1, 2, flyPolicy);
            Assert.IsTrue(contact.IsInRange, contact.Reason);
            Assert.AreEqual(2, contact.DistanceUnits, "Altitude must not become diagonal distance.");
            var airTarget = new SpatialPose(East, movementMode: SpatialMovementMode.Fly, altitudeBand: 1);
            Assert.AreEqual(SpatialQueryReasons.TargetModeNotAllowed, board.QueryContact(source, airTarget, 1, 2, ground).Reason);
        }

        [Test]
        public void PairTargetUsesEachHeightAndExplicitSelectedCellNeverSubstitutesAnotherBodyCell()
        {
            var board = Board(new Dictionary<HexCoord, int> { [East] = 1, [EastTwo] = 2 });
            var source = new SpatialPose(Origin);
            var pair = new SpatialPose(EastTwo, footprint: SpatialFootprintKind.AdjacentPair, bodyScaleRank: 2);
            var policy = new SpatialAttackPolicy(AllModes, AllModes, 1);
            var automatic = board.QueryContact(source, pair, 1, 3, policy);
            Assert.IsTrue(automatic.IsInRange, automatic.Reason);
            Assert.AreEqual(East, automatic.TargetCell);
            var explicitHighCell = board.QueryContact(source, new SpatialPose(EastTwo), 1, 3, policy);
            Assert.AreEqual(SpatialQueryReasons.VerticalReachExceeded, explicitHighCell.Reason);
            Assert.AreEqual(EastTwo, explicitHighCell.TargetCell);
        }

        [Test]
        public void PairSourceChecksHeightBeforeSelectingNearestLegalContact()
        {
            var board = Board(new Dictionary<HexCoord, int> { [East] = 1 });
            var pair = new SpatialPose(East, footprint: SpatialFootprintKind.AdjacentPair, bodyScaleRank: 2);
            var contact = board.QueryContact(pair, new SpatialPose(EastTwo), 1, 3,
                new SpatialAttackPolicy(AllModes, AllModes, 0));
            Assert.IsTrue(contact.IsInRange, contact.Reason);
            Assert.AreEqual(Origin, contact.SourceCell);
            Assert.AreEqual(2, contact.DistanceUnits);
        }

        [Test]
        public void MinimumRangeCannotSelectFartherBodyCellToEscapeDeadZone()
        {
            var pair = new SpatialPose(EastTwo, footprint: SpatialFootprintKind.AdjacentPair, bodyScaleRank: 2);
            var contact = Board().QueryContact(new SpatialPose(Origin), pair, 2, 4, Policy());
            Assert.AreEqual(SpatialQueryReasons.BelowMinimumRange, contact.Reason);
            Assert.AreEqual(East, contact.TargetCell);
        }

        [Test]
        public void PoseCopiesAndEqualityRetainModeAndAltitude()
        {
            var pose = new SpatialPose(Origin, movementMode: SpatialMovementMode.Fly, altitudeBand: 2);
            var moved = pose.WithAnchor(East).WithFacing(4);
            Assert.AreEqual(SpatialMovementMode.Fly, moved.MovementMode);
            Assert.AreEqual(2, moved.AltitudeBand);
            Assert.AreNotEqual(new SpatialPose(Origin), pose);
            Assert.AreEqual(pose, moved.WithAnchor(Origin).WithFacing(0));
            Assert.AreEqual(pose.GetHashCode(), moved.WithAnchor(Origin).WithFacing(0).GetHashCode());
        }

        [Test]
        public void CoverUsesLastIncomingEdgeAndHighestTierWhileMeleeIgnoresOnlyCellCover()
        {
            var cellCover = new SpatialSceneObjectRules("bush", EastTwo, null, false, false, 1);
            var edgeCover = new SpatialSceneObjectRules("ruin", East, EastTwo, false, false, 2);
            var board = Board().WithSceneObjects(new[] { cellCover, edgeCover });
            Assert.AreEqual(2, board.QueryContact(new SpatialPose(Origin), new SpatialPose(EastTwo), 1, 4, Policy()).CoverTier);
            Assert.AreEqual(1, board.QueryContact(new SpatialPose(new HexCoord(2, 1)), new SpatialPose(EastTwo), 1, 4, Policy()).CoverTier);
            Assert.AreEqual(2, board.QueryContact(new SpatialPose(East), new SpatialPose(EastTwo), 1, 4, Policy(true)).CoverTier);
            Assert.AreEqual(0, board.WithSceneObjects(new[] { cellCover }).QueryContact(new SpatialPose(East),
                new SpatialPose(EastTwo), 1, 4, Policy(true)).CoverTier);
        }

        [Test]
        public void HighGroundAirAndOversizedTargetsIgnoreOrdinaryLowCover()
        {
            var cover = new SpatialSceneObjectRules("ruin", EastTwo, null, false, false, 2, 2);
            var flat = Board().WithSceneObjects(new[] { cover });
            Assert.AreEqual(2, flat.QueryContact(new SpatialPose(Origin), new SpatialPose(EastTwo), 1, 4, Policy()).CoverTier);
            Assert.AreEqual(0, Board(new Dictionary<HexCoord, int> { [Origin] = 1 }).WithSceneObjects(new[] { cover })
                .QueryContact(new SpatialPose(Origin), new SpatialPose(EastTwo), 1, 4, Policy()).CoverTier);
            Assert.AreEqual(0, flat.QueryContact(new SpatialPose(Origin, movementMode: SpatialMovementMode.Fly, altitudeBand: 1),
                new SpatialPose(EastTwo), 1, 4, Policy()).CoverTier);
            Assert.AreEqual(0, flat.QueryContact(new SpatialPose(Origin),
                new SpatialPose(EastTwo, movementMode: SpatialMovementMode.Fly, altitudeBand: 1), 1, 4, Policy()).CoverTier);
            Assert.AreEqual(0, flat.QueryContact(new SpatialPose(Origin), new SpatialPose(EastTwo, bodyScaleRank: 3),
                1, 4, Policy()).CoverTier);
        }

        [Test]
        public void HighGroundIgnoresCoverButCannotCrossAnEntityWall()
        {
            var original = Board(new Dictionary<HexCoord, int> { [Origin] = 1 },
                new[] { new SpatialDirectedEdge(East, EastTwo) });
            var cover = new SpatialSceneObjectRules("cover", EastTwo, null, false, false, 2);
            var blocked = original.WithSceneObjects(new[] { Wall("wall", East, EastTwo, true), cover });
            Assert.IsFalse(blocked.QueryContact(new SpatialPose(Origin), new SpatialPose(EastTwo), 1, 4, Policy()).IsInRange);
            var open = blocked.WithSceneObjects(new[] { Wall("wall", East, EastTwo, false), cover });
            var contact = open.QueryContact(new SpatialPose(Origin), new SpatialPose(EastTwo), 1, 4, Policy());
            Assert.IsTrue(contact.IsInRange, contact.Reason);
            Assert.AreEqual(0, contact.CoverTier);
        }

        [Test]
        public void WallCanBeTargetedWithoutExposingUnitBehindItOrIgnoringAnotherWall()
        {
            var board = Board(walls: new[] { new SpatialDirectedEdge(East, EastTwo) })
                .WithSceneObjects(new[] { Wall("wall", East, EastTwo, true) });
            var source = new SpatialPose(East);
            Assert.IsFalse(board.QueryContact(source, new SpatialPose(EastTwo), 1, 4, Policy()).IsInRange);
            var wallContact = board.QuerySceneObjectContact(source, East, "wall", 1, 4, Policy());
            Assert.IsTrue(wallContact.IsInRange, wallContact.Reason);
            Assert.AreEqual(EastTwo, wallContact.TargetCell, "An actor on the selected endpoint strikes the other endpoint.");
            Assert.IsTrue(board.QuerySceneObjectContact(new SpatialPose(EastTwo), EastTwo, "wall", 1, 4, Policy()).IsInRange);
            Assert.IsFalse(board.QueryContact(source, new SpatialPose(EastTwo), 1, 4, Policy()).IsInRange,
                "The target-only query must not mutate the board.");
            var secondBlocker = new SpatialSceneObjectRules("other", Origin, East, true, true, 0);
            var blocked = board.WithSceneObjects(new[] { Wall("wall", East, EastTwo, true), secondBlocker });
            Assert.IsFalse(blocked.QuerySceneObjectContact(new SpatialPose(Origin), East, "wall", 1, 4, Policy()).IsInRange);
        }

        [Test]
        public void DestroyedWallRefreshesRouteAndSightButPreservesOtherSameConnectionWall()
        {
            var three = new HexCoord(3, 0); var four = new HexCoord(4, 0);
            var original = Board(walls: new[] { new SpatialDirectedEdge(East, EastTwo), new SpatialDirectedEdge(three, four) });
            var live = original.WithSceneObjects(new[] { Wall("first", East, EastTwo, true), Wall("second", three, four, true) });
            var before = live.QueryMovementPath(new SpatialPose(East), EastTwo, 1000000, 1000);
            Assert.Greater(before.Path.Count, 1, "The live wall keeps an actual alternate route.");
            Assert.IsFalse(live.QueryLineOfSight(East, EastTwo).HasLineOfSight);
            var dead = live.WithSceneObjects(new[] { Wall("first", East, EastTwo, false), Wall("second", three, four, true) });
            var after = dead.QueryMovementPath(new SpatialPose(East), EastTwo, 1000000, 1000);
            Assert.AreEqual(1, after.Path.Count);
            Assert.IsTrue(dead.QueryLineOfSight(East, EastTwo).HasLineOfSight);
            Assert.IsTrue(dead.QueryLineOfSight(EastTwo, East).HasLineOfSight);
            Assert.AreEqual(SpatialQueryReasons.Ok, dead.InspectPlacement(new SpatialPose(EastTwo,
                footprint: SpatialFootprintKind.AdjacentPair, bodyScaleRank: 2)));
            Assert.IsFalse(dead.QueryLineOfSight(three, four).HasLineOfSight, "Same connection ID is not blanket replacement authority.");
            Assert.IsFalse(original.QueryLineOfSight(East, EastTwo).HasLineOfSight);
            Assert.IsFalse(live.QueryLineOfSight(East, EastTwo).HasLineOfSight);
        }

        [Test]
        public void CellObjectBlocksPlacementAndEffectsUntilDestroyedWithoutErasingBaseObstacle()
        {
            var live = Board().WithSceneObjects(new[] { new SpatialSceneObjectRules("stone", East, null, true, true, 0) });
            Assert.AreEqual(SpatialQueryReasons.SceneObjectBlocksMovement, live.InspectPlacement(new SpatialPose(East)));
            Assert.IsFalse(live.QueryLineOfSight(Origin, EastTwo).HasLineOfSight);
            Assert.IsTrue(live.QuerySceneObjectContact(new SpatialPose(Origin), East, "stone", 1, 4, Policy()).IsInRange);
            var dead = live.WithSceneObjects(new[] { new SpatialSceneObjectRules("stone", East, null, false, false, 0) });
            Assert.AreEqual(SpatialQueryReasons.Ok, dead.InspectPlacement(new SpatialPose(East)));
            Assert.IsTrue(dead.QueryLineOfSight(Origin, EastTwo).HasLineOfSight);
            var baseObstacle = Board(baseObstacle: East).WithSceneObjects(new[] {
                new SpatialSceneObjectRules("stone", East, null, false, false, 0) });
            Assert.AreEqual(SpatialQueryReasons.EntityObstacle, baseObstacle.InspectPlacement(new SpatialPose(East)));
            Assert.IsFalse(baseObstacle.QueryLineOfSight(Origin, EastTwo).HasLineOfSight);
        }

        [Test]
        public void SceneObjectQueriesRejectWrongIdsAnchorsAndMismatchedWallReplacement()
        {
            var board = Board().WithSceneObjects(new[] { new SpatialSceneObjectRules("stone", East, null, false, false, 0) });
            Assert.AreEqual(SpatialQueryReasons.SceneObjectMissing,
                board.QuerySceneObjectContact(new SpatialPose(Origin), East, "absent", 1, 4, Policy()).Reason);
            Assert.AreEqual(SpatialQueryReasons.SceneObjectAnchorMismatch,
                board.QuerySceneObjectContact(new SpatialPose(Origin), EastTwo, "stone", 1, 4, Policy()).Reason);
            Assert.Throws<ArgumentException>(() => Board().WithSceneObjects(new[] { Wall("wrong", East, EastTwo, false) }));
            var staticWall = Board(walls: new[] { new SpatialDirectedEdge(East, EastTwo) });
            Assert.Throws<ArgumentException>(() => staticWall.WithSceneObjects(new[] {
                new SpatialSceneObjectRules("wrong", East, EastTwo, false, false, 0, replacesWallConnectionId: "different") }));
        }

        private static SpatialAttackPolicy Policy(bool isMelee = false) => new SpatialAttackPolicy(AllModes, AllModes, 4, isMelee);
        private static SpatialSceneObjectRules Wall(string id, HexCoord cell, HexCoord other, bool intact) =>
            new SpatialSceneObjectRules(id, cell, other, intact, intact, 0, replacesWallConnectionId: "shared_wall");

        private static SpatialQueryBoard Board(Dictionary<HexCoord, int> heights = null,
            IReadOnlyList<SpatialDirectedEdge> walls = null, HexCoord? baseObstacle = null)
        {
            var cells = new Dictionary<HexCoord, SpatialCellRules>();
            for (int q = 0; q <= 4; q++)
            for (int r = 0; r <= 1; r++)
            {
                var cell = new HexCoord(q, r);
                int height = heights != null && heights.TryGetValue(cell, out int value) ? value : 0;
                cells.Add(cell, new SpatialCellRules(height, false, false, baseObstacle == cell, 0));
            }
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            foreach (HexCoord from in cells.Keys)
            foreach (HexCoord to in from.Neighbors())
                if (cells.ContainsKey(to)) edges.Add(new SpatialDirectedEdge(from, to), new SpatialEdgeRules(1, true, true));
            if (walls != null)
                foreach (SpatialDirectedEdge edge in walls)
                {
                    edges[edge] = new SpatialEdgeRules(1, false, false, edgeKind: SpatialEdgeKind.EntityWall, connectionId: "shared_wall");
                    edges[new SpatialDirectedEdge(edge.To, edge.From)] = edges[edge];
                }
            return new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(1, 16));
        }
    }
}
