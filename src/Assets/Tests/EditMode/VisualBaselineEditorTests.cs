using System.Linq;
using NUnit.Framework;
using TianZhang.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TianZhang.Tests.EditMode
{
    public sealed class VisualBaselineEditorTests
    {
        [Test]
        public void FormalRenderingAndSceneBaselineIsValid()
        {
            Assert.DoesNotThrow(SceneArchitectureValidator.Validate);
        }

        [Test]
        public void HexMeshesSeparateTerrainAndFeedbackLayers()
        {
            Mesh column = AssetDatabase.LoadAssetAtPath<Mesh>(VisualBaselineBuilder.HexColumnMeshPath);
            Mesh overlay = AssetDatabase.LoadAssetAtPath<Mesh>(VisualBaselineBuilder.HexOverlayMeshPath);
            Assert.IsNotNull(column);
            Assert.AreEqual(2, column.subMeshCount, "Hex top and side must use separate submeshes.");
            Assert.Greater(column.GetIndexCount(0), 0);
            Assert.Greater(column.GetIndexCount(1), 0);
            Assert.IsNotNull(overlay);
            Assert.AreEqual(1, overlay.subMeshCount);
            Assert.AreNotSame(column, overlay);
        }

        [Test]
        public void AdventureScenePersistsIndependentVisualLayers()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            Transform[] transforms = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .ToArray();
            foreach (string name in new[]
                     { "VisualBaselineBoard", "SurfaceOverlay", "ReachableOverlay", "SelectedOverlay", "AttackOverlay", "VisualBaselineOccluder" })
                Assert.AreEqual(1, transforms.Count(item => item.name == name), "Missing or duplicate visual layer: " + name);
            Assert.AreEqual(9, transforms.Count(item => item.name.StartsWith("VisualHex_")));
        }

        [Test]
        public void AdventureSceneFacingProbesMatchTheFrozenSixDirectionContract()
        {
            int[,] expectations =
            {
                { 1, 0, 1, 90 }, { 1, -1, 0, 150 }, { 0, -1, 0, 210 },
                { -1, 0, 1, 270 }, { -1, 1, 0, 330 }, { 0, 1, 1, 30 },
            };
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            Transform[] transforms = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .ToArray();
            Transform board = transforms.Single(item => item.name == "VisualBaselineBoard");

            for (int direction = 0; direction < expectations.GetLength(0); direction++)
            {
                int q = expectations[direction, 0];
                int r = expectations[direction, 1];
                int heightLevel = expectations[direction, 2];
                int yaw = expectations[direction, 3];
                string cellName = "VisualHex_" + q + "_" + r + "_Height_" + heightLevel;
                Assert.AreEqual(1, transforms.Count(item => item.name == cellName),
                    "Every rule neighbor needs exactly one known-height visual cell.");
                Transform cell = transforms.Single(item => item.name == cellName);
                Assert.Less(Vector3.Distance(cell.localPosition, HexToVisualPosition(q, r, 0f)), 0.001f);
                Assert.Less(Mathf.Abs(cell.localScale.y - HeightForLevel(heightLevel)), 0.001f);

                string probeName = "FacingProbe_" + direction;
                Assert.AreEqual(1, transforms.Count(item => item.parent == board && item.name == probeName),
                    "Every frozen direction needs exactly one static technical probe.");
                Transform probe = board.Find(probeName);
                Assert.Less(Vector3.Distance(probe.localPosition, HexToVisualPosition(q, r, HeightForLevel(heightLevel))), 0.001f);
                Assert.Less(Quaternion.Angle(probe.localRotation, Quaternion.Euler(0f, yaw, 0f)), 0.01f);
                Vector3 expectedForward = new Vector3(q + r * 0.5f, 0f, r * 0.8660254f).normalized;
                Assert.Less(Vector3.Angle(probe.localRotation * Vector3.forward, expectedForward), 0.01f,
                    "UnitMarker local +Z must face the matching rule neighbor center.");
                MeshRenderer[] renderers = probe.GetComponentsInChildren<MeshRenderer>(true);
                Assert.GreaterOrEqual(renderers.Length, 2);
                foreach (MeshRenderer renderer in renderers)
                {
                    Assert.AreEqual(ShadowCastingMode.On, renderer.shadowCastingMode);
                    Assert.IsTrue(renderer.receiveShadows);
                }
            }
        }

        [Test]
        public void SettlementCharterLayoutControlsChildHeight()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.SettlementScenePath, OpenSceneMode.Single);
            VerticalLayoutGroup layout = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<VerticalLayoutGroup>(true))
                .Single(item => item.name == "CharterSitePanel");
            Assert.IsTrue(layout.childControlHeight,
                "CharterSitePanel must honor child preferred heights so every action remains inside the 1920x1080 canvas.");
        }

        [Test]
        public void UnitMarkerUses3DMeshesAndStandardShadows()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(VisualBaselineBuilder.UnitMarkerPrefabPath);
            try
            {
                Assert.Zero(root.GetComponentsInChildren<SpriteRenderer>(true).Length);
                MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
                Assert.GreaterOrEqual(renderers.Length, 2);
                Assert.IsTrue(renderers.Any(item => item.name == "Facing"),
                    "The technical marker must expose its local +Z facing geometry.");
                foreach (MeshRenderer renderer in renderers)
                {
                    Assert.AreEqual(ShadowCastingMode.On, renderer.shadowCastingMode);
                    Assert.IsTrue(renderer.receiveShadows);
                    Assert.AreEqual(
                        VisualBaselineBuilder.UnitMaterialPath,
                        AssetDatabase.GetAssetPath(renderer.sharedMaterial));
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static float HeightForLevel(int heightLevel) => 0.34f + heightLevel * 0.28f;

        private static Vector3 HexToVisualPosition(int q, int r, float y) =>
            new Vector3(q + r * 0.5f, y, r * 0.8660254f + 1f);
    }
}
