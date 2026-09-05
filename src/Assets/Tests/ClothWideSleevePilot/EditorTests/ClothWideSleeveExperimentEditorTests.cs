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
        [TestCase(2f)]
        [TestCase(4f)]
        [TestCase(6f)]
        [TestCase(8f)]
        [TestCase(9f)]
        public void ActionBoundariesDoNotTeleportOrSnapPose(float boundary)
        {
            var before = ClothWideSleeveMotion.Evaluate(boundary - 0.0001f);
            var after = ClothWideSleeveMotion.Evaluate(boundary + 0.0001f);
            Assert.That(Mathf.Abs(after.distance - before.distance), Is.LessThan(0.001f));
            Assert.That(Mathf.Abs(after.yawOffset - before.yawOffset), Is.LessThan(0.01f));
            Assert.That(Mathf.Abs(after.armUp - before.armUp), Is.LessThan(0.001f));
            Assert.That(Mathf.Abs(after.armForward - before.armForward), Is.LessThan(0.001f));
            Assert.That(Mathf.Abs(after.forearm - before.forearm), Is.LessThan(0.001f));
        }

        [Test]
        public void StopChangesVelocityInstantlyWithoutChangingPoseOrPositionDiscontinuously()
        {
            const float dt = 1f / 60f;
            var before = ClothWideSleeveMotion.Evaluate(9f - dt);
            var at = ClothWideSleeveMotion.Evaluate(9f);
            var after = ClothWideSleeveMotion.Evaluate(9f + dt);
            Assert.That((at.distance - before.distance) / dt, Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That((after.distance - at.distance) / dt, Is.Zero);
            Assert.That(at.armUp, Is.EqualTo(before.armUp));
            Assert.That(after.armUp, Is.EqualTo(before.armUp));
            Assert.That(at.distance, Is.EqualTo(1.25f));
        }

        [Test]
        public void AllRecordedActionStepsAreContinuousAndReturnToRest()
        {
            var previous = ClothWideSleeveMotion.Evaluate(0f);
            for (int frame = 1; frame < 720; frame++)
            {
                var current = ClothWideSleeveMotion.Evaluate(frame / 60f);
                Assert.That(Mathf.Abs(current.distance - previous.distance), Is.LessThanOrEqualTo(1.25f / 60f + 0.0001f));
                Assert.That(Mathf.Abs(current.yawOffset - previous.yawOffset), Is.LessThan(5f));
                previous = current;
            }
            Assert.That(previous.distance, Is.EqualTo(1.25f));
            Assert.That(previous.armUp, Is.EqualTo(-0.62f));
            Assert.That(previous.armForward, Is.Zero);
            Assert.That(previous.yawOffset, Is.Zero);
        }

        [Test]
        public void PairedSphereDistanceIncludesTheContinuousBridgeAndUnequalRadii()
        {
            var a = Vector3.zero;
            var b = Vector3.up * 2f;
            Assert.That(ClothWideSleeveCollisionProbe.ProxySignedDistance(Vector3.up, a, b, 0.5f, 0.5f),
                Is.EqualTo(-0.5f).Within(0.001f), "The bridge is not a gap between two spheres.");
            Assert.That(ClothWideSleeveCollisionProbe.ProxySignedDistance(Vector3.up + Vector3.right, a, b, 0.5f, 0.5f),
                Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(ClothWideSleeveCollisionProbe.ProxySignedDistance(-Vector3.up, a, b, 0.5f, 0.25f),
                Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(ClothWideSleeveCollisionProbe.ProxySignedDistance(Vector3.zero, a, Vector3.up * 0.1f, 1f, 0.2f),
                Is.EqualTo(-1f).Within(0.001f), "Nested endpoints reduce to the enclosing sphere.");
        }

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
