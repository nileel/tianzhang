using System;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.Rendering;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>The map's existing support marker and a grounded arrow for the one directed gate.</summary>
    public sealed class JindanSealView : MonoBehaviour
    {
        [SerializeField] private GameObject supportMarker;
        [SerializeField] private Material edgeMaterial;
        private CombatBattlefieldSurface surface;
        private LineRenderer arrow;

        public void ConfigureSurface(CombatBattlefieldSurface value) => surface = value;

        public void Present(CombatUnitPresentationHex[] path, int outer, int inner, bool destroyed)
        {
            bool active = path != null && path.Length == 2;
            supportMarker.transform.Find("Intact").gameObject.SetActive(!destroyed);
            supportMarker.transform.Find("Failed").gameObject.SetActive(destroyed);
            supportMarker.transform.Find("Intact/Glow").gameObject.SetActive(active && outer > 0);
            if (!active)
            {
                if (arrow != null) arrow.gameObject.SetActive(false);
                return;
            }
            if (surface == null) throw new InvalidOperationException("jindan_seal_surface_missing");
            if (arrow == null)
            {
                var obj = new GameObject("JindanSealDirection");
                obj.transform.SetParent(transform, false);
                arrow = obj.AddComponent<LineRenderer>();
                arrow.sharedMaterial = edgeMaterial; arrow.useWorldSpace = true;
                arrow.widthMultiplier = .07f; arrow.shadowCastingMode = ShadowCastingMode.Off;
                arrow.receiveShadows = false;
            }
            Vector3 from = surface.Center(path[0]), to = surface.Center(path[1]);
            Vector3 direction = to - from, side = Vector3.Cross(Vector3.up, direction) * .2f;
            var points = new[] { from, to, to - direction * .3f + side, to, to - direction * .3f - side };
            arrow.gameObject.SetActive(inner > 0);
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", outer > 0 ? new Color(.85f, .35f, .9f) : new Color(1f, .4f, .2f));
            arrow.SetPropertyBlock(properties);
            surface.DrawPath(arrow, points, .035f);
        }
    }
}
