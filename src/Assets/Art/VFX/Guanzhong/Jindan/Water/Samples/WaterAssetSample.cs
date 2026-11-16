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

namespace TianZhang.Art.Jindan.Water.Samples
{
    /// <summary>Standalone resource sample; only this scene binds new art to production settlement snapshots.</summary>
    public sealed class WaterAssetSample : MonoBehaviour
    {
        public CombatBattlefieldSurface surface;
        public Static3DCombatUnitPresentationProvider provider;
        public Camera sampleCamera;
        public GameObject wet, shallow, deep, dry, rise, ebb, steam, plantErode, plantSource;
        public GameObject[] staticWaterOverlays;
        public Material burningMaterial;
        public JindanDemoCatalogData catalog;
        public CombatRuleSetData rules;
        public EnvironmentProfileAsset environment;
        public AttackProfileData[] attacks;
        public bool capture;
        public int captureWidth = 1920;
        public bool Complete { get; private set; }
        public string Failure { get; private set; }
        private WaterSampleRules demo;
        private readonly Dictionary<HexCoord, GameObject> sheets = new();
        private readonly List<Mesh> meshes = new();
        private readonly List<Material> materialInstances = new();
        private readonly List<string> proof = new();
        private GameObject plant;
        private string output, evidence, phase;
        private int frame;
        private RenderTexture target;
        private Texture2D pixels;
        private float supportError;
        private int supportSamples;
        private float legacyInteriorError;

        private IEnumerator Start()
        {
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/d12-water-redesign/" + captureWidth));
            evidence = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../assets/generated-scene-art/guanzhong-jindan-demo/d12-ability-assets/water/evidence/redesign-20261117/" + captureWidth));
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
            demo = new WaterSampleRules(this); provider.ConfigureSurface(surface);
            Check(staticWaterOverlays.Length > 0 && staticWaterOverlays.All(go => go && !go.activeInHierarchy), "original static water overlays are disabled");
            proof.Add("original static water overlays disabled=" + staticWaterOverlays.Length);
            provider.Prepare(JindanRehearsalFactory.Descriptors(demo.Spawned, "combat_player_default_v1"));
            Focus(5);
            foreach (var state in demo.Session.Surfaces.Cells) SetState(state.Cell, state.Kind);
            plant = Instantiate(plantSource, Point(demo.PlantCell), Quaternion.identity);
            phase = "01-before"; yield return Frames(45); Key(phase); RecordState();
            Check(demo.Session.Surfaces.Get(demo.Source).Kind == CombatSurfaceKind.ShallowWater, "source starts shallow");
            var untouched = new HexCoord(4, 1);
            var untouchedKind = demo.Session.Surfaces.Get(untouched).Kind;
            yield return Tide(CombatTideAction.Rise, "03-first-rise", "02-first-rise-moving");
            Check(demo.Session.Surfaces.Get(demo.PlantCell).Kind == CombatSurfaceKind.Wet && !PlantDestroyed(), "first rise wet with plant retained");
            Check(plant != null, "visible plant retained after first rise");
            demo.NextTideTurn(); yield return Tide(CombatTideAction.Rise, "04-second-rise", "04-second-rise-moving");
            Check(demo.Session.Surfaces.Get(demo.PlantCell).Kind == CombatSurfaceKind.ShallowWater && PlantDestroyed(), "second rise shallow and plant eroded");
            Check(plant == null, "eroded plant fully removed");
            demo.NextTideTurn(); yield return Tide(CombatTideAction.Ebb, "05-first-ebb", "05-first-ebb-moving");
            Check(demo.Session.Surfaces.Get(demo.PlantCell).Kind == CombatSurfaceKind.Wet && demo.Session.Surfaces.Get(demo.Source).Kind == CombatSurfaceKind.ShallowWater, "first ebb restores one water step");
            demo.NextTideTurn(); yield return Tide(CombatTideAction.Ebb, "06-second-ebb", "06-second-ebb-moving");
            Check(demo.Session.Surfaces.Get(demo.PlantCell).Kind == CombatSurfaceKind.None && demo.Session.Surfaces.Get(demo.Source).Kind == CombatSurfaceKind.Wet, "second ebb dry neighbours and wet source");
            var before = demo.Session.Surfaces.Cells.ToDictionary(s => s.Cell, s => s.Kind);
            demo.Execute(demo.Close());
            var ended = demo.Session.Surfaces.Cells.Where(s => before[s.Cell] != s.Kind).Select(s => new CombatSurfaceChange(s.Cell, before[s.Cell], s.Kind)).ToArray();
            yield return Transition(ended, null, "07-terminated", "07-terminating");
            Check(demo.Session.Surfaces.Get(demo.Source).Kind == CombatSurfaceKind.None, "terminate never restores overwritten natural water");
            Check(demo.Session.Surfaces.Get(untouched).Kind == untouchedKind, "untouched natural water preserved");
            Check(PlantDestroyed() && plant == null, "ebb and termination do not regrow plant");
            // D3.2's N=12 curved ramp mesh has a <0.1mm analytic interpolation bound; flat and tread bands are exact.
            Check(supportError < .0001f, "surface and wave triangle interiors follow support within 0.1mm");
            proof.Add("support triangle-interior samples=" + supportSamples + " maxErrorMetres=" + supportError.ToString("G9"));
            proof.Add("original vertex-only stair adaptation interior errorMetres=" + legacyInteriorError.ToString("G9"));
            proof.Add("camera=38deg size5 primary plus size3 closeups; Unity=" + Application.unityVersion);
            proof.Add("Only sample calls production CombatCommandService; Adventure/HUD resource binding remains pending.");
        }

