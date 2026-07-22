using System;
using System.Collections.Generic;

namespace TianZhang.Core.SpatialRules
{
    public readonly struct SpatialHexCoord : IEquatable<SpatialHexCoord>
    {
        private static readonly SpatialHexCoord[] DirectionOffsets =
        {
            new SpatialHexCoord(1, 0),
            new SpatialHexCoord(1, -1),
            new SpatialHexCoord(0, -1),
            new SpatialHexCoord(-1, 0),
            new SpatialHexCoord(-1, 1),
            new SpatialHexCoord(0, 1),
        };

        public int Q { get; }
        public int R { get; }
        public int S => -Q - R;

        public SpatialHexCoord(int q, int r)
        {
            Q = q;
            R = r;
        }

        public SpatialHexCoord Step(int direction)
        {
            if (direction < 0 || direction >= DirectionOffsets.Length)
                throw new ArgumentOutOfRangeException(nameof(direction));

            var offset = DirectionOffsets[direction];
            return new SpatialHexCoord(Q + offset.Q, R + offset.R);
        }

        public IEnumerable<SpatialHexCoord> Neighbors()
        {
            for (int direction = 0; direction < DirectionOffsets.Length; direction++)
                yield return Step(direction);
        }

        public bool IsAdjacentTo(SpatialHexCoord other) => TopologicalDistanceTo(other) == 1;

        public int TopologicalDistanceTo(SpatialHexCoord other)
        {
            long deltaQ = other.Q - (long)Q;
            long deltaR = other.R - (long)R;
            return checked((int)((Math.Abs(deltaQ) + Math.Abs(deltaR) + Math.Abs(deltaQ + deltaR)) / 2));
        }

        public bool Equals(SpatialHexCoord other) => Q == other.Q && R == other.R;
        public override bool Equals(object obj) => obj is SpatialHexCoord other && Equals(other);
        public override int GetHashCode() => (Q * 397) ^ R;
        public override string ToString() => $"({Q}, {R}, {S})";

        public static bool operator ==(SpatialHexCoord left, SpatialHexCoord right) => left.Equals(right);
        public static bool operator !=(SpatialHexCoord left, SpatialHexCoord right) => !left.Equals(right);
    }

    public readonly struct SpatialDirectedEdge : IEquatable<SpatialDirectedEdge>
    {
        public SpatialHexCoord From { get; }
        public SpatialHexCoord To { get; }

        public SpatialDirectedEdge(SpatialHexCoord from, SpatialHexCoord to)
        {
            if (!from.IsAdjacentTo(to))
                throw new ArgumentException("A directed spatial edge must connect topological neighbors.", nameof(to));

            From = from;
            To = to;
        }

        public bool Equals(SpatialDirectedEdge other) => From == other.From && To == other.To;
        public override bool Equals(object obj) => obj is SpatialDirectedEdge other && Equals(other);
        public override int GetHashCode() => (From.GetHashCode() * 397) ^ To.GetHashCode();
    }
}
