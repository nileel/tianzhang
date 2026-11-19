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
            CombatBattlefieldSurface.Cell support, float lift, int subdivisions = 12) =>
            CombatSurfaceMesh.Fit(source, transform, surface, support, lift, subdivisions);

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
