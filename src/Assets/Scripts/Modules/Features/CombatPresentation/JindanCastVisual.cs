using UnityEngine;
using UnityEngine.Rendering;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>One committed cast, from the visible caster to the rule's landing point.</summary>
    public sealed class JindanCastVisual : MonoBehaviour
    {
        private const float GatherTime = .48f;
        private const float ImpactTime = .86f;
        public const float Duration = 1.48f;
        private const int TrailSamples = 24;
        private enum Shape { Charge, Trail, Column, Ground, Lamp }
        private enum Element { Fire, Earth, Water, Space, Sun, Moon }
        private static Material lightMaterial;
        private MaterialPropertyBlock paint;
        private MeshRenderer seed, column, impactPool;
        private LineRenderer trail;
        private ParticleSystem sparks;
        private Color tint;
        private Element element;
        private Vector3 hand, destination;
        private float started;
        private bool burst;
        public bool HasLanded => Time.time - started >= ImpactTime;

        internal static JindanCastVisual Play(string effectId, Bounds caster, Vector3 target, GameObject particles)
        {
            var cast = new GameObject("JindanCast_" + effectId).AddComponent<JindanCastVisual>();
            cast.paint = new MaterialPropertyBlock();
            (cast.tint, cast.element) = effectId switch
            {
                "EARTH_MANIFEST" => (new Color(.79f, .48f, .17f), Element.Earth),
                "EARTH_GRAVITY" => (new Color(.97f, .39f, .13f), Element.Earth),
                "WATER_TIDE" => (new Color(.08f, .71f, .98f), Element.Water),
                "SPACE_SCALE" => (new Color(.66f, .33f, .93f), Element.Space),
                "SPACE_SEAL" => (new Color(.27f, .37f, .97f), Element.Space),
                "YANG_RADIANCE" => (new Color(1f, .74f, .12f), Element.Sun),
                "YIN_CONCEAL" => (new Color(.49f, .26f, .86f), Element.Moon),
                _ => (new Color(1f, .20f, .018f), Element.Fire),
            };
            var camera = Camera.main.transform;
            var aim = Vector3.ProjectOnPlane(target - caster.center, Vector3.up).normalized;
            cast.hand = caster.center + aim * .25f + Vector3.up * (caster.size.y * .17f) - camera.forward * .11f;
            cast.destination = target;
            cast.started = Time.time;
            cast.seed = Plate("GatheredLight", cast.transform, camera.rotation);
            cast.impactPool = Plate("LandingFlash", cast.transform, Quaternion.Euler(90f, 0, 0));
            cast.column = Plate("RisingPlume", cast.transform,
                Quaternion.LookRotation(Vector3.ProjectOnPlane(camera.forward, Vector3.up), Vector3.up));
            cast.impactPool.transform.position = target;
            cast.column.enabled = cast.impactPool.enabled = false;
            var ribbon = new GameObject("FlowingTrail");
            ribbon.transform.SetParent(cast.transform, false);
            cast.trail = ribbon.AddComponent<LineRenderer>();
            cast.trail.alignment = LineAlignment.View;
            cast.trail.textureMode = LineTextureMode.Stretch;
            cast.trail.positionCount = TrailSamples;
            cast.trail.useWorldSpace = true;
            Prepare(cast.trail);
            cast.trail.enabled = false;

            var plume = Instantiate(particles, target, Quaternion.identity, cast.transform);
            cast.sparks = plume.GetComponentInChildren<ParticleSystem>();
            var main = cast.sparks.main;
            main.startColor = Color.Lerp(cast.tint, Color.white, .30f);
            cast.sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            cast.Gather(0);
            return cast;
        }

        // One local halo accompanies the existing public flame mesh throughout maintenance.
        internal static void LightFireLamp(Transform glow)
        {
            var flame = glow.Find("CenterSpiritFlame");
            flame.localScale *= 1.48f;
            var halo = Plate("LampGlow", glow, Camera.main.transform.rotation);
            halo.transform.localPosition = flame.localPosition + Vector3.up * .13f;
            halo.transform.localScale = Vector3.one * 1.3f;
            var properties = new MaterialPropertyBlock();
            SetAppearance(halo, properties, Shape.Lamp, Element.Fire, new Color(1f,.27f,.018f), .92f);
            var ground = Plate("WarmStone", glow, Quaternion.Euler(90f, 0, 0));
            ground.transform.localPosition = Vector3.up * .065f;
            ground.transform.localScale = Vector3.one * 1.32f;
            SetAppearance(ground, properties, Shape.Lamp, Element.Fire, new Color(1f,.32f,.045f), .66f);
        }

        private static MeshRenderer Plate(string name, Transform parent, Quaternion rotation)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            var collider = quad.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            quad.transform.SetParent(parent, false);
            quad.transform.rotation = rotation;
            var renderer = quad.GetComponent<MeshRenderer>();
            Prepare(renderer);
            return renderer;
        }

        private static void Prepare(Renderer renderer)
        {
            if (lightMaterial == null) lightMaterial = Resources.Load<Material>("JindanCastLight");
            renderer.sharedMaterial = lightMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static void SetAppearance(Renderer renderer, MaterialPropertyBlock block, Shape shape,
            Element family, Color colour, float opacity)
        {
            block.SetColor("_Tint", colour);
            block.SetFloat("_Family", (int)family);
            block.SetFloat("_Shape", (int)shape);
            block.SetFloat("_Opacity", opacity);
            renderer.SetPropertyBlock(block);
        }

        private void Update()
        {
            float t = Time.time - started;
            if (t < GatherTime) Gather(t / GatherTime);
            else if (t < ImpactTime) Release((t - GatherTime) / (ImpactTime - GatherTime));
            else Unfold(Mathf.Clamp01((t - ImpactTime) / (Duration - ImpactTime)));
        }

        private void Gather(float t)
        {
            seed.transform.position = hand;
            seed.transform.localScale = Vector3.one * (.42f + .52f * Mathf.Sin(t * Mathf.PI * .78f));
            SetAppearance(seed, paint, Shape.Charge, element, tint, .72f + .28f * t);
        }

        private Vector3 AlongCast(float fraction)
        {
            var point = Vector3.Lerp(hand, destination, fraction);
            point.y += .42f * Mathf.Sin(Mathf.PI * fraction);
            return point;
        }

        private void DrawTrail(float from, float to, float alpha)
        {
            trail.enabled = alpha > .015f;
            if (!trail.enabled) return;
            for (int j = 0; j < TrailSamples; j++)
                trail.SetPosition(j, AlongCast(Mathf.Lerp(from, to, j / (TrailSamples - 1f))));
            trail.widthMultiplier = .36f;
            SetAppearance(trail, paint, Shape.Trail, element, tint, alpha);
        }

        private void Release(float t)
        {
            float travel = Mathf.SmoothStep(0f, 1f, t);
            DrawTrail(0f, travel, 1f);
            seed.transform.position = AlongCast(travel);
            seed.transform.localScale = Vector3.one * .69f;
            SetAppearance(seed, paint, Shape.Charge, element, tint, 1f);
        }

        private void Unfold(float t)
        {
            if (!burst)
            {
                burst = true;
                seed.enabled = false;
                column.enabled = impactPool.enabled = true;
                sparks.Play(true);
                sparks.Emit(18);
            }
            DrawTrail(Mathf.Min(1f, t * 4f), 1f, Mathf.Clamp01(1f - t * 4f));
            float flare = Mathf.Sin(Mathf.PI * Mathf.Pow(t, .51f));
            float height = .56f + flare * 1.65f;
            column.transform.position = destination + Vector3.up * (height * .5f);
            column.transform.localScale = new Vector3(.72f + flare * .82f, height, 1);
            SetAppearance(column, paint, Shape.Column, element, tint, Mathf.Clamp01((1f - t) * 2.6f));
            impactPool.transform.localScale = Vector3.one * Mathf.Lerp(.46f, 2.15f, Mathf.Sqrt(t));
            SetAppearance(impactPool, paint, Shape.Ground, element, tint, 1f - t * t);
        }
    }
}