        private IEnumerator Tide(CombatTideAction action, string settled, string moving)
        {
            var command = demo.Tide(action); var quote = demo.Commands.Preview(demo.Session, command);
            Check(quote.IsLegal, quote.RejectionReason);
            var changes = quote.Tide.Changes.ToArray();
            proof.Add(settled + " frozen=" + string.Join(";", changes.Select(c => c.Cell + ":" + c.Before + "->" + c.After)));
            demo.Execute(command);
            var receipt = demo.Session.Surfaces.Settlements.Last(s => s.SourceRef == demo.Session.Jindan.Seats.Single(s => s.Definition.ActorId == WaterSampleRules.WaterActor).Definition.AbilityInstanceId);
            Check(receipt.Changes.SequenceEqual(changes), "receipt matches frozen changed set");
            foreach (var push in receipt.Pushes)
            {
                proof.Add(settled + " push " + push.ActorId + " " + push.Movement.Origin.Anchor + "->" + push.Movement.Destination.Anchor + " direction=" + push.Direction + " reason=" + push.Movement.Reason);
                if (push.Movement.Path.Count == 0) continue;
                demo.Session.Combatants.TryGet(push.ActorId, out var actor);
                var previous = new CombatUnitPresentationPose(Hex(push.Movement.Origin.Anchor), actor.Facing);
                var steps = new List<CombatUnitPresentationMovementStep>();
                foreach (var pose in push.Movement.Path)
                {
                    var next = new CombatUnitPresentationPose(Hex(pose.Anchor), actor.Facing);
                    steps.Add(new CombatUnitPresentationMovementStep(previous, next, isForced: true)); previous = next;
                }
                provider.Present(new CombatUnitPresentationEventProjection(actor.Id, CombatUnitPresentationEvent.Move,
                    Hex(push.Movement.Origin.Anchor), Hex(push.Movement.Destination.Anchor), actor.Facing, null,
                    push.Movement.Path.Select(p => Hex(p.Anchor)).ToArray(), movementSteps: steps));
            }
            yield return Transition(changes, action, settled, moving);
            Check(!provider.IsPresenting, "push presentation finished before settled keyframe");
            foreach (var actor in demo.Session.Combatants.All)
                if (provider.TryGetPresentedUnit(actor.Id, out var unit))
                    Check(Vector3.Distance(unit.transform.position, Point(actor.Position)) < .02f, "presented unit at committed landing");
        }

