using System;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.Rendering;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>Grounded display of the active edge scale and its supplied support durability.</summary>
    public sealed class JindanScaleView : MonoBehaviour
    {
        [SerializeField] private GameObject supportPrefab;
        [SerializeField] private Material edgeMaterial;
        private CombatBattlefieldSurface surface;
        private GameObject support;
        private GameObject intact;
        private GameObject failed;
        private GameObject glow;
        private LineRenderer edges;

        public void ConfigureSurface(CombatBattlefieldSurface value) => surface = value;

        public void Present(CombatUnitPresentationHex[] path, int metric, int durability, int maximum)
        {
            if (path == null || path.Length < 2)
            {
                if (support != null) support.SetActive(false);
                if (edges != null) edges.gameObject.SetActive(false);
                return;
            }
            if (surface == null) throw new InvalidOperationException("jindan_scale_surface_missing");
            EnsureVisuals();
            var points = new Vector3[path.Length];
            for (int index = 0; index < path.Length; index++) points[index] = surface.Center(path[index]);
            Vector3 midpoint = (points[0] + points[1]) * .5f;
            if (!surface.TryHeight(midpoint, out float height))
                throw new InvalidOperationException("jindan_scale_support_outside_surface");
            midpoint.y = height;
            support.transform.position = midpoint;
            support.SetActive(true);
            intact.SetActive(durability > 0);
            failed.SetActive(durability <= 0);
            // The approved stele has intact/failed meshes; damage extinguishes its existing glow.
            glow.SetActive(durability > 0 && durability >= maximum);
            edges.gameObject.SetActive(durability > 0);
            if (durability <= 0) return;
            Color color = metric <= 1 ? new Color(.28f, .78f, .68f, 1f) : new Color(.95f, .72f, .22f, 1f);
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", color);
            edges.SetPropertyBlock(properties);
            surface.DrawPath(edges, points, .03f);
        }

        private void EnsureVisuals()
        {
            if (support != null) return;
            support = Instantiate(supportPrefab, transform);
            support.name = "JindanScaleSupport";
            intact = support.transform.Find("Intact").gameObject;
            failed = support.transform.Find("Failed").gameObject;
            glow = support.transform.Find("Intact/Glow").gameObject;
            var edgeObject = new GameObject("JindanScaleEdges");
            edgeObject.transform.SetParent(transform, false);
            edges = edgeObject.AddComponent<LineRenderer>();
            edges.sharedMaterial = edgeMaterial;
            edges.useWorldSpace = true;
            edges.widthMultiplier = .055f;
            edges.shadowCastingMode = ShadowCastingMode.Off;
            edges.receiveShadows = false;
        }
    }
}
