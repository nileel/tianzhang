using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using TianZhang.Core;
using TianZhang.Content;
using TianZhang.Tactical;

using TianZhang.Spatial;

namespace TianZhang.HexTile
{
    public class HexTilemapManager : MonoBehaviour
    {
        [Header("Tilemap 引用")]
        public Tilemap groundTilemap;
        public Tilemap overlayTilemap;
        public Tilemap unitTilemap;

        [Header("Tile 素材")]
        public TileBase groundTile;
        public TileBase moveHighlightTile;
        public TileBase attackHighlightTile;
        public TileBase selectedTile;

        [Header("角色 Prefab")]
        public GameObject unitPrefab;

        [Header("网格参数")]
        public int gridRadius = 5;

        public HexGrid Grid { get; private set; } = new HexGrid();
        public TacticalGridModel TacticalGrid { get; private set; } = new TacticalGridModel();
        public TilemapTacticalRenderer TacticalRenderer { get; private set; }

        // 记录所有合法六角格及其世界坐标
        private Dictionary<HexCoord, Vector3> hexWorldPositions = new Dictionary<HexCoord, Vector3>();
        public List<HexCoord> allHexCoords = new List<HexCoord>();

        private void Awake()
        {
            EnsureTacticalRenderer();
            CacheAllHexPositions();
            RebuildTacticalGridModel();
        }

        private void EnsureTacticalRenderer()
        {
            if (TacticalRenderer == null)
                TacticalRenderer = GetComponent<TilemapTacticalRenderer>();

            if (TacticalRenderer == null)
                TacticalRenderer = gameObject.AddComponent<TilemapTacticalRenderer>();

            TacticalRenderer.Initialize(this);
        }

        /// <summary>预计算所有六角格的世界坐标</summary>
        private void CacheAllHexPositions()
        {
            hexWorldPositions.Clear();
            allHexCoords.Clear();
            for (int q = -gridRadius; q <= gridRadius; q++)
            {
                for (int r = Mathf.Max(-gridRadius, -q - gridRadius);
                         r <= Mathf.Min(gridRadius, -q + gridRadius); r++)
                {
                    var coord = new HexCoord(q, r);
                    allHexCoords.Add(coord);
                    // 用 Unity GetCellCenterWorld 获取真实世界坐标
                    var cell = new Vector3Int(coord.q, coord.r, 0);
                    var worldPos = groundTilemap.GetCellCenterWorld(cell);
                    hexWorldPositions[coord] = worldPos;
                }
            }
        }

        public void GenerateHexGrid()
        {
            EnsureTacticalRenderer();
            CacheAllHexPositions();
            RebuildTacticalGridModel();
            TacticalRenderer.RenderGrid(TacticalGrid);

            Debug.Log($"六角格: {allHexCoords.Count} 格, 坐标已缓存");
        }

        public TacticalGridModel RebuildTacticalGridModel()
        {
            TacticalGrid = TacticalGridModel.FromHexGrid(allHexCoords, Grid);
            return TacticalGrid;
        }

        public EnvironmentPresentationSnapshot PresentEnvironment(
            TacticalGridModel model,
            EnvironmentProfileRuntime environment)
        {
            if (model == null)
                throw new System.ArgumentNullException(nameof(model));

            EnsureTacticalRenderer();
            TacticalGrid = model;
            return TacticalRenderer.PresentEnvironment(model, environment);
        }

        /// <summary>屏幕坐标 → 最近的六角格（精确匹配）</summary>
        public HexCoord ScreenToHex(Vector3 screenPos)
        {
            Vector3 worldPos = Camera.main.ScreenToWorldPoint(screenPos);
            worldPos.z = 0;

            HexCoord best = allHexCoords.Count > 0 ? allHexCoords[0] : new HexCoord(0, 0);
            float bestDist = float.MaxValue;

            foreach (var kv in hexWorldPositions)
            {
                float dist = Vector3.Distance(worldPos, kv.Value);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = kv.Key;
                }
            }

            // 超出网格太远则忽略
            if (bestDist > 2f)
            {
                Debug.Log($"点击位置超出网格 ({worldPos}, 最近格 {best} 距离 {bestDist:F1})");
            }

            return best;
        }

        public Vector3 HexToWorld(HexCoord coord)
        {
            if (hexWorldPositions.TryGetValue(coord, out var pos))
                return pos;
            // fallback
            return groundTilemap.GetCellCenterWorld(new Vector3Int(coord.q, coord.r, 0));
        }

        public void HighlightMoveRange(List<HexCoord> tiles)
        {
            ClearOverlay();
            if (moveHighlightTile == null) return;
            foreach (var tile in tiles)
                overlayTilemap.SetTile(new Vector3Int(tile.q, tile.r, 0), moveHighlightTile);
        }

        public void HighlightAttackRange(List<HexCoord> tiles)
        {
            if (attackHighlightTile == null) return;
            foreach (var tile in tiles)
                overlayTilemap.SetTile(new Vector3Int(tile.q, tile.r, 0), attackHighlightTile);
        }

        public void ClearOverlay()
        {
            overlayTilemap.ClearAllTiles();
        }

        public GameObject PlaceUnitMarker(HexCoord coord, Color color, string label)
        {
            var worldPos = HexToWorld(coord);
            var go = Instantiate(unitPrefab, worldPos, Quaternion.identity, unitTilemap.transform);
            go.name = label;

            var sr = go.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.color = color;
                sr.sortingOrder = 10;
                go.transform.localScale = Vector3.one * 0.8f;
            }

            return go;
        }
    }
}
