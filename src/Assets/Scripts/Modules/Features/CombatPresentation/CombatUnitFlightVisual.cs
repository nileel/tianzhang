using System;
using System.Collections;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.Rendering;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>Projects committed altitude with the existing static human mesh and world-space markers.</summary>
    internal sealed class CombatUnitFlightVisual
    {
        // Same visual metre interval as the D1 terrace levels; not an input to combat rules.
        public const float MetresPerBand = .75f;
        private readonly Transform root;
        private readonly LineRenderer ring, tether;

        public CombatUnitFlightVisual(Transform root, Material material)
        {
            if (material == null) throw new ArgumentException("flight_line_material_missing");
            this.root = root;
            ring = Line("FlightGroundProjection", true, 6);
            tether = Line("FlightAltitudeLine", false, 2);
            LineRenderer Line(string name, bool loop, int count)
            {
                var go = new GameObject(name); go.transform.SetParent(root, false);
                var line = go.AddComponent<LineRenderer>(); line.sharedMaterial = material;
                line.loop = loop; line.positionCount = count; line.useWorldSpace = true; line.widthMultiplier = .025f;
                line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
                var properties = new MaterialPropertyBlock(); properties.SetColor("_BaseColor", new Color(.3f, .8f, 1f));
                line.SetPropertyBlock(properties); return line;
            }
        }

        public static Vector3 Position(CombatUnitPresentationPose pose, CombatBattlefieldSurface surface) =>
            (surface != null ? surface.Center(pose.Position) : Static3DCombatUnitPresentationProvider.HexToWorld(pose.Position)) +
            Vector3.up * (pose.AltitudeBand * MetresPerBand);

        public void Refresh(CombatUnitPresentationPose pose, CombatBattlefieldSurface surface)
        {
            bool visible = pose.AltitudeBand > 0 && root.gameObject.activeInHierarchy;
            ring.enabled = tether.enabled = visible;
            if (!visible) return;
            Vector3 ground = root.position;
            if (surface != null && surface.TryHeight(ground, out float y)) ground.y = y + .025f;
            else ground = Static3DCombatUnitPresentationProvider.HexToWorld(pose.Position) + Vector3.up * .025f;
            for (int i = 0; i < 6; i++)
            {
                float angle = (30 + i * 60) * Mathf.Deg2Rad;
                Vector3 point = ground + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * .35f;
                if (surface != null && surface.TryHeight(point, out float height)) point.y = height + .025f;
                ring.SetPosition(i, point);
            }
            tether.SetPosition(0, ground); tether.SetPosition(1, root.position);
        }

        public static IEnumerator Transition(Transform root, CombatUnitPresentationPose target,
            CombatBattlefieldSurface surface, CombatUnitSurfacePose support, float yaw, float speed, Action complete,
            GameObject gravityPressurePrefab = null, GameObject landingDustPrefab = null)
        {
            Vector3 end = Position(target, surface); Quaternion rotation = Quaternion.Euler(0, yaw, 0);
            if (target.AltitudeBand == 0 && support != null) support.Evaluate(end, yaw, out end, out rotation);
            Vector3 start = root.position; Quaternion startRotation = root.rotation;
            float duration = Mathf.Max(.1f, Vector3.Distance(start, end) / speed);
            var pressure = gravityPressurePrefab != null ? UnityEngine.Object.Instantiate(gravityPressurePrefab, root) : null;
            for (float time = 0; time < duration;)
            {
                yield return null;
                time = Mathf.Min(duration, time + Time.deltaTime);
                root.position = Vector3.Lerp(start, end, time / duration);
                root.rotation = Quaternion.Slerp(startRotation, rotation, time / duration);
            }
            if (pressure != null)
            {
                foreach (var particles in pressure.GetComponentsInChildren<ParticleSystem>())
                    particles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                UnityEngine.Object.Destroy(pressure, .2f);
                if (target.AltitudeBand == 0)
                {
                    var dust = UnityEngine.Object.Instantiate(landingDustPrefab, end, Quaternion.identity, root);
                    UnityEngine.Object.Destroy(dust, 1f);
                }
            }
            complete();
        }
    }
}
