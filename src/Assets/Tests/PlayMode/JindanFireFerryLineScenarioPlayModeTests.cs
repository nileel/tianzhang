using System;
using System.Collections;
using NUnit.Framework;
using TianZhang.Features.Adventure;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class JindanFireFerryLineScenarioPlayModeTests
    {
        private GameObject runtimeFixtureRoot;
        private Scene runtimeFixtureScene;

        [UnityTest]
        public IEnumerator FixtureSceneManifestsEscalatesAndEndsWithoutDoubleCharging()
        {
            JindanFireFerryLineScenarioController controller = CreateRuntimeFixture();
            yield return null;
            JindanFireFerryLineScenarioLedger before = controller.Ledger;

            Assert.IsTrue(controller.TryManifest());
            Assert.IsTrue(controller.HasSourceRef);
            Assert.AreEqual(before.Actions - 1, controller.Ledger.Actions);
            Assert.AreEqual(before.Spirit - 1, controller.Ledger.Spirit);
            Assert.AreEqual(before.Fuel - 1, controller.Ledger.Fuel);
            Assert.AreEqual(1, controller.Ledger.OccupiedSurfaceCapacity);
            Assert.IsTrue(GameObject.Find("SourceFireMarker").activeSelf);

            Assert.IsTrue(controller.TryEscalate());
            JindanFireFerryLineScenarioLedger afterEscalate = controller.Ledger;
            Assert.IsTrue(controller.HasFerryLine);
            Assert.AreEqual(before.Actions - 2, afterEscalate.Actions);
            Assert.AreEqual(before.Spirit - 2, afterEscalate.Spirit);
            Assert.AreEqual(before.Fuel - 2, afterEscalate.Fuel);
            Assert.AreEqual(0, afterEscalate.FireBudget);
            Assert.AreEqual(1, afterEscalate.OccupiedSurfaceCapacity,
                "The chain shares the manifest surface reservation instead of taking a second one.");
            Assert.AreEqual(1, afterEscalate.OccupiedEnvironmentCapacity);
            for (int index = 0; index < controller.RouteNodeCount; index++)
                Assert.IsTrue(controller.IsFerryLineMarkerVisible(index));

            Assert.IsFalse(controller.TryEscalate());
            Assert.AreEqual("escalate_ferry_line_already_active", controller.LastFailureReason);
            AssertLedgerEqual(afterEscalate, controller.Ledger);

            controller.EndScenario();
            Assert.IsTrue(controller.HasEnded);
            Assert.IsFalse(controller.HasSourceRef);
            Assert.IsFalse(controller.HasFerryLine);
            Assert.AreEqual(0, controller.Ledger.OccupiedSurfaceCapacity);
            Assert.AreEqual(0, controller.Ledger.OccupiedEnvironmentCapacity);
            Assert.AreEqual(afterEscalate.Actions, controller.Ledger.Actions);
            Assert.AreEqual(afterEscalate.Spirit, controller.Ledger.Spirit);
            Assert.AreEqual(afterEscalate.Fuel, controller.Ledger.Fuel);
            Assert.AreEqual(afterEscalate.FireBudget, controller.Ledger.FireBudget);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FixtureSceneRejectsAllCounterexamplesWithoutHalfStateAndUsesEndForActiveLine()
        {
            JindanFireFerryLineScenarioController controller = CreateRuntimeFixture();
            yield return null;

            JindanFireFerryLineScenarioLedger initial = controller.Ledger;
            Assert.IsFalse(controller.TryEscalate());
            Assert.AreEqual("escalate_source_missing", controller.LastFailureReason);
            AssertLedgerEqual(initial, controller.Ledger);

            controller.SetForceSurfaceCapacityInsufficient(true);
            Assert.IsFalse(controller.TryManifest());
            Assert.AreEqual("manifest_surface_capacity_insufficient", controller.LastFailureReason);
            AssertLedgerEqual(initial, controller.Ledger);
            controller.SetForceSurfaceCapacityInsufficient(false);

            AssertEscalateFailurePreservesManifest(controller,
                () => controller.SetFormationEyeActive(false),
                "escalate_formation_eye_invalid");
            AssertEscalateFailurePreservesManifest(controller,
                () => controller.SetWaterBlocked(true),
                "escalate_route_water_blocked");
            AssertEscalateFailurePreservesManifest(controller,
                () => controller.SetRouteIsolated(true),
                "escalate_route_isolated");
            AssertEscalateFailurePreservesManifest(controller,
                () => controller.SetFuelDisconnected(true),
                "escalate_route_fuel_disconnected");
            AssertEscalateFailurePreservesManifest(controller,
                () => controller.SetForceNonAdjacentRoute(true),
                "escalate_route_non_adjacent");
            AssertEscalateFailurePreservesManifest(controller,
                () => controller.SetForceFireBudgetInsufficient(true),
                "escalate_fire_budget_insufficient");
            AssertEscalateFailurePreservesManifest(controller,
                () => controller.SetForceEnvironmentCapacityInsufficient(true),
                "escalate_environment_capacity_insufficient");

            AssertActiveLineEndsWithoutRefund(controller, () => controller.SetWaterBlocked(true));
            AssertActiveLineEndsWithoutRefund(controller, () => controller.SetFormationEyeActive(false));
            AssertActiveLineEndsWithoutRefund(controller, controller.ExtinguishSource);

            controller.ResetFixtureState();
            Assert.IsTrue(controller.TryManifest());
            Assert.IsTrue(controller.TryEscalate());
            JindanFireFerryLineScenarioLedger beforeDisable = controller.Ledger;
            controller.gameObject.SetActive(false);
            Assert.IsTrue(controller.HasEnded);
            Assert.AreEqual(0, controller.Ledger.OccupiedSurfaceCapacity);
            Assert.AreEqual(0, controller.Ledger.OccupiedEnvironmentCapacity);
            Assert.AreEqual(beforeDisable.Actions, controller.Ledger.Actions);
            Assert.AreEqual(beforeDisable.FireBudget, controller.Ledger.FireBudget);
            controller.gameObject.SetActive(true);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (runtimeFixtureScene.IsValid() && runtimeFixtureScene.isLoaded)
            {
                if (SceneManager.GetActiveScene() == runtimeFixtureScene)
                {
                    for (int index = 0; index < SceneManager.sceneCount; index++)
                    {
                        Scene candidate = SceneManager.GetSceneAt(index);
                        if (candidate != runtimeFixtureScene && candidate.isLoaded)
                        {
                            SceneManager.SetActiveScene(candidate);
                            break;
                        }
                    }
                }
                AsyncOperation unload = SceneManager.UnloadSceneAsync(runtimeFixtureScene);
                if (unload != null)
                    while (!unload.isDone) yield return null;
            }
            else if (runtimeFixtureRoot != null)
            {
                Object.Destroy(runtimeFixtureRoot);
                yield return null;
            }
            runtimeFixtureRoot = null;
            runtimeFixtureScene = default;
        }

        private JindanFireFerryLineScenarioController CreateRuntimeFixture()
        {
            runtimeFixtureScene = SceneManager.CreateScene("JindanFireFerryLineScenarioRuntimeFixture");
            Assert.IsTrue(SceneManager.SetActiveScene(runtimeFixtureScene));
            runtimeFixtureRoot = new GameObject("JindanFireFerryLineScenarioRuntimeFixtureRoot");
            var controllerObject = new GameObject("JindanFireFerryLineScenarioController");
            controllerObject.transform.SetParent(runtimeFixtureRoot.transform, false);
            JindanFireFerryLineScenarioController controller =
                controllerObject.AddComponent<JindanFireFerryLineScenarioController>();
            var eye = new GameObject("FormationEye_0_1");
            eye.transform.SetParent(runtimeFixtureRoot.transform, false);
            var sourceMarker = new GameObject("SourceFireMarker");
            sourceMarker.transform.SetParent(runtimeFixtureRoot.transform, false);
            var routeMarkers = new[]
            {
                new GameObject("FerryFireMarker_0"),
                new GameObject("FerryFireMarker_1"),
                new GameObject("FerryFireMarker_2"),
            };
            foreach (GameObject marker in routeMarkers)
                marker.transform.SetParent(runtimeFixtureRoot.transform, false);
            controller.ConfigureFixturePresentation(eye, sourceMarker, routeMarkers, null);
            Assert.AreEqual(JindanFireFerryLineScenarioController.FixtureIdValue, controller.FixtureId);
            return controller;
        }

        private static void AssertEscalateFailurePreservesManifest(
            JindanFireFerryLineScenarioController controller,
            Action applyCountermeasure,
            string expectedReason)
        {
            controller.ResetFixtureState();
            Assert.IsTrue(controller.TryManifest());
            JindanFireFerryLineScenarioLedger before = controller.Ledger;
            applyCountermeasure();
            Assert.IsFalse(controller.TryEscalate());
            Assert.AreEqual(expectedReason, controller.LastFailureReason);
            Assert.IsTrue(controller.HasSourceRef);
            Assert.IsFalse(controller.HasFerryLine);
            AssertLedgerEqual(before, controller.Ledger);
        }

        private static void AssertActiveLineEndsWithoutRefund(
            JindanFireFerryLineScenarioController controller,
            Action applyCountermeasure)
        {
            controller.ResetFixtureState();
            Assert.IsTrue(controller.TryManifest());
            Assert.IsTrue(controller.TryEscalate());
            JindanFireFerryLineScenarioLedger before = controller.Ledger;
            applyCountermeasure();
            Assert.IsTrue(controller.HasEnded);
            Assert.IsFalse(controller.HasSourceRef);
            Assert.IsFalse(controller.HasFerryLine);
            Assert.AreEqual(0, controller.Ledger.OccupiedSurfaceCapacity);
            Assert.AreEqual(0, controller.Ledger.OccupiedEnvironmentCapacity);
            Assert.AreEqual(before.Actions, controller.Ledger.Actions);
            Assert.AreEqual(before.Spirit, controller.Ledger.Spirit);
            Assert.AreEqual(before.Fuel, controller.Ledger.Fuel);
            Assert.AreEqual(before.FireBudget, controller.Ledger.FireBudget);
        }

        private static void AssertLedgerEqual(
            JindanFireFerryLineScenarioLedger expected,
            JindanFireFerryLineScenarioLedger actual)
        {
            Assert.AreEqual(expected.Actions, actual.Actions);
            Assert.AreEqual(expected.Spirit, actual.Spirit);
            Assert.AreEqual(expected.Fuel, actual.Fuel);
            Assert.AreEqual(expected.FireBudget, actual.FireBudget);
            Assert.AreEqual(expected.AvailableSurfaceCapacity, actual.AvailableSurfaceCapacity);
            Assert.AreEqual(expected.AvailableEnvironmentCapacity, actual.AvailableEnvironmentCapacity);
            Assert.AreEqual(expected.OccupiedSurfaceCapacity, actual.OccupiedSurfaceCapacity);
            Assert.AreEqual(expected.OccupiedEnvironmentCapacity, actual.OccupiedEnvironmentCapacity);
            Assert.AreEqual(expected.SourceActive, actual.SourceActive);
            Assert.AreEqual(expected.FerryLineActive, actual.FerryLineActive);
            Assert.AreEqual(expected.Ended, actual.Ended);
        }
    }
}