        private IEnumerator Transition(CombatSurfaceChange[] changes, CombatTideAction? action, string settled, string moving)
        {
            var effects = new List<GameObject>();
            foreach (var change in changes)
            {
                if (!sheets.ContainsKey(change.Cell)) sheets[change.Cell] = Place(wet, change.Cell, .018f);
                if (action.HasValue)
                {
                    var wave = Place(action == CombatTideAction.Rise ? rise : ebb, change.Cell, .023f); effects.Add(wave);
                    Vector3 direction = Point(change.Cell) - Point(demo.Source); direction.y = 0;
                    if (direction.sqrMagnitude < .001f) direction = Vector3.right;
                    direction.Normalize(); if (action == CombatTideAction.Ebb) direction = -direction;
                    foreach (var spray in wave.GetComponentsInChildren<ParticleSystem>())
                        spray.transform.rotation = Quaternion.LookRotation(Vector3.up + direction * .5f);
                    var props = new MaterialPropertyBlock(); props.SetVector("_Flow", new Vector4(direction.x, direction.z, 0, 0));
                    foreach (var renderer in wave.GetComponentsInChildren<Renderer>()) renderer.SetPropertyBlock(props);
                }
                if (change.Before == CombatSurfaceKind.Burning && change.After == CombatSurfaceKind.Wet)
                    effects.Add(Instantiate(steam, Point(change.Cell) + Vector3.up * .04f, Quaternion.identity));
                if (change.After == CombatSurfaceKind.None)
                {
                    Destroy(sheets[change.Cell]); sheets[change.Cell] = Place(dry, change.Cell, .018f);
                }
            }
            if (PlantDestroyed() && plant != null)
            {
                var erode = Instantiate(plantErode, plant.transform.position, plant.transform.rotation); effects.Add(erode);
                Destroy(plant); plant = null;
            }
            phase = moving;
            for (int f = 0; f < 39; f++)
            {
                float t = (f + 1) / 39f;
                foreach (var c in changes.Where(c => c.After != CombatSurfaceKind.None))
                {
                    var renderer = sheets[c.Cell].GetComponentInChildren<Renderer>();
                    if (f == 0) renderer.sharedMaterial = NewMaterial(MaterialFor(c.After));
                    var m = renderer.sharedMaterial;
                    m.SetFloat("_Depth", Mathf.Lerp(Depth(c.Before), Depth(c.After), t));
                    m.SetFloat("_Opacity", c.Before == CombatSurfaceKind.None ? t : 1);
                    m.SetColor("_BaseColor", Color.Lerp(MaterialFor(c.Before).GetColor("_BaseColor"), MaterialFor(c.After).GetColor("_BaseColor"), t));
                    if (c.Before == CombatSurfaceKind.Burning) m.SetColor("_MudColor", Color.Lerp(new Color(.92f, .27f, .055f), MaterialFor(c.After).GetColor("_MudColor"), t));
                }
                yield return Frames(1);
                if (f == 16)
                {
                    Key(moving); ParticleVisibility(effects, moving); FrontVisibility(effects, moving);
                    Focus(3); Key(moving + "-closeup"); Focus(5);
                }
            }
            yield return Frames(40);
            foreach (var effect in effects)
                foreach (var ps in effect.GetComponentsInChildren<ParticleSystem>()) Check(!ps.IsAlive(), "steam ended");
            if (capture) VerifyInvisible(effects.Where(e => e.GetComponent<Animation>() != null).Concat(changes.Where(c => c.After == CombatSurfaceKind.None).Select(c => sheets[c.Cell])).ToArray());
            foreach (var effect in effects) Destroy(effect);
            foreach (var c in changes) SetState(c.Cell, c.After);
            phase = settled; yield return Frames(50); Key(settled); RecordState();
            Key(settled + "-default-zoom"); Focus(3); Key(settled + "-closeup"); Focus(5);
        }

