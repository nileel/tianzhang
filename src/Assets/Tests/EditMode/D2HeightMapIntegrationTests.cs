using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TianZhang.Content;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.Spatial;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Tests.EditMode
{
    /// <summary>
    /// D2: the fixed Guanzhong terraced map and the old flat map share the existing environment
    /// asset, combat entry and spatial query chain. These tests exercise the imported production
    /// assets, not an offline copy of the map.
    /// </summary>
    public sealed class D2HeightMapIntegrationTests
    {
        private const string DemoAssetPath = "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_jindan_demo.asset";
        private const string WildAssetPath = "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset";

        private static EnvironmentProfileAsset Load(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>(path);
            Assert.IsNotNull(asset, "Missing imported environment asset: " + path);
            return asset;
        }

        private static SpatialQueryBoard Board(EnvironmentProfileAsset asset)
        {
            var grid = new TacticalGridModel();
            foreach (EnvironmentBattlefieldCell cell in asset.battlefieldCells)
                grid.SetTile(new TacticalTileData(new HexCoord(cell.q, cell.r))
                {
                    HeightLevel = cell.heightLevel,
                    MovementBurdenUnits = cell.cellMovementBurdenUnits,
                    BlocksGroundMove = cell.blocksGroundMove,
                    BlocksLineOfSight = cell.blocksLineOfSight,
                });
            Assert.IsTrue(SpatialQueryBoardFactory.TryCreate(grid, asset, out SpatialQuerySnapshot snapshot, out string reason), reason);
            return snapshot.Board;
        }

        private static int Height(EnvironmentProfileAsset asset, int q, int r) =>
            asset.battlefieldCells.Single(cell => cell.q == q && cell.r == r).heightLevel;

        private static EnvironmentDirectedEdge CliffEdge(EnvironmentProfileAsset asset) =>
            asset.directedEdges.First(edge => edge.edgeKind == EnvironmentEdgeKind.OpenCliff &&
                Height(asset, edge.fromQ, edge.fromR) != Height(asset, edge.toQ, edge.toR) &&
                !asset.battlefieldCells.Single(cell => cell.q == edge.fromQ && cell.r == edge.fromR).blocksGroundMove &&
                !asset.battlefieldCells.Single(cell => cell.q == edge.toQ && cell.r == edge.toR).blocksGroundMove);

        [Test]
        public void DemoAssetProjectsTheFixedHeightMapAndEveryUsedEdgeKind()
        {
            var asset = Load(DemoAssetPath);
            Assert.AreEqual("2", asset.mapSchemaVersion);
            Assert.AreEqual("guanzhong-jindan-v3-d1-supported-map-20261024.4", asset.mapVersion);
            Assert.AreEqual(144, asset.battlefieldCells.Length);
            Assert.AreEqual(105, asset.battlefieldCells.Count(cell => !cell.blocksGroundMove));
            Assert.AreEqual(36, asset.battlefieldCells.Count(cell => cell.surfaceKind == "ramp_shoulder"));
            Assert.AreEqual(3, asset.battlefieldCells.Count(cell => cell.surfaceKind == "grass_earth" && cell.blocksGroundMove));
            Assert.AreEqual(760, asset.directedEdges.Length);
            Assert.AreEqual(436, asset.directedEdges.Count(edge => edge.edgeKind == EnvironmentEdgeKind.Flat));
            Assert.AreEqual(210, asset.directedEdges.Count(edge => edge.edgeKind == EnvironmentEdgeKind.Slope));
            Assert.AreEqual(8, asset.directedEdges.Count(edge => edge.edgeKind == EnvironmentEdgeKind.Stairs));
            Assert.AreEqual(2, asset.directedEdges.Count(edge => edge.edgeKind == EnvironmentEdgeKind.OneWayDrop));
            Assert.AreEqual(100, asset.directedEdges.Count(edge => edge.edgeKind == EnvironmentEdgeKind.OpenCliff));
            Assert.AreEqual(4, asset.directedEdges.Count(edge => edge.edgeKind == EnvironmentEdgeKind.EntityWall));
            Assert.AreEqual(218, asset.directedEdges.Count(edge => edge.movementBurdenUnits == 1));
            Assert.AreEqual(8, asset.battlefieldCells.Count(cell => cell.cellMovementBurdenUnits == 1));
            Assert.IsTrue(asset.directedEdges.Where(edge =>
                edge.edgeKind == EnvironmentEdgeKind.Slope || edge.edgeKind == EnvironmentEdgeKind.Stairs)
                .All(edge => !string.IsNullOrWhiteSpace(edge.connectionId)));
            Assert.IsFalse(string.IsNullOrWhiteSpace(asset.physicalSurfaceProfiles));
            Assert.IsFalse(string.IsNullOrWhiteSpace(asset.mapMarkers));
        }

        [Test]
        public void GroundRouteReachesTheHighPlateauWhileCliffsStayClosedToGroundMovement()
        {
            var asset = Load(DemoAssetPath);
            var board = Board(asset);
            var start = new SpatialPose(new HexCoord(4, 0));
            var destination = new HexCoord(8, 12);
            Assert.AreEqual(2, Height(asset, 8, 12));

            SpatialMovementPathResult route = board.QueryMovementPath(start, destination, 10_000_000, 1000, targetFacing: 1);
            Assert.Greater(route.Steps.Count, 0, route.Reason);
            Assert.AreEqual(destination, route.Steps[route.Steps.Count - 1].To.Anchor);
            Assert.AreEqual(1, route.Steps[route.Steps.Count - 1].To.Facing);
            int units = route.Edges.Sum(edge => edge.DistanceUnits);
            Assert.LessOrEqual(units, 53, "D1 main route costs 53 movement units; the shared query must not exceed it.");
            Assert.GreaterOrEqual(units, 39, "The high plateau cannot be reached with fewer units than the D1 single-cell witness.");

            var reachable = board.FindReachableWithinActionPoints(start, 10_000_000, 1000);
            Assert.IsTrue(reachable.Keys.Any(pose => pose.Anchor == destination));

            EnvironmentDirectedEdge cliff = CliffEdge(asset);
            var cliffMove = board.InspectEdge(
                new HexCoord(cliff.fromQ, cliff.fromR), new HexCoord(cliff.toQ, cliff.toR), SpatialQueryKind.Movement);
            Assert.IsFalse(cliffMove.IsLegal);
            Assert.AreEqual(SpatialQueryReasons.OpenCliffBlocksGround, cliffMove.Reason);
            Assert.IsTrue(board.InspectEdge(
                new HexCoord(cliff.fromQ, cliff.fromR), new HexCoord(cliff.toQ, cliff.toR), SpatialQueryKind.Attack).IsLegal,
                "An open cliff blocks ordinary ground movement, not the effect path.");
        }

        [Test]
        public void SlopeQuotesTheSameMovementCostBothWaysAndNeverEntersAttackMetric()
        {
            var asset = Load(DemoAssetPath);
            var board = Board(asset);
            var low = new HexCoord(2, 3);
            var high = new HexCoord(2, 4);
            Assert.AreEqual(0, Height(asset, 2, 3));
            Assert.AreEqual(1, Height(asset, 2, 4));

            SpatialMovementPathResult up = board.QueryMovementPath(new SpatialPose(low), high, 1_000_000, 1000);
            SpatialMovementPathResult down = board.QueryMovementPath(new SpatialPose(high), low, 1_000_000, 1000);
            Assert.AreEqual(1, up.Steps.Count, up.Reason);
            Assert.AreEqual(1, down.Steps.Count, down.Reason);
            Assert.AreEqual(3, up.Edges[0].DistanceUnits);
            Assert.AreEqual(3, down.Edges[0].DistanceUnits);
            Assert.AreEqual(up.ActionPointCostUnits, down.ActionPointCostUnits);
            Assert.AreEqual(2, board.QueryMetricDistance(low, high, SpatialQueryKind.Attack).DistanceUnits);
        }

        [Test]
        public void OneWayDropAndEntityWallOnlyAdmitTheirDeclaredDirections()
        {
            var board = Board(Load(DemoAssetPath));
            var high = new HexCoord(9, 9);
            var low = new HexCoord(9, 8);
            Assert.IsTrue(board.InspectEdge(high, low, SpatialQueryKind.Movement).IsLegal);
            var reverse = board.InspectEdge(low, high, SpatialQueryKind.Movement);
            Assert.IsFalse(reverse.IsLegal);
            Assert.AreEqual(SpatialQueryReasons.DirectedEdgeBlocksMovement, reverse.Reason);
            Assert.IsTrue(board.InspectEdge(high, low, SpatialQueryKind.Attack).IsLegal);
            Assert.IsTrue(board.InspectEdge(low, high, SpatialQueryKind.Attack).IsLegal);

            var wallFrom = new HexCoord(8, 14);
            var wallTo = new HexCoord(9, 14);
            Assert.AreEqual(SpatialQueryReasons.EntityWallBlocksMovement,
                board.InspectEdge(wallFrom, wallTo, SpatialQueryKind.Movement).Reason);
            Assert.AreEqual(SpatialQueryReasons.EntityWallBlocksEffects,
                board.InspectEdge(wallFrom, wallTo, SpatialQueryKind.Attack).Reason);
            Assert.AreEqual(SpatialQueryReasons.EntityWallBlocksMovement,
                board.InspectEdge(wallTo, wallFrom, SpatialQueryKind.Movement).Reason);
        }

        [Test]
        public void AdjacentPairStandsAndMovesAcrossTheNamedOneLevelRampConnection()
        {
            var board = Board(Load(DemoAssetPath));
            var highAnchor = new SpatialPose(new HexCoord(2, 4), 5, SpatialFootprintKind.AdjacentPair, 2);
            Assert.AreEqual(new HexCoord(2, 3), highAnchor.OccupiedCells[1]);
            Assert.AreEqual(SpatialQueryReasons.Ok, board.InspectPlacement(highAnchor));
            var lowAnchor = new SpatialPose(new HexCoord(2, 3), 2, SpatialFootprintKind.AdjacentPair, 2);
            Assert.AreEqual(new HexCoord(2, 4), lowAnchor.OccupiedCells[1]);
            Assert.AreEqual(SpatialQueryReasons.Ok, board.InspectPlacement(lowAnchor));

            var start = new SpatialPose(new HexCoord(2, 3), 5, SpatialFootprintKind.AdjacentPair, 2);
            SpatialMovementPathResult move = board.QueryMovementPath(
                start, new HexCoord(2, 4), 10_000_000, 1000, targetFacing: 5);
            Assert.AreEqual(1, move.Steps.Count, move.Reason);
            Assert.AreEqual(new SpatialPose(new HexCoord(2, 4), 5, SpatialFootprintKind.AdjacentPair, 2), move.Steps[0].To);
            Assert.AreEqual(3, move.Edges[0].DistanceUnits);
            Assert.AreEqual(1, move.Edges[0].MovementBurdenUnits);
        }

        [Test]
        public void AdjacentPairCannotBridgeCliffsWallsOrTwoLevels()
        {
            var asset = Load(DemoAssetPath);
            var board = Board(asset);
            EnvironmentDirectedEdge cliff = CliffEdge(asset);
            var cliffFrom = new HexCoord(cliff.fromQ, cliff.fromR);
            var cliffTo = new HexCoord(cliff.toQ, cliff.toR);
            var cliffPose = new SpatialPose(cliffFrom, (cliffFrom.DirectionTo(cliffTo) + 3) % 6,
                SpatialFootprintKind.AdjacentPair, 2);
            Assert.AreEqual(SpatialQueryReasons.PairCrossHeightConnectionInvalid, board.InspectPlacement(cliffPose));

            var wallFrom = new HexCoord(9, 14);
            var wallTo = new HexCoord(8, 14);
            var wallPose = new SpatialPose(wallFrom, (wallFrom.DirectionTo(wallTo) + 3) % 6,
                SpatialFootprintKind.AdjacentPair, 2);
            Assert.AreEqual(SpatialQueryReasons.DirectedEdgeBlocksMovement, board.InspectPlacement(wallPose));

            var synthetic = TwoLevelBoard(firstConnection: "same", secondConnection: "same");
            var anchor = new HexCoord(0, 0);
            var twoLevels = new SpatialPose(anchor, (anchor.DirectionTo(new HexCoord(1, 0)) + 3) % 6,
                SpatialFootprintKind.AdjacentPair, 2);
            Assert.AreEqual(SpatialQueryReasons.PairCrossHeightConnectionInvalid, synthetic.InspectPlacement(twoLevels));

            var mismatched = TwoLevelBoard(firstConnection: "left", secondConnection: "right", upperHeight: 1);
            Assert.AreEqual(SpatialQueryReasons.PairCrossHeightConnectionInvalid, mismatched.InspectPlacement(twoLevels));
        }

        [Test]
        public void OldFlatGuanzhongMapKeepsOneHundredCellsAnd522FlatEdges()
        {
            var asset = Load(WildAssetPath);
            Assert.AreEqual(100, asset.battlefieldCells.Length);
            Assert.AreEqual(522, asset.directedEdges.Length);
            Assert.IsTrue(asset.battlefieldCells.All(cell =>
                cell.heightLevel == 0 && cell.cellMovementBurdenUnits == 0 &&
                cell.surfaceKind == "none" && cell.physicalSurfaceId == "legacy_flat"));
            Assert.IsTrue(asset.directedEdges.All(edge =>
                edge.edgeKind == EnvironmentEdgeKind.Flat && edge.movementBurdenUnits == 0));

            var board = Board(asset);
            SpatialMovementPathResult quote = board.QueryMovementPath(
                new SpatialPose(new HexCoord(0, 0)), new HexCoord(1, 0), 100_000, 1000);
            Assert.AreEqual(1, quote.Steps.Count, quote.Reason);
            Assert.AreEqual(2, quote.Edges[0].DistanceUnits);
            Assert.AreEqual(0, quote.Edges[0].MovementBurdenUnits);
        }

        private static SpatialQueryBoard TwoLevelBoard(string firstConnection, string secondConnection, int upperHeight = 2)
        {
            var lower = new HexCoord(0, 0);
            var upper = new HexCoord(1, 0);
            var cells = new Dictionary<HexCoord, SpatialCellRules>
            {
                [lower] = new SpatialCellRules(0, false, false, false, 0),
                [upper] = new SpatialCellRules(upperHeight, false, false, false, 0),
            };
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>
            {
                [new SpatialDirectedEdge(lower, upper)] = new SpatialEdgeRules(
                    2, true, true, edgeKind: SpatialEdgeKind.Slope, movementBurdenUnits: 1, connectionId: firstConnection),
                [new SpatialDirectedEdge(upper, lower)] = new SpatialEdgeRules(
                    2, true, true, edgeKind: SpatialEdgeKind.Slope, movementBurdenUnits: 1, connectionId: secondConnection),
            };
            return new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(2, 16));
        }
    }
}
