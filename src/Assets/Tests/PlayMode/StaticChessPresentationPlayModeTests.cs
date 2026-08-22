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
    public sealed class StaticChessPresentationPlayModeTests
    {
        [UnityTest]
        public IEnumerator Fixed1920By1080MatrixKeepsAllSixPresentationEventsOnTheRoot()
        {
            Screen.SetResolution(1920, 1080, false);
            LogAssert.Expect(LogType.Error, "[AdventureInstaller] game_bootstrap_missing");
            SceneManager.LoadScene("AdventureScene");
            yield return null;

            foreach (StaticChessPresentationEvent presentationEvent in
                     (StaticChessPresentationEvent[])Enum.GetValues(typeof(StaticChessPresentationEvent)))
            for (int direction = 0; direction < 6; direction++)
            {
                GameObject probe = GameObject.Find("FacingProbe_" + direction);
                Assert.IsNotNull(probe, "Missing frozen six-direction probe " + direction + ".");
                StaticChessPresentationController controller = probe.GetComponent<StaticChessPresentationController>();
                Assert.IsNotNull(controller);
                Assert.Zero(probe.GetComponentsInChildren<Animator>(true).Length);
                Assert.Zero(probe.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length);

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
                    controller.Tick(0.16f);
                    controller.Tick(1f);
                }
                finally
                {
                    controller.CastEffectRequested -= onCast;
                }

                Assert.IsFalse(controller.IsPresenting);
                Assert.Less(Vector3.Distance(restPosition, probe.transform.position), 0.0001f);
                Assert.Less(Quaternion.Angle(restRotation, probe.transform.rotation), 0.001f);
                Assert.AreEqual(
                    presentationEvent == StaticChessPresentationEvent.Cast ? 1 : 0,
                    castSignals,
                    "Only cast may expose one one-shot visual-effect signal.");
                AssertChildStatesUnchanged(childStates);
            }
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
