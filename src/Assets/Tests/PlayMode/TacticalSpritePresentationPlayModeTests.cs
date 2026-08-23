using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TianZhang.Features.CombatPresentation;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TianZhang.Tests.PlayMode
{
    public sealed class TacticalSpritePresentationPlayModeTests
    {
        private static readonly StaticChessPresentationEvent[] NonIdleEvents =
        {
            StaticChessPresentationEvent.Move,
            StaticChessPresentationEvent.Attack,
            StaticChessPresentationEvent.Hit,
            StaticChessPresentationEvent.Cast,
            StaticChessPresentationEvent.Death,
        };

        [UnityTest]
        public IEnumerator TacticalSpriteRouteSwitchIsMutuallyExclusive()
        {
            Screen.SetResolution(1920, 1080, false);
            LogAssert.Expect(LogType.Error, "[AdventureInstaller] game_bootstrap_missing");
            SceneManager.LoadScene("AdventureScene");
            yield return null;

            GameObject board = GameObject.Find(TacticalSpriteProbeMatrix.BoardName);
            Assert.IsNotNull(board, "AdventureScene must persist the visual baseline board.");
            Transform group = board.transform.Find(TacticalSpriteProbeMatrix.GroupName);
            Assert.IsNotNull(group, "AdventureScene must persist the tactical sprite group.");
            Assert.IsFalse(group.gameObject.activeInHierarchy,
                "The 2D tactical sprite group must be inactive by default.");

            for (int direction = 0; direction < TacticalSpriteProbeMatrix.DirectionCount; direction++)
                Assert.IsTrue(
                    board.transform.Find(TacticalSpriteProbeMatrix.FacingProbePrefix + direction).gameObject.activeInHierarchy,
                    "The 3D facing probes must be active by default.");

            TacticalSpriteProbeMatrix.SetActiveRoute(true);
            yield return null;

            Assert.IsTrue(group.gameObject.activeInHierarchy,
                "The 2D tactical sprite group must be active after switching to the 2D route.");
            for (int direction = 0; direction < TacticalSpriteProbeMatrix.DirectionCount; direction++)
                Assert.IsFalse(
                    board.transform.Find(TacticalSpriteProbeMatrix.FacingProbePrefix + direction).gameObject.activeInHierarchy,
                    "The non-target 3D route must not participate in rendering.");
        }

        [UnityTest]
        public IEnumerator TacticalSpriteRootAdvancesAndResetsAcrossRealFrames()
        {
            Screen.SetResolution(1920, 1080, false);
            LogAssert.Expect(LogType.Error, "[AdventureInstaller] game_bootstrap_missing");
            SceneManager.LoadScene("AdventureScene");
            yield return null;

            TacticalSpriteProbeMatrix.SetActiveRoute(true);
            yield return null;

            foreach (StaticChessPresentationEvent presentationEvent in NonIdleEvents)
            for (int direction = 0; direction < 6; direction++)
            {
                GameObject probe = GameObject.Find("TacticalSpriteProbe_" + direction);
                Assert.IsNotNull(probe, "Missing frozen six-direction tactical sprite probe " + direction + ".");
                TacticalSpritePresentationController controller = probe.GetComponent<TacticalSpritePresentationController>();
                Assert.IsNotNull(controller);
                Assert.Zero(probe.GetComponentsInChildren<Animator>(true).Length);
                Assert.Zero(probe.GetComponentsInChildren<Animation>(true).Length);
                Assert.Zero(probe.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length);
                Assert.AreEqual("FuYuan_TacticalDirection_" + direction, controller.ActiveSpriteName,
                    "The active sprite must stay frozen per direction.");

                Vector3 restPosition = probe.transform.position;
                Quaternion restRotation = probe.transform.rotation;
                Dictionary<Transform, TransformState> childStates = CaptureChildStates(probe.transform);
                int castSignals = 0;
                Action onCast = () => castSignals++;
                controller.CastEffectRequested += onCast;
                try
                {
                    controller.StartPresentation(
                        presentationEvent,
                        restPosition + restRotation * Vector3.forward * 0.2f);
                    Assert.IsTrue(controller.IsPresenting,
                        "A non-idle presentation must drive the root across real frames.");

                    bool observedIntermediate = false;
                    for (int frame = 0; frame < 5 && !observedIntermediate; frame++)
                    {
                        yield return null;
                        if (Vector3.Distance(restPosition, probe.transform.position) > 0.0001f ||
                            Quaternion.Angle(restRotation, probe.transform.rotation) > 0.001f)
                            observedIntermediate = true;
                    }
                    Assert.IsTrue(observedIntermediate,
                        "A non-idle presentation must show an intermediate root change in real frames.");

                    float waited = 0f;
                    int guard = 0;
                    while (controller.IsPresenting && waited < 3f && guard++ < 100000)
                    {
                        yield return null;
                        waited += Time.deltaTime;
                    }
                    Assert.IsFalse(controller.IsPresenting,
                        "The presentation must self-advance to reset in real frames.");
                    yield return null; // 让 LateUpdate 完成相机平面对齐后再断言子节点。
                }
                finally
                {
                    controller.CastEffectRequested -= onCast;
                }

                Assert.Less(Vector3.Distance(restPosition, probe.transform.position), 0.0001f,
                    "The root must return to its rest position.");
                Assert.Less(Quaternion.Angle(restRotation, probe.transform.rotation), 0.001f,
                    "The root must return to its rest rotation.");
                Assert.AreEqual(
                    presentationEvent == StaticChessPresentationEvent.Cast ? 1 : 0,
                    castSignals,
                    "Only cast may expose one one-shot visual-effect signal.");
                AssertChildStatesUnchanged(childStates);
            }
        }

        [UnityTest]
        public IEnumerator IdlePresentationStaysStatic()
        {
            Screen.SetResolution(1920, 1080, false);
            LogAssert.Expect(LogType.Error, "[AdventureInstaller] game_bootstrap_missing");
            SceneManager.LoadScene("AdventureScene");
            yield return null;

            TacticalSpriteProbeMatrix.SetActiveRoute(true);
            yield return null;

            GameObject probe = GameObject.Find("TacticalSpriteProbe_0");
            Assert.IsNotNull(probe);
            TacticalSpritePresentationController controller = probe.GetComponent<TacticalSpritePresentationController>();
            Assert.IsNotNull(controller);
            Vector3 restPosition = probe.transform.position;
            Quaternion restRotation = probe.transform.rotation;
            int castSignals = 0;
            Action onCast = () => castSignals++;
            controller.CastEffectRequested += onCast;
            try
            {
                controller.StartPresentation(StaticChessPresentationEvent.Idle, restPosition);
                Assert.IsFalse(controller.IsPresenting, "Idle must not start a presentation.");
            }
            finally
            {
                controller.CastEffectRequested -= onCast;
            }
            yield return null;

            Assert.Less(Vector3.Distance(restPosition, probe.transform.position), 0.0001f);
            Assert.Less(Quaternion.Angle(restRotation, probe.transform.rotation), 0.001f);
            Assert.AreEqual(0, castSignals, "Idle must not expose a one-shot signal.");
        }

        private static Dictionary<Transform, TransformState> CaptureChildStates(Transform root)
        {
            var result = new Dictionary<Transform, TransformState>();
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                if (transform != root)
                    result.Add(transform, new TransformState(transform));
            return result;
        }

        private static void AssertChildStatesUnchanged(IReadOnlyDictionary<Transform, TransformState> states)
        {
            foreach (KeyValuePair<Transform, TransformState> pair in states)
            {
                Assert.AreEqual(pair.Value.LocalPosition, pair.Key.localPosition);
                Assert.Less(Quaternion.Angle(pair.Value.LocalRotation, pair.Key.localRotation), 0.001f);
                Assert.AreEqual(pair.Value.LocalScale, pair.Key.localScale);
            }
        }

        private readonly struct TransformState
        {
            public TransformState(Transform transform)
            {
                LocalPosition = transform.localPosition;
                LocalRotation = transform.localRotation;
                LocalScale = transform.localScale;
            }

            public Vector3 LocalPosition { get; }
            public Quaternion LocalRotation { get; }
            public Vector3 LocalScale { get; }
        }
    }
}