        private bool PlantDestroyed() => demo.Session.SceneObjects.Find("LowPlant_4_6").IsDestroyed;
        private void RecordState()
        {
            proof.Add(phase + " tick=" + demo.Session.TurnScheduler.CurrentTick + " source=" + demo.Session.Surfaces.Get(demo.Source).Kind +
                " plantSurface=" + demo.Session.Surfaces.Get(demo.PlantCell).Kind + " plantDurability=" + demo.Session.SceneObjects.Find("LowPlant_4_6").Durability +
                " fuel=" + demo.Session.Surfaces.Get(demo.PlantCell).Fuel + " visiblePlant=" + (plant != null));
        }
        private static float Depth(CombatSurfaceKind kind) => kind == CombatSurfaceKind.DeepWater ? 2 : kind == CombatSurfaceKind.ShallowWater ? 1 : 0;
        private Material MaterialFor(CombatSurfaceKind kind) => (kind == CombatSurfaceKind.DeepWater ? deep : kind == CombatSurfaceKind.ShallowWater ? shallow : wet).GetComponentInChildren<Renderer>().sharedMaterial;
        private Material NewMaterial(Material source) { var m = new Material(source); materialInstances.Add(m); return m; }
        private void SetState(HexCoord cell, CombatSurfaceKind kind)
        {
            if (sheets.TryGetValue(cell, out var old)) { Destroy(old); sheets.Remove(cell); }
            if (kind == CombatSurfaceKind.None) return;
            var go = Place(kind == CombatSurfaceKind.DeepWater ? deep : kind == CombatSurfaceKind.ShallowWater ? shallow : wet, cell, .018f);
            go.name = "ResourceSurface_" + cell + "_" + kind;
            if (kind == CombatSurfaceKind.Burning) go.GetComponentInChildren<Renderer>().sharedMaterial = burningMaterial;
            sheets[cell] = go;
        }
        private GameObject Place(GameObject prefab, HexCoord cell, float lift)
        {
            var go = Instantiate(prefab, Point(cell), Quaternion.identity);
            var support = surface.Cells.Single(c => c.q == cell.Q && c.r == cell.R);
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
            {
                if (support.kind == 2 && legacyInteriorError == 0)
                {
                    var original = Instantiate(filter.sharedMesh); var points = original.vertices;
                    for (int i = 0; i < points.Length; i++)
                    {
                        Vector3 p = filter.transform.TransformPoint(points[i]); p.y = surface.Height(support, p.z) + lift;
                        points[i] = filter.transform.InverseTransformPoint(p);
                    }
                    original.vertices = points;
                    legacyInteriorError = WaterSampleSupport.InteriorError(original, filter.transform, surface, support, lift, out _);
                    proof.Add("stair source=" + filter.sharedMesh.name + " vertices=" + filter.sharedMesh.vertexCount + " cell=" + cell +
                        " root=" + go.transform.position.ToString("F5") + " bands=" + support.riserZ.Length + " lift=" + lift);
                    Destroy(original);
                }
                bool front = prefab == rise || prefab == ebb;
                var mesh = WaterSampleSupport.Fit(filter.sharedMesh, filter.transform, surface, support, lift, front ? 1 : 12); meshes.Add(mesh);
                float error = WaterSampleSupport.InteriorError(mesh, filter.transform, surface, support, lift, out int count);
                if (error > .0001f) proof.Add("SUPPORT_PROBE cell=" + cell + " kind=" + support.kind + " lower=" + support.lowerY + " upper=" + support.upperY +
                    " bands=" + string.Join(",", support.riserZ) + " heights=" + string.Join(",", support.riserY) + " error=" + error + " root=" + go.transform.position + " mesh=" + mesh.bounds);
                supportError = Mathf.Max(supportError, error);
                supportSamples += count;
                if (front) { var bounds = mesh.bounds; bounds.Expand(new Vector3(.3f, .6f, .3f)); mesh.bounds = bounds; }
                filter.sharedMesh = mesh;
            }
            return go;
        }
        private static CombatUnitPresentationHex Hex(HexCoord cell) => new CombatUnitPresentationHex(cell.Q, cell.R);
        private Vector3 Point(HexCoord cell) => surface.Center(Hex(cell));
        private void Focus(float size)
        {
            sampleCamera.transform.rotation = Quaternion.Euler(38, 0, 0); sampleCamera.orthographic = true; sampleCamera.orthographicSize = size;
            sampleCamera.transform.position = Point(demo.Source) + Vector3.up * .4f - sampleCamera.transform.forward * 12;
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
            sampleCamera.Render(); var previous = RenderTexture.active; RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(); RenderTexture.active = previous;
        }
        private void Key(string name)
        {
            if (!capture) return;
            Render(); File.WriteAllBytes(Path.Combine(evidence, name + ".png"), pixels.EncodeToPNG());
            Check(FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled).All(r => r.sharedMaterials.All(m => m && m.shader.isSupported)), "no missing or unsupported materials");
        }
        private void ParticleVisibility(List<GameObject> effects, string name)
        {
            if (!capture) return;
            var renderers = effects.Where(e => e.name.StartsWith("ExtinguishSteam", StringComparison.Ordinal)).SelectMany(e => e.GetComponentsInChildren<ParticleSystemRenderer>()).ToArray();
            if (renderers.Length == 0) return;
            Render(); var visible = pixels.GetPixels32(); foreach (var r in renderers) r.enabled = false;
            Render(); int changed = Difference(visible, pixels.GetPixels32()); foreach (var r in renderers) r.enabled = true;
            proof.Add(name + " steam-only changedPixels=" + changed); Check(changed > 10, "steam rendered visibly");
        }
        private void VerifyInvisible(GameObject[] effects)
        {
            Render(); var before = pixels.GetPixels32(); foreach (var e in effects) e.SetActive(false);
            Render(); int changed = Difference(before, pixels.GetPixels32());
            proof.Add(phase + " expired-effects changedPixels=" + changed); Check(changed == 0, "no faded front/plant/dry residual");
        }
        private void FrontVisibility(List<GameObject> effects, string name)
        {
            if (!capture) return;
            var renderers = effects.Where(e => e.name.StartsWith("Tide", StringComparison.Ordinal)).SelectMany(e => e.GetComponentsInChildren<Renderer>()).ToArray();
            if (renderers.Length == 0) return;
            Render(); var visible = pixels.GetPixels32(); foreach (var r in renderers) r.enabled = false;
            Render(); var hidden = pixels.GetPixels32(); int changed = Difference(visible, hidden); foreach (var r in renderers) r.enabled = true;
            proof.Add(name + " front-only changedPixels=" + changed); Check(changed > 25, "travelling foam rendered visibly");
            int minX=target.width, minY=target.height, maxX=-1, maxY=-1;
            for (int i=0; i<visible.Length; i++)
                if (Math.Abs(visible[i].r-hidden[i].r)+Math.Abs(visible[i].g-hidden[i].g)+Math.Abs(visible[i].b-hidden[i].b)>3)
                { int x=i%target.width, y=i/target.width; minX=Math.Min(minX,x); minY=Math.Min(minY,y); maxX=Math.Max(maxX,x); maxY=Math.Max(maxY,y); }
            var sample = renderers.OfType<MeshRenderer>().First();
            var properties = new MaterialPropertyBlock(); sample.GetPropertyBlock(properties);
            var b = sample.bounds;
            Vector3 screenMin = new Vector3(float.MaxValue,float.MaxValue,0), screenMax = new Vector3(float.MinValue,float.MinValue,0);
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            {
                var p = sampleCamera.WorldToScreenPoint(b.center + Vector3.Scale(b.extents,new Vector3(x,y,z)));
                screenMin=Vector3.Min(screenMin,p); screenMax=Vector3.Max(screenMax,p);
            }
            proof.Add(name + " owner=WaterAssetSample.Transition->" + sample.transform.root.name + "/" + sample.name +
                " shader=" + sample.sharedMaterial.shader.name + " mesh=" + sample.GetComponent<MeshFilter>().sharedMesh.name +
                " cullingWorldBounds=" + b + " projectedCullingBounds=" + screenMin.ToString("F2") + ".." + screenMax.ToString("F2") +
                " drawnFrontPixelRect=(" + minX + "," + minY + ")..(" + maxX + "," + maxY + ")" +
                " cameraSize=" + sampleCamera.orthographicSize + " progress=" + properties.GetFloat("_Progress") + " flow=" + properties.GetVector("_Flow") +
                " writer=LegacyAnimation + _Flow PropertyBlock; shaderMaxCrest=.29m");
        }
        private static int Difference(Color32[] a, Color32[] b)
        {
            int count = 0; for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) > 3) count++;
            return count;
        }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private void OnGUI() => GUI.Label(new Rect(24, 18, 900, 40), "D12.2 资源样例 · " + phase + " · 正式 Demo 尚未接线");
        private void OnDestroy()
        {
            if (capture) Time.captureDeltaTime = 0;
            if (sampleCamera) sampleCamera.targetTexture = null;
            if (target) { target.Release(); Destroy(target); } if (pixels) Destroy(pixels);
            foreach (var m in meshes) Destroy(m); foreach (var m in materialInstances) Destroy(m);
        }
    }
}
