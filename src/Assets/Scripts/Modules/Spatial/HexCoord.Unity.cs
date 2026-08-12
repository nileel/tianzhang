using UnityEngine;

namespace TianZhang.Spatial
{
    public partial struct HexCoord
    {
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
    }
}
