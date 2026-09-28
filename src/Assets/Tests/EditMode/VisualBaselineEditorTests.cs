using System.Linq;
using NUnit.Framework;
using TianZhang.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        public void FormalAdventureSceneExcludesTheTechnicalBaselineAndComparisonRoute()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            Transform[] transforms = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .ToArray();
            foreach (string nonFormalName in new[]
                     {
                         "VisualBaselineBoard",
                         "BattleVisualComparisonPanel",
                         "Comparison2DRouteButton",
                         "ComparisonStatic3DRouteButton",
                     })
            {
                Assert.Zero(transforms.Count(item => item.name == nonFormalName),
                    "Formal Adventure must not retain the comparison entry: " + nonFormalName);
            }
            Assert.Zero(AssetDatabase.GetDependencies(SceneBuildSupport.AdventureScenePath, true)
                .Count(path => path.EndsWith("/UnitMarker.prefab")));
        }

        [Test]
        public void BaselineAssetsRemainAvailableOnlyAsDedicatedQaFixtures()
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Mesh>(VisualBaselineBuilder.HexColumnMeshPath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Mesh>(VisualBaselineBuilder.HexOverlayMeshPath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(VisualBaselineBuilder.UnitMarkerPrefabPath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(VisualBaselineBuilder.StaticChessPrefabPath));
        }
    }
}
