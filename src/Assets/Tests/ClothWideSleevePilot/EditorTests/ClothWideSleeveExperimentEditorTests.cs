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
        public void IsolationHasExplicitGroupsAndDoesNotTurnOrMoveTheBody()
        {
            Assert.AreEqual(ClothWideSleevePilotController.IsolationMode.A_SkinOnly,
                ClothWideSleevePilotController.ParseIsolationMode("A"));
            Assert.AreEqual(ClothWideSleevePilotController.IsolationMode.B_NoBodyCollision,
                ClothWideSleevePilotController.ParseIsolationMode("B"));
            Assert.AreEqual(ClothWideSleevePilotController.IsolationMode.C_BodyCollision,
                ClothWideSleevePilotController.ParseIsolationMode("C"));
            Assert.Throws<System.ArgumentException>(() => ClothWideSleevePilotController.ParseIsolationMode("D"));
            for (int frame = 0; frame < 480; frame++)
            {
                var sample = ClothWideSleevePilotController.EvaluateMotion(frame / 60f, true);
                Assert.That(sample.yawOffset, Is.Zero);
                Assert.That(sample.distance, Is.Zero);
                if (frame >= 360)
                {
                    Assert.That(sample.armUp, Is.EqualTo(-0.62f));
                    Assert.That(sample.armForward, Is.Zero);
                }
            }
            Assert.That(ClothWideSleevePilotController.EvaluateMotion(8.5f, false).distance, Is.GreaterThan(0f),
                "The original Retest 02 path must keep its movement trial.");
        }

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

        [Test]
        public void DiagnosedPinReleaseChangesOnly28FixedMaxDistancesWithoutChangingTheScene()
        {
            EditorSceneManager.OpenScene(Editor.ClothWideSleeveExperimentSceneBuilder.ScenePath, OpenSceneMode.Single);
            var controller = Object.FindFirstObjectByType<ClothWideSleevePilotController>();
            var before = controller.SleeveCloth.coefficients;
            var changed = (ClothSkinningCoefficient[])before.Clone();
            ClothWideSleevePilotController.ReleaseDiagnosedPins(changed);
            Assert.AreEqual(96, before.Count(c => c.maxDistance <= 0.0001f));
            Assert.AreEqual(68, changed.Count(c => c.maxDistance <= 0.0001f));
            Assert.AreEqual(28, changed.Where((c, i) => c.maxDistance != before[i].maxDistance).Count());
            Assert.AreEqual(before.Max(c => c.maxDistance), changed.Max(c => c.maxDistance));
            for (int i = 0; i < changed.Length; i++)
            {
                Assert.AreEqual(before[i].collisionSphereDistance, changed[i].collisionSphereDistance);
                if (before[i].maxDistance > 0.0001f) Assert.AreEqual(before[i].maxDistance, changed[i].maxDistance);
            }
            Assert.AreEqual(96, controller.SleeveCloth.coefficients.Count(c => c.maxDistance <= 0.0001f));
            Assert.Throws<System.InvalidOperationException>(() => ClothWideSleevePilotController.ReleaseDiagnosedPins(changed));
        }

        [Test]
        public void BodyProbeCapturesAllVisiblePartsWithoutChangingConstraintsOrProxies()
        {
            EditorSceneManager.OpenScene(Editor.ClothWideSleeveExperimentSceneBuilder.ScenePath, OpenSceneMode.Single);
            var controller = Object.FindFirstObjectByType<ClothWideSleevePilotController>();
            var cloth = controller.SleeveCloth;
            var coefficients = cloth.coefficients;
            var pairs = cloth.sphereColliders;
            bool enabled = cloth.enabled;
            cloth.enabled = false;
            try
            {
                using (var probe = new ClothWideSleeveSkinningProbe(controller.SleeveRenderer, cloth, controller.Animator))
                {
                    var snapshot = probe.Measure(true);
                    CollectionAssert.AreEquivalent(new[] { "SuperHero_Male", "Eyes", "Eyebrows" },
                        snapshot.bodySurfaces.Select(b => b.rendererName));
                    var body = snapshot.bodySurfaces.Single(b => b.rendererName == "SuperHero_Male");
                    Assert.AreEqual(7275, body.worldVertices.Length);
                    Assert.AreEqual(12566 * 3, body.triangles.Length);
                    Assert.That(snapshot.bodySurfaces.Max(b => b.bakeVsCpuMaxMeters), Is.LessThan(.00001f));
                    CollectionAssert.AreEqual(coefficients, cloth.coefficients);
                    CollectionAssert.AreEqual(pairs, cloth.sphereColliders);
                }
            }
            finally { cloth.enabled = enabled; }
        }
    }
}
