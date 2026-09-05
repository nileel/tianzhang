using System;
using System.Collections.Generic;
using UnityEngine;

namespace TianZhang.ClothWideSleevePilot
{
    // Diagnostic geometry only; never registers colliders with physics or Cloth.
    public sealed class ClothWideSleeveCollisionProbe : IDisposable
    {
        private readonly Cloth cloth;
        private readonly SkinnedMeshRenderer renderer;
        private readonly Vector3[] rest;
        private readonly BoneWeight[] weights;
        private readonly Matrix4x4[] bindposes, skinMatrices;
        private readonly Transform[] bones;
        private readonly ClothSkinningCoefficient[] coefficients;
        private readonly List<Vector3> linePoints = new List<Vector3>();
        private readonly List<Color> lineColors = new List<Color>();
        private readonly Color[] colors = { Color.yellow, Color.cyan, Color.magenta };

        [Serializable]
        public sealed class Measurement
        {
            public float pinnedMaxErrorMeters;
            public bool coordinateProofValid;
            public int freeVerticesInsideProxy;
            public float maximumProxyDepthMeters;
            public float maximumMotionConstraintExcessMeters;
            public Vector3 clothMin, clothMax;
            public Vector3[] colliderCenters;
            public float[] colliderRadii;
        }

        public ClothWideSleeveCollisionProbe(Cloth cloth, SkinnedMeshRenderer renderer)
        {
            this.cloth = cloth;
            this.renderer = renderer;
            rest = renderer.sharedMesh.vertices;
            weights = renderer.sharedMesh.boneWeights;
            bindposes = renderer.sharedMesh.bindposes;
            bones = renderer.bones;
            skinMatrices = new Matrix4x4[bones.Length];
            coefficients = cloth.coefficients;
            if (rest.Length != weights.Length || rest.Length != coefficients.Length)
                throw new InvalidOperationException("Cloth/skin vertex correspondence cannot be verified.");
        }

