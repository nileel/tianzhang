using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Content;
using TianZhang.Spatial;

namespace TianZhang.Infrastructure.UnityContent
{
    public static class SpatialQuerySnapshotReasons
    {
        public const string GridNotConfigured = "grid_not_configured";
        public const string EnvironmentProfileNotConfigured = "environment_profile_not_configured";
        public const string QueryLimitsNotConfigured = "query_limits_not_configured";
        public const string CellsNotConfigured = "cells_not_configured";
        public const string DirectedEdgesNotConfigured = "directed_edges_not_configured";
        public const string DirectedEdgeCellNotConfigured = "directed_edge_cell_not_configured";
        public const string InvalidDirectedEdge = "invalid_directed_edge";
        public const string DuplicateDirectedEdge = "duplicate_directed_edge";
        public const string DuplicateUnitAnchor = "duplicate_unit_anchor";
        public const string EntityObstacleSourceNotConfigured = "entity_obstacle_source_not_configured";
    }

    public sealed class SpatialQuerySnapshot
    {
        internal SpatialQuerySnapshot(
            SpatialQueryBoard board,
            EnvironmentProfileRuntime environment,
            IReadOnlyDictionary<int, HexCoord> unitAnchors)
        {
            Board = board ?? throw new ArgumentNullException(nameof(board));
            Environment = environment ?? throw new ArgumentNullException(nameof(environment));
            UnitAnchors = unitAnchors ?? throw new ArgumentNullException(nameof(unitAnchors));
        }

        public SpatialQueryBoard Board { get; }
        public EnvironmentProfileRuntime Environment { get; }
        public IReadOnlyDictionary<int, HexCoord> UnitAnchors { get; }

        public IReadOnlyCollection<HexCoord> Occupied
        {
            get
            {
                var occupied = new List<HexCoord>(UnitAnchors.Count);
                foreach (var coord in UnitAnchors.Values)
                    occupied.Add(coord);
                return occupied;
            }
        }
    }

    public static class SpatialQueryBoardFactory
    {
        public static bool TryCreate(
            TacticalGridModel grid,
            EnvironmentProfileAsset profile,
            out SpatialQuerySnapshot snapshot,
            out string reason)
        {
            snapshot = null;
            if (grid == null)
                return Fail(SpatialQuerySnapshotReasons.GridNotConfigured, out reason);

            if (profile == null)
                return Fail(SpatialQuerySnapshotReasons.EnvironmentProfileNotConfigured, out reason);
            if (!profile.TryCreateDefinition(out var definition, out reason) ||
                !EnvironmentProfileRuntime.TryCreate(definition, out var environment, out reason))
                return false;

            return TryCreate(grid, environment, out snapshot, out reason);
        }

