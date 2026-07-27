using System;
using System.Collections.Generic;
using TianZhang.Core.SpatialRules;

namespace TianZhang.Tactical
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
            IReadOnlyDictionary<int, SpatialHexCoord> unitAnchors)
        {
            Board = board ?? throw new ArgumentNullException(nameof(board));
            Environment = environment ?? throw new ArgumentNullException(nameof(environment));
            UnitAnchors = unitAnchors ?? throw new ArgumentNullException(nameof(unitAnchors));
        }

        public SpatialQueryBoard Board { get; }
        public EnvironmentProfileRuntime Environment { get; }
        public IReadOnlyDictionary<int, SpatialHexCoord> UnitAnchors { get; }

        public IReadOnlyCollection<SpatialHexCoord> Occupied
        {
            get
            {
                var occupied = new List<SpatialHexCoord>(UnitAnchors.Count);
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
            EnvironmentProfileData profile,
            out SpatialQuerySnapshot snapshot,
            out string reason)
        {
            snapshot = null;
            if (grid == null)
                return Fail(SpatialQuerySnapshotReasons.GridNotConfigured, out reason);

            if (!grid.TryConfigureEnvironmentProfile(profile, out reason))
                return false;

            return TryCreate(grid, out snapshot, out reason);
        }

        public static bool TryCreate(
            TacticalGridModel grid,
            out SpatialQuerySnapshot snapshot,
            out string reason)
        {
            snapshot = null;
            if (grid == null)
                return Fail(SpatialQuerySnapshotReasons.GridNotConfigured, out reason);
            if (grid.EnvironmentRules == null)
                return Fail(SpatialQuerySnapshotReasons.EnvironmentProfileNotConfigured, out reason);
            if (grid.Count == 0)
                return Fail(SpatialQuerySnapshotReasons.CellsNotConfigured, out reason);

            var environment = grid.EnvironmentRules;

            var cells = new Dictionary<SpatialHexCoord, SpatialCellRules>();
            var unitAnchors = new Dictionary<int, SpatialHexCoord>();
            foreach (var tile in grid.Tiles)
            {
                if (tile.IsEntityObstacle && string.IsNullOrWhiteSpace(tile.EntityObstacleSourceId))
                    return Fail(SpatialQuerySnapshotReasons.EntityObstacleSourceNotConfigured, out reason);

                var coord = ToSpatial(tile.Coord);
                cells.Add(coord, new SpatialCellRules(
                    tile.HeightLevel,
                    tile.BlocksGroundMove,
                    tile.BlocksLineOfSight,
                    tile.IsEntityObstacle,
                    0));
                if (!tile.IsOccupied)
                    continue;
                if (unitAnchors.ContainsKey(tile.OccupiedUnitId))
                    return Fail(SpatialQuerySnapshotReasons.DuplicateUnitAnchor, out reason);
                unitAnchors.Add(tile.OccupiedUnitId, coord);
            }

            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            foreach (var configured in environment.DirectedEdges)
            {
                var from = new SpatialHexCoord(configured.fromQ, configured.fromR);
                var to = new SpatialHexCoord(configured.toQ, configured.toR);
                if (!cells.ContainsKey(from) || !cells.ContainsKey(to))
                    return Fail(SpatialQuerySnapshotReasons.DirectedEdgeCellNotConfigured, out reason);

                SpatialDirectedEdge edge;
                SpatialEdgeRules rules;
                try
                {
                    edge = new SpatialDirectedEdge(from, to);
                    rules = new SpatialEdgeRules(
                        configured.metricDistanceUnits,
                        configured.allowsMovement,
                        configured.allowsEffects);
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

        private static SpatialHexCoord ToSpatial(TianZhang.Core.HexCoord coord) =>
            new SpatialHexCoord(coord.q, coord.r);

        private static bool Fail(string failureReason, out string reason)
        {
            reason = failureReason;
            return false;
        }
    }
}
