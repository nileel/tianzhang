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

        [Serializable] public sealed class DropEdge
        {
            public int fromQ, fromR, toQ, toR;
        }

        [SerializeField] private Cell[] cells = Array.Empty<Cell>();
        [SerializeField] private DropEdge[] oneWayDrops = Array.Empty<DropEdge>();
        private Dictionary<Vector2Int,Cell> index;
        public sealed class EarthRamp
        {
            public EarthRamp(CombatUnitPresentationHex a,CombatUnitPresentationHex b,Vector3 low,Vector3 high)
            { A=a;B=b;Low=low;High=high; }
            public CombatUnitPresentationHex A {get;} public CombatUnitPresentationHex B {get;}
            public Vector3 Low {get;} public Vector3 High {get;}
            public bool TryHeight(Vector3 point,out float height)
            {
                Vector3 direction=High-Low;direction.y=0;direction.Normalize();
                Vector3 delta=point-Low;delta.y=0;
                float along=Vector3.Dot(delta,direction),across=Vector3.Dot(delta,Vector3.Cross(Vector3.up,direction));
                height=Mathf.Lerp(Low.y,High.y,(1-Mathf.Cos(Mathf.PI*Mathf.Clamp01((along-.1f)/.4f)))*.5f);
                return along>=.09999f && along<=.50001f && Mathf.Abs(across)<=.25001f;
            }
        }
        private EarthRamp[] earthRamps=Array.Empty<EarthRamp>();
        public void SetEarthRamps(EarthRamp[] value)=>earthRamps=value;
        public bool IsEarthRamp(Vector3 point)=>Array.Exists(earthRamps,r=>r.TryHeight(point,out _));
        public bool HasEarthRamp(CombatUnitPresentationHex a,CombatUnitPresentationHex b)=>
            Array.Exists(earthRamps,r=>(r.A.Q==a.Q && r.A.R==a.R && r.B.Q==b.Q && r.B.R==b.R)||
                (r.B.Q==a.Q && r.B.R==a.R && r.A.Q==b.Q && r.A.R==b.R));
        private static readonly Vector2Int[] Neighbors = { Vector2Int.zero,new Vector2Int(1,0),new Vector2Int(1,-1),
            new Vector2Int(0,-1),new Vector2Int(-1,0),new Vector2Int(-1,1),new Vector2Int(0,1) };
        public IReadOnlyList<Cell> Cells => cells;
        public bool IsOneWayDrop(CombatUnitPresentationHex from, CombatUnitPresentationHex to)
        {
            if(HasEarthRamp(from,to))return false;
            foreach (DropEdge edge in oneWayDrops)
                if (edge.fromQ == from.Q && edge.fromR == from.R && edge.toQ == to.Q && edge.toR == to.R)
                    return true;
            return false;
        }
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
            foreach(var ramp in earthRamps) if(ramp.TryHeight(p,out float y)) height=Mathf.Max(height,y);
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
            foreach (var ramp in earthRamps)
            {
                float a = (ramp.High.y-ray.origin.y)/ray.direction.y;
                float b = (ramp.Low.y-ray.origin.y)/ray.direction.y;
                float lo = Mathf.Min(a,b), hi = Mathf.Max(a,b);
                ramp.TryHeight(ray.GetPoint(lo),out float startHeight);
                float sign = ray.GetPoint(lo).y-startHeight;
                for (int i=0;i<24;i++)
                {
                    float mid=(lo+hi)*.5f; Vector3 point=ray.GetPoint(mid);
                    ramp.TryHeight(point,out float y);
                    if((point.y-y)*sign>0)lo=mid;else hi=mid;
                }
                float distance=(lo+hi)*.5f;
                if(distance>=0 && distance<nearest && ramp.TryHeight(ray.GetPoint(distance),out _) &&
                    TryCell(ray.GetPoint(distance),out var cell))
                { nearest=distance; result=cell; }
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
