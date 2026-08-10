using System.Collections.Generic;
using NUnit.Framework;
using TianZhang.Core;
using TianZhang.Spatial;
using TianZhang.Tactical;
using UnityEngine;

using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    public class SpatialQueryBoardTests
    {
        private static readonly HexCoord Origin = new HexCoord(0, 0);
        private static readonly HexCoord East = new HexCoord(1, 0);
        private static readonly HexCoord EastTwo = new HexCoord(2, 0);

        [Test]
        public void DirectedWeightedEdgesAreTheOnlyDistanceSource()
        {
            var board = CreateBoard(
                new[] { Origin, East, EastTwo },
                new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>
                {
                    [new SpatialDirectedEdge(Origin, East)] = new SpatialEdgeRules(1, true, true),
                    [new SpatialDirectedEdge(East, EastTwo)] = new SpatialEdgeRules(1, true, true),
                });

            Assert.AreEqual(SpatialQueryReasons.ReverseDirectedEdgeNotPermitted,
                board.InspectEdge(East, Origin, SpatialQueryKind.Movement).Reason);
            Assert.AreEqual(2,
                board.QueryMetricDistance(Origin, EastTwo, SpatialQueryKind.Attack).DistanceUnits);
        }

        [Test]
        public void ExpandedNeighborIsOutsideBasicAttackRange()
        {
            var board = CreateBoard(
                new[] { Origin, East },
                new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>
                {
                    [new SpatialDirectedEdge(Origin, East)] = new SpatialEdgeRules(4, true, true),
                });

            var range = board.QueryRange(Origin, 1, 1, SpatialQueryKind.Attack, true);

            Assert.IsTrue(range.TryGet(East, out var entry));
            Assert.IsFalse(entry.IsInRange);
            Assert.AreEqual(SpatialQueryReasons.AboveMaximumRange, entry.Reason);
        }

        [Test]
        public void EntityObstacleBlocksMovementAndLineOfSight()
        {
            var cells = CreateCells(Origin, East, EastTwo);
            cells[East] = new SpatialCellRules(0, false, false, true, 0);
            var board = new SpatialQueryBoard(
                cells,
                new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>
                {
                    [new SpatialDirectedEdge(Origin, East)] = new SpatialEdgeRules(2, true, true),
                    [new SpatialDirectedEdge(East, EastTwo)] = new SpatialEdgeRules(2, true, true),
                },
                new SpatialQueryLimits(2, 16));

            Assert.AreEqual(SpatialQueryReasons.EntityObstacle,
                board.InspectEdge(Origin, East, SpatialQueryKind.Movement).Reason);
            Assert.AreEqual(SpatialQueryReasons.EntityObstacle,
                board.QueryLineOfSight(Origin, EastTwo).Reason);
        }

        [Test]
        public void AreaQueryUsesEffectPathWithoutRequiringOrdinaryLineOfSight()
        {
            var cells = CreateCells(Origin, East, EastTwo);
            cells[East] = new SpatialCellRules(0, false, true, false, 0);
            var board = new SpatialQueryBoard(
                cells,
                new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>
                {
                    [new SpatialDirectedEdge(Origin, East)] = new SpatialEdgeRules(1, true, true),
                    [new SpatialDirectedEdge(East, EastTwo)] = new SpatialEdgeRules(1, true, true),
                },
                new SpatialQueryLimits(1, 16));

            Assert.IsFalse(board.QueryLineOfSight(Origin, EastTwo).HasLineOfSight);
            var area = board.QueryRangeEntry(
                Origin,
                EastTwo,
                0,
                2,
                SpatialQueryKind.Area,
                requireLineOfSight: false,
                activeEffectBlockers: 0);
            Assert.IsTrue(area.IsInRange, area.Reason);
        }

        [Test]
        public void DifferentHeightFailsClosed()
        {
            var cells = CreateCells(Origin, East);
            cells[East] = new SpatialCellRules(1, false, false, false, 0);
            var board = new SpatialQueryBoard(
                cells,
                new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>
                {
                    [new SpatialDirectedEdge(Origin, East)] = new SpatialEdgeRules(2, true, true),
                },
                new SpatialQueryLimits(2, 16));

            Assert.AreEqual(SpatialQueryReasons.HeightRuleUnconfigured,
                board.QueryMetricDistance(Origin, East, SpatialQueryKind.Attack).Reason);
        }

        [Test]
        public void OccupiedLandingIsExcludedWithoutBecomingEntityObstacle()
        {
            var board = CreateBoard(
                new[] { Origin, East },
                new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>
                {
                    [new SpatialDirectedEdge(Origin, East)] = new SpatialEdgeRules(2, true, true),
                });

            var reachable = board.FindReachable(Origin, 1, new[] { East });

            Assert.IsFalse(reachable.ContainsKey(East));
            Assert.IsTrue(board.InspectEdge(Origin, East, SpatialQueryKind.Movement).IsLegal);
        }

        [Test]
        public void UnityFactoryMapsExplicitProfileAndRejectsDuplicateUnitAnchors()
        {
            var profile = ScriptableObject.CreateInstance<EnvironmentProfileData>();
            try
            {
                profile.profileId = "factory_fixture";
                profile.unitsPerRange = 2;
                profile.maxQueryRange = 16;
                profile.surfacePrototypeRefs = new[] { "surface_grassland" };
                profile.phenomenonChannels = new[]
                {
                    new EnvironmentPhenomenonChannelData
                    {
                        channel = EnvironmentPhenomenonChannel.Airflow,
                        phenomenonTypeRefs = new[] { "wind", "gust", "breeze" },
                    },
                    new EnvironmentPhenomenonChannelData
                    {
                        channel = EnvironmentPhenomenonChannel.Visibility,
                        phenomenonTypeRefs = new[] { "mist" },
                    },
                    new EnvironmentPhenomenonChannelData
                    {
                        channel = EnvironmentPhenomenonChannel.Temperature,
                        phenomenonTypeRefs = new[] { "heat" },
                    },
                    new EnvironmentPhenomenonChannelData
                    {
                        channel = EnvironmentPhenomenonChannel.Precipitation,
                        phenomenonTypeRefs = new[] { "rain" },
                    },
                    new EnvironmentPhenomenonChannelData
                    {
                        channel = EnvironmentPhenomenonChannel.SuspendedHazard,
                        phenomenonTypeRefs = new[] { "ash" },
                    },
                    new EnvironmentPhenomenonChannelData
                    {
                        channel = EnvironmentPhenomenonChannel.CloudDischarge,
                        phenomenonTypeRefs = new[] { "storm" },
                    },
                };
                profile.phenomenonPairs = new[]
                {
                    new EnvironmentPhenomenonPairing
                    {
                        channel = EnvironmentPhenomenonChannel.Airflow,
                        firstTypeRef = "wind",
                        secondTypeRef = "gust",
                        resultTypeRef = "gust",
                    },
                };
                profile.elementRelationRefs = new[]
                {
                    "element_wood",
                    "element_fire",
                    "element_earth",
                    "element_metal",
                    "element_water",
                };
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

                var validGrid = new TacticalGridModel();
                validGrid.SetTile(new TacticalTileData(new HexCoord(0, 0)) { OccupiedUnitId = 7 });
                validGrid.SetTile(new TacticalTileData(new HexCoord(1, 0)));

                Assert.IsTrue(
                    SpatialQueryBoardFactory.TryCreate(validGrid, profile, out var snapshot, out var reason),
                    reason);
                Assert.IsTrue(snapshot.Board.InspectEdge(Origin, East, SpatialQueryKind.Attack).IsLegal);
                CollectionAssert.AreEqual(new[] { Origin }, snapshot.Occupied);
                Assert.IsTrue(snapshot.Environment.IsSurfacePrototypeConfigured("surface_grassland", out var surfaceReason));
                Assert.AreEqual(EnvironmentRuntimeReasons.Ok, surfaceReason);
                Assert.IsTrue(snapshot.Environment.TryResolvePhenomenonPair(
                    EnvironmentPhenomenonChannel.Airflow,
                    "gust",
                    "wind",
                    out var resultType,
                    out var pairingReason));
                Assert.AreEqual("gust", resultType);
                Assert.AreEqual(EnvironmentRuntimeReasons.Ok, pairingReason);
                Assert.IsFalse(snapshot.Environment.TryResolvePhenomenonPair(
                    EnvironmentPhenomenonChannel.Airflow,
                    "gust",
                    "breeze",
                    out _,
                    out var missingPairReason));
                Assert.AreEqual(EnvironmentRuntimeReasons.PhenomenonPairNotConfigured, missingPairReason);

                var duplicateGrid = new TacticalGridModel();
                duplicateGrid.SetTile(new TacticalTileData(new HexCoord(0, 0)) { OccupiedUnitId = 7 });
                duplicateGrid.SetTile(new TacticalTileData(new HexCoord(1, 0)) { OccupiedUnitId = 7 });
                Assert.IsFalse(
                    SpatialQueryBoardFactory.TryCreate(duplicateGrid, profile, out _, out var duplicateReason));
                Assert.AreEqual(SpatialQuerySnapshotReasons.DuplicateUnitAnchor, duplicateReason);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        private static SpatialQueryBoard CreateBoard(
            IEnumerable<HexCoord> coords,
            IReadOnlyDictionary<SpatialDirectedEdge, SpatialEdgeRules> edges)
        {
            return new SpatialQueryBoard(CreateCells(coords), edges, new SpatialQueryLimits(2, 16));
        }

        private static Dictionary<HexCoord, SpatialCellRules> CreateCells(
            params HexCoord[] coords)
        {
            return CreateCells((IEnumerable<HexCoord>)coords);
        }

        private static Dictionary<HexCoord, SpatialCellRules> CreateCells(
            IEnumerable<HexCoord> coords)
        {
            var cells = new Dictionary<HexCoord, SpatialCellRules>();
            foreach (var coord in coords)
                cells.Add(coord, new SpatialCellRules(0, false, false, false, 0));
            return cells;
        }
    }
}
