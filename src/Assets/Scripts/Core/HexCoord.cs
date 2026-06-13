using UnityEngine;

namespace TianZhang.Core
{
    /// <summary>
    /// 立方坐标六角格（Flat-Top）
    /// q + r + s = 0
    /// </summary>
    [System.Serializable]
    public struct HexCoord
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
            this.q = q;
            this.r = r;
            this.s = s;
        }

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
            return (Mathf.Abs(q - other.q) + Mathf.Abs(r - other.r) + Mathf.Abs(s - other.s)) / 2;
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
            int diff = Mathf.Abs(dirA - dirB);
            if (diff > 3) diff = 6 - diff;
            return diff;
        }

        // ---- Unity Tilemap 转换（Flat-Top, odd-r offset）----
        public Vector2Int ToOffset()
        {
            int col = q + (r - (r & 1)) / 2;
            int row = r;
            return new Vector2Int(col, row);
        }

        public static HexCoord FromOffset(Vector2Int offset)
        {
            int col = offset.x;
            int row = offset.y;
            int q = col - (row - (row & 1)) / 2;
            int r = row;
            return new HexCoord(q, r);
        }

        // ---- 世界坐标（Flat-Top hex）----
        public Vector3 ToWorld(float size = 1f)
        {
            float x = size * (1.5f * q);
            float y = size * (Mathf.Sqrt(3f) / 2f * q + Mathf.Sqrt(3f) * r);
            return new Vector3(x, y, 0);
        }

        public static HexCoord FromWorld(Vector3 world, float size = 1f)
        {
            float q = (2f / 3f * world.x) / size;
            float r = (-1f / 3f * world.x + Mathf.Sqrt(3f) / 3f * world.y) / size;
            return Round(q, r);
        }

        private static HexCoord Round(float q, float r)
        {
            float s = -q - r;
            int rq = Mathf.RoundToInt(q);
            int rr = Mathf.RoundToInt(r);
            int rs = Mathf.RoundToInt(s);

            float dq = Mathf.Abs(rq - q);
            float dr = Mathf.Abs(rr - r);
            float ds = Mathf.Abs(rs - s);

            if (dq > dr && dq > ds)
                rq = -rr - rs;
            else if (dr > ds)
                rr = -rq - rs;

            return new HexCoord(rq, rr, -rq - rr);
        }

        public override bool Equals(object obj) =>
            obj is HexCoord other && this == other;

        public override int GetHashCode() =>
            q.GetHashCode() ^ (r.GetHashCode() << 16);

        public override string ToString() => $"({q}, {r}, {s})";
    }
}
