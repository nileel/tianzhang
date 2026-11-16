using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Entity;
using TianZhang.Features.Adventure;
using TianZhang.Features.CombatPresentation;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.Spatial;
using UnityEngine;

namespace TianZhang.Art.Jindan.Fire.Samples
{
    public sealed class FireAssetSample : MonoBehaviour
    {
        public CombatBattlefieldSurface surface;
        public Static3DCombatUnitPresentationProvider provider;
        public Camera sampleCamera;
        public GameObject lowFire, embers, center, edge, scorched, wet, shallow, deep, steam;
        public GameObject[] staticWaterOverlays;
        public JindanDemoCatalogData catalog;
        public CombatRuleSetData rules;
        public EnvironmentProfileAsset environment;
        public AttackProfileData[] attacks;
        public bool capture;
        public int captureWidth = 1920;
        public bool Complete { get; private set; }
        public string Failure { get; private set; }
        private FireSampleRules demo;
        private readonly Dictionary<HexCoord, GameObject> fires = new();
        private readonly Dictionary<HexCoord, GameObject> sheets = new();
        private readonly List<GameObject> warmth = new();
        private readonly List<Mesh> meshes = new();
        private readonly List<string> proof = new();
        private readonly HashSet<long> consumed = new();
        private GameObject stele;
        private string output, evidence, phase;
        private int frame, steamCount;
        private RenderTexture target;
        private Texture2D pixels;

        private IEnumerator Start()
        {
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/d12-fire-redesign-20261117/" + captureWidth));
            evidence = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../assets/generated-scene-art/guanzhong-jindan-demo/d12-ability-assets/fire/evidence/redesign-20261117/" + captureWidth));
            if (capture)
            {
                Directory.CreateDirectory(output); Directory.CreateDirectory(evidence); Time.captureDeltaTime = 1f / 60;
                target = new RenderTexture(captureWidth, captureWidth == 1920 ? 1080 : 768, 24);
                pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false); sampleCamera.targetTexture = target;
            }
            var stack = new Stack<IEnumerator>(); stack.Push(Run());
            while (stack.Count > 0)
            {
                object next = null; bool moved = false;
                try { moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current; }
                catch (Exception exception)
                {
                    Failure = exception.ToString();
                    if (capture) File.WriteAllLines(Path.Combine(evidence, "runtime-proof.txt"), proof.Concat(new[] { Failure }));
                    Debug.LogException(exception); break;
                }
                if (!moved) { stack.Pop(); continue; }
                if (next is IEnumerator nested) { stack.Push(nested); continue; }
                yield return next;
            }
            if (capture) File.WriteAllLines(Path.Combine(evidence, "runtime-proof.txt"), proof.Concat(new[] { Failure ?? "PASS" }));
            Complete = true;
        }

