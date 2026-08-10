using NUnit.Framework;
using TianZhang.Spatial;

namespace TianZhang.Tests
{
    public class HexGridTests
    {
        [Test]
        public void FindPathUsesTheUnifiedCoordinateNeighborsAndSkipsBlockedCells()
        {
            var grid = new HexGrid();
            var start = new HexCoord(0, 0);
            var blocked = new HexCoord(1, 0);
            var target = new HexCoord(2, 0);
            grid.SetBlocked(blocked, true);

            var path = grid.FindPath(start, target, maxSteps: 4);

            Assert.IsNotNull(path);
            Assert.AreEqual(target, path[path.Count - 1]);
            CollectionAssert.DoesNotContain(path, blocked);
        }

        [Test]
        public void OccupancyUsesTheUnifiedCoordinateAsItsKey()
        {
            var grid = new HexGrid();
            var coord = new HexCoord(-1, 1);

            grid.SetOccupied(coord, 42);

            Assert.IsTrue(grid.IsOccupied(new HexCoord(-1, 1)));
            Assert.AreEqual(42, grid.GetOccupant(new HexCoord(-1, 1)));
        }
    }
}
