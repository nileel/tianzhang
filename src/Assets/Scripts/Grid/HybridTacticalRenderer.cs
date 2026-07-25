using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Core;
using UnityEngine;

namespace TianZhang.Tactical
{
    [DisallowMultipleComponent]
    public sealed class HybridTacticalRenderer : MonoBehaviour, ITacticalRenderer
    {
        private const float HexCornerOffsetDegrees = 30f;

        [SerializeField] private Camera presentationCamera;
        [SerializeField] private float tileRadius = 1f;
        [SerializeField] private float tileThickness = 0.22f;
        [SerializeField] private float heightStep = 0.35f;
        [SerializeField] private float raycastDistance = 100f;

        private readonly Dictionary<HexCoord, TileView> tileViews = new Dictionary<HexCoord, TileView>();
        private readonly Dictionary<int, HexCoord> colliderBindings = new Dictionary<int, HexCoord>();
        private readonly Dictionary<HexCoord, GameObject> unitMarkers = new Dictionary<HexCoord, GameObject>();
        private readonly List<Transform> billboardTransforms = new List<Transform>();
        private readonly List<Mesh> generatedMeshes = new List<Mesh>();

        private Material terrainMaterial;
        private Texture2D unitTexture;
        private Sprite unitSprite;

        public TacticalGridModel Model { get; private set; }
        public Camera PresentationCamera => presentationCamera;
        public int VisualTileCount => tileViews.Count;

        public void SetPresentationCamera(Camera camera)
        {
            presentationCamera = camera;
        }

        public void RenderGrid(TacticalGridModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            ClearVisuals();
            Model = model;
            foreach (var tile in model.Tiles.OrderBy(value => value.Coord.q).ThenBy(value => value.Coord.r))
                CreateTileVisual(tile);

            foreach (var tile in model.Tiles.Where(value => value.IsOccupied))
                PlaceUnitMarker(tile.Coord, new Color(0.92f, 0.82f, 0.35f), "Unit_" + tile.OccupiedUnitId);
        }

        public HexCoord ScreenToHex(Vector3 screenPosition)
        {
            return TryScreenToHex(screenPosition, out var coord) ? coord : default;
        }

        public bool TryScreenToHex(Vector3 screenPosition, out HexCoord coord)
        {
            coord = default;
            var camera = presentationCamera != null ? presentationCamera : Camera.main;
            return camera != null && TryRaycastToHex(camera.ScreenPointToRay(screenPosition), out coord);
        }

        public bool TryRaycastToHex(Ray ray, out HexCoord coord)
        {
            coord = default;
            foreach (var hit in Physics.RaycastAll(ray, raycastDistance, ~0, QueryTriggerInteraction.Ignore)
                         .OrderBy(value => value.distance))
            {
                if (TryGetBoundHex(hit.collider, out coord))
                    return true;
            }

            return false;
        }

        public bool TryGetBoundHex(Collider collider, out HexCoord coord)
        {
            coord = default;
            return collider != null && colliderBindings.TryGetValue(collider.GetInstanceID(), out coord);
        }

        public bool TryGetTileCollider(HexCoord coord, out Collider collider)
        {
            collider = null;
            if (!tileViews.TryGetValue(coord, out var view))
                return false;

            collider = view.Collider;
            return collider != null;
        }

        public Vector3 HexToWorld(HexCoord coord)
        {
            float x = Mathf.Sqrt(3f) * tileRadius * (coord.q + coord.r * 0.5f);
            float z = 1.5f * tileRadius * coord.r;
            int heightLevel = Model != null && Model.TryGetTile(coord, out var tile) ? tile.HeightLevel : 0;
            return new Vector3(x, heightLevel * heightStep, z);
        }

        public void HighlightMoveRange(IEnumerable<HexCoord> tiles)
        {
            ApplyHighlight(tiles, new Color(0.25f, 0.8f, 0.4f));
        }

        public void HighlightAttackRange(IEnumerable<HexCoord> tiles)
        {
            ApplyHighlight(tiles, new Color(0.9f, 0.32f, 0.28f));
        }

        public void HighlightSelected(HexCoord coord)
        {
            if (tileViews.TryGetValue(coord, out var view))
                SetTileColor(view, new Color(1f, 0.82f, 0.25f));
        }

        public void ClearOverlay()
        {
            foreach (var view in tileViews.Values)
                SetTileColor(view, view.BaseColor);
        }

        public GameObject PlaceUnitMarker(HexCoord coord, Color color, string label)
        {
            if (Model == null || !Model.Contains(coord))
                throw new ArgumentException("Unit markers must bind to an existing tactical tile.", nameof(coord));

            if (unitMarkers.TryGetValue(coord, out var existing))
                DestroyUnityObject(existing);

            var marker = new GameObject(string.IsNullOrWhiteSpace(label) ? "HybridUnit" : label);
            marker.transform.SetParent(transform, false);
            marker.transform.position = HexToWorld(coord);

            var shadow = new GameObject("GroundShadow");
            shadow.transform.SetParent(marker.transform, false);
            shadow.transform.localPosition = Vector3.up * (tileThickness + 0.01f);
            shadow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shadow.transform.localScale = Vector3.one * 0.65f;
            var shadowRenderer = shadow.AddComponent<SpriteRenderer>();
            shadowRenderer.sprite = GetUnitSprite();
            shadowRenderer.color = new Color(0f, 0f, 0f, 0.35f);
            shadowRenderer.sortingOrder = 1;

            var billboard = new GameObject("Billboard");
            billboard.transform.SetParent(marker.transform, false);
            billboard.transform.localPosition = Vector3.up * (tileThickness + 0.55f);
            billboard.transform.localScale = Vector3.one * 0.75f;
            var spriteRenderer = billboard.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = GetUnitSprite();
            spriteRenderer.color = color;
            spriteRenderer.sortingOrder = 2;

            unitMarkers[coord] = marker;
            billboardTransforms.Add(billboard.transform);
            return marker;
        }

