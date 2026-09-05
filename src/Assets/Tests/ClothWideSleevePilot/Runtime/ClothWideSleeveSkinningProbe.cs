using System;
using UnityEngine;

namespace TianZhang.ClothWideSleevePilot
{
    // Read-only snapshots: BakeMesh is NOT assumed to be independent skinning while Cloth is enabled.
    public sealed class ClothWideSleeveSkinningProbe : IDisposable
    {
        private readonly SkinnedMeshRenderer renderer;
        private readonly Cloth cloth;
        private readonly Mesh baked = new Mesh();
        private readonly Vector3[] rest;
        private readonly BoneWeight[] weights;
        private readonly Matrix4x4[] bindposes, matrices;
        private readonly Transform[] bones;
        private readonly ClothSkinningCoefficient[] coefficients;

        [Serializable]
        public sealed class Snapshot
        {
            public string rendererName, rendererQuality, globalSkinWeights, qualityName;
            public bool clothEnabled, equalVertexCounts;
            public int sourceCount, bakedCount, clothCount;
            public float bakeVsCpuMaxMeters, bakeVsCpuPinnedMaxMeters, clothCandidateVsBakeMaxMeters;
            public Vector3 rendererPosition, rendererScale, rendererBoundsMin, rendererBoundsMax;
            public Quaternion rendererRotation;
            public int[] pinnedIndices;
            public Vector3[] cpuWorld, bakedWorld, clothRaw, clothCandidateWorld;
            public Vector3[] bodyProxyCenters;
            public float[] bodyProxyRadii;
        }

        public ClothWideSleeveSkinningProbe(SkinnedMeshRenderer renderer, Cloth cloth)
        {
            this.renderer = renderer;
            this.cloth = cloth;
            rest = renderer.sharedMesh.vertices;
            weights = renderer.sharedMesh.boneWeights;
            bindposes = renderer.sharedMesh.bindposes;
            bones = renderer.bones;
            matrices = new Matrix4x4[bones.Length];
            coefficients = cloth.coefficients;
            if (rest.Length != weights.Length || rest.Length != coefficients.Length)
                throw new InvalidOperationException("Snapshot input counts disagree.");
        }

        public Snapshot Measure(bool includeVertices)
        {
            renderer.BakeMesh(baked, false);
            Vector3[] bakeVertices = baked.vertices;
            Vector3[] raw = cloth.enabled ? cloth.vertices : Array.Empty<Vector3>();
            var snapshot = new Snapshot
            {
                rendererName = renderer.name, rendererQuality = renderer.quality.ToString(),
                globalSkinWeights = QualitySettings.skinWeights.ToString(),
                qualityName = QualitySettings.names[QualitySettings.GetQualityLevel()], clothEnabled = cloth.enabled,
                sourceCount = rest.Length, bakedCount = bakeVertices.Length, clothCount = raw.Length,
                equalVertexCounts = rest.Length == bakeVertices.Length && (!cloth.enabled || rest.Length == raw.Length),
                rendererPosition = renderer.transform.position, rendererRotation = renderer.transform.rotation,
                rendererScale = renderer.transform.lossyScale,
                rendererBoundsMin = renderer.bounds.min, rendererBoundsMax = renderer.bounds.max
            };
            if (bakeVertices.Length != rest.Length) return snapshot;
            Vector3[] cpu = new Vector3[rest.Length], worldBake = new Vector3[rest.Length];
            Vector3[] candidate = new Vector3[raw.Length];
            var pins = new System.Collections.Generic.List<int>();
            for (int i = 0; i < bones.Length; i++) matrices[i] = bones[i].localToWorldMatrix * bindposes[i];
            for (int i = 0; i < rest.Length; i++)
            {
                BoneWeight w = weights[i];
                cpu[i] = matrices[w.boneIndex0].MultiplyPoint3x4(rest[i]) * w.weight0 +
                    matrices[w.boneIndex1].MultiplyPoint3x4(rest[i]) * w.weight1 +
                    matrices[w.boneIndex2].MultiplyPoint3x4(rest[i]) * w.weight2 +
                    matrices[w.boneIndex3].MultiplyPoint3x4(rest[i]) * w.weight3;
                worldBake[i] = renderer.transform.TransformPoint(bakeVertices[i]);
                float error = Vector3.Distance(cpu[i], worldBake[i]);
                snapshot.bakeVsCpuMaxMeters = Mathf.Max(snapshot.bakeVsCpuMaxMeters, error);
                if (coefficients[i].maxDistance <= 0.0001f)
                {
                    pins.Add(i);
                    snapshot.bakeVsCpuPinnedMaxMeters = Mathf.Max(snapshot.bakeVsCpuPinnedMaxMeters, error);
                }
                if (raw.Length != rest.Length) continue;
                // This is the old probe's candidate conversion, NOT a claimed coordinate contract.
                candidate[i] = renderer.transform.TransformPoint(raw[i]);
                snapshot.clothCandidateVsBakeMaxMeters = Mathf.Max(snapshot.clothCandidateVsBakeMaxMeters,
                    Vector3.Distance(candidate[i], worldBake[i]));
            }
            if (includeVertices)
            {
                snapshot.pinnedIndices = pins.ToArray();
                snapshot.cpuWorld = cpu;
                snapshot.bakedWorld = worldBake;
                snapshot.clothRaw = raw;
                snapshot.clothCandidateWorld = candidate;
                var pairs = cloth.sphereColliders;
                snapshot.bodyProxyCenters = new Vector3[pairs.Length * 2];
                snapshot.bodyProxyRadii = new float[pairs.Length * 2];
                for (int p = 0; p < pairs.Length; p++)
                {
                    CaptureSphere(pairs[p].first, p * 2);
                    CaptureSphere(pairs[p].second, p * 2 + 1);
                }
                void CaptureSphere(SphereCollider sphere, int index)
                {
                    snapshot.bodyProxyCenters[index] = sphere.transform.TransformPoint(sphere.center);
                    Vector3 scale = sphere.transform.lossyScale;
                    snapshot.bodyProxyRadii[index] = sphere.radius *
                        Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                }
            }
            return snapshot;
        }

        public void Dispose() => UnityEngine.Object.Destroy(baked);
    }
}