        public static bool TryCreate(
            TacticalGridModel grid,
            EnvironmentProfileRuntime environment,
            out SpatialQuerySnapshot snapshot,
            out string reason)
        {
            snapshot = null;
            if (grid == null)
                return Fail(SpatialQuerySnapshotReasons.GridNotConfigured, out reason);
            if (environment == null)
                return Fail(SpatialQuerySnapshotReasons.EnvironmentProfileNotConfigured, out reason);
            if (grid.Count == 0)
                return Fail(SpatialQuerySnapshotReasons.CellsNotConfigured, out reason);

            var cells = new Dictionary<HexCoord, SpatialCellRules>();
            var airCells = environment.FlightSpace?.cells.ToDictionary(value => new HexCoord(value.q, value.r));
            var airEdges = environment.FlightSpace?.edges.ToDictionary(value =>
                new SpatialDirectedEdge(new HexCoord(value.fromQ, value.fromR), new HexCoord(value.toQ, value.toR)));
            var unitAnchors = new Dictionary<int, HexCoord>();
            foreach (var tile in grid.Tiles)
            {
                if (tile.IsEntityObstacle && string.IsNullOrWhiteSpace(tile.EntityObstacleSourceId))
                    return Fail(SpatialQuerySnapshotReasons.EntityObstacleSourceNotConfigured, out reason);

                var coord = tile.Coord;
                EnvironmentFlightCell air = airCells != null && airCells.TryGetValue(coord, out var configuredAir) ? configuredAir : null;
                cells.Add(coord, new SpatialCellRules(
                    tile.HeightLevel,
                    tile.BlocksGroundMove,
                    tile.BlocksLineOfSight,
                    tile.IsEntityObstacle,
                    tile.MovementBurdenUnits, air?.allowsFlight == true && !tile.BlocksFlyingMove,
                    air?.maximumEffectiveHeight ?? 0, air?.movementBurdenUnits ?? 0));
                if (!tile.IsOccupied)
                    continue;
                if (unitAnchors.ContainsKey(tile.OccupiedUnitId))
                    return Fail(SpatialQuerySnapshotReasons.DuplicateUnitAnchor, out reason);
                unitAnchors.Add(tile.OccupiedUnitId, coord);
            }

            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            foreach (var configured in environment.DirectedEdges)
            {
                var from = new HexCoord(configured.fromQ, configured.fromR);
                var to = new HexCoord(configured.toQ, configured.toR);
                if (!cells.ContainsKey(from) || !cells.ContainsKey(to))
                    return Fail(SpatialQuerySnapshotReasons.DirectedEdgeCellNotConfigured, out reason);

                SpatialDirectedEdge edge;
                SpatialEdgeRules rules;
                try
                {
                    edge = new SpatialDirectedEdge(from, to);
                    EnvironmentFlightEdge air = airEdges != null && airEdges.TryGetValue(edge, out var configuredAir) ? configuredAir : null;
                    rules = new SpatialEdgeRules(
                        configured.metricDistanceUnits,
                        configured.allowsMovement,
                        configured.allowsEffects,
                        clearanceLimit: configured.clearanceLimit,
                        edgeKind: ToSpatialEdgeKind(configured.edgeKind),
                        movementBurdenUnits: configured.movementBurdenUnits,
                        connectionId: configured.connectionId, allowsFlight: air?.allowsFlight == true,
                        flightMovementBurdenUnits: air?.movementBurdenUnits ?? 0);
                }
                catch (ArgumentException)
                {
                    return Fail(SpatialQuerySnapshotReasons.InvalidDirectedEdge, out reason);
                }

                if (edges.ContainsKey(edge))
                    return Fail(SpatialQuerySnapshotReasons.DuplicateDirectedEdge, out reason);
                edges.Add(edge, rules);
            }

            snapshot = new SpatialQuerySnapshot(
                new SpatialQueryBoard(
                    cells,
                    edges,
                    new SpatialQueryLimits(environment.UnitsPerRange, environment.MaxQueryRange)),
                environment,
                unitAnchors);
            reason = SpatialQueryReasons.Ok;
            return true;
        }

        private static SpatialEdgeKind ToSpatialEdgeKind(EnvironmentEdgeKind kind)
        {
            return kind switch
            {
                EnvironmentEdgeKind.Flat => SpatialEdgeKind.Flat,
                EnvironmentEdgeKind.Slope => SpatialEdgeKind.Slope,
                EnvironmentEdgeKind.Stairs => SpatialEdgeKind.Stairs,
                EnvironmentEdgeKind.OneWayDrop => SpatialEdgeKind.OneWayDrop,
                EnvironmentEdgeKind.OpenCliff => SpatialEdgeKind.OpenCliff,
                EnvironmentEdgeKind.EntityWall => SpatialEdgeKind.EntityWall,
                EnvironmentEdgeKind.Climb => SpatialEdgeKind.Climb,
                EnvironmentEdgeKind.Bridge => SpatialEdgeKind.Bridge,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }

        private static bool Fail(string failureReason, out string reason)
        {
            reason = failureReason;
            return false;
        }
    }
}
