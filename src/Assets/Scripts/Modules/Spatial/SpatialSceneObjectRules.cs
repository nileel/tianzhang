using System;
using System.Collections.Generic;

namespace TianZhang.Spatial
{
    /// <summary>Passive spatial facts projected from one scene-object instance.</summary>
    public sealed class SpatialSceneObjectRules
    {
        public SpatialSceneObjectRules(string id, HexCoord cell, HexCoord? edgeOther, bool blocksMovement,
            bool blocksSight, int coverTier, int maxProtectedBodyScaleRank = 2,
            string replacesWallConnectionId = null, bool isSupportMarker = false)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Scene object ID is required.", nameof(id));
            if (edgeOther.HasValue && !cell.TryGetDirectionTo(edgeOther.Value, out _))
                throw new ArgumentException("Scene object edge must join adjacent cells.", nameof(edgeOther));
            if (coverTier < 0 || coverTier > 2) throw new ArgumentOutOfRangeException(nameof(coverTier));
            if (maxProtectedBodyScaleRank < 0 || coverTier > 0 && maxProtectedBodyScaleRank == 0)
                throw new ArgumentOutOfRangeException(nameof(maxProtectedBodyScaleRank));
            if (replacesWallConnectionId != null &&
                (!edgeOther.HasValue || string.IsNullOrWhiteSpace(replacesWallConnectionId)))
                throw new ArgumentException("A wall replacement needs an edge and connection ID.", nameof(replacesWallConnectionId));
            Id = id; Cell = cell; EdgeOther = edgeOther;
            BlocksMovement = blocksMovement; BlocksSight = blocksSight; CoverTier = coverTier;
            MaxProtectedBodyScaleRank = maxProtectedBodyScaleRank;
            ReplacesWallConnectionId = replacesWallConnectionId;
            if (isSupportMarker && (blocksMovement || blocksSight || coverTier != 0 || replacesWallConnectionId != null))
                throw new ArgumentException("A support marker cannot own obstruction or cover.", nameof(isSupportMarker));
            IsSupportMarker = isSupportMarker;
        }