        public void UpdateLines()
        {
            linePoints.Clear();
            lineColors.Clear();
            var pairs = cloth.sphereColliders;
            for (int p = 0; p < pairs.Length; p++)
            {
                Vector3 a = Center(pairs[p].first), b = Center(pairs[p].second);
                float ra = Radius(pairs[p].first), rb = Radius(pairs[p].second);
                for (int end = 0; end < 2; end++)
                    for (int axis = 0; axis < 3; axis++)
                    {
                        Vector3 u = axis == 0 ? Vector3.up : Vector3.right;
                        Vector3 v = axis == 2 ? Vector3.up : Vector3.forward;
                        for (int i = 0; i < 32; i++)
                        {
                            float angle = i * Mathf.PI / 16f;
                            float next = (i + 1) * Mathf.PI / 16f;
                            Vector3 center = end == 0 ? a : b;
                            float radius = end == 0 ? ra : rb;
                            AddLine(center + radius * (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)),
                                center + radius * (u * Mathf.Cos(next) + v * Mathf.Sin(next)), p);
                        }
                    }
                Vector3 d = b - a;
                Vector3 axisDirection = d.normalized;
                Vector3 radial = Vector3.Cross(axisDirection, Vector3.up);
                if (radial.sqrMagnitude < 0.01f) radial = Vector3.Cross(axisDirection, Vector3.right);
                radial.Normalize();
                Vector3 other = Vector3.Cross(axisDirection, radial);
                float k = Mathf.Clamp((ra - rb) / Mathf.Max(0.0001f, d.magnitude), -1f, 1f);
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI * 0.25f;
                    Vector3 n = axisDirection * k + Mathf.Sqrt(1f - k * k) *
                        (radial * Mathf.Cos(angle) + other * Mathf.Sin(angle));
                    AddLine(a + ra * n, b + rb * n, p);
                }
            }
        }

        private void AddLine(Vector3 a, Vector3 b, int pair)
        {
            linePoints.Add(a);
            linePoints.Add(b);
            lineColors.Add(colors[pair % colors.Length]);
        }

        public void DrawOverlay(Camera camera)
        {
            Rect r = camera.pixelRect;
            GUI.BeginGroup(new Rect(r.x, Screen.height - r.yMax, r.width, r.height));
            Matrix4x4 matrix = GUI.matrix;
            Color originalColor = GUI.color;
            for (int i = 0; i < lineColors.Count; i++)
            {
                Vector3 a = camera.WorldToViewportPoint(linePoints[i * 2]);
                Vector3 b = camera.WorldToViewportPoint(linePoints[i * 2 + 1]);
                if (a.z <= 0f || b.z <= 0f) continue;
                Vector2 p = new Vector2(a.x * r.width, (1f - a.y) * r.height);
                Vector2 q = new Vector2(b.x * r.width, (1f - b.y) * r.height);
                Vector2 delta = q - p;
                GUI.color = lineColors[i];
                GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, p);
                GUI.DrawTexture(new Rect(p.x, p.y, delta.magnitude, 1.2f), Texture2D.whiteTexture);
                GUI.matrix = matrix;
            }
            GUI.color = originalColor;
            GUI.EndGroup();
        }

        public Measurement Measure()
        {
            Vector3[] vertices = cloth.vertices;
            if (vertices.Length != rest.Length) throw new InvalidOperationException("Cloth vertex count changed.");
            for (int i = 0; i < bones.Length; i++) skinMatrices[i] = bones[i].localToWorldMatrix * bindposes[i];
            var pairs = cloth.sphereColliders;
            var m = new Measurement
            {
                clothMin = Vector3.positiveInfinity, clothMax = Vector3.negativeInfinity,
                colliderCenters = new Vector3[pairs.Length * 2], colliderRadii = new float[pairs.Length * 2]
            };
            for (int i = 0; i < pairs.Length; i++)
            {
                m.colliderCenters[i * 2] = Center(pairs[i].first);
                m.colliderCenters[i * 2 + 1] = Center(pairs[i].second);
                m.colliderRadii[i * 2] = Radius(pairs[i].first);
                m.colliderRadii[i * 2 + 1] = Radius(pairs[i].second);
            }
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = renderer.transform.TransformPoint(vertices[i]);
                BoneWeight w = weights[i];
                Vector3 skinned = skinMatrices[w.boneIndex0].MultiplyPoint3x4(rest[i]) * w.weight0 +
                    skinMatrices[w.boneIndex1].MultiplyPoint3x4(rest[i]) * w.weight1 +
                    skinMatrices[w.boneIndex2].MultiplyPoint3x4(rest[i]) * w.weight2 +
                    skinMatrices[w.boneIndex3].MultiplyPoint3x4(rest[i]) * w.weight3;
                float skinDistance = Vector3.Distance(world, skinned);
                m.clothMin = Vector3.Min(m.clothMin, world);
                m.clothMax = Vector3.Max(m.clothMax, world);
                m.maximumMotionConstraintExcessMeters = Mathf.Max(m.maximumMotionConstraintExcessMeters,
                    skinDistance - coefficients[i].maxDistance);
                if (coefficients[i].maxDistance <= 0.0001f)
                {
                    m.pinnedMaxErrorMeters = Mathf.Max(m.pinnedMaxErrorMeters, skinDistance);
                    continue;
                }
                float depth = 0f;
                for (int p = 0; p < pairs.Length; p++)
                    depth = Mathf.Max(depth, -ProxySignedDistance(world, m.colliderCenters[p * 2],
                        m.colliderCenters[p * 2 + 1], m.colliderRadii[p * 2], m.colliderRadii[p * 2 + 1]));
                if (depth > 0.005f) m.freeVerticesInsideProxy++;
                m.maximumProxyDepthMeters = Mathf.Max(m.maximumProxyDepthMeters, depth);
            }
            // Check the coordinate mapping against pinned vertices; this does NOT prove every free vertex index.
            // On failure the reported penetration/constraint numbers must not be used as physical evidence.
            m.coordinateProofValid = m.pinnedMaxErrorMeters < 0.005f;
            return m;
        }

        public static float ProxySignedDistance(Vector3 point, Vector3 a, Vector3 b, float ra, float rb)
        {
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 20; i++)
            {
                float t1 = (2f * lo + hi) / 3f, t2 = (lo + 2f * hi) / 3f;
                if (DistanceAt(t1) < DistanceAt(t2)) hi = t2; else lo = t1;
            }
            return Mathf.Min(DistanceAt(0f), DistanceAt(1f), DistanceAt((lo + hi) * 0.5f));
            float DistanceAt(float t) => Vector3.Distance(point, Vector3.Lerp(a, b, t)) - Mathf.Lerp(ra, rb, t);
        }

        private static Vector3 Center(SphereCollider c) => c.transform.TransformPoint(c.center);
        private static float Radius(SphereCollider c)
        {
            Vector3 s = c.transform.lossyScale;
            return c.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        }
        public void Dispose()
        {
            linePoints.Clear();
            lineColors.Clear();
        }
    }
}
