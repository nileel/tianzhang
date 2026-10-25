using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TianZhang.Tests.PlayMode
{
    /// <summary>Acceptance-only upright piece: actual mesh/triangle-edge contact at real risers.</summary>
    internal sealed class GuanzhongStairBodyProbe
    {
        internal readonly GameObject Body;
        private readonly Vector3[] vertices;
        private readonly Vector3[] lowTriangleCenters;
        private readonly (int a,int b)[] edges;
        private readonly Collider[] support,terrain;
        private readonly float[] risers;
        private readonly float soleLimit;
        internal int Poses,PointChecks,StaticPoses;
        internal float MaxStaticFootGap,MaxMovingFootGap,MaxPenetration,MaxLift;

        internal GuanzhongStairBodyProbe(GameObject body, Collider[] support, Collider[] terrain, float[] risers)
        {
            Body=body;
            this.support=support;
            this.terrain=terrain;
            this.risers=risers;
            body.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            body.transform.localScale=Vector3.one;
            var points=new List<Vector3>();
            var centers=new List<Vector3>();
            var edgeSet=new HashSet<(int,int)>();
            foreach (MeshFilter filter in body.GetComponentsInChildren<MeshFilter>())
            {
                int offset=points.Count;
                Vector3[] v=filter.sharedMesh.vertices.Select(p => body.transform.InverseTransformPoint(filter.transform.TransformPoint(p))*1.4f).ToArray();
                points.AddRange(v);
                int[] indices=filter.sharedMesh.triangles;
                for (int i=0;i<indices.Length;i+=3)
                {
                    int a=indices[i],b=indices[i+1],c=indices[i+2];
                    if (Mathf.Min(v[a].y,Mathf.Min(v[b].y,v[c].y))>.8f) continue;
                    centers.Add((v[a]+v[b]+v[c])/3);
                    foreach ((int x,int y) in new[] {(a,b),(b,c),(c,a)})
                        edgeSet.Add((offset+Math.Min(x,y),offset+Math.Max(x,y)));
                }
            }
            vertices=points.ToArray();
            lowTriangleCenters=centers.ToArray();
            edges=edgeSet.ToArray();
            soleLimit=vertices.Min(v => v.y)+.01f;
            body.transform.localScale=Vector3.one*1.4f;
        }

        internal float Fit(Vector3 center, float yaw, bool stationary)
        {
            Quaternion rotation=Quaternion.Euler(0,yaw,0);
            Vector3[] rotated=vertices.Select(v => rotation*v).ToArray();
            float left=float.NegativeInfinity,right=float.NegativeInfinity,required=float.NegativeInfinity;
            void Sample(Vector3 local, Vector3 p, bool boundary)
            {
                Vector3 world=p+new Vector3(center.x,0,center.z);
                // At the discontinuity use the closed high-side limit, only 10 micrometres away.
                if (boundary) world.z+=.00001f;
                float h=Height(world,support);
                if (!float.IsNegativeInfinity(h))
                {
                    required=Mathf.Max(required,h-p.y);
                    if (local.y<=soleLimit)
                    {
                        if (local.x<0) left=Mathf.Max(left,h-p.y);
                        else right=Mathf.Max(right,h-p.y);
                    }
                }
            }
            for (int i=0;i<vertices.Length;i++) Sample(vertices[i],rotated[i],false);
            foreach ((int a,int b) in edges)
            {
                float za=rotated[a].z+center.z,zb=rotated[b].z+center.z;
                foreach (float z in risers)
                {
                    if (z<=Mathf.Min(za,zb) || z>=Mathf.Max(za,zb)) continue;
                    float t=(z-za)/(zb-za);
                    Sample(Vector3.Lerp(vertices[a],vertices[b],t),Vector3.Lerp(rotated[a],rotated[b],t),true);
                }
            }
            Assert.That(float.IsInfinity(left) || float.IsInfinity(right),Is.False,"Both actual feet must have finite support candidates.");
            float rootY=Mathf.Max(left,right);
            Assert.That(required-rootY,Is.LessThan(.002f),"A non-foot body part must not drive a compensating whole-body lift.");
            center.y=rootY;
            Body.transform.SetPositionAndRotation(center,rotation);
            float gap=Mathf.Abs(left-right);
            if (stationary)
            {
                Assert.That(gap,Is.LessThan(.002f),$"Three-platform foot contact: {center} yaw={yaw}");
                MaxStaticFootGap=Mathf.Max(MaxStaticFootGap,gap);
                StaticPoses++;
            }
            else MaxMovingFootGap=Mathf.Max(MaxMovingFootGap,gap);
            Verify(center,rotation);
            Poses++;
            return rootY;
        }

        internal void Verify(Vector3 position, Quaternion rotation)
        {
            Body.transform.SetPositionAndRotation(position,rotation);
            float penetration=0;
            Vector3 worst=Vector3.zero;
            void Sample(Vector3 p, bool boundary=false)
            {
                Vector3 world=rotation*p+position;
                if (boundary) world.z+=.00001f;
                float h=Height(world,terrain);
                float depth=h-world.y;
                if (depth>penetration) { penetration=depth; worst=world; }
                PointChecks++;
            }
            foreach (Vector3 p in vertices) Sample(p);
            foreach (Vector3 p in lowTriangleCenters) Sample(p);
            foreach ((int a,int b) in edges)
            {
                float za=(rotation*vertices[a]).z+position.z,zb=(rotation*vertices[b]).z+position.z;
                foreach (float z in risers)
                {
                    if (z<=Mathf.Min(za,zb) || z>=Mathf.Max(za,zb)) continue;
                    Sample(Vector3.Lerp(vertices[a],vertices[b],(z-za)/(zb-za)),true);
                }
            }
            MaxPenetration=Mathf.Max(MaxPenetration,penetration);
            Assert.That(penetration,Is.LessThan(.002f),$"Actual body/side wall intrusion {penetration} at {worst}; root={position}");
        }

        internal static float Height(Vector3 point, Collider[] colliders)
        {
            float height=float.NegativeInfinity;
            foreach (Collider collider in colliders)
            {
                Bounds b=collider.bounds;
                if (point.x<b.min.x-.00001f || point.x>b.max.x+.00001f || point.z<b.min.z-.00001f || point.z>b.max.z+.00001f) continue;
                if (collider.Raycast(new Ray(new Vector3(point.x,8,point.z),Vector3.down),out RaycastHit hit,16))
                    height=Mathf.Max(height,hit.point.y);
            }
            return height;
        }
    }
}
