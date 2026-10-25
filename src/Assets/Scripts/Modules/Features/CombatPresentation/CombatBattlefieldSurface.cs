using System;
using System.Collections.Generic;
using TianZhang.Gameplay.Contracts;
using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>Finite D1 support profiles baked by the scene builder. No movement or occupancy rules.</summary>
    public sealed class CombatBattlefieldSurface : MonoBehaviour
    {
        [Serializable] public sealed class Cell
        {
            public int q, r;
            public int kind; // 0 flat, 1 C1 ramp, 2 actual horizontal stair treads
            public float lowerY, upperY, startZ, endZ;
            public float[] riserZ = Array.Empty<float>();
            public float[] riserY = Array.Empty<float>();
        }

        [SerializeField] private Cell[] cells = Array.Empty<Cell>();
        private Dictionary<Vector2Int,Cell> index;
        private static readonly Vector2Int[] Neighbors = { Vector2Int.zero,new Vector2Int(1,0),new Vector2Int(1,-1),
            new Vector2Int(0,-1),new Vector2Int(-1,0),new Vector2Int(-1,1),new Vector2Int(0,1) };
        public IReadOnlyList<Cell> Cells => cells;
        public bool IsStair(Vector3 point)
        {
            foreach(Cell cell in cells) if(cell.kind==2 && Contains(cell,point)) return true;
            return false;
        }
        public static Vector3 Horizontal(int q, int r) => new Vector3(q + .5f*r, 0, 1 + .8660254f*r);
        public static bool Contains(Cell cell, Vector3 p)
        {
            Vector3 d = p - Horizontal(cell.q, cell.r);
            return Mathf.Abs(d.x) <= .50001f && Mathf.Abs(.5f*d.x + .8660254f*d.z) <= .50001f &&
                Mathf.Abs(.5f*d.x - .8660254f*d.z) <= .50001f;
        }

        public float Height(Cell cell, float z)
        {
            if (cell.kind == 1)
                return Mathf.Lerp(cell.lowerY, cell.upperY,
                    (1-Mathf.Cos(Mathf.PI*Mathf.Clamp01((z-cell.startZ)/(cell.endZ-cell.startZ))))*.5f);
            float h = cell.lowerY;
            if (cell.kind == 2)
                for (int i=0; i<cell.riserZ.Length; i++)
                    if (z >= cell.riserZ[i]) h = cell.riserY[i];
            return h;
        }

        public bool TryHeight(Vector3 p, out float height)
        {
            if(index==null)
            {
                index=new Dictionary<Vector2Int,Cell>();
                foreach(Cell cell in cells) index.Add(new Vector2Int(cell.q,cell.r),cell);
            }
            height = float.NegativeInfinity;
            int r=Mathf.RoundToInt((p.z-1)/.8660254f),q=Mathf.RoundToInt(p.x-.5f*r);
            var key=new Vector2Int(q,r);
            foreach(var offset in Neighbors)
                if(index.TryGetValue(key+offset,out Cell cell) && Contains(cell,p))
                    height = Mathf.Max(height,Height(cell,p.z));
            return !float.IsNegativeInfinity(height);
        }

        public Vector3 Center(CombatUnitPresentationHex hex)
        {
            foreach (Cell cell in cells)
                if (cell.q == hex.Q && cell.r == hex.R)
                {
                    Vector3 p = Horizontal(cell.q,cell.r);
                    p.y = Height(cell,p.z);
                    return p;
                }
            throw new InvalidOperationException("presentation_support_cell_missing:"+hex.Q+":"+hex.R);
        }

        public bool TryCell(Vector3 p, out CombatUnitPresentationHex hex)
        {
            foreach (Cell cell in cells)
                if (Contains(cell,p)) { hex = new CombatUnitPresentationHex(cell.q,cell.r); return true; }
            hex = default;
            return false;
        }

        public bool TryPick(Ray ray, out CombatUnitPresentationHex hex)
        {
            hex = default;
            CombatUnitPresentationHex result = default;
            float nearest = float.PositiveInfinity;
            if (Mathf.Abs(ray.direction.y) < .00001f) return false;
            foreach (Cell cell in cells)
            {
                if (cell.kind == 1)
                {
                    float a = (cell.upperY-ray.origin.y)/ray.direction.y;
                    float b = (cell.lowerY-ray.origin.y)/ray.direction.y;
                    float lo = Mathf.Min(a,b), hi = Mathf.Max(a,b);
                    float sign = ray.GetPoint(lo).y-Height(cell,ray.GetPoint(lo).z);
                    for (int i=0;i<24;i++)
                    {
                        float mid = (lo+hi)*.5f;
                        Vector3 p = ray.GetPoint(mid);
                        if ((p.y-Height(cell,p.z))*sign > 0) lo = mid; else hi = mid;
                    }
                    Consider((lo+hi)*.5f,cell);
                }
                else
                {
                    Consider((cell.lowerY-ray.origin.y)/ray.direction.y,cell);
                    for (int i=0;i<cell.riserY.Length;i++)
                        Consider((cell.riserY[i]-ray.origin.y)/ray.direction.y,cell);
                }
            }
            hex = result;
            return !float.IsPositiveInfinity(nearest);

            void Consider(float distance, Cell cell)
            {
                if (distance < 0 || distance >= nearest) return;
                Vector3 p = ray.GetPoint(distance);
                if (!Contains(cell,p) || Mathf.Abs(p.y-Height(cell,p.z)) > .0001f) return;
                nearest = distance;
                result = new CombatUnitPresentationHex(cell.q,cell.r);
            }
        }

        public void DrawRing(LineRenderer line, CombatUnitPresentationHex hex, float radius, float offset)
        {
            Vector3 center = Center(hex);
            const int subdivisions = 12;
            line.positionCount = 6*subdivisions;
            for (int corner=0;corner<6;corner++)
            {
                float a = (30+corner*60)*Mathf.Deg2Rad, b = a+Mathf.PI/3;
                Vector3 start = center+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius;
                Vector3 end = center+new Vector3(Mathf.Cos(b),0,Mathf.Sin(b))*radius;
                for (int i=0;i<subdivisions;i++)
                {
                    Vector3 p = Vector3.Lerp(start,end,i/(float)subdivisions);
                    if (!TryHeight(p,out float y)) throw new InvalidOperationException("outline_outside_support");
                    p.y = y+offset;
                    line.SetPosition(corner*subdivisions+i,p);
                }
            }
        }
        public void DrawPath(LineRenderer line,IReadOnlyList<Vector3> points,float offset)
        {
            var samples=new List<Vector3>();
            for(int segment=1;segment<points.Count;segment++)
            {
                int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(points[segment-1],points[segment])/.03f));
                for(int i=segment==1 ? 0 : 1;i<=count;i++)
                {
                    Vector3 p=Vector3.Lerp(points[segment-1],points[segment],i/(float)count);
                    if(TryHeight(p,out float y)) p.y=y+offset;
                    samples.Add(p);
                }
            }
            line.positionCount=samples.Count;
            line.SetPositions(samples.ToArray());
        }
    }
}
