using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TianZhang.Content;
using TianZhang.Editor;
using TianZhang.Infrastructure.UnityContent;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TianZhang.Tests.EditMode
{
    public sealed class GuanzhongBattlefieldBaselineEditorTests
    {

        private static float CenterSurfaceY(Transform ground)
        {
            float highest = float.NegativeInfinity;
            Vector3 center = ground.position;
            foreach (MeshFilter filter in ground.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                Assert.IsNotNull(mesh);
                Assert.IsTrue(mesh.isReadable, "Source mesh must remain readable for triangle evidence.");
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 a = filter.transform.TransformPoint(vertices[triangles[index]]);
                    Vector3 b = filter.transform.TransformPoint(vertices[triangles[index + 1]]);
                    Vector3 c = filter.transform.TransformPoint(vertices[triangles[index + 2]]);
                    float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(denominator) < 1e-10f) continue;
                    float u = ((b.z - c.z) * (center.x - c.x) + (c.x - b.x) * (center.z - c.z)) / denominator;
                    float v = ((c.z - a.z) * (center.x - c.x) + (a.x - c.x) * (center.z - c.z)) / denominator;
                    float w = 1f - u - v;
                    if (u >= 0f && v >= 0f && w >= 0f)
                        highest = Mathf.Max(highest, u * a.y + v * b.y + w * c.y);
                }
            }
            Assert.IsFalse(float.IsInfinity(highest), "No source triangle supports the logical cell center.");
            return highest;
        }

        [Test]
        public void ApprovedStairPersistsAsNormalizedReservePrefabOutsideTheFlatScene()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GuanzhongTerrainAssetBuilder.StairPrefabPath);
            Assert.IsNotNull(prefab);
            Assert.AreEqual(Vector3.zero, prefab.transform.localPosition);
            Assert.AreEqual(Vector3.one, prefab.transform.localScale);
            Assert.Zero(prefab.GetComponentsInChildren<Collider>(true).Length);
            Transform alignment = prefab.transform.Find("MeasuredFbxAlignment");
            Assert.IsNotNull(alignment);
            Assert.AreEqual(1, alignment.childCount);
            Assert.Less(Vector3.Distance(alignment.localPosition, new Vector3(0, 0, -.407173167f)), .00001f);
            Assert.Less(Quaternion.Angle(alignment.localRotation, Quaternion.Euler(0, -90, 0)), .001f);
            Assert.Less(Vector3.Distance(alignment.localScale, Vector3.one * .4143463054f), .00001f);
            Assert.AreEqual(GuanzhongTerrainAssetBuilder.AssetRoot + "/Stair.fbx",
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(alignment.GetChild(0).gameObject));
            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            Assert.AreEqual(1, filters.Length);
            Assert.AreEqual(GuanzhongTerrainAssetBuilder.AssetRoot + "/Stair.fbx",
                AssetDatabase.GetAssetPath(filters[0].sharedMesh));
            Assert.AreEqual(63497, filters[0].sharedMesh.vertexCount);
            Assert.AreEqual(98138 * 3, filters[0].sharedMesh.triangles.Length);
            MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            Assert.AreEqual(1, renderers.Length);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(GuanzhongTerrainAssetBuilder.AssetRoot + "/Stair.mat");
            Assert.IsNotNull(material);
            CollectionAssert.AreEqual(new[] { material }, renderers[0].sharedMaterials);
            Assert.AreEqual(GuanzhongTerrainAssetBuilder.AssetRoot + "/Stair_basecolor.JPEG",
                AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")));

            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            foreach (Transform item in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)))
                Assert.AreNotEqual(GuanzhongTerrainAssetBuilder.StairPrefabPath,
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(item.gameObject),
                    "The reserve stair must not fabricate a route in the flat formal battlefield.");
        }

        [Test]
        public void AdventureBuilderPersistsOnlyGuanzhongEnvironmentEndpointsAsFunctionalGround()
        {
            AdventureSceneBuilder.Build();
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            Assert.IsFalse(scene.isDirty, "AdventureScene must reopen from the Builder save without unsaved changes.");
            GameObject backdrop = scene.GetRootGameObjects().Single(root => root.name == "VisualBackdrop");
            Assert.IsFalse(backdrop.activeSelf,
                "The legacy backdrop plane must not intersect the approved terrain's rock walls.");

            EnvironmentProfileAsset profile = AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>(
                "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset");
            AdventureMapData map = AssetDatabase.LoadAssetAtPath<AdventureMapData>(
                "Assets/Data/Adventures/AdventureMap_guanzhong_wild.asset");
            Assert.IsNotNull(profile);
            Assert.IsNotNull(map);

            var expectedCells = new HashSet<Vector2Int>();
            foreach (EnvironmentDirectedEdge edge in profile.directedEdges)
            {
                expectedCells.Add(new Vector2Int(edge.fromQ, edge.fromR));
                expectedCells.Add(new Vector2Int(edge.toQ, edge.toR));
            }
            Assert.AreEqual(6, expectedCells.Count,
                "The current Guanzhong environment profile must expose exactly six unique edge endpoints.");
            AdventureNodeData start = map.nodes.Single(item => item.nodeId == "start");
            AdventureNodeData encounter = map.nodes.Single(item => item.nodeId == "shijiahou_encounter");
            Assert.IsTrue(expectedCells.Contains(new Vector2Int(start.q, start.r)),
                "The start marker coordinate must be part of the environment endpoint ground.");
            Assert.IsTrue(expectedCells.Contains(new Vector2Int(encounter.q, encounter.r)),
                "The encounter marker coordinate must be part of the environment endpoint ground.");

            GameObject battlefield = scene.GetRootGameObjects().Single(root => root.name == "GuanzhongBattlefield");
            Assert.IsTrue(battlefield.activeSelf, "The functional battlefield must be active by default.");
            Transform[] cells = battlefield.GetComponentsInChildren<Transform>(true)
                .Where(item => item.parent == battlefield.transform)
                .ToArray();
            Assert.AreEqual(expectedCells.Count, cells.Length,
                "The functional battlefield must not add return nodes, radius-grid cells, or comparison cells.");
            Assert.IsNull(battlefield.transform.Find("GuanzhongHex_0_2"),
                "The navigation return node must not become a combat ground cell.");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GuanzhongTerrainAssetBuilder.TilePrefabPath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(GuanzhongTerrainAssetBuilder.AssetRoot + "/Tile.mat");
            Assert.IsNotNull(prefab);
            Assert.IsNotNull(material);
            Assert.AreEqual(GuanzhongTerrainAssetBuilder.AssetRoot + "/Tile_basecolor.JPEG",
                AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")));
            foreach (Vector2Int coord in expectedCells)
            {
                Transform cell = battlefield.transform.Find("GuanzhongHex_" + coord.x + "_" + coord.y);
                Assert.IsNotNull(cell, "Missing functional ground cell: " + coord + ".");
                Assert.Less(Vector3.Distance(cell.localPosition,
                    new Vector3(coord.x + coord.y * 0.5f, 0.34f, coord.y * 0.8660254f + 1f)), 0.001f);
                Assert.AreEqual(Vector3.one, cell.localScale);
                Assert.Less(Quaternion.Angle(Quaternion.identity, cell.localRotation), 0.001f);
                Assert.AreSame(prefab, PrefabUtility.GetCorrespondingObjectFromSource(cell.gameObject));
                Assert.Zero(cell.GetComponentsInChildren<Collider>(true).Length,
                    "Functional ground must not add collision or blocking semantics.");

                Transform alignment = cell.Find("MeasuredFbxAlignment");
                Assert.IsNotNull(alignment);
                Assert.Less(Vector3.Distance(alignment.localScale, Vector3.one * 1.9770544759f), 0.00001f);
                Assert.AreEqual(1, alignment.childCount, "Keep the original nested FBX hierarchy.");
                Assert.AreEqual(GuanzhongTerrainAssetBuilder.AssetRoot + "/Tile.fbx",
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(alignment.GetChild(0).gameObject));
                MeshFilter[] filters = cell.GetComponentsInChildren<MeshFilter>(true);
                Assert.AreEqual(1, filters.Length);
                Assert.AreEqual(GuanzhongTerrainAssetBuilder.AssetRoot + "/Tile.fbx",
                    AssetDatabase.GetAssetPath(filters[0].sharedMesh));
                Assert.AreEqual(59149, filters[0].sharedMesh.vertexCount);
                Assert.AreEqual(99346 * 3, filters[0].sharedMesh.triangles.Length);
                MeshRenderer[] renderers = cell.GetComponentsInChildren<MeshRenderer>(true);
                Assert.AreEqual(1, renderers.Length);
                CollectionAssert.AreEqual(new[] { material }, renderers[0].sharedMaterials);
                Assert.AreEqual(ShadowCastingMode.On, renderers[0].shadowCastingMode);
                Assert.IsTrue(renderers[0].receiveShadows);
                // Original triangle measurement: root datum + 0.000432711, not grass-crown bounds.
                Assert.AreEqual(0.340432711f, CenterSurfaceY(cell), 0.001f);
            }

            Transform[] all = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .ToArray();
            Transform comparisonBoard = all.Single(item => item.name == "VisualBaselineBoard");
            Transform comparisonPanel = all.Single(item => item.name == "BattleVisualComparisonPanel");
            Assert.IsFalse(comparisonBoard.gameObject.activeSelf,
                "The preserved visual baseline fixture must stay hidden in the formal scene.");
            Assert.IsFalse(comparisonPanel.gameObject.activeSelf,
                "The preserved comparison panel fixture must stay hidden in the formal scene.");
        }
    }
}
