using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TianZhang.Features.CombatPresentation;
using TianZhang.Gameplay.Contracts;
using UnityEngine;

namespace TianZhang.Art.Jindan.Earth.Samples
{
    /// <summary>Resource demonstration only. Reuses the production presentation port, never changes combat rules.</summary>
    public sealed class EarthAssetSample : MonoBehaviour
    {
        public CombatBattlefieldSurface surface;
        public Static3DCombatUnitPresentationProvider provider;
        public Camera sampleCamera;
        public GameObject wallForm, wallDissolve, rampForm, rampDissolve, pressure, landingDust;
        public bool capture;
        public int captureWidth = 1920;
        public bool Complete { get; private set; }
        public string Failure { get; private set; }
        private string sequence, phase, output, evidence;
        private int frame;
        private RenderTexture target;
        private Texture2D pixels;
        private readonly List<string> proof = new List<string>();
        private readonly CombatUnitPresentationHex casterCell = new CombatUnitPresentationHex(4, 4);
        private readonly CombatUnitPresentationHex targetCell = new CombatUnitPresentationHex(4, 5);

        private IEnumerator Start()
        {
            provider.ConfigureSurface(surface);
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/d12-earth/" + captureWidth));
            evidence = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../assets/generated-scene-art/guanzhong-jindan-demo/d12-ability-assets/earth/evidence/" + captureWidth));
            if (capture)
            {
                Directory.CreateDirectory(output); Directory.CreateDirectory(evidence);
                Time.captureDeltaTime = 1f / 60;
                target = new RenderTexture(captureWidth, captureWidth == 1920 ? 1080 : 768, 24);
                pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                sampleCamera.targetTexture = target;
            }
            var routine = Run();
            // Flatten nested enumerators so validation failures are recorded and batch capture can exit cleanly.
            var stack = new Stack<IEnumerator>(); stack.Push(routine);
            while (stack.Count > 0)
            {
                object next = null; bool moved = false;
                try { moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current; }
                catch (Exception exception) { Failure = exception.ToString(); Debug.LogException(exception); break; }
                if (!moved) { stack.Pop(); continue; }
                if (next is IEnumerator nested) { stack.Push(nested); continue; }
                yield return next;
            }
            if (capture) File.WriteAllLines(Path.Combine(evidence, "runtime-proof.txt"), proof.Concat(new[] { Failure ?? "PASS" }));
            Complete = true;
        }

        private IEnumerator Run()
        {
            Check(surface.Cells.Count == 144, "144 production support cells");
            VerifyRampGeometry();
            yield return Manifest(false);
            yield return Manifest(true);
            yield return Gravity(false);
            yield return Gravity(true);
        }

        private void PrepareUnits(CombatUnitPresentationHex caster, CombatUnitPresentationHex targetCellValue)
        {
            provider.Prepare(new[] {
                new CombatUnitPresentationDescriptor("earth", "combat_player_default_v1", CombatUnitDisplayFaction.Player, caster, 0),
                new CombatUnitPresentationDescriptor("ally", "combat_player_default_v1", CombatUnitDisplayFaction.Player, targetCellValue, 3)
            });
        }

        private void Focus(Vector3 center, float size = 3)
        {
            // Same production 38-degree view; 3 is the existing camera's minimum gameplay zoom.
            sampleCamera.transform.rotation = Quaternion.Euler(38, 0, 0);
            sampleCamera.orthographic = true; sampleCamera.orthographicSize = size;
            sampleCamera.transform.position = center - sampleCamera.transform.forward * 12;
        }