        public string Id { get; }
        public HexCoord Cell { get; }
        public HexCoord? EdgeOther { get; }
        public bool BlocksMovement { get; }
        public bool BlocksSight { get; }
        public int CoverTier { get; }
        public int MaxProtectedBodyScaleRank { get; }
        public string ReplacesWallConnectionId { get; }
        public bool IsSupportMarker { get; }
    }

    /// <summary>Immutable spatial projection; never owns durability, damage, turns or AI.</summary>
    internal sealed class SpatialSceneObjectSet
    {
        internal static readonly SpatialSceneObjectSet Empty = new SpatialSceneObjectSet();
        private readonly Dictionary<string, SpatialSceneObjectRules> byId =
            new Dictionary<string, SpatialSceneObjectRules>(StringComparer.Ordinal);
        private readonly Dictionary<HexCoord, SpatialSceneObjectRules> byCell =
            new Dictionary<HexCoord, SpatialSceneObjectRules>();
        private readonly Dictionary<SpatialDirectedEdge, SpatialSceneObjectRules> byEdge =
            new Dictionary<SpatialDirectedEdge, SpatialSceneObjectRules>();

        private SpatialSceneObjectSet() { }

        internal SpatialSceneObjectSet(IReadOnlyList<SpatialSceneObjectRules> objects,
            IReadOnlyDictionary<HexCoord, SpatialCellRules> cells,
            IReadOnlyDictionary<SpatialDirectedEdge, SpatialEdgeRules> edges)
        {
            if (objects == null) throw new ArgumentNullException(nameof(objects));
            foreach (SpatialSceneObjectRules item in objects)
            {
                if (item == null || byId.ContainsKey(item.Id))
                    throw new ArgumentException("Scene objects require distinct non-null IDs.", nameof(objects));
                if (!cells.ContainsKey(item.Cell) || item.EdgeOther.HasValue && !cells.ContainsKey(item.EdgeOther.Value))
                    throw new ArgumentException("Scene object anchor is outside the board.", nameof(objects));
                byId.Add(item.Id, item);
                // An ability marker is targetable but does not consume a primary scene-object anchor.
                if (item.IsSupportMarker) continue;
                if (!item.EdgeOther.HasValue)
                {
                    if (byCell.ContainsKey(item.Cell))
                        throw new ArgumentException("Only one primary scene object may occupy a cell.", nameof(objects));
                    byCell.Add(item.Cell, item);
                    continue;
                }
                var forward = new SpatialDirectedEdge(item.Cell, item.EdgeOther.Value);
                var reverse = new SpatialDirectedEdge(item.EdgeOther.Value, item.Cell);
                if (byEdge.ContainsKey(forward))
                    throw new ArgumentException("Only one primary scene object may occupy an edge.", nameof(objects));
                if (item.ReplacesWallConnectionId != null &&
                    (!MatchesWall(edges, forward, item.ReplacesWallConnectionId) ||
                     !MatchesWall(edges, reverse, item.ReplacesWallConnectionId)))
                    throw new ArgumentException("Wall replacement must match both anchored EntityWall edges and their connection ID.", nameof(objects));
                byEdge.Add(forward, item); byEdge.Add(reverse, item);
            }
        }

        private static bool MatchesWall(IReadOnlyDictionary<SpatialDirectedEdge, SpatialEdgeRules> edges,
            SpatialDirectedEdge key, string connectionId) => edges.TryGetValue(key, out var edge) &&
            edge.EdgeKind == SpatialEdgeKind.EntityWall &&
            string.Equals(edge.ConnectionId, connectionId, StringComparison.Ordinal);

        internal bool TryGet(string id, out SpatialSceneObjectRules item)
        {
            item = null;
            return id != null && byId.TryGetValue(id, out item);
        }

        internal SpatialEdgeRules ResolveEdge(SpatialDirectedEdge key, SpatialEdgeRules original)
        {
            if (!byEdge.TryGetValue(key, out var item) || item.ReplacesWallConnectionId == null) return original;
            // Only this exact pair replaces its static wall. Other edges may share the connection ID.
            return new SpatialEdgeRules(original.MetricDistanceUnits, true, true, original.EffectBlockerMask,
                original.ClearanceLimit, SpatialEdgeKind.Flat, original.MovementBurdenUnits, original.ConnectionId,
                original.AllowsFlight, original.FlightMovementBurdenUnits);
        }

        internal bool BlocksCell(HexCoord cell, bool movement) =>
            byCell.TryGetValue(cell, out var item) && (movement ? item.BlocksMovement : item.BlocksSight);

        internal bool BlocksEdge(HexCoord from, HexCoord to, bool movement) =>
            byEdge.TryGetValue(new SpatialDirectedEdge(from, to), out var item) &&
            (movement ? item.BlocksMovement : item.BlocksSight);

        internal int GetCoverTier(HexCoord target, HexCoord? previous, int bodyScaleRank, bool ignoreCellCover)
        {
            int tier = 0;
            if (!ignoreCellCover && byCell.TryGetValue(target, out var cell) &&
                bodyScaleRank <= cell.MaxProtectedBodyScaleRank) tier = cell.CoverTier;
            if (previous.HasValue && byEdge.TryGetValue(new SpatialDirectedEdge(previous.Value, target), out var edge) &&
                bodyScaleRank <= edge.MaxProtectedBodyScaleRank) tier = Math.Max(tier, edge.CoverTier);
            return tier;
        }

        internal IReadOnlyList<SpatialSceneObjectRules> WithoutBlocking(string id)
        {
            var result = new List<SpatialSceneObjectRules>(byId.Count);
            foreach (SpatialSceneObjectRules item in byId.Values)
                result.Add(item.Id == id
                    ? new SpatialSceneObjectRules(item.Id, item.Cell, item.EdgeOther, false, false, 0,
                        item.MaxProtectedBodyScaleRank, item.ReplacesWallConnectionId, item.IsSupportMarker)
                    : item);
            return result.AsReadOnly();
        }
    }
}
