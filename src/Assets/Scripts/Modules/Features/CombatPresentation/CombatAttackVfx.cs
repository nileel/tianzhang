using System.Collections.Generic;
using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>
    /// One-shot lightweight attack effect: a melee slash crescent over the target or a glowing
    /// projectile with a comet tail and an impact ring. Everything is generated at runtime from
    /// quads and small meshes with a soft radial texture, so no external art or VFX package is
    /// required. It reads no combat state and never writes back; the provider owns its lifetime.
    /// </summary>
    public sealed class CombatAttackVfx : MonoBehaviour
    {
        public const string SlashObjectName = "CombatAttackVfx_Slash";
        public const string ProjectileObjectName = "CombatAttackVfx_Projectile";
        private const int ArcSegments = 12;
        private const int RingSegments = 28;
        private const int TailCount = 4;

        private enum Mode { Slash, Projectile }

        private static Texture2D glowTexture;

        private Mode mode;
        private Color color;
        private float scale = 1f;
        private float duration = 0.4f;
        private float elapsed;
        private Vector3 from;
        private Vector3 to;
        private Transform head;
        private readonly List<Transform> tail = new List<Transform>();
        private readonly List<Material> tailMaterials = new List<Material>();
        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private GameObject ribbonHost;
        private Material ribbonMaterial;
        private Material headMaterial;
        private bool impacted;

        public static CombatAttackVfx PlaySlash(Vector3 actorFeet, Vector3 targetWorld, Color tint, float size, float seconds)
        {
            CombatAttackVfx effect = Create(SlashObjectName, actorFeet, tint, size, seconds);
            effect.mode = Mode.Slash;
            effect.from = actorFeet;
            effect.to = targetWorld;
            effect.CreateRibbon();
            effect.RenderSlash(0f);
            return effect;
        }

        public static CombatAttackVfx PlayProjectile(Vector3 origin, Vector3 target, Color tint, float size, float seconds)
        {
            CombatAttackVfx effect = Create(ProjectileObjectName, origin, tint, size, seconds);
            effect.mode = Mode.Projectile;
            effect.from = origin;
            effect.to = target;
            effect.head = effect.CreateQuad("Head", 0.55f, tint, out effect.headMaterial);
            effect.CreateRibbon();
            for (int index = 0; index < TailCount; index++)
            {
                float tailSize = 0.34f - index * 0.06f;
                effect.tail.Add(effect.CreateQuad("Tail" + index, tailSize, tint, out Material tailMaterial));
                effect.tailMaterials.Add(tailMaterial);
            }
            return effect;
        }

        private static CombatAttackVfx Create(string objectName, Vector3 position, Color tint, float size, float seconds)
        {
            var root = new GameObject(objectName);
            root.transform.position = position;
            CombatAttackVfx effect = root.AddComponent<CombatAttackVfx>();
            effect.color = tint;
            effect.scale = Mathf.Max(0.1f, size);
            effect.duration = Mathf.Max(0.05f, seconds);
            return effect;
        }

        private static Material CreateMaterial(string name, Color tint)
        {
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            return new Material(shader)
            {
                name = name,
                mainTexture = GlowTexture(),
                color = tint,
            };
        }

        /// <summary>Soft radial alpha so every quad reads as a glow instead of a flat disc.</summary>
        private static Texture2D GlowTexture()
        {
            if (glowTexture != null) return glowTexture;
            const int size = 64;
            glowTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "CombatAttackVfx_Glow",
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float alpha = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                alpha *= alpha;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
            glowTexture.SetPixels32(pixels);
            glowTexture.Apply(false, true);
            return glowTexture;
        }

        private Transform CreateQuad(string name, float size, Color tint, out Material material)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Object.Destroy(quad.GetComponent<Collider>());
            quad.transform.localScale = Vector3.one * size * scale;
            var renderer = quad.GetComponent<MeshRenderer>();
            material = CreateMaterial("CombatAttackVfx_" + name, tint);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return quad.transform;
        }

        private void CreateRibbon()
        {
            mesh = new Mesh { name = "CombatAttackVfx_Ribbon" };
            mesh.MarkDynamic();
            var host = new GameObject("Ribbon");
            // Vertices are authored in world space, so the ribbon host stays at the origin.
            host.transform.SetParent(null);
            ribbonHost = host;
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = host.AddComponent<MeshRenderer>();
            ribbonMaterial = CreateMaterial("CombatAttackVfx_Ribbon", Color.white);
            meshRenderer.sharedMaterial = ribbonMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            if (mode == Mode.Slash)
            {
                RenderSlash(progress);
            }
            else
            {
                RenderProjectile(progress);
            }
            if (elapsed >= duration + 0.1f)
            {
                Destroy(gameObject);
            }
        }

        private void RenderSlash(float progress)
        {
            // The crescent is anchored to the attacker and oriented by the attack direction, so
            // it travels and turns with the character instead of floating over the target cell.
            Vector3 forward = to - from;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            float radius = (0.62f + 0.18f * progress) * scale;
            float sweep = Mathf.Lerp(60f, 170f, progress);
            Vector3 center = from + Vector3.up * 0.95f + forward * 0.6f;
            float alpha = Mathf.Clamp01(1.1f - progress);
            var points = new List<Vector3>(ArcSegments + 1);
            for (int index = 0; index <= ArcSegments; index++)
            {
                float angle = Mathf.Lerp(-sweep * 0.5f, sweep * 0.5f, index / (float)ArcSegments) * Mathf.Deg2Rad;
                points.Add(center + right * Mathf.Sin(angle) * radius + Vector3.up * (Mathf.Cos(angle) * radius * 0.65f));
            }
            BuildArcStrip(points, forward, 0.18f * scale * (1f - 0.3f * progress), alpha);
        }

        private void RenderProjectile(float progress)
        {
            float travel = Mathf.Clamp01(progress / 0.55f);
            Vector3 position = Vector3.Lerp(from, to, travel);
            position.y += Mathf.Sin(travel * Mathf.PI) * 0.25f;
            if (!impacted && travel >= 1f)
            {
                impacted = true;
                if (head != null) head.gameObject.SetActive(false);
                foreach (Transform part in tail) part.gameObject.SetActive(false);
            }
            if (!impacted)
            {
                if (head != null)
                {
                    head.position = position;
                    if (Camera.main != null) head.rotation = Camera.main.transform.rotation;
                }
                for (int index = 0; index < tail.Count; index++)
                {
                    float t = (index + 1f) / (tail.Count + 1f);
                    Vector3 point = Vector3.Lerp(from, position, Mathf.Clamp01(travel - t * 0.35f));
                    point.y += Mathf.Sin(t * Mathf.PI) * 0.2f;
                    tail[index].position = point;
                    if (Camera.main != null) tail[index].rotation = Camera.main.transform.rotation;
                    Color tint = color;
                    tint.a = Mathf.Lerp(0.45f, 0.05f, t);
                    tailMaterials[index].color = tint;
                }
                mesh.Clear();
                return;
            }
            float ringProgress = Mathf.Clamp01((progress - 0.55f) / 0.45f);
            float radius = Mathf.Lerp(0.2f, 0.9f, ringProgress) * scale;
            BuildRing(to, radius, 0.1f * scale, Mathf.Clamp01(1f - ringProgress));
        }

        /// <summary>Ribbon in the plane whose normal is <paramref name="planeNormal"/>.</summary>
        private void BuildArcStrip(IReadOnlyList<Vector3> points, Vector3 planeNormal, float width, float alpha)
        {
            int samples = points.Count;
            if (samples < 2) { mesh.Clear(); return; }
            var vertices = new Vector3[samples * 2];
            var colors = new Color[samples * 2];
            var triangles = new int[(samples - 1) * 6];
            for (int index = 0; index < samples; index++)
            {
                Vector3 tangent = points[Mathf.Min(index + 1, samples - 1)] - points[Mathf.Max(index - 1, 0)];
                if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.forward;
                tangent.Normalize();
                Vector3 normal = Vector3.Cross(tangent, planeNormal).normalized;
                if (normal.sqrMagnitude < 0.5f) normal = Vector3.up;
                vertices[index * 2] = points[index] - normal * width * 0.5f;
                vertices[index * 2 + 1] = points[index] + normal * width * 0.5f;
                Color vertex = color;
                vertex.a = alpha * (1f - 0.6f * index / (float)(samples - 1));
                colors[index * 2] = vertex;
                colors[index * 2 + 1] = vertex;
                if (index == samples - 1) continue;
                int start = index * 6;
                triangles[start] = index * 2;
                triangles[start + 1] = index * 2 + 2;
                triangles[start + 2] = index * 2 + 1;
                triangles[start + 3] = index * 2 + 1;
                triangles[start + 4] = index * 2 + 2;
                triangles[start + 5] = index * 2 + 3;
            }
            ApplyMesh(vertices, colors, triangles);
        }

        /// <summary>Flat ground ring around <paramref name="center"/>.</summary>
        private void BuildRing(Vector3 center, float radius, float width, float alpha)
        {
            int samples = RingSegments + 1;
            var vertices = new Vector3[samples * 2];
            var colors = new Color[samples * 2];
            var triangles = new int[RingSegments * 6];
            for (int index = 0; index < samples; index++)
            {
                float angle = index / (float)RingSegments * Mathf.PI * 2f;
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 point = center + new Vector3(0f, 0.15f, 0f) + radial * radius;
                Vector3 normal = radial;
                vertices[index * 2] = point - normal * width * 0.5f;
                vertices[index * 2 + 1] = point + normal * width * 0.5f;
                Color vertex = color;
                vertex.a = alpha;
                colors[index * 2] = vertex;
                colors[index * 2 + 1] = vertex;
                if (index == RingSegments) continue;
                int start = index * 6;
                triangles[start] = index * 2;
                triangles[start + 1] = index * 2 + 2;
                triangles[start + 2] = index * 2 + 1;
                triangles[start + 3] = index * 2 + 1;
                triangles[start + 4] = index * 2 + 2;
                triangles[start + 5] = index * 2 + 3;
            }
            ApplyMesh(vertices, colors, triangles);
        }

        private void ApplyMesh(Vector3[] vertices, Color[] colors, int[] triangles)
        {
            // Sample the glow texture at its bright centre; the vertex alpha carries the fade.
            var uv = new Vector2[vertices.Length];
            for (int index = 0; index < uv.Length; index++) uv[index] = new Vector2(0.5f, 0.5f);
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            ribbonMaterial.color = Color.white;
        }

        private void OnDestroy()
        {
            if (head != null) Destroy(head.gameObject);
            foreach (Transform part in tail)
                if (part != null) Destroy(part.gameObject);
            if (ribbonHost != null) Destroy(ribbonHost);
            if (mesh != null) Destroy(mesh);
            foreach (Material material in tailMaterials)
                if (material != null) Destroy(material);
            if (ribbonMaterial != null) Destroy(ribbonMaterial);
            if (headMaterial != null) Destroy(headMaterial);
        }
    }
}
