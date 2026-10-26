using System;
using System.Collections.Generic;
using System.Linq;

namespace TianZhang.Content
{
    /// <summary>Explicit air permissions. An absent configuration grants no flight.</summary>
    [Serializable]
    public sealed class EnvironmentFlightConfiguration
    {
        public EnvironmentFlightCell[] cells;
        public EnvironmentFlightEdge[] edges;
        public bool IsEmpty => (cells == null || cells.Length == 0) && (edges == null || edges.Length == 0);

        public bool TryValidate(EnvironmentBattlefieldCell[] ground, EnvironmentDirectedEdge[] topology, out string reason)
        {
            reason = "flight_space_invalid";
            if (cells == null || edges == null || ground == null || topology == null ||
                cells.Length != ground.Length || edges.Length != topology.Length) return false;
            var remainingCells = new HashSet<string>(ground.Select(value => Key(value.q, value.r)));
            foreach (var cell in cells)
                if (cell == null || !remainingCells.Remove(Key(cell.q, cell.r)) || cell.maximumEffectiveHeight < 0 ||
                    cell.movementBurdenUnits < 0) return false;
            var remainingEdges = new HashSet<string>(topology.Select(value => EdgeKey(value.fromQ, value.fromR, value.toQ, value.toR)));
            foreach (var edge in edges)
                if (edge == null || !remainingEdges.Remove(EdgeKey(edge.fromQ, edge.fromR, edge.toQ, edge.toR)) ||
                    edge.movementBurdenUnits < 0) return false;
            reason = string.Empty;
            return true;
        }

        internal static string Key(int q, int r) => q + ":" + r;
        private static string EdgeKey(int q, int r, int tq, int tr) => Key(q, r) + ">" + Key(tq, tr);
    }

    [Serializable]
    public sealed class EnvironmentFlightCell
    {
        public int q, r;
        public bool allowsFlight;
        public int maximumEffectiveHeight;
        public int movementBurdenUnits;
    }

    [Serializable]
    public sealed class EnvironmentFlightEdge
    {
        public int fromQ, fromR, toQ, toR;
        public bool allowsFlight;
        public int movementBurdenUnits;
    }
}
