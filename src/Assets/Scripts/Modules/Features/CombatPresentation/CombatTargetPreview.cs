using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>A local surface pass for one static unit; never changes its source materials or colliders.</summary>
    internal sealed class CombatTargetPreview
    {
        private static readonly int Sweep = Shader.PropertyToID("_Sweep");
        private readonly List<MeshRenderer> surfaces = new List<MeshRenderer>();
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private float startedAt;
        public bool IsVisible { get; private set; }

        public CombatTargetPreview(Renderer[] bodies, Material material)
        {
            if (material == null) throw new InvalidOperationException("combat_target_preview_material_missing");
            foreach (Renderer body in bodies)
            {
                MeshFilter source = body.GetComponent<MeshFilter>();
                if (source == null || source.sharedMesh == null)
                    throw new InvalidOperationException("combat_target_preview_requires_static_mesh");
                var surface = new GameObject("CombatTargetPreviewSurface");
                surface.layer = body.gameObject.layer;
                surface.transform.SetParent(body.transform, false);
                surface.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                var renderer = surface.AddComponent<MeshRenderer>();
                var materials = new Material[source.sharedMesh.subMeshCount];
                for (int index = 0; index < materials.Length; index++) materials[index] = material;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.enabled = false;
                surfaces.Add(renderer);
            }
        }

        public void SetVisible(bool visible)
        {
            if (IsVisible == visible) return;
            IsVisible = visible;
            if (visible) startedAt = Time.unscaledTime;
            foreach (MeshRenderer surface in surfaces)
                if (surface != null) surface.enabled = visible;
        }

        public void Refresh(Bounds bounds)
        {
            if (!IsVisible) return;
            // A narrow band rises once per 1.8 seconds, leaving the model legible between passes.
            float phase = Mathf.Repeat((Time.unscaledTime - startedAt) / 1.8f, 1f);
            float halfWidth = bounds.size.y * .09f;
            float center = Mathf.Lerp(bounds.min.y - halfWidth, bounds.max.y + halfWidth, phase);
            properties.SetVector(Sweep, new Vector4(center, halfWidth, 0f, 0f));
            foreach (MeshRenderer surface in surfaces)
                if (surface != null) surface.SetPropertyBlock(properties);
        }
    }
}
