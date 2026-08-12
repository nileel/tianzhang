namespace TianZhang.Spatial
{
    /// <summary>
    /// 立方坐标六角格（Flat-Top）
    /// q + r + s = 0
    /// </summary>
    [System.Serializable]
    public partial struct HexCoord : System.IEquatable<HexCoord>
    {
        public int q, r, s;

        public HexCoord(int q, int r)
        {
            this.q = q;
            this.r = r;
            this.s = -q - r;
        }

        public HexCoord(int q, int r, int s)
        {
            if ((long)q + r + s != 0)
                throw new System.ArgumentException("Hex cube coordinates must satisfy q + r + s = 0.", nameof(s));
            this.q = q;
            this.r = r;
            this.s = s;
        }

        public int Q => q;
        public int R => r;

        // ---- 六个方向 ----
        public static readonly HexCoord[] Directions = new HexCoord[]
        {
            new HexCoord( 1,  0, -1), // 右  (E)
            new HexCoord( 1, -1,  0), // 右上 (NE)
            new HexCoord( 0, -1,  1), // 左上 (NW)
            new HexCoord(-1,  0,  1), // 左  (W)
            new HexCoord(-1,  1,  0), // 左下 (SW)
            new HexCoord( 0,  1, -1), // 右下 (SE)
        };

        // ---- 运算符 ----
        public static HexCoord operator +(HexCoord a, HexCoord b) =>
            new HexCoord(a.q + b.q, a.r + b.r, a.s + b.s);

        public static HexCoord operator -(HexCoord a, HexCoord b) =>
            new HexCoord(a.q - b.q, a.r - b.r, a.s - b.s);

        public static bool operator ==(HexCoord a, HexCoord b) =>
            a.q == b.q && a.r == b.r && a.s == b.s;

        public static bool operator !=(HexCoord a, HexCoord b) => !(a == b);

        // ---- 常用方法 ----
        public int Distance(HexCoord other)
        {
            return (System.Math.Abs(q - other.q) + System.Math.Abs(r - other.r) +
                System.Math.Abs(s - other.s)) / 2;
        }

        public HexCoord Neighbor(int direction)
        {
            return this + Directions[direction % 6];
        }

        public HexCoord[] AllNeighbors()
        {
            var neighbors = new HexCoord[6];
            for (int i = 0; i < 6; i++)
                neighbors[i] = Neighbor(i);
            return neighbors;
        }

        public int TopologicalDistanceTo(HexCoord other)
        {
            long deltaQ = other.q - (long)q;
            long deltaR = other.r - (long)r;
            long distance = (System.Math.Abs(deltaQ) + System.Math.Abs(deltaR) + System.Math.Abs(deltaQ + deltaR)) / 2;
            if (distance > int.MaxValue)
                throw new System.OverflowException("Hex distance exceeds Int32 range.");
            return (int)distance;
        }

        public System.Collections.Generic.IEnumerable<HexCoord> Neighbors()
        {
            for (int index = 0; index < Directions.Length; index++)
                yield return Neighbor(index);
        }

        public HexCoord Step(int direction)
        {
            if (direction < 0 || direction >= Directions.Length)
                throw new System.ArgumentOutOfRangeException(nameof(direction));
            return Neighbor(direction);
        }

        public bool TryGetDirectionTo(HexCoord neighbor, out int direction)
        {
            direction = DirectionTo(neighbor);
            return direction >= 0;
        }

        /// <summary>六角格方向 → 0-5 索引</summary>
        public int DirectionTo(HexCoord target)
        {
            var diff = target - this;
            for (int i = 0; i < 6; i++)
                if (Directions[i] == diff)
                    return i;
            return -1; // 不相邻
        }

        /// <summary>两个相邻格方向之差（用于判断朝向：正面/侧面/背面）</summary>
        public static int DirectionDiff(int dirA, int dirB)
        {
            int diff = System.Math.Abs(dirA - dirB);
            if (diff > 3) diff = 6 - diff;
            return diff;
        }

        public bool Equals(HexCoord other) => this == other;

        public override bool Equals(object obj) =>
            obj is HexCoord other && this == other;

        public override int GetHashCode() =>
            q.GetHashCode() ^ (r.GetHashCode() << 16);

        public override string ToString() => $"({q}, {r}, {s})";
    }

    public readonly struct SpatialDirectedEdge : System.IEquatable<SpatialDirectedEdge>
    {
        public SpatialDirectedEdge(HexCoord from, HexCoord to)
        {
            if (!from.TryGetDirectionTo(to, out _))
                throw new System.ArgumentException("Directed spatial edges must connect topological neighbors.", nameof(to));
            From = from;
            To = to;
        }

        public HexCoord From { get; }
        public HexCoord To { get; }

        public bool Equals(SpatialDirectedEdge other) => From == other.From && To == other.To;
        public override bool Equals(object obj) => obj is SpatialDirectedEdge other && Equals(other);
        public override int GetHashCode() => unchecked((From.GetHashCode() * 397) ^ To.GetHashCode());
    }
}
