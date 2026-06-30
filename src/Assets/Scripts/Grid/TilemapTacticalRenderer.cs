using System;
using System.Collections.Generic;
using TianZhang.Core;
using TianZhang.HexTile;
using UnityEngine;

namespace TianZhang.Tactical
{
    [DisallowMultipleComponent]
    public class TilemapTacticalRenderer : MonoBehaviour, ITacticalRenderer
    {
        private HexTilemapManager tilemapManager;

        public TacticalGridModel Model { get; private set; }

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
            manager.groundTilemap.color = new Color(0.45f, 0.5f, 0.55f, 1f);

            foreach (var tile in model.Tiles)
            {
                var cell = new Vector3Int(tile.Coord.q, tile.Coord.r, 0);
                manager.groundTilemap.SetTile(cell, manager.groundTile);
            }
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
    }
}
