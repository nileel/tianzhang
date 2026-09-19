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
        [Test]
        public void AdventureBuilderPersistsOnlyGuanzhongEnvironmentEndpointsAsFunctionalGround()
        {
            AdventureSceneBuilder.Build();
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            Assert.IsFalse(scene.isDirty, "AdventureScene must reopen from the Builder save without unsaved changes.");

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

            Mesh column = AssetDatabase.LoadAssetAtPath<Mesh>(VisualBaselineBuilder.HexColumnMeshPath);
            Material top = AssetDatabase.LoadAssetAtPath<Material>(VisualBaselineBuilder.GroundTopMaterialPath);
            Material side = AssetDatabase.LoadAssetAtPath<Material>(VisualBaselineBuilder.GroundSideMaterialPath);
            Assert.IsNotNull(column);
            Assert.IsNotNull(top);
            Assert.IsNotNull(side);
            foreach (Vector2Int coord in expectedCells)
            {
                Transform cell = battlefield.transform.Find("GuanzhongHex_" + coord.x + "_" + coord.y);
                Assert.IsNotNull(cell, "Missing functional ground cell: " + coord + ".");
                Assert.Less(Vector3.Distance(cell.localPosition,
                    new Vector3(coord.x + coord.y * 0.5f, 0f, coord.y * 0.8660254f + 1f)), 0.001f);
                Assert.AreEqual(new Vector3(1f, 0.34f, 1f), cell.localScale);
                Assert.Zero(cell.GetComponentsInChildren<Collider>(true).Length,
                    "Functional ground must not add collision or blocking semantics.");

                MeshFilter filter = cell.GetComponent<MeshFilter>();
                MeshRenderer renderer = cell.GetComponent<MeshRenderer>();
                Assert.AreSame(column, filter.sharedMesh);
                Assert.AreEqual(2, renderer.sharedMaterials.Length);
                Assert.AreSame(top, renderer.sharedMaterials[0]);
                Assert.AreSame(side, renderer.sharedMaterials[1]);
                Assert.AreEqual(ShadowCastingMode.On, renderer.shadowCastingMode);
                Assert.IsTrue(renderer.receiveShadows);
                Assert.AreEqual(0.34f, renderer.bounds.max.y, 0.001f);
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
