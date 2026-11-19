using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.Rendering;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>Projects the unique surface slot and the same frozen tide quote used by Combat.</summary>
    public sealed class CombatSurfaceView : MonoBehaviour
    {
        [SerializeField] private GameObject wetMudPrefab, shallowWaterPrefab, deepWaterPrefab, wetToDryPrefab;
        [SerializeField] private GameObject tideRisePrefab, tideEbbPrefab, steamPrefab, erodePrefab;
        [SerializeField] private GameObject lowFirePrefab, scorchedPrefab;
        [SerializeField] private CombatSceneObjectView sceneObjects;
        [SerializeField] private Material lineMaterial;
        [SerializeField] private GameObject[] staticWaterOverlays = Array.Empty<GameObject>();
        private CombatBattlefieldSurface surface;
        private readonly Dictionary<CombatUnitPresentationHex, GameObject> sheets = new();
        private readonly Dictionary<CombatUnitPresentationHex, CombatSurfaceDisplayKind> kinds = new();
        private readonly Dictionary<(CombatUnitPresentationHex, Mesh, float), Mesh> meshes = new();
        private readonly List<GameObject> transients = new();
        private readonly Dictionary<CombatUnitPresentationHex, GameObject> drying = new();
        private readonly List<LineRenderer> previewLines = new();
        private string signature;
        private bool active;

        public void ConfigureSurface(CombatBattlefieldSurface value) { surface = value; signature = null; }

        public void Present(CombatSurfacePresentationSnapshot snapshot, CombatUnitPresentationHex[] tailCells = null)
        {
            if (snapshot == null) { Clear(); return; }
            if (surface == null) throw new InvalidOperationException("surface_presentation_support_missing");
            string next = snapshot.Revision + ":" + string.Join(";", snapshot.Changes.Select(Hex)) + ":" +
                string.Join(";", snapshot.Pushes.Select(p => p.ActorId + Hex(p.Origin) + Hex(p.Destination) + p.Direction + p.Reason)) +
                ":" + (snapshot.Source.HasValue ? Hex(snapshot.Source.Value) : "") + ":" + snapshot.WaterlineHeight + ":" +
                string.Join(";", (tailCells ?? Array.Empty<CombatUnitPresentationHex>()).Select(Hex));
            if (active && signature == next) return;
            active = true;
            foreach (var original in staticWaterOverlays) original.SetActive(false);
            foreach (var cell in snapshot.Cells)
            {
                var sheet = SetState(cell.Cell, cell.Kind);
                if (cell.Kind != CombatSurfaceDisplayKind.Burning) continue;
                var properties = new MaterialPropertyBlock();
                properties.SetFloat("_Opacity", 1f);
                properties.SetFloat("_Strength", tailCells != null && Array.IndexOf(tailCells, cell.Cell) >= 0 ? .65f : 1f);
                sheet.GetComponentInChildren<MeshRenderer>().SetPropertyBlock(properties);
            }
            int used = 0;
            foreach (var cell in snapshot.Changes) Draw(ref used, Border(cell, .035f), new Color(.2f, .95f, .94f), true);
            foreach (var push in snapshot.Pushes)
            {
                Vector3 start = surface.Center(push.Origin), end = surface.Center(push.Destination);
                bool moved = !push.Origin.Equals(push.Destination);
                if (!moved)
                {
                    var directions = new[] { new Vector2Int(1,0),new Vector2Int(1,-1),new Vector2Int(0,-1),
                        new Vector2Int(-1,0),new Vector2Int(-1,1),new Vector2Int(0,1) };
                    Vector2Int d = push.Direction >= 0 ? directions[push.Direction] : Vector2Int.zero;
                    end = start + (CombatBattlefieldSurface.Horizontal(d.x, d.y) - CombatBattlefieldSurface.Horizontal(0,0)) * .3f;
                }
                if (!moved)
                {
                    // A cross marks the actual stopped landing, including a unit already at its tide source.
                    var center = start + Vector3.up * .10f;
                    var left = new Vector3(.14f, 0, .14f);
                    var right = new Vector3(.14f, 0, -.14f);
                    Draw(ref used, new[] { center - left, center + left }, new Color(1, .35f, .2f));
                    Draw(ref used, new[] { center - right, center + right }, new Color(1, .35f, .2f));
                    if (push.Direction < 0) continue;
                }
                Vector3 direction = (end - start).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, direction) * .12f;
                start.y += .08f; end.y += .08f;
                Draw(ref used, new[] { start, end, end - direction * .18f + side, end,
                    end - direction * .18f - side }, moved ? new Color(.45f, .95f, 1) : new Color(1, .35f, .2f));
            }
            if (snapshot.Source.HasValue)
            {
                Vector3 bottom = surface.Center(snapshot.Source.Value);
                Vector3 top = bottom + Vector3.up * (.75f * (snapshot.WaterlineHeight - snapshot.SourceHeight));
                Draw(ref used, new[] { bottom + Vector3.up * .06f, top, top + Vector3.right * .24f }, new Color(.4f, .85f, 1));
            }
            for (int i = used; i < previewLines.Count; i++) previewLines[i].gameObject.SetActive(false);
            signature = next;
        }

        private GameObject Prefab(CombatSurfaceDisplayKind kind) => kind switch
        {
            CombatSurfaceDisplayKind.Wet => wetMudPrefab,
            CombatSurfaceDisplayKind.ShallowWater => shallowWaterPrefab,
            CombatSurfaceDisplayKind.DeepWater => deepWaterPrefab,
            CombatSurfaceDisplayKind.Scorched => scorchedPrefab,
            CombatSurfaceDisplayKind.Burning => lowFirePrefab,
            _ => null
        };

        private GameObject SetState(CombatUnitPresentationHex cell, CombatSurfaceDisplayKind kind)
        {
            if (kinds.TryGetValue(cell, out var current) && current == kind) return sheets[cell];
            if (sheets.Remove(cell, out var old)) { old.SetActive(false); Destroy(old); kinds.Remove(cell); }
            if (kind == CombatSurfaceDisplayKind.None) return null;
            if (drying.Remove(cell, out var dry) && dry != null) { dry.SetActive(false); Destroy(dry); }
            var sheet = kind == CombatSurfaceDisplayKind.Burning
                ? Instantiate(lowFirePrefab, surface.Center(cell) + Vector3.up * .025f, Quaternion.identity, transform)
                : PlaceSheet(Prefab(kind), cell, .018f);
            sheet.name = "Surface_" + Hex(cell) + "_" + kind;
            kinds.Add(cell, kind);
            sheets.Add(cell, sheet);
            return sheet;
        }

        private GameObject PlaceSheet(GameObject prefab, CombatUnitPresentationHex cell, float lift, bool front = false)
        {
            var support = surface.Cells.Single(value => value.q == cell.Q && value.r == cell.R);
            var sheet = Instantiate(prefab, surface.Center(cell), Quaternion.identity, transform);
            foreach (var filter in sheet.GetComponentsInChildren<MeshFilter>())
            {
                var key = (cell, filter.sharedMesh, lift);
                if (!meshes.TryGetValue(key, out var mesh))
                {
                    mesh = CombatSurfaceMesh.Fit(filter.sharedMesh, filter.transform, surface, support, lift, front ? 1 : 12);
                    if (front)
                    {
                        var bounds = mesh.bounds;
                        bounds.Expand(new Vector3(.3f, .6f, .3f));
                        mesh.bounds = bounds;
                    }
                    meshes.Add(key, mesh);
                }
                filter.sharedMesh = mesh;
            }
            return sheet;
        }

        public void PlaySettlement(CombatSurfaceSettlementSnapshot settlement)
        {
            if (surface == null) return;
            active = true;
            // Present reconciles against the latest full snapshot even if several receipts ran in one frame.
            signature = null;
            transients.RemoveAll(value => value == null);
            foreach (var change in settlement.Changes)
            {
                var sheet = SetState(change.Cell, change.After);
                if (settlement.TideSource.HasValue)
                {
                    Vector3 flow = surface.Center(change.Cell) - surface.Center(settlement.TideSource.Value);
                    flow.y = 0;
                    flow = flow.sqrMagnitude < .001f ? Vector3.right : flow.normalized;
                    if (!settlement.Rising) flow = -flow;
                    var front = PlaceSheet(settlement.Rising ? tideRisePrefab : tideEbbPrefab, change.Cell, .023f, true);
                    foreach (var spray in front.GetComponentsInChildren<ParticleSystem>())
                        spray.transform.rotation = Quaternion.LookRotation(Vector3.up + flow * .5f);
                    var properties = new MaterialPropertyBlock();
                    properties.SetVector("_Flow", new Vector4(flow.x, flow.z, 0, 0));
                    foreach (var renderer in front.GetComponentsInChildren<MeshRenderer>()) renderer.SetPropertyBlock(properties);
                    Expire(front);
                }
                if (change.Before == CombatSurfaceDisplayKind.Burning && change.After == CombatSurfaceDisplayKind.Wet)
                    Expire(Instantiate(steamPrefab, surface.Center(change.Cell) + Vector3.up * .04f, Quaternion.identity, transform));
                if (change.After is CombatSurfaceDisplayKind.Wet or CombatSurfaceDisplayKind.ShallowWater or CombatSurfaceDisplayKind.DeepWater)
                    StartCoroutine(BlendWater(sheet.GetComponentInChildren<MeshRenderer>(), change.Before));
                else if (change.After == CombatSurfaceDisplayKind.None && change.Before >= CombatSurfaceDisplayKind.Wet)
                {
                    var dry = PlaceSheet(wetToDryPrefab, change.Cell, .018f);
                    drying[change.Cell] = dry;
                    Expire(dry);
                }
            }
            foreach (string id in settlement.ErodedPlants)
            {
                var plant = sceneObjects.ErodePlant(id);
                if (plant == null) continue;
                var eroded = Instantiate(erodePrefab, plant.position, plant.rotation, transform);
                eroded.transform.localScale = plant.lossyScale;
                Expire(eroded);
            }
        }

        private void Expire(GameObject effect)
        {
            transients.Add(effect);
            Destroy(effect, 1.2f);
        }

        private IEnumerator BlendWater(MeshRenderer renderer, CombatSurfaceDisplayKind before)
        {
            var to = renderer.sharedMaterial;
            var start = before >= CombatSurfaceDisplayKind.Wet ? Prefab(before) : wetMudPrefab;
            var from = start.GetComponentInChildren<MeshRenderer>().sharedMaterial;
            Color fromMud = before == CombatSurfaceDisplayKind.Burning ? new Color(.92f, .27f, .055f) : from.GetColor("_MudColor");
            var properties = new MaterialPropertyBlock();
            for (float elapsed = 0; elapsed < .65f && renderer != null && renderer.gameObject.activeInHierarchy; elapsed += Time.deltaTime)
            {
                float t = elapsed / .65f;
                properties.SetFloat("_Depth", Mathf.Lerp(from.GetFloat("_Depth"), to.GetFloat("_Depth"), t));
                properties.SetFloat("_Opacity", before == CombatSurfaceDisplayKind.None ? t : 1f);
                properties.SetColor("_BaseColor", Color.Lerp(from.GetColor("_BaseColor"), to.GetColor("_BaseColor"), t));
                properties.SetColor("_MudColor", Color.Lerp(fromMud, to.GetColor("_MudColor"), t));
                renderer.SetPropertyBlock(properties);
                yield return null;
            }
            if (renderer != null) renderer.SetPropertyBlock(null);
        }

        private Vector3[] Border(CombatUnitPresentationHex cell, float lift)
        {
            var support = surface.Cells.Single(value => value.q == cell.Q && value.r == cell.R);
            var center = surface.Center(cell); var points = new Vector3[6];
            for (int i = 0; i < 6; i++)
            {
                float angle = (30 + i * 60) * Mathf.Deg2Rad;
                var p = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * .56f;
                p.y = surface.Height(support, p.z);
                points[i] = p + Vector3.up * lift;
            }
            return points;
        }

        private void Draw(ref int used, Vector3[] points, Color color, bool loop = false)
        {
            if (used == previewLines.Count)
            {
                var obj = new GameObject("TideQuoteLine"); obj.transform.SetParent(transform, false);
                var renderer = obj.AddComponent<LineRenderer>(); renderer.sharedMaterial = lineMaterial;
                renderer.widthMultiplier = .035f; renderer.useWorldSpace = true;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                previewLines.Add(renderer);
            }
            var line = previewLines[used++]; line.gameObject.SetActive(true); line.loop = loop;
            line.positionCount = points.Length; line.SetPositions(points);
            var properties = new MaterialPropertyBlock(); properties.SetColor("_BaseColor", color); line.SetPropertyBlock(properties);
        }

        private void Clear()
        {
            if (!active) return;
            StopAllCoroutines();
            foreach (var obj in sheets.Values.Concat(transients))
                if (obj != null) { obj.SetActive(false); Destroy(obj); }
            sheets.Clear(); kinds.Clear(); drying.Clear(); transients.Clear();
            sceneObjects.ClearErosion();
            foreach (var mesh in meshes.Values) Destroy(mesh);
            meshes.Clear();
            foreach (var line in previewLines) if (line != null) line.gameObject.SetActive(false);
            foreach (var original in staticWaterOverlays) if (original != null) original.SetActive(true);
            active = false; signature = null;
        }
        private void OnDisable() => Clear();
        private static string Hex(CombatUnitPresentationHex cell) => cell.Q + "_" + cell.R;
    }
}
