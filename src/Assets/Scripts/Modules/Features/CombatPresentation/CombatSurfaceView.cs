using System;
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
        [SerializeField] private GameObject surfacePrefab;
        [SerializeField] private Material groundMaterial;
        [SerializeField] private Material lineMaterial;
        [SerializeField] private GameObject[] staticWaterOverlays = Array.Empty<GameObject>();
        private CombatBattlefieldSurface surface;
        private readonly Dictionary<CombatUnitPresentationHex, GameObject> sheets = new();
        private readonly List<Mesh> meshes = new();
        private readonly List<LineRenderer> previewLines = new();
        private string signature;
        private bool active;

        public void ConfigureSurface(CombatBattlefieldSurface value) { surface = value; signature = null; }

        public void Present(CombatSurfacePresentationSnapshot snapshot)
        {
            if (snapshot == null) { Clear(); return; }
            if (surface == null) throw new InvalidOperationException("surface_presentation_support_missing");
            string next = snapshot.Revision + ":" + string.Join(";", snapshot.Changes.Select(Hex)) + ":" +
                string.Join(";", snapshot.Pushes.Select(p => p.ActorId + Hex(p.Origin) + Hex(p.Destination) + p.Direction + p.Reason)) +
                ":" + snapshot.Source + ":" + snapshot.WaterlineHeight;
            if (active && signature == next) return;
            active = true;
            foreach (var original in staticWaterOverlays) original.SetActive(false);
            foreach (var cell in snapshot.Cells)
            {
                if (cell.Kind == CombatSurfaceDisplayKind.None)
                { if (sheets.TryGetValue(cell.Cell, out var dry)) dry.SetActive(false); continue; }
                if (!sheets.TryGetValue(cell.Cell, out var sheet)) sheet = CreateSheet(cell.Cell);
                sheet.SetActive(true);
                bool water = cell.Kind is CombatSurfaceDisplayKind.ShallowWater or CombatSurfaceDisplayKind.DeepWater;
                foreach (var renderer in sheet.GetComponentsInChildren<Renderer>())
                {
                    renderer.sharedMaterial = water ? surfacePrefab.GetComponentInChildren<Renderer>().sharedMaterial : groundMaterial;
                    var properties = new MaterialPropertyBlock();
                    properties.SetColor("_BaseColor", ColorFor(cell.Kind));
                    renderer.SetPropertyBlock(properties);
                }
                sheet.name = "Surface_" + Hex(cell.Cell) + "_" + cell.Kind;
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

        private GameObject CreateSheet(CombatUnitPresentationHex cell)
        {
            var support = surface.Cells.Single(value => value.q == cell.Q && value.r == cell.R);
            var sheet = Instantiate(surfacePrefab, transform);
            sheet.transform.position = surface.Center(cell);
            foreach (var filter in sheet.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = Instantiate(filter.sharedMesh); meshes.Add(mesh);
                var vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 world = filter.transform.TransformPoint(vertices[i]);
                    // The approved D3 water sheet follows the actual support, including ramp/stair surfaces.
                    world.y = surface.Height(support, world.z) + .018f;
                    vertices[i] = filter.transform.InverseTransformPoint(world);
                }
                mesh.vertices = vertices; mesh.RecalculateNormals(); mesh.RecalculateBounds(); filter.sharedMesh = mesh;
            }
            sheets.Add(cell, sheet); return sheet;
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
            foreach (var sheet in sheets.Values) if (sheet != null) sheet.SetActive(false);
            foreach (var line in previewLines) if (line != null) line.gameObject.SetActive(false);
            foreach (var original in staticWaterOverlays) if (original != null) original.SetActive(true);
            active = false; signature = null;
        }
        private void OnDisable() => Clear();
        private void OnDestroy() { foreach (var mesh in meshes) Destroy(mesh); }
        private static string Hex(CombatUnitPresentationHex cell) => cell.Q + "_" + cell.R;
        private static Color ColorFor(CombatSurfaceDisplayKind kind) => kind switch
        {
            CombatSurfaceDisplayKind.Scorched => new Color(.12f, .095f, .065f),
            CombatSurfaceDisplayKind.Burning => new Color(.92f, .27f, .055f),
            CombatSurfaceDisplayKind.Wet => new Color(.25f, .26f, .17f),
            CombatSurfaceDisplayKind.ShallowWater => new Color(.22f, .48f, .43f),
            CombatSurfaceDisplayKind.DeepWater => new Color(.075f, .25f, .36f),
            _ => Color.white,
        };
    }
}
