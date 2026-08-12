using System;
using System.Collections.Generic;
using TianZhang.HexTile;
using TianZhang.Content;
using UnityEngine;

using TianZhang.Spatial;

namespace TianZhang.Tactical
{
    [DisallowMultipleComponent]
    public class TilemapTacticalRenderer : MonoBehaviour, ITacticalRenderer
    {
        private HexTilemapManager tilemapManager;

        public TacticalGridModel Model { get; private set; }
        public EnvironmentPresentationSnapshot EnvironmentPresentation { get; private set; }

        public void Initialize(HexTilemapManager manager)
        {
            tilemapManager = manager;
        }

        public void RenderGrid(TacticalGridModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            var manager = RequireManager();
            if (manager.groundTilemap == null)
                throw new InvalidOperationException("Ground tilemap is required for tilemap tactical rendering.");

            Model = model;
            manager.groundTilemap.ClearAllTiles();
            manager.overlayTilemap?.ClearAllTiles();
            manager.groundTilemap.color = Color.white;

            foreach (var tile in model.Tiles)
            {
                var cell = new Vector3Int(tile.Coord.q, tile.Coord.r, 0);
                manager.groundTilemap.SetTile(cell, manager.groundTile);
            }

        }

        public EnvironmentPresentationSnapshot PresentEnvironment(
            TacticalGridModel model,
            EnvironmentProfileRuntime environment)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            var manager = RequireManager();
            Model = model;
            EnvironmentPresentation = EnvironmentPresentationSnapshot.Create(model, environment);

            if (manager.groundTilemap == null)
                return EnvironmentPresentation;

            foreach (var tile in model.Tiles)
            {
                var cell = new Vector3Int(tile.Coord.q, tile.Coord.r, 0);
                manager.groundTilemap.SetTileFlags(cell, UnityEngine.Tilemaps.TileFlags.None);
                manager.groundTilemap.SetColor(cell, GetTerrainColor(tile));
            }

            return EnvironmentPresentation;
        }

        public HexCoord ScreenToHex(Vector3 screenPosition)
        {
            return RequireManager().ScreenToHex(screenPosition);
        }

        public Vector3 HexToWorld(HexCoord coord)
        {
            return RequireManager().HexToWorld(coord);
        }

        public void HighlightMoveRange(IEnumerable<HexCoord> tiles)
        {
            RequireManager().HighlightMoveRange(ToList(tiles));
        }

        public void HighlightAttackRange(IEnumerable<HexCoord> tiles)
        {
            RequireManager().HighlightAttackRange(ToList(tiles));
        }

        public void ClearOverlay()
        {
            RequireManager().ClearOverlay();
        }

        public GameObject PlaceUnitMarker(HexCoord coord, Color color, string label)
        {
            return RequireManager().PlaceUnitMarker(coord, color, label);
        }

        private HexTilemapManager RequireManager()
        {
            if (tilemapManager == null)
                tilemapManager = GetComponent<HexTilemapManager>();

            if (tilemapManager == null)
                throw new InvalidOperationException("TilemapTacticalRenderer requires a HexTilemapManager.");

            return tilemapManager;
        }

        private static List<HexCoord> ToList(IEnumerable<HexCoord> tiles)
        {
            return tiles == null ? new List<HexCoord>() : new List<HexCoord>(tiles);
        }

        private static Color GetTerrainColor(TacticalTileData tile)
        {
            if (tile.BlocksGroundMove || tile.TerrainType == TacticalTerrainType.Obstacle)
                return new Color(0.38f, 0.25f, 0.18f);
            if (tile.TerrainType == TacticalTerrainType.Water)
                return new Color(0.24f, 0.48f, 0.7f);
            if (tile.TerrainType == TacticalTerrainType.HighGround || tile.HeightLevel > 0)
                return new Color(0.58f, 0.72f, 0.4f);
            return new Color(0.62f, 0.78f, 0.5f);
        }
    }
}
