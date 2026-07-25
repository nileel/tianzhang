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

        public SpatialHexCoord(int q, int r)
        {
            Q = q;
            R = r;
        }

        public int Q { get; }
        public int R { get; }

        public int TopologicalDistanceTo(SpatialHexCoord other)
        {
            long deltaQ = other.Q - (long)Q;
            long deltaR = other.R - (long)R;
            long distance = (Math.Abs(deltaQ) + Math.Abs(deltaR) + Math.Abs(deltaQ + deltaR)) / 2;
            if (distance > int.MaxValue)
                throw new OverflowException("Hex distance exceeds Int32 range.");
            return (int)distance;
        }

        public IEnumerable<SpatialHexCoord> Neighbors()
        {
            for (int index = 0; index < DirectionOffsets.Length; index++)
                yield return this + DirectionOffsets[index];
        }

        public SpatialHexCoord Step(int direction)
        {
            if (direction < 0 || direction >= DirectionOffsets.Length)
                throw new ArgumentOutOfRangeException(nameof(direction));
            return this + DirectionOffsets[direction];
        }

        public bool TryGetDirectionTo(SpatialHexCoord neighbor, out int direction)
        {
            for (int index = 0; index < DirectionOffsets.Length; index++)
            {
                if (this + DirectionOffsets[index] == neighbor)
                {
                    direction = index;
                    return true;
                }
            }

            direction = -1;
            return false;
        }

        public static SpatialHexCoord operator +(SpatialHexCoord left, SpatialHexCoord right) =>
            new SpatialHexCoord(checked(left.Q + right.Q), checked(left.R + right.R));

        public static bool operator ==(SpatialHexCoord left, SpatialHexCoord right) => left.Equals(right);
        public static bool operator !=(SpatialHexCoord left, SpatialHexCoord right) => !left.Equals(right);

        public bool Equals(SpatialHexCoord other) => Q == other.Q && R == other.R;
        public override bool Equals(object obj) => obj is SpatialHexCoord other && Equals(other);
        public override int GetHashCode() => unchecked((Q * 397) ^ R);
        public override string ToString() => $"({Q},{R})";
    }

    public readonly struct SpatialDirectedEdge : IEquatable<SpatialDirectedEdge>
    {
        public SpatialDirectedEdge(SpatialHexCoord from, SpatialHexCoord to)
        {
            if (!from.TryGetDirectionTo(to, out _))
                throw new ArgumentException("Directed spatial edges must connect topological neighbors.", nameof(to));
            From = from;
            To = to;
        }

        public SpatialHexCoord From { get; }
        public SpatialHexCoord To { get; }

        public bool Equals(SpatialDirectedEdge other) => From == other.From && To == other.To;
        public override bool Equals(object obj) => obj is SpatialDirectedEdge other && Equals(other);
        public override int GetHashCode() => unchecked((From.GetHashCode() * 397) ^ To.GetHashCode());
    }
}
