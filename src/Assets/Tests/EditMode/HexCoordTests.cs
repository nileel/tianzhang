using System;
using NUnit.Framework;
using TianZhang.Spatial;

namespace TianZhang.Tests
{
    public class HexCoordTests
    {
        [Test]
        public void AxialConstructorDerivesCubeCoordinateAndRejectsInvalidCubeInput()
        {
            var coord = new HexCoord(2, -5);

            Assert.AreEqual(2, coord.Q);
            Assert.AreEqual(-5, coord.R);
            Assert.AreEqual(3, coord.s);
            Assert.Throws<ArgumentException>(() => new HexCoord(1, 1, 1));
        }

        [Test]
        public void DirectionAndTopologyUseTheSameSixNeighbors()
        {
            var origin = new HexCoord(0, 0);

            for (int direction = 0; direction < HexCoord.Directions.Length; direction++)
            {
                var neighbor = origin.Step(direction);

                Assert.AreEqual(origin.Neighbor(direction), neighbor);
                Assert.AreEqual(1, origin.Distance(neighbor));
                Assert.AreEqual(1, origin.TopologicalDistanceTo(neighbor));
                Assert.IsTrue(origin.TryGetDirectionTo(neighbor, out var resolvedDirection));
                Assert.AreEqual(direction, resolvedDirection);
            }
        }
    }
}
