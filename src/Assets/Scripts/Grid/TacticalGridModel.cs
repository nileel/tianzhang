using System;
using System.Collections.Generic;
using TianZhang.Core;

namespace TianZhang.Tactical
{
    public enum TacticalTerrainType
    {
        Plain,
        Obstacle,
        Water,
        HighGround,
        Custom,
    }

    [Serializable]
    public struct TacticalTileData
    {
        public const int NoUnit = -1;

        public HexCoord Coord;
        public TacticalTerrainType TerrainType;
        public int HeightLevel;
        public bool BlocksGroundMove;
        public bool BlocksFlyingMove;
        public bool BlocksLineOfSight;
        public bool BlocksLanding;
        public bool IsEntityObstacle;
        public int OccupiedUnitId;

        public bool IsOccupied => OccupiedUnitId >= 0;

        public TacticalTileData(HexCoord coord)
        {
            Coord = coord;
            TerrainType = TacticalTerrainType.Plain;
            HeightLevel = 0;
            BlocksGroundMove = false;
            BlocksFlyingMove = false;
            BlocksLineOfSight = false;
            BlocksLanding = false;
            IsEntityObstacle = false;
            OccupiedUnitId = NoUnit;
        }
    }

    public class TacticalGridModel
    {
        private readonly Dictionary<HexCoord, TacticalTileData> tiles =
            new Dictionary<HexCoord, TacticalTileData>();

        public int Count => tiles.Count;
        public IEnumerable<TacticalTileData> Tiles => tiles.Values;

        public void Clear()
        {
            tiles.Clear();
        }

        public void SetTile(TacticalTileData tile)
        {
            tiles[tile.Coord] = tile;
        }

        public bool Contains(HexCoord coord)
        {
            return tiles.ContainsKey(coord);
        }

        public bool TryGetTile(HexCoord coord, out TacticalTileData tile)
        {
            return tiles.TryGetValue(coord, out tile);
        }

        public TacticalTileData GetTile(HexCoord coord)
        {
            if (!tiles.TryGetValue(coord, out var tile))
                throw new KeyNotFoundException($"Tactical tile not found: {coord}");

            return tile;
        }

        public TacticalTileData GetOrCreateTile(HexCoord coord)
        {
            if (tiles.TryGetValue(coord, out var tile))
                return tile;

            tile = new TacticalTileData(coord);
            tiles[coord] = tile;
            return tile;
        }

        public void SetHeight(HexCoord coord, int heightLevel)
        {
            var tile = GetOrCreateTile(coord);
            tile.HeightLevel = heightLevel;
            tiles[coord] = tile;
        }

        public void SetBlocked(
            HexCoord coord,
            bool blocksGroundMove,
            bool blocksFlyingMove = false,
            bool blocksLineOfSight = false,
            bool blocksLanding = false)
        {
            var tile = GetOrCreateTile(coord);
            tile.BlocksGroundMove = blocksGroundMove;
            tile.BlocksFlyingMove = blocksFlyingMove;
            tile.BlocksLineOfSight = blocksLineOfSight;
            tile.BlocksLanding = blocksLanding;
            tile.TerrainType = blocksGroundMove ? TacticalTerrainType.Obstacle : TacticalTerrainType.Plain;
            tiles[coord] = tile;
        }

        public bool BlocksGroundMove(HexCoord coord)
        {
            return !tiles.TryGetValue(coord, out var tile) || tile.BlocksGroundMove;
        }

        public bool BlocksFlyingMove(HexCoord coord)
        {
            return !tiles.TryGetValue(coord, out var tile) || tile.BlocksFlyingMove;
        }

        public bool BlocksLineOfSight(HexCoord coord)
        {
            return tiles.TryGetValue(coord, out var tile) && tile.BlocksLineOfSight;
        }

        public bool CanEnterByGround(HexCoord coord, bool ignoreOccupant = false)
        {
            if (!tiles.TryGetValue(coord, out var tile))
                return false;

            if (tile.BlocksGroundMove || tile.BlocksLanding)
                return false;

            return ignoreOccupant || !tile.IsOccupied;
        }

        public void SetOccupied(HexCoord coord, int unitId)
        {
            var tile = GetOrCreateTile(coord);
            tile.OccupiedUnitId = unitId;
            tiles[coord] = tile;
        }

        public void ClearOccupied(HexCoord coord)
        {
            if (!tiles.TryGetValue(coord, out var tile))
                return;

            tile.OccupiedUnitId = TacticalTileData.NoUnit;
            tiles[coord] = tile;
        }

        public bool IsOccupied(HexCoord coord)
        {
            return tiles.TryGetValue(coord, out var tile) && tile.IsOccupied;
        }

        public int GetOccupant(HexCoord coord)
        {
            return tiles.TryGetValue(coord, out var tile) ? tile.OccupiedUnitId : TacticalTileData.NoUnit;
        }

        public HexGrid ToHexGrid()
        {
            var grid = new HexGrid();
            foreach (var tile in tiles.Values)
            {
                if (tile.BlocksGroundMove)
                    grid.SetBlocked(tile.Coord, true);

                if (tile.IsOccupied)
                    grid.SetOccupied(tile.Coord, tile.OccupiedUnitId);
            }

            return grid;
        }

        public static TacticalGridModel FromHexGrid(IEnumerable<HexCoord> coords, HexGrid source)
        {
            if (coords == null)
                throw new ArgumentNullException(nameof(coords));
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var model = new TacticalGridModel();
            foreach (var coord in coords)
            {
                bool blocked = source.IsBlocked(coord);
                model.SetTile(new TacticalTileData(coord)
                {
                    TerrainType = blocked ? TacticalTerrainType.Obstacle : TacticalTerrainType.Plain,
                    BlocksGroundMove = blocked,
                    BlocksLanding = blocked,
                    OccupiedUnitId = source.GetOccupant(coord),
                });
            }

            return model;
        }
    }
}