        private IEnumerator Run()
        {
            demo = new FireSampleRules(this); provider.ConfigureSurface(surface);
            provider.Prepare(JindanRehearsalFactory.Descriptors(demo.Spawned, "combat_player_default_v1"));
            Check(staticWaterOverlays.All(go => go && !go.activeInHierarchy), "static water disabled");
            proof.Add("Fixture: production board/catalog/stats; staged actors earth(7,11), water(8,11), fire(7,14); dry fuel4 at (7,13),(8,12),(8,13). No runtime mutation of rules/fuel/durability.");
            stele = Instantiate(center, Point(demo.Fire.Support.Cell), Quaternion.identity);
            foreach (var cell in surface.Cells) warmth.Add(PlaceSheet(edge, new HexCoord(cell.q, cell.r), .025f));
            foreach (var cell in demo.Session.Surfaces.Cells) SetSurface(cell.Cell, cell.Kind);
            Focus(5); yield return Present("01-before");
            Check(demo.Fire.Main == null && fires.Count == 0, "undeployed has no warmth or fire");
            long sequence = demo.Session.Surfaces.SettlementSequence;
            var quote = demo.Commands.Preview(demo.Session, demo.Deploy());
            Check(quote.IsLegal && demo.Session.Surfaces.SettlementSequence == sequence && steamCount == 0, "preview is pure");
            demo.Execute(demo.Deploy()); yield return Present("02-deployed-no-fire");
            Check(demo.Fire.Main != null && fires.Count == 0, "deployment does not ignite");
            FocusBoard(); Key("02-whole-board-warm-range"); Focus(5);
            demo.NextFireTurn(); yield return Present("03-deployment-end-no-fire");
            Check(demo.Seat.MaintenancePayments == 0 && fires.Count == 0, "deployment own end does not tick");
            demo.NextFireTurn(); yield return Present("04-three-burning");
            Check(demo.Seat.MaintenancePayments == 1 && fires.Count == 3, "first later own end ignites three dry fuel cells");
            Check(demo.FuelCells.All(c => demo.Session.Surfaces.Get(c).Kind == CombatSurfaceKind.Burning), "expected three fuel cells burning");
            foreach (var fire in fires)
            {
                Geometry(fire.Value.GetComponentInChildren<MeshRenderer>(), "ground-flame " + fire.Key,
                    "FireAssetSample.Sync -> Instantiate(lowFire); Sync writes _Strength/_Opacity; LowFire.shader Frag writes silhouette/colour from _Time");
                Visibility(new[] { fire.Value }, "default-zoom-ground-flame " + fire.Key);
            }
            var spirit = stele.transform.Find("Intact/Glow/CenterSpiritFlame");
            Check(spirit && spirit.gameObject.activeInHierarchy, "center spirit follows active Glow");
            Geometry(spirit.GetComponent<MeshRenderer>(), "center-spirit",
                "FireAssetBuilder.BuildCenter -> Intact/Glow/CenterSpiritFlame; FireAssetSample.Sync toggles Glow from Main");
            Visibility(new[] { spirit.gameObject }, "default-zoom-center-spirit");
            Visibility(fires.Values.ToArray(), "default-zoom-three-low-fire");
            Visibility(warmth.ToArray(), "whole-board-weak-warmth");
            demo.ToActor(FireSampleRules.EarthActor);
            demo.Execute(demo.Strike()); yield return Present("05-center-damaged");
            Check(demo.Fire.Support.Durability == 225 && demo.Fire.Main != null && warmth.All(w => w.activeSelf) && spirit.gameObject.activeInHierarchy,
                "375 damage leaves225, main warmth and center spirit");
            demo.Execute(demo.Strike()); yield return Present("06-center-destroyed-tails");
            Check(demo.Fire.Support.IsDestroyed && demo.Fire.Main == null && fires.Count == 3 && demo.Fire.Tails.Single().RemainingCycles == 2, "center destroyed retains paid two-cycle tail");
            Check(warmth.All(w => !w.activeSelf) && !stele.transform.Find("Intact").gameObject.activeSelf && !spirit.gameObject.activeInHierarchy,
                "main warmth, center and center spirit removed immediately");
            demo.ToActor(FireSampleRules.FireActor);
            Check(demo.Fire.Tails.Single().RemainingCycles == 2, "next owner start does not age tail");
            demo.EndCurrent(); yield return Present("07-tail-one-own-cycle");
            Check(demo.Fire.Tails.Single().RemainingCycles == 1, "first later owner end leaves one cycle");
            demo.ToActor(FireSampleRules.WaterActor);
            var tide = demo.Tide(); var preview = demo.Commands.Preview(demo.Session, tide);
            Check(preview.IsLegal, "tide preview: " + preview.RejectionReason);
            sequence = demo.Session.Surfaces.SettlementSequence;
            var previewChanges = preview.Tide.Changes.ToArray();
            Check(steamCount == 0 && demo.Session.Surfaces.SettlementSequence == sequence, "water preview emits no steam");
            demo.Execute(tide);
            var receipt = demo.Session.Surfaces.Settlements.Last(r => r.Sequence > sequence && r.Changes.Count > 0);
            Check(receipt.Changes.SequenceEqual(previewChanges), "water receipt matches frozen quote");
            var extinguished = receipt.Changes.Where(c => c.Before == CombatSurfaceKind.Burning && c.After == CombatSurfaceKind.Wet).Select(c => c.Cell).ToArray();
            Check(extinguished.Length > 0 && extinguished.Length < 3, "tide extinguishes a proper subset; remaining tail can expire naturally");
            var effects = ConsumeReceipts(); Check(ConsumeReceipts().Count == 0, "settlement replay cannot duplicate steam");
            yield return Sync(); phase = "07-water-extinguish-moving"; yield return Frames(10); Key(phase); Visibility(effects.ToArray(), "shared-steam");
            yield return Frames(50);
            foreach (var effect in effects) Check(!effect.GetComponent<ParticleSystem>().IsAlive(), "shared steam finite lifetime");
            if (capture) Invisible(effects.ToArray(), "expired-steam");
            foreach (var effect in effects) Destroy(effect);
            yield return Present("08-water-wet-tail-remains");
            Check(extinguished.All(c => demo.Session.Surfaces.Get(c).Kind == CombatSurfaceKind.Wet), "water extinguished cells are wet");
            demo.ToActor(FireSampleRules.FireActor);
            Check(demo.Fire.Tails.Single().RemainingCycles == 1, "non-owner ends and owner start do not age tail");
            demo.NextFireTurn(); yield return Present("10-naturally-scorched");
            Check(demo.Fire.Tails.Count == 0 && fires.Count == 0, "second later own end clears tail and all low fire");
            Check(demo.FuelCells.Except(extinguished).All(c => demo.Session.Surfaces.Get(c).Kind == CombatSurfaceKind.Scorched), "natural exhaustion leaves scorch");
            Check(extinguished.All(c => demo.Session.Surfaces.Get(c).Kind == CombatSurfaceKind.Wet), "natural expiry never overwrites water extinguish");
            Check(warmth.All(w => !w.activeSelf), "no main-range residual");
            proof.Add("Consumed steam instances=" + steamCount + "; owned burning maximum=3; original static water disabled=" + staticWaterOverlays.Length);
            proof.Add("Only sample bindings tested; production Adventure/View/V3 HUD integration pending. All video frames camera38deg size5; size3 supplementary keys; Unity=" + Application.unityVersion);
        }

