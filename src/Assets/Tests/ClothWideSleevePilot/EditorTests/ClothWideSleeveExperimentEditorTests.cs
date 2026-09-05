using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TianZhang.ClothWideSleevePilot.EditorTests
{
    public sealed class ClothWideSleeveExperimentEditorTests
    {
        [Test]
        public void SavedExperimentHasARealWideSleeveClothAndRemainsOutsideBuildSettings()
        {
            string[] enabledScenes = EditorBuildSettings.scenes.Where(item => item.enabled).Select(item => item.path).ToArray();
            CollectionAssert.AreEqual(new[]
            {
                "Assets/Scenes/StartMenuScene.unity",
                "Assets/Scenes/WorldScene.unity",
                "Assets/Scenes/SettlementScene.unity",
                "Assets/Scenes/AdventureScene.unity"
            }, enabledScenes);
            CollectionAssert.DoesNotContain(enabledScenes, Editor.ClothWideSleeveExperimentSceneBuilder.ScenePath);

            EditorSceneManager.OpenScene(Editor.ClothWideSleeveExperimentSceneBuilder.ScenePath, OpenSceneMode.Single);
            ClothWideSleevePilotController controller = Object.FindFirstObjectByType<ClothWideSleevePilotController>();
            Assert.IsNotNull(controller);
            Assert.IsNotNull(controller.Animator);
            Assert.IsTrue(controller.Animator.avatar.isValid);
            Assert.IsTrue(controller.Animator.avatar.isHuman);
            Assert.AreEqual(1, Object.FindObjectsByType<Cloth>(FindObjectsSortMode.None).Length);

            Cloth cloth = controller.SleeveCloth;
            SkinnedMeshRenderer renderer = controller.SleeveRenderer;
            Assert.AreEqual("TZ_LeftWideSleeve_Cloth", renderer.name);
            Assert.GreaterOrEqual(renderer.sharedMesh.vertexCount, 400);
            Assert.Greater(renderer.bounds.size.y, 0.70f, "The pilot must retain a visibly hanging sleeve belly.");
            Assert.Greater(renderer.bounds.size.x, 0.50f, "The pilot must remain a full arm-length sleeve.");

            ClothSkinningCoefficient[] coefficients = cloth.coefficients;
            Assert.AreEqual(renderer.sharedMesh.vertexCount, coefficients.Length);
            Assert.Greater(coefficients.Count(item => item.maxDistance <= 0.0001f), 24, "The sleeve root needs a pinned band.");
            Assert.Greater(coefficients.Count(item => item.maxDistance >= 0.33f), 24, "The sleeve belly needs a constrained loose region.");
            Assert.That(coefficients.Max(item => item.maxDistance), Is.InRange(0.379f, 0.381f));
            Assert.That(cloth.stretchingStiffness, Is.EqualTo(0.88f).Within(0.001f));
            Assert.That(cloth.bendingStiffness, Is.EqualTo(0.70f).Within(0.001f));
            Assert.AreEqual(3, cloth.sphereColliders.Length, "Torso, upper arm and forearm collision pairs are required.");
            Assert.IsTrue(cloth.useGravity);
            Assert.IsTrue(cloth.enableContinuousCollision);

            var selfCollision = new List<uint>();
            cloth.GetSelfAndInterCollisionIndices(selfCollision);
            Assert.Greater(selfCollision.Count, 300, "The broad sleeve must not silently disable self-collision.");

            string sourceBlend = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "assets", "source",
                "characters", "cloth-wide-sleeve-pilot", "TZ_ClothWideSleevePilot_v001.blend"));
            Assert.IsTrue(File.Exists(sourceBlend), "Editable Blender source is required beside the Unity pilot.");
        }
    }
}