        public int GetUnitMarkerCount(HexCoord coord)
        {
            return unitMarkers.ContainsKey(coord) ? 1 : 0;
        }

        private void LateUpdate()
        {
            var camera = presentationCamera != null ? presentationCamera : Camera.main;
            if (camera == null)
                return;

            for (int index = billboardTransforms.Count - 1; index >= 0; index--)
            {
                var billboard = billboardTransforms[index];
                if (billboard == null)
                {
                    billboardTransforms.RemoveAt(index);
                    continue;
                }

                var direction = camera.transform.position - billboard.position;
                if (direction.sqrMagnitude > 0.0001f)
                    billboard.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }
        }

        private void CreateTileVisual(TacticalTileData tile)
        {
            var tileObject = new GameObject("HybridTile_" + tile.Coord.q + "_" + tile.Coord.r);
            tileObject.transform.SetParent(transform, false);
            tileObject.transform.position = HexToWorld(tile.Coord);

            var filter = tileObject.AddComponent<MeshFilter>();
            var mesh = CreateHexPrismMesh();
            generatedMeshes.Add(mesh);
            filter.sharedMesh = mesh;

            var meshRenderer = tileObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = GetTerrainMaterial();
            var baseColor = GetTileColor(tile);
            var collider = tileObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;

            var view = new TileView(meshRenderer, collider, baseColor);
            tileViews.Add(tile.Coord, view);
            colliderBindings.Add(collider.GetInstanceID(), tile.Coord);
            SetTileColor(view, baseColor);
        }

        private Mesh CreateHexPrismMesh()
        {
            var vertices = new Vector3[14];
            vertices[0] = Vector3.up * tileThickness;
            vertices[7] = Vector3.zero;
            for (int index = 0; index < 6; index++)
            {
                float radians = (HexCornerOffsetDegrees + 60f * index) * Mathf.Deg2Rad;
                var point = new Vector3(Mathf.Cos(radians) * tileRadius, 0f, Mathf.Sin(radians) * tileRadius);
                vertices[index + 1] = point + Vector3.up * tileThickness;
                vertices[index + 8] = point;
            }

            var triangles = new List<int>(72);
            for (int index = 0; index < 6; index++)
            {
                int next = (index + 1) % 6;
                triangles.Add(0);
                triangles.Add(index + 1);
                triangles.Add(next + 1);
                triangles.Add(7);
                triangles.Add(next + 8);
                triangles.Add(index + 8);
                triangles.Add(index + 1);
                triangles.Add(index + 8);
                triangles.Add(next + 8);
                triangles.Add(index + 1);
                triangles.Add(next + 8);
                triangles.Add(next + 1);
            }

            var mesh = new Mesh { name = "HybridHexPrism" };
            mesh.vertices = vertices;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void ApplyHighlight(IEnumerable<HexCoord> tiles, Color color)
        {
            if (tiles == null)
                return;

            foreach (var coord in tiles)
            {
                if (tileViews.TryGetValue(coord, out var view))
                    SetTileColor(view, color);
            }
        }

        private static Color GetTileColor(TacticalTileData tile)
        {
            if (tile.BlocksGroundMove)
                return new Color(0.38f, 0.25f, 0.18f);
            if (tile.HeightLevel > 0)
                return new Color(0.48f, 0.64f, 0.38f);
            return new Color(0.31f, 0.5f, 0.27f);
        }

        private static void SetTileColor(TileView view, Color color)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            block.SetColor("_BaseColor", color);
            view.Renderer.SetPropertyBlock(block);
        }

        private Material GetTerrainMaterial()
        {
            if (terrainMaterial != null)
                return terrainMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
            if (shader == null)
                throw new InvalidOperationException("No supported terrain presentation shader is available.");

            terrainMaterial = new Material(shader) { name = "HybridTacticalTerrainMaterial" };
            return terrainMaterial;
        }

        private Sprite GetUnitSprite()
        {
            if (unitSprite != null)
                return unitSprite;

            unitTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "HybridTacticalUnitTexture" };
            unitTexture.SetPixel(0, 0, Color.white);
            unitTexture.Apply();
            unitSprite = Sprite.Create(unitTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            unitSprite.name = "HybridTacticalUnitSprite";
            return unitSprite;
        }

        private void ClearVisuals()
        {
            foreach (var marker in unitMarkers.Values)
                DestroyUnityObject(marker);
            unitMarkers.Clear();
            billboardTransforms.Clear();

            foreach (var view in tileViews.Values)
                DestroyUnityObject(view.Renderer.gameObject);
            tileViews.Clear();
            colliderBindings.Clear();

            foreach (var mesh in generatedMeshes)
                DestroyUnityObject(mesh);
            generatedMeshes.Clear();
        }

        private void OnDestroy()
        {
            ClearVisuals();
            DestroyUnityObject(terrainMaterial);
            DestroyUnityObject(unitSprite);
            DestroyUnityObject(unitTexture);
        }

        private static void DestroyUnityObject(UnityEngine.Object value)
        {
            if (value == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(value);
            else
                UnityEngine.Object.DestroyImmediate(value);
        }

        private sealed class TileView
        {
            public TileView(MeshRenderer renderer, MeshCollider collider, Color baseColor)
            {
                Renderer = renderer;
                Collider = collider;
                BaseColor = baseColor;
            }

            public MeshRenderer Renderer { get; }
            public MeshCollider Collider { get; }
            public Color BaseColor { get; }
        }
    }
}