        private IEnumerator Manifest(bool ramp)
        {
            sequence = ramp ? "ramp" : "wall"; frame = 0;
            var a = ramp ? new CombatUnitPresentationHex(7, 8) : new CombatUnitPresentationHex(4, 1);
            var b = ramp ? new CombatUnitPresentationHex(7, 9) : new CombatUnitPresentationHex(4, 2);
            PrepareUnits(ramp ? new CombatUnitPresentationHex(6, 8) : new CombatUnitPresentationHex(3, 1),
                ramp ? new CombatUnitPresentationHex(8, 8) : new CombatUnitPresentationHex(3, 2));
            Vector3 low = surface.Center(a), high = surface.Center(b), direction = high - low; direction.y = 0;
            if (ramp) Check(high.y > low.y, "sample ramp rises to its existing upper cell");
            Vector3 position = ramp ? low : new Vector3((low.x + high.x) / 2, Mathf.Min(low.y, high.y), (low.z + high.z) / 2);
            Quaternion rotation = Quaternion.LookRotation(direction);
            Vector3 scale = ramp ? new Vector3(1, (high.y - low.y) / .75f, 1) : Vector3.one;
            Focus((low + high) / 2 + Vector3.up * .35f);
            phase = "empty"; yield return Frames(24); Key("01-empty");
            Color32[] baseline = capture ? pixels.GetPixels32() : null;
            var formed = Instantiate(ramp ? rampForm : wallForm, position, rotation); formed.transform.localScale = scale;
            Matrix4x4 original = formed.GetComponentInChildren<MeshFilter>().transform.localToWorldMatrix;
            phase = "forming"; yield return Frames(10); Key("02-forming");
            yield return Frames(14);
            Check(formed.transform.position == position && formed.transform.localScale == scale, "formation root anchors unchanged");
            Check(formed.GetComponentInChildren<MeshFilter>().transform.localToWorldMatrix == original, "formation mesh transform unchanged");
            phase = "solid"; yield return Frames(40); Key("03-solid");
            Focus((low + high) / 2 + Vector3.up * .35f, 5); Key("03-solid-default-zoom");
            Focus((low + high) / 2 + Vector3.up * .35f);
            Destroy(formed); yield return null;
            var dissolved = Instantiate(ramp ? rampDissolve : wallDissolve, position, rotation); dissolved.transform.localScale = scale;
            phase = "dissolving"; yield return Frames(10); Key("04-dissolving");
            yield return Frames(38); phase = "cleared"; Key("05-cleared");
            Check(dissolved.GetComponentsInChildren<ParticleSystem>().All(p => !p.IsAlive()), "dissolve particles fully cleared");
            Check(dissolved.transform.position == position && dissolved.transform.localScale == scale, "dissolve root anchors unchanged");
            if (capture)
            {
                var final = pixels.GetPixels32(); long difference = 0;
                for (int i = 0; i < final.Length; i++) difference += Math.Abs(final[i].r - baseline[i].r) +
                    Math.Abs(final[i].g - baseline[i].g) + Math.Abs(final[i].b - baseline[i].b);
                double average = difference / (final.Length * 3.0);
                proof.Add(sequence + " cleared-vs-empty mean RGB difference=" + average.ToString("F6"));
                Check(average < .15, "no residual visible surface or shadow after dissolve");
            }
            proof.Add(sequence + " root=" + position.ToString("F5") + " scale=" + scale.ToString("F5") + " form/dissolve=.35 seconds");
            yield return Frames(24); Destroy(dissolved); yield return null;
        }

        private void Altitude(string actor, CombatUnitPresentationHex cell, int band) => provider.Present(
            new CombatUnitPresentationEventProjection(actor, CombatUnitPresentationEvent.Idle, cell, cell,
                actor == "earth" ? 0 : 3, null, altitudeBand: band));

        private IEnumerator Gravity(bool land)
        {
            sequence = land ? "gravity-land" : "gravity-low"; frame = 0;
            PrepareUnits(casterCell, targetCell);
            if (!land) { Altitude("earth", casterCell, 1); while (provider.IsPresenting) yield return null; }
            Altitude("ally", targetCell, 2); while (provider.IsPresenting) yield return null;
            provider.TryGetPresentedUnit("ally", out GameObject ally);
            float ground = surface.Center(targetCell).y;
            Check(Mathf.Abs(ally.transform.position.y - ground - 1.5f) < .002f, "high altitude is 1.5m");
            Focus((surface.Center(casterCell) + surface.Center(targetCell)) / 2 + Vector3.up * 1);
            phase = "high"; yield return Frames(30); Key("01-high");
            var trace = Instantiate(pressure, ally.transform); trace.transform.localPosition = Vector3.zero;
            Altitude("ally", targetCell, land ? 0 : 1);
            phase = "descending"; float priorY = ally.transform.position.y; int descentFrames = 0;
            while (provider.IsPresenting)
            {
                yield return Frames(1); descentFrames++;
                float y = ally.transform.position.y;
                Check(y <= priorY + .0001f, "gravity movement is downward"); priorY = y;
                if (descentFrames == (land ? 10 : 5)) Key("02-descending");
            }
            GameObject dust = null;
            foreach (var ps in trace.GetComponentsInChildren<ParticleSystem>()) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            if (land) dust = Instantiate(landingDust, ally.transform.position, Quaternion.identity);
            phase = land ? "landed" : "low"; yield return Frames(5); Key("03-result");
            float finalOffset = ally.transform.position.y - ground;
            Check(Mathf.Abs(finalOffset - (land ? 0 : .75f)) < .002f, "final feet match production altitude/support");
            var ring = ally.GetComponentsInChildren<LineRenderer>(true).Single(l => l.name == "FlightGroundProjection");
            Check(ring.enabled == !land, "ground projection agrees with airborne state");
            yield return Frames(35);
            Check(trace.GetComponentsInChildren<ParticleSystem>().All(p => !p.IsAlive()), "pressure trace cleared");
            if (dust) Check(dust.GetComponentsInChildren<ParticleSystem>().All(p => !p.IsAlive()), "landing dust cleared");
            Destroy(trace); if (dust) Destroy(dust);
            phase = "released"; yield return Frames(24); Key("04-released");
            Check(Mathf.Abs(ally.transform.position.y - ground - finalOffset) < .0001f, "release does not lift actor");
            proof.Add(sequence + " actual descent frames=" + descentFrames + " fixedDelta=1/60 finalFootOffset=" + finalOffset.ToString("F6") +
                " movementOwner=Static3DCombatUnitPresentationProvider.Present->CombatUnitFlightVisual.Transition speed=4m/s");
        }

