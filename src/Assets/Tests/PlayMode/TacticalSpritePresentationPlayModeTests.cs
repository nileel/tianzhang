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
        private static readonly CombatUnitPresentationEvent[] NonIdleEvents =
        {
            CombatUnitPresentationEvent.Move,
            CombatUnitPresentationEvent.Attack,
            CombatUnitPresentationEvent.Hit,
            CombatUnitPresentationEvent.Cast,
            CombatUnitPresentationEvent.Death,
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
        public IEnumerator ApprovedVisualTransformsPersistWithRuntimeNumericProof()
        {
            Screen.SetResolution(1920, 1080, false);
            LogAssert.Expect(LogType.Error, "[AdventureInstaller] game_bootstrap_missing");
            SceneManager.LoadScene("AdventureScene");
            yield return null;

            Camera camera = Camera.main;
            Assert.IsNotNull(camera, "AdventureScene must expose the frozen visual baseline camera.");
            GameObject board = GameObject.Find(TacticalSpriteProbeMatrix.BoardName);
            Assert.IsNotNull(board, "AdventureScene must persist the visual baseline board.");

            for (int direction = 0; direction < TacticalSpriteProbeMatrix.DirectionCount; direction++)
            {
                Transform probe = board.transform.Find(TacticalSpriteProbeMatrix.FacingProbePrefix + direction);
                Assert.IsNotNull(probe, "Missing static 3D facing probe " + direction + ".");
                Transform figure = probe.Find("FuYuan_Model");
                Transform basePlaceholder = probe.Find("StaticChessBase");
                Assert.IsNotNull(figure);
                Assert.IsNotNull(basePlaceholder);
                Assert.AreEqual(Vector3.zero, figure.localPosition);
                Assert.Less(Quaternion.Angle(figure.localRotation, Quaternion.Euler(-90f, 0f, 0f)), 0.01f);
                Assert.AreEqual(Vector3.one, figure.localScale);
                Assert.AreEqual(Vector3.zero, basePlaceholder.localPosition);
                Assert.Less(Quaternion.Angle(basePlaceholder.localRotation, Quaternion.identity), 0.01f);
                Assert.AreEqual(new Vector3(0.66f, 0.04f, 0.66f), basePlaceholder.localScale);

                Bounds figureBounds = CombinedRendererBounds(figure);
                Bounds baseBounds = CombinedRendererBounds(basePlaceholder);
                Rect figureScreenRect = ScreenRectFromBounds(camera, figureBounds);
                Rect baseScreenRect = ScreenRectFromBounds(camera, baseBounds);
                Debug.Log("[VisualTransformProof] route=3D direction=" + direction +
                          " model=FuYuan_Model localEuler=(-90,0,0)" +
                          " figureWorldMin=" + figureBounds.min.ToString("F4") +
                          " figureWorldMax=" + figureBounds.max.ToString("F4") +
                          " figureScreenRect=" + RectText(figureScreenRect) +
                          " baseLocalPosition=(0,0,0)" +
                          " baseWorldMin=" + baseBounds.min.ToString("F4") +
                          " baseWorldMax=" + baseBounds.max.ToString("F4") +
                          " baseScreenRect=" + RectText(baseScreenRect));
            }

            TacticalSpriteProbeMatrix.SetActiveRoute(true);
            yield return null;

            Transform group = board.transform.Find(TacticalSpriteProbeMatrix.GroupName);
            Assert.IsNotNull(group);
            for (int direction = 0; direction < TacticalSpriteProbeMatrix.DirectionCount; direction++)
            {
                Transform probe = group.Find("TacticalSpriteProbe_" + direction);
                Assert.IsNotNull(probe, "Missing tactical sprite probe " + direction + ".");
                TacticalSpritePresentationController controller = probe.GetComponent<TacticalSpritePresentationController>();
                SpriteRenderer renderer = probe.GetComponentInChildren<SpriteRenderer>(true);
                Assert.IsNotNull(controller);
                Assert.IsNotNull(renderer);
                Assert.IsNotNull(renderer.sprite);
                Assert.AreEqual("FuYuan_TacticalDirection_" + direction, controller.ActiveSpriteName);
                Vector2 normalizedPivot = new Vector2(
                    renderer.sprite.pivot.x / renderer.sprite.rect.width,
                    renderer.sprite.pivot.y / renderer.sprite.rect.height);
                Assert.Less(Vector2.Distance(normalizedPivot, new Vector2(0.5f, 0.18f)), 0.001f);
                Rect spriteScreenRect = ScreenRectFromBounds(camera, renderer.bounds);
                Debug.Log("[VisualTransformProof] route=2D direction=" + direction +
                          " sprite=" + controller.ActiveSpriteName +
                          " fallback=fail_closed" +
                          " normalizedPivot=" + normalizedPivot.ToString("F4") +
                          " worldMin=" + renderer.bounds.min.ToString("F4") +
                          " worldMax=" + renderer.bounds.max.ToString("F4") +
                          " screenRect=" + RectText(spriteScreenRect));
            }

            Transform occlusionProbe = group.Find("TacticalSpriteOcclusionProbe");
            Transform occluder = board.transform.Find("VisualBaselineOccluder");
            Assert.IsNotNull(occlusionProbe);
            Assert.IsNotNull(occluder);
            SpriteRenderer occlusionSprite = occlusionProbe.GetComponentInChildren<SpriteRenderer>(true);
            Renderer occluderRenderer = occluder.GetComponent<Renderer>();
            Assert.IsNotNull(occlusionSprite);
            Assert.IsNotNull(occluderRenderer);
            Rect occlusionSpriteRect = ScreenRectFromBounds(camera, occlusionSprite.bounds);
            Rect occluderRect = ScreenRectFromBounds(camera, occluderRenderer.bounds);
            Rect overlap = RectIntersection(occlusionSpriteRect, occluderRect);
            Debug.Log("[VisualTransformProof] route=2D target=TacticalSpriteOcclusionProbe" +
                      " sprite=" + occlusionSprite.sprite.name +
                      " spriteScreenRect=" + RectText(occlusionSpriteRect) +
                      " occluderScreenRect=" + RectText(occluderRect) +
                      " overlapScreenRect=" + RectText(overlap));
            Assert.Greater(overlap.width * overlap.height, 0f,
                "The existing occluder must overlap the tactical sprite in the frozen camera.");
            Assert.Less(overlap.width * overlap.height,
                occlusionSpriteRect.width * occlusionSpriteRect.height,
                "The existing occluder must not cover the entire tactical sprite bounds.");
            Assert.Less(overlap.yMax, occlusionSpriteRect.yMax,
                "The existing occluder must leave the upper tactical sprite visible.");
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

            foreach (CombatUnitPresentationEvent presentationEvent in NonIdleEvents)
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
                    presentationEvent == CombatUnitPresentationEvent.Cast ? 1 : 0,
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
                controller.StartPresentation(CombatUnitPresentationEvent.Idle, restPosition);
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

        private static Bounds CombinedRendererBounds(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Assert.Greater(renderers.Length, 0, root.name + " must contain a visible renderer.");
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        private static Rect ScreenRectFromBounds(Camera camera, Bounds bounds)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Vector2 screenMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 screenMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 point = camera.WorldToViewportPoint(new Vector3(
                    x == 0 ? min.x : max.x,
                    y == 0 ? min.y : max.y,
                    z == 0 ? min.z : max.z));
                Vector2 proofPoint = new Vector2(point.x * 1920f, point.y * 1080f);
                screenMin = Vector2.Min(screenMin, proofPoint);
                screenMax = Vector2.Max(screenMax, proofPoint);
            }
            return Rect.MinMaxRect(screenMin.x, screenMin.y, screenMax.x, screenMax.y);
        }

        private static Rect RectIntersection(Rect first, Rect second)
        {
            float xMin = Mathf.Max(first.xMin, second.xMin);
            float yMin = Mathf.Max(first.yMin, second.yMin);
            float xMax = Mathf.Min(first.xMax, second.xMax);
            float yMax = Mathf.Min(first.yMax, second.yMax);
            return xMax > xMin && yMax > yMin
                ? Rect.MinMaxRect(xMin, yMin, xMax, yMax)
                : Rect.zero;
        }

        private static string RectText(Rect rect) =>
            "(" + rect.xMin.ToString("F2") + "," + rect.yMin.ToString("F2") + ")-(" +
            rect.xMax.ToString("F2") + "," + rect.yMax.ToString("F2") + ")";

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
