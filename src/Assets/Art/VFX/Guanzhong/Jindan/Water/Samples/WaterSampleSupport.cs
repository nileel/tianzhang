using System.Collections.Generic;
using System.Linq;
using TianZhang.Features.CombatPresentation;
using UnityEngine;

namespace TianZhang.Art.Jindan.Water.Samples
{
    /// <summary>Sample-local adaptation of the original water sheet, using D3.3's tread-strip clipping.</summary>
    internal static class WaterSampleSupport
    {
        public static Mesh Fit(Mesh source, Transform transform, CombatBattlefieldSurface surface,
            CombatBattlefieldSurface.Cell support, float lift)
        {
            var mesh = Object.Instantiate(source);
            var world = source.vertices.Select(transform.TransformPoint).ToArray();
            if (support.kind == 0)
            {
                mesh.vertices = world.Select(p => transform.InverseTransformPoint(new Vector3(p.x, surface.Height(support, p.z) + lift, p.z))).ToArray();
            }
            else if (support.kind == 1)
            {
                // Match the existing D3.2 ramp's 12 subdivisions per original hex radius.
                const int subdivisions = 12;
                var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
                var indices = source.triangles;
                for (int t = 0; t < indices.Length; t += 3)
                {
                    Vector3 a = world[indices[t]], b = world[indices[t + 1]], c = world[indices[t + 2]];
                    Vector3 Point(int i, int j)
                    {
                        Vector3 p = a + (b - a) * (i / (float)subdivisions) + (c - a) * (j / (float)subdivisions);
                        p.y = surface.Height(support, p.z) + lift; return p;
                    }
                    void Triangle(Vector3 p, Vector3 q, Vector3 r)
                    {
                        foreach (var v in new[] { p, q, r })
                        { triangles.Add(vertices.Count); vertices.Add(transform.InverseTransformPoint(v)); uv.Add(new Vector2(v.x, v.z)); }
                    }
                    for (int i = 0; i < subdivisions; i++) for (int j = 0; j < subdivisions - i; j++)
                    {
                        Triangle(Point(i, j), Point(i + 1, j), Point(i, j + 1));
                        if (i + j < subdivisions - 1) Triangle(Point(i + 1, j), Point(i + 1, j + 1), Point(i, j + 1));
                    }
                }
                mesh.Clear(); mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            }
            else
            {
                // Same strip construction as d3.3-stone-stairs/build-stairs.py. Duplicate the two heights at every riser.
                var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
                var indices = source.triangles;
                for (int band = 0; band <= support.riserZ.Length; band++)
                {
                    float lower = band == 0 ? float.NegativeInfinity : support.riserZ[band - 1];
                    float upper = band == support.riserZ.Length ? float.PositiveInfinity : support.riserZ[band];
                    float height = (band == 0 ? support.lowerY : support.riserY[band - 1]) + lift;
                    for (int t = 0; t < indices.Length; t += 3)
                    {
                        var polygon = new List<Vector3> { world[indices[t]], world[indices[t + 1]], world[indices[t + 2]] };
                        if (band > 0) polygon = Clip(polygon, lower, true);
                        if (band < support.riserZ.Length) polygon = Clip(polygon, upper, false);
                        if (polygon.Count < 3) continue;
                        int start = vertices.Count;
                        foreach (var p in polygon)
                        {
                            vertices.Add(transform.InverseTransformPoint(new Vector3(p.x, height, p.z)));
                            uv.Add(new Vector2(p.x, p.z));
                        }
                        for (int p = 1; p < polygon.Count - 1; p++)
                        { triangles.Add(start); triangles.Add(start + p); triangles.Add(start + p + 1); }
                    }
                }
                mesh.Clear(); mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            }
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private static List<Vector3> Clip(List<Vector3> polygon, float z, bool above)
        {
            var result = new List<Vector3>();
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector3 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                bool insideA = above ? a.z >= z : a.z <= z, insideB = above ? b.z >= z : b.z <= z;
                if (insideA) result.Add(a);
                if (insideA != insideB) result.Add(Vector3.LerpUnclamped(a, b, (z - a.z) / (b.z - a.z)));
            }
            return result;
        }
        public static float InteriorError(Mesh mesh, Transform transform, CombatBattlefieldSurface surface,
            CombatBattlefieldSurface.Cell support, float lift, out int count)
        {
            float error = 0; count = 0;
            var vertices = mesh.vertices.Select(transform.TransformPoint).ToArray(); var indices = mesh.triangles;
            for (int t = 0; t < indices.Length; t += 3)
            {
                Vector3 a = vertices[indices[t]], b = vertices[indices[t + 1]], c = vertices[indices[t + 2]];
                if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-12f) continue;
                // Strictly interior samples avoid the discontinuous shared stair-edge convention.
                foreach (var w in new[] { new Vector3(1f/3,1f/3,1f/3), new Vector3(.8f,.1f,.1f), new Vector3(.1f,.8f,.1f), new Vector3(.1f,.1f,.8f) })
                {
                    Vector3 p = a * w.x + b * w.y + c * w.z;
                    error = Mathf.Max(error, Mathf.Abs(p.y - surface.Height(support, p.z) - lift)); count++;
                }
            }
            return error;
        }
    }
}
