using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TianZhang.Tests.PlayMode
{
    /// <summary>Acceptance-only actual-mesh contact fitting; no runtime movement/rule owner.</summary>
    internal sealed class GuanzhongRampBodyProbe
    {
        internal readonly GameObject Body;
        private readonly Vector3[] vertices;
        private readonly Vector3[] triangleProbes;
        private readonly int[] firstFoot;
        private readonly int[] secondFoot;
        private readonly bool pair;
        private readonly Collider[] terrain;
        private readonly Collider[] supports;
        internal float MaxFootGap, MaxBodyPenetration;
        internal int Poses, VertexChecks;

        internal GuanzhongRampBodyProbe(GameObject body, float scale, bool pair, Collider[] terrain, Collider[] supports)
        {
            Body = body;
            this.pair = pair;
            this.terrain = terrain;
            this.supports = supports;
            body.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            body.transform.localScale = Vector3.one;
            var all = new List<Vector3>();
            var samples = new List<Vector3>();
            foreach (MeshFilter filter in body.GetComponentsInChildren<MeshFilter>())
            {
                var local = filter.sharedMesh.vertices.Select(v => body.transform.InverseTransformPoint(filter.transform.TransformPoint(v))*scale).ToArray();
                all.AddRange(local);
                int[] indices = filter.sharedMesh.triangles;
                for (int i = 0; i < indices.Length; i += 3)
                {
                    Vector3 a = local[indices[i]], b = local[indices[i+1]], c = local[indices[i+2]];
                    if (Mathf.Min(a.y, Mathf.Min(b.y,c.y)) > .3f) continue;
                    samples.Add((a+b+c)/3);
                    samples.Add((a+b)/2);
                    samples.Add((b+c)/2);
                    samples.Add((c+a)/2);
                }
            }
            vertices = all.ToArray();
            triangleProbes = samples.ToArray();
            float low = vertices.Min(v => v.y) + .01f;
            firstFoot = Enumerable.Range(0,vertices.Length).Where(i => vertices[i].y <= low &&
                (pair ? vertices[i].z > 0 : vertices[i].x < 0)).ToArray();
            secondFoot = Enumerable.Range(0,vertices.Length).Where(i => vertices[i].y <= low &&
                (pair ? vertices[i].z < 0 : vertices[i].x >= 0)).ToArray();
            Assert.That(firstFoot.Length, Is.GreaterThan(0));
            Assert.That(secondFoot.Length, Is.GreaterThan(0));
            body.transform.localScale = Vector3.one*scale;
        }

        internal void Fit(Vector3 anchor, float yaw, bool verify)
        {
            float yawRad = yaw*Mathf.Deg2Rad;
            Vector3 center = anchor - (pair ? .5f : 0)*new Vector3(Mathf.Sin(yawRad),0,Mathf.Cos(yawRad));
            float z = center.z - 1 - Mathf.Sqrt(3)*.5f;
            float length = 5*Mathf.Sqrt(3)*.5f;
            float g = z > 0 && z < length ? .75f*Mathf.PI/(2*length)*Mathf.Sin(Mathf.PI*z/length) : 0;
            float pitch = Mathf.Atan(g*Mathf.Cos(yawRad));
            float roll = -Mathf.Atan2(g*Mathf.Sin(yawRad),Mathf.Sqrt(1+Mathf.Pow(g*Mathf.Cos(yawRad),2)));
            float initial = pair ? pitch : roll;
            float lo = initial-20*Mathf.Deg2Rad, hi = initial+20*Mathf.Deg2Rad;
            float Difference(float angle)
            {
                Quaternion rotation = Rotation(yawRad,pair ? angle : pitch,pair ? roll : angle);
                return Required(firstFoot,center,rotation)-Required(secondFoot,center,rotation);
            }
            float lowDifference = Difference(lo);
            Assert.That(lowDifference*Difference(hi), Is.LessThanOrEqualTo(0), "Actual sole contact must be bracketed.");
            for (int i = 0; i < 22; i++)
            {
                float middle = (lo+hi)/2;
                float difference = Difference(middle);
                if (lowDifference*difference <= 0) hi = middle;
                else { lo = middle; lowDifference = difference; }
            }
            if (pair) pitch = (lo+hi)/2; else roll = (lo+hi)/2;
            Quaternion finalRotation = Rotation(yawRad,pitch,roll);
            float first = Required(firstFoot,center,finalRotation), second = Required(secondFoot,center,finalRotation);
            float rootY = Mathf.Max(first,second);
            Assert.That(float.IsInfinity(rootY), Is.False, "No whole foot group may lose finite support.");
            center.y = rootY;
            Body.transform.SetPositionAndRotation(center,finalRotation);
            if (!verify) return;
            float footGap = Mathf.Abs(first-second);
            MaxFootGap = Mathf.Max(MaxFootGap,footGap);
            Assert.That(footGap, Is.LessThan(.002f), $"Unsupported foot at {anchor} yaw={yaw}");
            float penetration = 0;
            Vector3 worst=Vector3.zero, worstLocal=Vector3.zero;
            foreach (Vector3 v in vertices.Concat(triangleProbes))
            {
                Vector3 p = finalRotation*v+center;
                if (p.y > 1.22f) continue;
                float h = Height(p.x,p.z);
                if (!float.IsNegativeInfinity(h) && h-p.y>penetration)
                { penetration=h-p.y; worst=p; worstLocal=v; }
                VertexChecks++;
            }
            MaxBodyPenetration = Mathf.Max(MaxBodyPenetration,penetration);
            if (penetration>=.002f)
            {
                foreach (Collider collider in terrain)
                    if (collider.Raycast(new Ray(new Vector3(worst.x,4,worst.z),Vector3.down),out RaycastHit hit,8))
                        Debug.Log($"D32_INTERSECTION collider={collider.transform.parent.name}/{collider.name} hit={hit.point:F6} normal={hit.normal:F6}");
            }
            Assert.That(penetration, Is.LessThan(.002f), $"Body/terrain intersection at {anchor} yaw={yaw}: {penetration}; root={center:F6} pitch={pitch} roll={roll} worst={worst:F6} local={worstLocal:F6}");
            Poses++;
        }

        private float Required(int[] group, Vector3 center, Quaternion rotation)
        {
            float required = float.NegativeInfinity;
            foreach (int i in group)
            {
                Vector3 p = rotation*vertices[i];
                required = Mathf.Max(required,Height(center.x+p.x,center.z+p.z,true)-p.y);
            }
            return required;
        }

        private float Height(float x, float z, bool supportOnly=false)
        {
            var ray = new Ray(new Vector3(x,4,z),Vector3.down);
            float height = float.NegativeInfinity;
            foreach (Collider collider in supportOnly ? supports : terrain)
                if (collider.Raycast(ray,out RaycastHit hit,8)) height = Mathf.Max(height,hit.point.y);
            return height;
        }

        private static Quaternion Rotation(float yaw,float pitch,float roll)
        {
            Vector3 forward = new Vector3(Mathf.Sin(yaw)*Mathf.Cos(pitch),Mathf.Sin(pitch),Mathf.Cos(yaw)*Mathf.Cos(pitch));
            Vector3 right = new Vector3(Mathf.Cos(yaw),0,-Mathf.Sin(yaw));
            Vector3 up = Vector3.Cross(forward,right);
            return Quaternion.LookRotation(forward,-right*Mathf.Sin(roll)+up*Mathf.Cos(roll));
        }
    }
}
