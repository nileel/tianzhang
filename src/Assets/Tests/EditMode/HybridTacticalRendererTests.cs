using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TianZhang.Core;
using TianZhang.Core.SpatialRules;
using TianZhang.Editor;
using TianZhang.Tactical;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TianZhang.Tests.EditMode
{
    public class HybridTacticalRendererTests
    {
        private const string PrototypeScenePath = "Assets/Scenes/HybridTacticalPrototype.unity";
        private static readonly HexCoord Origin = new HexCoord(0, 0);
        private static readonly HexCoord East = new HexCoord(1, 0);
        private static readonly HexCoord HighGround = new HexCoord(2, 0);

        [Test]
        public void HybridRendererMapsModelCoordinatesAndHeightWithoutWritingBack()
        {
            var root = new GameObject("HybridRendererTest");
            try
            {
                var renderer = root.AddComponent<HybridTacticalRenderer>();
                var model = CreateModel();

                renderer.RenderGrid(model);

                Assert.AreSame(model, renderer.Model);
                Assert.AreEqual(model.Count, renderer.VisualTileCount);
                Assert.IsTrue(renderer.TryGetTileCollider(HighGround, out var highGroundCollider));
                Assert.IsTrue(renderer.TryGetBoundHex(highGroundCollider, out var mappedCoord));
                Assert.AreEqual(HighGround, mappedCoord);
                Assert.Greater(renderer.HexToWorld(HighGround).y, renderer.HexToWorld(Origin).y);
                Assert.AreEqual(3, model.GetTile(HighGround).HeightLevel);
                Assert.AreEqual(7, model.GetOccupant(Origin));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void HybridRaycastReturnsOnlyTheColliderBoundHex()
        {
            var root = new GameObject("HybridRaycastTest");
            try
            {
                var renderer = root.AddComponent<HybridTacticalRenderer>();
                renderer.RenderGrid(CreateModel());
                Physics.SyncTransforms();

                var ray = new Ray(renderer.HexToWorld(East) + Vector3.up * 10f, Vector3.down);

                Assert.IsTrue(renderer.TryRaycastToHex(ray, out var selected));
                Assert.AreEqual(East, selected);
                Assert.IsFalse(renderer.TryRaycastToHex(new Ray(Vector3.up * 10f, Vector3.up), out _));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RendererSwitchKeepsSharedSpatialOutputAndSingleColumnOccupancy()
        {
            var hybridRoot = new GameObject("HybridComparisonRenderer");
            var tilemapRoot = new GameObject("TilemapComparisonRenderer");
            var profile = CreateProfile();
            var tile = ScriptableObject.CreateInstance<Tile>();
            try
            {
                var model = CreateModel();
                Assert.IsTrue(SpatialQueryBoardFactory.TryCreate(model, profile, out var snapshot, out var reason), reason);

                var tilemapRenderer = CreateTilemapRenderer(tilemapRoot, tile);
                var hybridRenderer = hybridRoot.AddComponent<HybridTacticalRenderer>();

                tilemapRenderer.RenderGrid(model);
                string tilemapOutput = CaptureRuleOutput(snapshot);

                hybridRenderer.RenderGrid(model);
                string hybridOutput = CaptureRuleOutput(snapshot);

                tilemapRenderer.RenderGrid(model);
                string tilemapOutputAfterFallback = CaptureRuleOutput(snapshot);

                Assert.AreEqual(tilemapOutput, hybridOutput);
                Assert.AreEqual(tilemapOutput, tilemapOutputAfterFallback);
                Assert.AreEqual(1, snapshot.Occupied.Count);
                Assert.AreEqual(7, model.GetOccupant(Origin));
                Assert.AreEqual(1, hybridRenderer.GetUnitMarkerCount(Origin));
            }
            finally
            {
                Object.DestroyImmediate(tile);
                Object.DestroyImmediate(tilemapRoot);
                Object.DestroyImmediate(hybridRoot);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void PrototypeControllerUsesBoundRayInputAndSharedQuerySnapshot()
        {
            var root = new GameObject("HybridPrototypeControllerTest");
            var profile = CreateProfile();
            try
            {
                var renderer = root.AddComponent<HybridTacticalRenderer>();
                var controller = root.AddComponent<HybridTacticalPrototypeController>();
                var model = CreateModel();

                Assert.IsTrue(controller.Initialize(model, profile, out var reason), reason);
                Physics.SyncTransforms();

                var ray = new Ray(renderer.HexToWorld(East) + Vector3.up * 10f, Vector3.down);
                Assert.IsTrue(controller.TrySelectFromRay(ray, out var selected));
                Assert.AreEqual(East, selected);
                Assert.AreEqual(East, controller.SelectedHex);
                Assert.AreSame(model, controller.Model);
                Assert.IsNotNull(controller.SpatialQuery);
                Assert.AreEqual(2, controller.SpatialQuery.Board
                    .QueryMetricDistance(new SpatialHexCoord(0, 0), new SpatialHexCoord(1, 0), SpatialQueryKind.Attack)
                    .DistanceUnits);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void PrototypeSceneIsIsolatedFromFormalBuildSettingsAndAdventureOwners()
        {
            SceneBuilder.BuildHybridTacticalPrototypeScene();
            var scene = EditorSceneManager.OpenScene(PrototypeScenePath, OpenSceneMode.Single);

            Assert.IsTrue(scene.IsValid());
            Assert.IsNotNull(Object.FindFirstObjectByType<HybridTacticalPrototypeController>());
            Assert.IsNotNull(Object.FindFirstObjectByType<HybridTacticalRenderer>());
            var camera = Object.FindFirstObjectByType<Camera>();
            Assert.IsNotNull(camera);
            Assert.IsTrue(camera.orthographic);
            Assert.IsNull(Object.FindFirstObjectByType<TianZhang.Adventure.AdventureSceneController>());
            CollectionAssert.DoesNotContain(
                EditorBuildSettings.scenes.Where(sceneEntry => sceneEntry.enabled).Select(sceneEntry => sceneEntry.path),
                PrototypeScenePath);
        }

        private static TacticalGridModel CreateModel()
        {
            var model = new TacticalGridModel();
            model.SetTile(new TacticalTileData(Origin) { OccupiedUnitId = 7 });
            model.SetTile(new TacticalTileData(East));
            model.SetTile(new TacticalTileData(HighGround) { HeightLevel = 3 });
            return model;
        }

        private static EnvironmentProfileData CreateProfile()
        {
            var profile = ScriptableObject.CreateInstance<EnvironmentProfileData>();
            profile.profileId = "hybrid_renderer_test";
            profile.unitsPerRange = 2;
            profile.maxQueryRange = 16;
            profile.directedEdges = new[]
            {
                new EnvironmentDirectedEdge
                {
                    fromQ = 0,
                    fromR = 0,
                    toQ = 1,
                    toR = 0,
                    metricDistanceUnits = 2,
                    allowsMovement = true,
                    allowsEffects = true,
                },
            };
            return profile;
        }

        private static TilemapTacticalRenderer CreateTilemapRenderer(GameObject root, Tile tile)
        {
            var grid = root.AddComponent<Grid>();
            grid.cellLayout = GridLayout.CellLayout.Hexagon;

            var ground = new GameObject("Ground");
            ground.transform.SetParent(root.transform);
            var groundTilemap = ground.AddComponent<Tilemap>();

            var overlay = new GameObject("Overlay");
            overlay.transform.SetParent(root.transform);
            var overlayTilemap = overlay.AddComponent<Tilemap>();

            root.SetActive(false);
            var manager = root.AddComponent<TianZhang.HexTile.HexTilemapManager>();
            manager.groundTilemap = groundTilemap;
            manager.overlayTilemap = overlayTilemap;
            manager.groundTile = tile;
            manager.gridRadius = 1;
            var renderer = root.AddComponent<TilemapTacticalRenderer>();
            renderer.Initialize(manager);
            root.SetActive(true);
            return renderer;
        }

        private static string CaptureRuleOutput(SpatialQuerySnapshot snapshot)
        {
            var board = snapshot.Board;
            var origin = new SpatialHexCoord(0, 0);
            var east = new SpatialHexCoord(1, 0);
            var metric = board.QueryMetricDistance(origin, east, SpatialQueryKind.Attack);
            var range = board.QueryRangeEntry(origin, east, 1, 1, SpatialQueryKind.Attack, true);
            var sight = board.QueryLineOfSight(origin, east);
            var reachable = board.FindReachable(origin, 1, snapshot.Occupied)
                .OrderBy(entry => entry.Key.Q)
                .ThenBy(entry => entry.Key.R)
                .Select(entry => entry.Key + ":" + entry.Value);
            return string.Join("|", new[]
            {
                metric.IsReachable + ":" + metric.DistanceUnits + ":" + metric.Reason,
                range.IsInRange + ":" + range.DistanceUnits + ":" + range.Reason,
                sight.HasLineOfSight + ":" + sight.Reason,
                string.Join(",", reachable),
            });
        }
    }
}
