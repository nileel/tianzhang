using NUnit.Framework;
using TianZhang.Core;
using TianZhang.Tactical;

namespace TianZhang.Tests
{
    public class TacticalGridModelTests
    {
        [Test]
        public void FromHexGridCopiesBlockersOccupantsAndDefaultHeight()
        {
            var source = new HexGrid();
            var center = new HexCoord(0, 0);
            var blocked = new HexCoord(1, 0);
            var occupied = new HexCoord(0, 1);

            source.SetBlocked(blocked, true);
            source.SetOccupied(occupied, 42);

            var model = TacticalGridModel.FromHexGrid(new[] { center, blocked, occupied }, source);

            Assert.AreEqual(3, model.Count);
            Assert.IsFalse(model.GetTile(center).BlocksGroundMove);
            Assert.IsTrue(model.GetTile(blocked).BlocksGroundMove);
            Assert.IsTrue(model.GetTile(blocked).BlocksLanding);
            Assert.AreEqual(0, model.GetTile(blocked).HeightLevel);
            Assert.AreEqual(42, model.GetTile(occupied).OccupiedUnitId);
            Assert.IsTrue(model.IsOccupied(occupied));
        }

        [Test]
        public void ToHexGridPreservesGroundBlockersAndOccupants()
        {
            var blocked = new HexCoord(1, 0);
            var occupied = new HexCoord(0, 1);
            var model = new TacticalGridModel();

            model.SetTile(new TacticalTileData(new HexCoord(0, 0)));
            model.SetTile(new TacticalTileData(blocked)
            {
                BlocksGroundMove = true,
            });
            model.SetTile(new TacticalTileData(occupied)
            {
                OccupiedUnitId = 7,
            });

            var grid = model.ToHexGrid();

            Assert.IsFalse(grid.IsBlocked(new HexCoord(0, 0)));
            Assert.IsTrue(grid.IsBlocked(blocked));
            Assert.AreEqual(7, grid.GetOccupant(occupied));
            Assert.IsTrue(grid.IsOccupied(occupied));
        }
    }
}
