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
    }
}
