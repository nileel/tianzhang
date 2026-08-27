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
        public IEnumerator Fixed1920By1080MatrixSamplesFrozenEventTimesAndRestoresOnlyTheRoot()
        {
            Screen.SetResolution(1920, 1080, false);
            LogAssert.Expect(LogType.Error, "[AdventureInstaller] game_bootstrap_missing");
            SceneManager.LoadScene("AdventureScene");
            yield return null;

            foreach (CombatUnitPresentationEvent presentationEvent in
                     (CombatUnitPresentationEvent[])Enum.GetValues(typeof(CombatUnitPresentationEvent)))
            for (int direction = 0; direction < 6; direction++)
            {
                GameObject probe = GameObject.Find("FacingProbe_" + direction);
                Assert.IsNotNull(probe, "Missing frozen six-direction probe " + direction + ".");
                StaticChessPresentationController controller = probe.GetComponent<StaticChessPresentationController>();
                Assert.IsNotNull(controller);
                Assert.Zero(probe.GetComponentsInChildren<Animator>(true).Length);
                Assert.Zero(probe.GetComponentsInChildren<Animation>(true).Length);
                Assert.Zero(probe.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length);

                Vector3 restPosition = probe.transform.position;
                Quaternion restRotation = probe.transform.rotation;
                Vector3 approvedPosition = restPosition + restRotation * Vector3.forward * 0.2f;
                Dictionary<Transform, TransformState> childStates = CaptureChildStates(probe.transform);
                int castSignals = 0;
                int effectSignals = 0;
                Action onCast = () => castSignals++;
                Action<CombatUnitPresentationEvent> onEffect = eventType =>
                {
                    Assert.AreEqual(presentationEvent, eventType);
                    effectSignals++;
                };
                controller.CastEffectRequested += onCast;
                controller.MotionEffectRequested += onEffect;
                try
                {
                    controller.StartPresentation(presentationEvent, approvedPosition);
                    AssertAtStart(presentationEvent, controller, probe.transform, restPosition, restRotation);

                    if (presentationEvent == CombatUnitPresentationEvent.Idle)
                    {
                        Assert.IsFalse(controller.IsPresenting);
                        Assert.AreEqual(0, controller.EffectPlayCount);
                        Assert.AreEqual(0, controller.CuePlayCount);
                        Assert.AreEqual(0, castSignals);
                        Assert.AreEqual(0, effectSignals);
                        continue;
                    }

                    float duration = StaticChessPresentationController.DurationFor(presentationEvent);
                    Assert.AreEqual(presentationEvent == CombatUnitPresentationEvent.Death
                        ? StaticChessPresentationController.DeathEventDuration
                        : StaticChessPresentationController.StandardEventDuration, duration, 0.0001f);
                    float elapsedProgress = 0f;
                    foreach (float sampleProgress in FrozenSampleProgresses(presentationEvent))
                    {
                        controller.Tick((sampleProgress - elapsedProgress) * duration);
                        elapsedProgress = sampleProgress;
                        Assert.AreEqual(sampleProgress, controller.PresentationProgress, 0.001f,
                            "The event must sample the frozen normalized time.");
                        AssertAtFrozenSample(presentationEvent, sampleProgress, probe.transform,
                            restPosition, restRotation, approvedPosition);

                        bool atOrPastKey = sampleProgress >= StaticChessPresentationController.KeyProgressFor(presentationEvent);
                        Assert.AreEqual(atOrPastKey ? 1 : 0, controller.EffectPlayCount);
                        Assert.AreEqual(atOrPastKey ? 1 : 0, controller.CuePlayCount);
                        Assert.AreEqual(atOrPastKey ? 1 : 0, effectSignals);
                        Assert.AreEqual(presentationEvent == CombatUnitPresentationEvent.Cast && atOrPastKey ? 1 : 0,
                            castSignals, "Only cast may emit one signal at its frozen key time.");
                        if (atOrPastKey)
                        {
                            Assert.IsNotNull(controller.ActiveEffect);
                            Assert.IsNotNull(controller.ActiveEffect.GetComponentInChildren<ParticleSystem>(true));
                            Assert.AreEqual(ExpectedCueName(presentationEvent), controller.LastPlayedCue.name);
                        }
                    }

                    controller.Tick((1f - elapsedProgress) * duration + 0.001f);
                    Assert.IsFalse(controller.IsPresenting);
                    Assert.AreEqual(CombatUnitPresentationEvent.Idle, controller.ActiveEvent);
                    Assert.Less(Vector3.Distance(restPosition, probe.transform.position), 0.0001f);
                    Assert.Less(Quaternion.Angle(restRotation, probe.transform.rotation), 0.001f);
                    Assert.AreEqual(1, controller.EffectPlayCount);
                    Assert.AreEqual(1, controller.CuePlayCount);
                    Assert.AreEqual(1, effectSignals);
                    Assert.AreEqual(presentationEvent == CombatUnitPresentationEvent.Cast ? 1 : 0, castSignals);
                    AssertChildStatesUnchanged(childStates);
                }
                finally
                {
                    controller.CastEffectRequested -= onCast;
                    controller.MotionEffectRequested -= onEffect;
                }
            }
        }

        private static float[] FrozenSampleProgresses(CombatUnitPresentationEvent presentationEvent)
        {
            switch (presentationEvent)
            {
                case CombatUnitPresentationEvent.Move: return new[] { 0.15f, 0.85f, 0.95f };
                case CombatUnitPresentationEvent.Attack: return new[] { 0.20f, 0.55f, 0.75f };
                case CombatUnitPresentationEvent.Hit: return new[] { 0.10f, 0.325f, 0.55f, 0.80f };
                case CombatUnitPresentationEvent.Cast: return new[] { 0.20f, 0.70f, 0.85f };
                case CombatUnitPresentationEvent.Death: return new[] { 0.25f, 0.70f, 0.85f };
                default: throw new ArgumentOutOfRangeException(nameof(presentationEvent));
            }
        }

        private static void AssertAtStart(
            CombatUnitPresentationEvent presentationEvent,
            StaticChessPresentationController controller,
            Transform root,
            Vector3 restPosition,
            Quaternion restRotation)
        {
            Assert.AreEqual(presentationEvent, controller.ActiveEvent);
            Assert.Less(Vector3.Distance(restPosition, root.position), 0.0001f);
            Assert.Less(Quaternion.Angle(restRotation, root.rotation), 0.001f);
            Assert.AreEqual(0, controller.EffectPlayCount);
            Assert.AreEqual(0, controller.CuePlayCount);
        }

        private static void AssertAtFrozenSample(
            CombatUnitPresentationEvent presentationEvent,
            float progress,
            Transform root,
            Vector3 restPosition,
            Quaternion restRotation,
            Vector3 approvedPosition)
        {
            switch (presentationEvent)
            {
                case CombatUnitPresentationEvent.Move:
                    if (Mathf.Abs(progress - 0.15f) < 0.001f)
                    {
                        Assert.Greater(root.position.y, restPosition.y + 0.10f);
                        Assert.Less(Vector3.Distance(new Vector3(root.position.x, 0f, root.position.z),
                            new Vector3(restPosition.x, 0f, restPosition.z)), 0.001f);
                    }
                    if (Mathf.Abs(progress - 0.85f) < 0.001f)
                    {
                        Assert.Greater(root.position.y, approvedPosition.y + 0.10f);
                        Assert.Less(Vector3.Distance(new Vector3(root.position.x, 0f, root.position.z),
                            new Vector3(approvedPosition.x, 0f, approvedPosition.z)), 0.001f);
                    }
                    break;
                case CombatUnitPresentationEvent.Attack:
                    if (Mathf.Abs(progress - 0.20f) < 0.001f)
                        Assert.Less(Vector3.Distance(restPosition, root.position), 0.001f);
                    if (Mathf.Abs(progress - 0.55f) < 0.001f)
                    {
                        Assert.Greater(Vector3.Dot(root.position - restPosition, restRotation * Vector3.forward), 0.08f);
                        Assert.Greater(Quaternion.Angle(restRotation, root.rotation), 8f);
                    }
                    break;
                case CombatUnitPresentationEvent.Hit:
                    if (Mathf.Abs(progress - 0.10f) < 0.001f)
                        Assert.Less(Vector3.Distance(restPosition, root.position), 0.001f);
                    if (Mathf.Abs(progress - 0.325f) < 0.001f)
                    {
                        Assert.Greater(Vector3.Distance(restPosition, root.position), 0.03f);
                        Assert.Greater(Quaternion.Angle(restRotation, root.rotation), 3f);
                    }
                    break;
                case CombatUnitPresentationEvent.Cast:
                    if (Mathf.Abs(progress - 0.20f) < 0.001f || Mathf.Abs(progress - 0.70f) < 0.001f)
                        Assert.Greater(root.position.y, restPosition.y + 0.04f);
                    break;
                case CombatUnitPresentationEvent.Death:
                    if (Mathf.Abs(progress - 0.25f) < 0.001f)
                        Assert.Less(root.position.y, restPosition.y - 0.16f);
                    if (Mathf.Abs(progress - 0.70f) < 0.001f)
                        Assert.Greater(Quaternion.Angle(restRotation, root.rotation), 50f);
                    break;
            }
        }

        private static string ExpectedCueName(CombatUnitPresentationEvent presentationEvent)
        {
            switch (presentationEvent)
            {
                case CombatUnitPresentationEvent.Move: return "FuYuan_StaticChessMotion_Move";
                case CombatUnitPresentationEvent.Attack: return "FuYuan_StaticChessMotion_Attack";
                case CombatUnitPresentationEvent.Hit: return "FuYuan_StaticChessMotion_Hit";
                case CombatUnitPresentationEvent.Cast: return "FuYuan_StaticChessMotion_Cast";
                case CombatUnitPresentationEvent.Death: return "FuYuan_StaticChessMotion_Death";
                default: throw new ArgumentOutOfRangeException(nameof(presentationEvent));
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
