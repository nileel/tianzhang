using System.Collections.Generic;
using NUnit.Framework;
using TianZhang.Core;
using TianZhang.Core.SpatialRules;
using TianZhang.Tactical;
using UnityEngine;

namespace TianZhang.Tests
{
    public class SpatialQueryBoardTests
    {
        private static readonly SpatialHexCoord Origin = new SpatialHexCoord(0, 0);
        private static readonly SpatialHexCoord East = new SpatialHexCoord(1, 0);
        private static readonly SpatialHexCoord EastTwo = new SpatialHexCoord(2, 0);

        [Test]
        public void QueryRangeUsesConfiguredFixedPointEdgesInsteadOfTopologicalDistance()
        {
            var board = CreateBoard(
                edges: new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>
                {
                    [new SpatialDirectedEdge(Origin, East)] = new SpatialEdgeRules(1, true, true),
                    [new SpatialDirectedEdge(East, EastTwo)] = new SpatialEdgeRules(1, true, true),
                });

            var range = board.QueryRange(Origin, 1, 1, SpatialQueryKind.Attack, true);

            Assert.IsTrue(range.TryGet(EastTwo, out var entry));
            Assert.IsTrue(entry.IsInRange);
            Assert.AreEqual(2, entry.DistanceUnits);
            Assert.IsTrue(entry.HasLineOfSight);
        }

        [Test]
        public void QueryRangeFailsClosedForHeightMismatchAndEntityObstacle()
        {
            var cells = new Dictionary<SpatialHexCoord, SpatialCellRules>
            {
                [Origin] = new SpatialCellRules(0, false, false, false),
                [East] = new SpatialCellRules(1, false, false, true),
            };
            var board = CreateBoard(
                cells,
                new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>
                {
                    [new SpatialDirectedEdge(Origin, East)] = new SpatialEdgeRules(2, true, true),
                });

            var range = board.QueryRange(Origin, 1, 1, SpatialQueryKind.Attack, true);

            Assert.IsTrue(range.TryGet(East, out var entry));
            Assert.IsFalse(entry.IsInRange);
            Assert.AreEqual(SpatialQueryReasons.HeightRuleUnconfigured, entry.Reason);

            var reachable = board.FindReachable(Origin, 1, new HashSet<SpatialHexCoord>());
            Assert.IsFalse(reachable.ContainsKey(East));
            Assert.AreEqual(SpatialQueryReasons.EntityObstacle, board.InspectEdge(Origin, East, SpatialQueryKind.Movement).Reason);
        }

        [Test]
        public void MissingDirectedEdgeDoesNotFallBackToAStandardStep()
        {
            var board = CreateBoard(new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>());

            var distance = board.QueryMetricDistance(Origin, East, SpatialQueryKind.Attack, 1);

            Assert.IsFalse(distance.IsReachable);
            Assert.AreEqual(SpatialQueryReasons.NoLegalPath, distance.Reason);
            Assert.AreEqual(SpatialQueryReasons.MissingDirectedEdge, board.InspectEdge(Origin, East, SpatialQueryKind.Attack).Reason);
        }

        [Test]
        public void QueryRangeReportsConfiguredCandidatesAboveTheRequestedMaximum()
        {
            var board = CreateBoard(new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>
            {
                [new SpatialDirectedEdge(Origin, East)] = new SpatialEdgeRules(2, true, true),
                [new SpatialDirectedEdge(East, EastTwo)] = new SpatialEdgeRules(2, true, true),
            });

            var range = board.QueryRange(Origin, 0, 1, SpatialQueryKind.Attack, false);

            Assert.IsTrue(range.TryGet(EastTwo, out var entry));
            Assert.IsFalse(entry.IsInRange);
            Assert.AreEqual(4, entry.DistanceUnits);
            Assert.AreEqual(SpatialQueryReasons.AboveMaximumRange, entry.Reason);
        }

        [Test]
        public void UnitySnapshotMapsExplicitRulesAndRejectsDuplicateUnitAnchors()
        {
            var profile = ScriptableObject.CreateInstance<EnvironmentProfileData>();
            try
            {
                profile.unitsPerRange = 2;
                profile.maxQueryRange = 4;
                profile.directedEdges = new[]
                {
                    new EnvironmentDirectedEdge
                    {
                        fromQ = 0,
                        fromR = 0,
                        toQ = 1,
                        toR = 0,
                        metricDistanceUnits = 2,
                        allowsMovement = true,
                        allowsEffects = true,
                    },
                };
                var grid = new TacticalGridModel();
                grid.SetTile(new TacticalTileData(new HexCoord(0, 0)) { OccupiedUnitId = 7 });
                grid.SetTile(new TacticalTileData(new HexCoord(1, 0)) { IsEntityObstacle = true });

                Assert.IsTrue(SpatialQueryBoardFactory.TryCreate(grid, profile, out var snapshot, out var reason), reason);
                Assert.AreEqual(Origin, snapshot.UnitAnchors[7]);
                Assert.AreEqual(
                    SpatialQueryReasons.EntityObstacle,
                    snapshot.Board.InspectEdge(Origin, East, SpatialQueryKind.Movement).Reason);

                grid.SetOccupied(new HexCoord(1, 0), 7);
                Assert.IsFalse(SpatialQueryBoardFactory.TryCreate(grid, profile, out _, out reason));
                Assert.AreEqual(SpatialQuerySnapshotReasons.DuplicateUnitAnchor, reason);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        private static SpatialQueryBoard CreateBoard(
            Dictionary<SpatialDirectedEdge, SpatialEdgeRules> edges)
        {
            return CreateBoard(new Dictionary<SpatialHexCoord, SpatialCellRules>
            {
                [Origin] = new SpatialCellRules(0, false, false, false),
                [East] = new SpatialCellRules(0, false, false, false),
                [EastTwo] = new SpatialCellRules(0, false, false, false),
            }, edges);
        }

        private static SpatialQueryBoard CreateBoard(
            Dictionary<SpatialHexCoord, SpatialCellRules> cells,
            Dictionary<SpatialDirectedEdge, SpatialEdgeRules> edges)
        {
            return new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(2, 4));
        }
    }
}
