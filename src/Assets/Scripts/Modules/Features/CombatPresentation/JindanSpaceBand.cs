using System;
using UnityEngine;
using TianZhang.Gameplay.Contracts;

namespace TianZhang.Features.CombatPresentation
{
    // One delivered wave/membrane on one directed edge. The strip follows the actual support surface.
    internal sealed class JindanSpaceBand : MonoBehaviour
    {
        private Mesh fittedStrip;
        private Renderer[] surfaces;
        private MaterialPropertyBlock properties;
        private float fadeStart;

        public static JindanSpaceBand Place(GameObject prefab, Transform parent, CombatBattlefieldSurface board,
            CombatUnitPresentationHex from, CombatUnitPresentationHex to)
        {
            var start = board.Center(from);
            var direction = board.Center(to) - start;
            var forward = Vector3.Cross(direction, Vector3.up);
            var rotation = Quaternion.LookRotation(forward, Vector3.Cross(forward, direction));
            var root = Instantiate(prefab, start, rotation, parent);
            root.name = prefab.name;
            root.transform.localScale = new Vector3(direction.magnitude, 1f, 1f);
            var band = root.AddComponent<JindanSpaceBand>();
            var strip = root.GetComponent<MeshFilter>();
            band.fittedStrip = Instantiate(strip.sharedMesh);
            var vertices = band.fittedStrip.vertices;
            for (int i = 0; i < vertices.Length; ++i)
            {
                Vector3 world = root.transform.TransformPoint(vertices[i]);
                if (!board.TryHeight(world, out float height))
                    throw new InvalidOperationException("space_band_outside_board");
                world.y = height + .035f;
                vertices[i] = root.transform.InverseTransformPoint(world);
            }
            band.fittedStrip.vertices = vertices;
            band.fittedStrip.RecalculateBounds();
            strip.sharedMesh = band.fittedStrip;
            band.surfaces = root.GetComponentsInChildren<Renderer>();
            band.properties = new MaterialPropertyBlock();
            band.Pulse(0);
            band.enabled = false;
            return band;
        }

        public void Pulse(int kind)
        {
            properties.SetFloat("_EventTime", Time.time);
            properties.SetFloat("_EventKind", kind);
            properties.SetFloat("_Opacity", 1f);
            Apply();
        }

        public void Dissolve()
        {
            Pulse(2);
            fadeStart = Time.time;
            enabled = true;
        }

        private void Update()
        {
            float opacity = Mathf.Clamp01(1f - (Time.time - fadeStart) / .5f);
            properties.SetFloat("_Opacity", opacity);
            Apply();
            if (opacity == 0f) Destroy(gameObject);
        }

        private void Apply()
        {
            foreach (var surface in surfaces) surface.SetPropertyBlock(properties);
        }

        private void OnDestroy()
        {
            if (fittedStrip) Destroy(fittedStrip);
        }
    }
}