        private IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return null;
                if (capture && frame % 2 == 0)
                {
                    Render(); string directory = Path.Combine(output, sequence); Directory.CreateDirectory(directory);
                    File.WriteAllBytes(Path.Combine(directory, (frame / 2).ToString("D4") + ".jpg"), pixels.EncodeToJPG(90));
                }
                frame++;
            }
        }

        private void Render()
        {
            sampleCamera.Render(); var previous = RenderTexture.active; RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(); RenderTexture.active = previous;
        }
        private void Key(string key)
        {
            if (!capture) return;
            Render(); File.WriteAllBytes(Path.Combine(evidence, sequence + "-" + key + ".png"), pixels.EncodeToPNG());
            foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                var particles = new ParticleSystem.Particle[ps.main.maxParticles]; int count = ps.GetParticles(particles);
                proof.Add(sequence + "/" + key + " ps=" + ps.name + " playing=" + ps.isPlaying + " time=" + ps.time +
                    " count=" + count + " bounds=" + ps.GetComponent<Renderer>().bounds);
                if (count > 0) proof.Add("particle position=" + particles[0].position + " velocity=" + particles[0].velocity +
                    " screen=" + sampleCamera.WorldToScreenPoint(particles[0].position) + " size=" + particles[0].GetCurrentSize(ps));
            }
            var particleRenderers = FindObjectsByType<ParticleSystemRenderer>(FindObjectsSortMode.None).Where(r => r.enabled).ToArray();
            if (particleRenderers.Length > 0 && key.Contains("02-"))
            {
                Color32[] withParticles = pixels.GetPixels32();
                foreach (var renderer in particleRenderers) renderer.enabled = false;
                Render(); Color32[] withoutParticles = pixels.GetPixels32();
                int changed = 0, max = 0;
                for (int i = 0; i < withParticles.Length; i++)
                {
                    int delta = Math.Abs(withParticles[i].r - withoutParticles[i].r) + Math.Abs(withParticles[i].g - withoutParticles[i].g) +
                        Math.Abs(withParticles[i].b - withoutParticles[i].b);
                    if (delta > 3) changed++; max = Math.Max(max, delta);
                }
                proof.Add(sequence + " particle-only changedPixels=" + changed + " maximumRgbDelta=" + max);
                Check(changed > 20 && max > 10, "particle output visible in captured frame");
                foreach (var renderer in particleRenderers) renderer.enabled = true;
                Render();
            }
        }
        private void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(sequence + ": " + message);
        }

        private void VerifyRampGeometry()
        {
            var go = Instantiate(rampForm); var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            int samples = 0; float maxError = 0;
            foreach (float rise in new[] { .375f, .75f }) for (int direction = 0; direction < 6; direction++)
            {
                var a = new CombatUnitPresentationHex(0, 0); var b = CombatUnitPresentationPose.Step(a, direction);
                var low = CombatBattlefieldSurface.Horizontal(0, 0); var high = CombatBattlefieldSurface.Horizontal(b.Q, b.R) + Vector3.up * rise;
                Vector3 forward = high - low; forward.y = 0;
                go.transform.SetPositionAndRotation(low, Quaternion.LookRotation(forward)); go.transform.localScale = new Vector3(1, rise / .75f, 1);
                var support = new CombatBattlefieldSurface.EarthRamp(a, b, low, high);
                foreach (Vector3 vertex in mesh.vertices)
                {
                    float expected = .75f * (1 - Mathf.Cos(Mathf.PI * Mathf.Clamp01((vertex.z - .1f) / .4f))) * .5f;
                    if (Mathf.Abs(vertex.y - expected) > .0001f) continue;
                    Vector3 world = go.transform.TransformPoint(vertex);
                    Check(support.TryHeight(world, out float y), "ramp vertex within existing support");
                    maxError = Mathf.Max(maxError, Mathf.Abs(world.y - y)); samples++;
                }
            }
            Check(samples > 100 && maxError < .001f, "six directions / two rises preserve slope support");
            proof.Add("ramp original mesh=" + mesh.name + " vertices=" + mesh.vertexCount + " triangles=" + mesh.triangles.Length / 3 +
                " six-direction support samples=" + samples + " maxErrorMetres=" + maxError.ToString("G9"));
            Destroy(go);
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(24, 18, 900, 40), "D12.1 资源样例 · " + sequence + " / " + phase + " · 正式 Demo 尚未接线");
        }
        private void OnDestroy()
        {
            if (capture) Time.captureDeltaTime = 0;
            if (sampleCamera) sampleCamera.targetTexture = null;
            if (target) { target.Release(); Destroy(target); }
            if (pixels) Destroy(pixels);
        }
    }
}