        private List<GameObject> ConsumeReceipts()
        {
            var effects = new List<GameObject>();
            foreach (var receipt in demo.Session.Surfaces.Settlements)
            {
                if (!consumed.Add(receipt.Sequence)) continue;
                foreach (var c in receipt.Changes.Where(c => c.Before == CombatSurfaceKind.Burning && c.After == CombatSurfaceKind.Wet))
                {
                    effects.Add(Instantiate(steam, Point(c.Cell) + Vector3.up * .04f, Quaternion.identity)); steamCount++;
                    proof.Add("steam receipt=" + receipt.Sequence + " " + c.Cell + " Burning->Wet");
                }
            }
            return effects;
        }
        private IEnumerator Present(string name)
        {
            Focus(5); phase = name; yield return Sync(); yield return Frames(50); Key(name + "-default-zoom"); Record();
            Focus(3); Key(name); Focus(5);
        }
        private IEnumerator Sync()
        {
            bool main = demo.Fire.Main != null;
            foreach (var go in warmth) go.SetActive(main);
            stele.transform.Find("Intact").gameObject.SetActive(!demo.Fire.Support.IsDestroyed);
            stele.transform.Find("Intact/Glow").gameObject.SetActive(main);
            stele.transform.Find("Failed").gameObject.SetActive(false);
            var burning = demo.Session.Surfaces.Cells.Where(c => c.Kind == CombatSurfaceKind.Burning && c.SourceRef == demo.Seat.Definition.AbilityInstanceId).Select(c => c.Cell).ToHashSet();
            Check(burning.Count <= 3, "shared main and tail capacity <=3");
            var removing = fires.Where(pair => !burning.Contains(pair.Key)).ToArray();
            foreach (var cell in burning)
            {
                if (!fires.ContainsKey(cell)) fires[cell] = Instantiate(lowFire, Point(cell) + Vector3.up * .025f, Quaternion.identity);
                var block = new MaterialPropertyBlock(); block.SetFloat("_Strength", main ? 1 : .65f); block.SetFloat("_Opacity", 1);
                foreach (var renderer in fires[cell].GetComponentsInChildren<MeshRenderer>()) renderer.SetPropertyBlock(block);
            }
            foreach (var pair in removing)
                foreach (var ps in pair.Value.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            for (int f = 0; f < 15 && removing.Length > 0; f++)
            {
                var block = new MaterialPropertyBlock(); block.SetFloat("_Strength", .65f); block.SetFloat("_Opacity", 1 - (f + 1) / 15f);
                foreach (var pair in removing) foreach (var r in pair.Value.GetComponentsInChildren<MeshRenderer>()) r.SetPropertyBlock(block);
                yield return Frames(1);
            }
            foreach (var pair in removing) { Destroy(pair.Value); fires.Remove(pair.Key); }
            foreach (var cell in demo.Session.Surfaces.Cells) SetSurface(cell.Cell, cell.Kind);
        }
        private readonly Dictionary<HexCoord, CombatSurfaceKind> displayedKinds = new();
        private void SetSurface(HexCoord cell, CombatSurfaceKind kind)
        {
            if (displayedKinds.TryGetValue(cell, out var oldKind) && oldKind == kind) return;
            displayedKinds[cell] = kind;
            if (sheets.TryGetValue(cell, out var old)) { Destroy(old); sheets.Remove(cell); }
            GameObject prefab = kind == CombatSurfaceKind.Wet ? wet : kind == CombatSurfaceKind.ShallowWater ? shallow :
                kind == CombatSurfaceKind.DeepWater ? deep : kind == CombatSurfaceKind.Scorched ? scorched : null;
            if (prefab != null) sheets[cell] = PlaceSheet(prefab, cell, .018f);
        }
        private GameObject PlaceSheet(GameObject prefab, HexCoord cell, float lift)
        {
            var go = Instantiate(prefab, Point(cell), Quaternion.identity);
            var support = surface.Cells.Single(c => c.q == cell.Q && c.r == cell.R);
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = FireSampleSupport.Fit(filter.sharedMesh, filter.transform, surface, support, lift);
                filter.sharedMesh = mesh; meshes.Add(mesh);
            }
            return go;
        }
        private void Record()
        {
            proof.Add(phase + " tick=" + demo.Session.TurnScheduler.CurrentTick + " main=" + (demo.Fire.Main != null) +
                " center=" + demo.Fire.Support.Durability + " burning=" + fires.Count + " maintenance=" + demo.Seat.MaintenancePayments +
                " tails=" + string.Join(",", demo.Fire.Tails.Select(t => t.RemainingCycles)) + " fuel=" + demo.Fire.RemainingFuel + "/" + demo.Fire.ReservedFuel +
                " surfaces=" + string.Join(";", demo.FuelCells.Select(c => c + "=" + demo.Session.Surfaces.Get(c).Kind)));
        }
        private void Geometry(MeshRenderer renderer, string label, string writer)
        {
            var bounds = renderer.bounds; var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int corner = 0; corner < 8; corner++)
            {
                var p = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                Vector2 screen = sampleCamera.WorldToScreenPoint(p); min = Vector2.Min(min, screen); max = Vector2.Max(max, screen);
            }
            var chain = new List<string>(); for (var node = renderer.transform; node; node = node.parent) chain.Add(node.name); chain.Reverse();
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
            float strength = block.isEmpty ? renderer.sharedMaterial.GetFloat("_Strength") : block.GetFloat("_Strength");
            float opacity = block.isEmpty ? renderer.sharedMaterial.GetFloat("_Opacity") : block.GetFloat("_Opacity");
            proof.Add(label + " hierarchy=" + string.Join("/", chain) + " active=" + renderer.gameObject.activeInHierarchy +
                " mesh=" + renderer.GetComponent<MeshFilter>().sharedMesh.name + " material=" + renderer.sharedMaterial.name +
                " shader=" + renderer.sharedMaterial.shader.name + " position=" + renderer.transform.position.ToString("F5") +
                " worldBounds=" + bounds + " camera=" + sampleCamera.name + " orthographicSize=" + sampleCamera.orthographicSize +
                " target=" + (target ? target.width + "x" + target.height : "GameView") +
                " conversion=Camera.WorldToScreenPoint(worldBoundsCorners) projectedMin=" + min.ToString("F2") +
                " projectedMax=" + max.ToString("F2") + " projectedSize=" + (max-min).ToString("F2") +
                " Strength=" + strength + " Opacity=" + opacity + " writer=" + writer);
        }
        private Vector3 Point(HexCoord cell) => surface.Center(new CombatUnitPresentationHex(cell.Q, cell.R));
        private void Focus(float size)
        {
            sampleCamera.transform.rotation = Quaternion.Euler(38, 0, 0); sampleCamera.orthographic = true; sampleCamera.orthographicSize = size;
            sampleCamera.transform.position = Point(new HexCoord(7, 12)) + Vector3.up * .3f - sampleCamera.transform.forward * 12;
        }
        private void FocusBoard()
        {
            var points = surface.Cells.Select(c => Point(new HexCoord(c.q, c.r))).ToArray();
            var bounds = new Bounds(points[0], Vector3.zero); foreach (var point in points) bounds.Encapsulate(point);
            float halfX = points.Max(p => Mathf.Abs(Vector3.Dot(p - bounds.center, sampleCamera.transform.right))) + .6f;
            float halfY = points.Max(p => Mathf.Abs(Vector3.Dot(p - bounds.center, sampleCamera.transform.up))) + .8f;
            sampleCamera.orthographicSize = Mathf.Max(halfY, halfX / sampleCamera.aspect) * 1.06f;
            sampleCamera.transform.position = bounds.center - sampleCamera.transform.forward * 20;
            proof.Add("whole board warmth cells=" + warmth.Count + " bounds=" + bounds + " cameraSize=" + sampleCamera.orthographicSize);
        }
        private IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return null;
                if (capture && frame % 2 == 0) { Render(); File.WriteAllBytes(Path.Combine(output, (frame / 2).ToString("D4") + ".jpg"), pixels.EncodeToJPG(90)); }
                frame++;
            }
        }
        private void Render()
        {
            sampleCamera.Render(); var old = RenderTexture.active; RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(); RenderTexture.active = old;
        }
        private void Key(string name)
        {
            if (!capture) return;
            Render(); File.WriteAllBytes(Path.Combine(evidence, name + ".png"), pixels.EncodeToPNG());
            Check(FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled).All(r => r.sharedMaterials.All(m => m && m.shader.isSupported)), "materials supported");
        }
        private void Visibility(GameObject[] objects, string label)
        {
            if (!capture) return;
            var renderers = objects.SelectMany(go => go.GetComponentsInChildren<Renderer>()).Where(r => r.enabled).ToArray();
            Render(); var before = pixels.GetPixels32(); foreach (var r in renderers) r.enabled = false;
            Render(); var after = pixels.GetPixels32(); int count = Difference(before, after); foreach (var r in renderers) r.enabled = true;
            int minX=target.width, minY=target.height, maxX=-1, maxY=-1;
            for (int i=0; i<before.Length; i++)
                if (Math.Abs(before[i].r-after[i].r)+Math.Abs(before[i].g-after[i].g)+Math.Abs(before[i].b-after[i].b)>3)
                { int x=i%target.width, y=i/target.width; minX=Math.Min(minX,x); minY=Math.Min(minY,y); maxX=Math.Max(maxX,x); maxY=Math.Max(maxY,y); }
            proof.Add(label + " changedPixels=" + count + " finalDrawnPixelRect=(" + minX + "," + minY + ")..(" + maxX + "," + maxY +
                ") cameraSize=" + sampleCamera.orthographicSize); Check(count > 10, label + " is visible");
        }
        private void Invisible(GameObject[] objects, string label)
        {
            Render(); var before = pixels.GetPixels32(); foreach (var go in objects) go.SetActive(false);
            Render(); int count = Difference(before, pixels.GetPixels32());
            proof.Add(label + " changedPixels=" + count); Check(count == 0, label + " no residue");
        }
        private static int Difference(Color32[] a, Color32[] b)
        {
            int count = 0; for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)>3) count++;
            return count;
        }
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private void OnGUI() => GUI.Label(new Rect(24, 18, 900, 40), "D12.3 资源样例 · " + phase + " · 正式 Demo 尚未接线");
        private void OnDestroy()
        {
            if (capture) Time.captureDeltaTime = 0;
            if (sampleCamera) sampleCamera.targetTexture = null;
            if (target) { target.Release(); Destroy(target); } if (pixels) Destroy(pixels);
            foreach (var mesh in meshes) Destroy(mesh);
        }
    }
}
