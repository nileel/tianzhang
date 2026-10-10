using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Features.Adventure;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongTacticalCameraPlayModeTests
    {
        private static TianZhang.Content.CombatRuleSetData GrowthRules
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.AssetDatabase.LoadAssetAtPath<TianZhang.Content.CombatRuleSetData>("Assets/Data/CombatRuleSet.asset");
#else
                throw new System.NotSupportedException("Editor fixture requires the production rule asset.");
#endif
            }
        }

        private readonly List<GameObject> temporary = new List<GameObject>();
        private Keyboard keyboard;
        private Mouse mouse;
        private Camera camera;
        private AdventureCameraController controller;
        private Renderer board;
        private InputSettings.BackgroundBehavior originalBackgroundBehavior;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode originalEditorInputBehavior;
#endif

        [SetUp]
        public void SetUp()
        {
            originalBackgroundBehavior = InputSystem.settings.backgroundBehavior;
#if UNITY_EDITOR
            originalEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            var cameraObject = Track(new GameObject("TacticalCameraInputTest"));
            camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 4;
            camera.transform.position = new Vector3(0, 8, -8);
            camera.transform.LookAt(Vector3.zero);
            var battlefield = Track(new GameObject("TestBattlefield"));
            var tile = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            tile.transform.SetParent(battlefield.transform, false);
            tile.transform.localScale = new Vector3(20, 0.1f, 20);
            board = tile.GetComponent<Renderer>();
            controller = cameraObject.AddComponent<AdventureCameraController>();
            controller.Configure(camera, battlefield.transform, 0, 4);
            MouseAt(new Vector2(Screen.width / 2f, Screen.height / 2f), false);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject value in temporary)
                if (value != null) Object.DestroyImmediate(value);
            temporary.Clear();
            InputSystem.settings.backgroundBehavior = originalBackgroundBehavior;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = originalEditorInputBehavior;
#endif
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        }

        [UnityTest]
        public IEnumerator KeyboardAndArrowMovementHaveEqualSpeedAndNormalizedDiagonal()
        {
            yield return null;
            var origin = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            Keys(Key.D);
            Tick();
            float straightDistance = Vector3.Distance(origin, camera.transform.position);
            Assert.Greater(straightDistance, 0);
            Assert.That(straightDistance, Is.EqualTo(4 * Time.unscaledDeltaTime).Within(0.001f));
            Assert.AreEqual(origin.y, camera.transform.position.y);
            Assert.AreEqual(rotation, camera.transform.rotation);
            camera.transform.position = origin;
            Keys(Key.RightArrow);
            Tick();
            Assert.That(Vector3.Distance(origin, camera.transform.position), Is.EqualTo(straightDistance).Within(0.001f));
            camera.transform.position = origin;
            Keys(Key.W, Key.D);
            Tick();
            Assert.That(Vector3.Distance(origin, camera.transform.position), Is.EqualTo(straightDistance).Within(0.001f));
            camera.transform.position = origin;
            Keys(Key.UpArrow, Key.RightArrow);
            Tick();
            Assert.That(Vector3.Distance(origin, camera.transform.position), Is.EqualTo(straightDistance).Within(0.001f));
            Keys();
        }

        [UnityTest]
        public IEnumerator AutomaticLateUpdateUsesUnscaledFrameTime()
        {
            yield return null;
            float priorScale = Time.timeScale;
            try
            {
                Time.timeScale = 0;
                var probe = camera.gameObject.AddComponent<TacticalCameraFrameProbe>();
                probe.Begin(camera);
                Keys(Key.D);
                int deadline = Time.frameCount + 30;
                while (probe.Samples < 5 && Time.frameCount < deadline) yield return null;
                Keys();
                Assert.GreaterOrEqual(probe.Samples, 5, "Five real LateUpdate frames must observe the injected held key.");
                Assert.Greater(probe.MeasuredDistance, 0);
                Assert.That(probe.MeasuredDistance, Is.EqualTo(probe.ExpectedDistance).Within(0.001f));
            }
            finally
            {
                Time.timeScale = priorScale;
                Keys();
            }
        }
        [UnityTest]
        public IEnumerator KeyboardFocusClampsToRealRendererBoundsAndReturnsFromEdge()
        {
            yield return null;
            Keys(Key.W, Key.D);
            int steps = Mathf.CeilToInt(40f / (4f * Time.unscaledDeltaTime)) + 2;
            for (int i = 0; i < steps; i++) Tick();
            Vector3 focus = GroundFocus();
            Bounds bounds = board.bounds;
            Assert.That(focus.x, Is.InRange(bounds.min.x - 0.001f, bounds.max.x + 0.001f));
            Assert.That(focus.z, Is.InRange(bounds.min.z - 0.001f, bounds.max.z + 0.001f));
            Vector3 atEdge = camera.transform.position;
            Tick();
            Assert.That(Vector3.Distance(atEdge, camera.transform.position), Is.LessThan(0.001f));
            Keys(Key.A, Key.S);
            Tick();
            Assert.Greater(Vector3.Distance(atEdge, camera.transform.position), 0);
            Keys();
        }

        [UnityTest]
        public IEnumerator MiddleMouseDragMovesGroundPlaneAndDisableClearsDrag()
        {
            yield return null;
            Vector2 start = new Vector2(Screen.width / 2f, Screen.height / 2f);
            MouseAt(start, true);
            Assert.AreSame(mouse, Mouse.current);
            Assert.IsTrue(mouse.middleButton.wasPressedThisFrame, "Injected mouse press must reach the active player input buffer.");
            Tick();
            Vector3 origin = camera.transform.position;
            MouseAt(start + new Vector2(30, 10), true);
            Tick();
            Assert.Greater(Vector3.Distance(origin, camera.transform.position), 0.001f);
            Assert.AreEqual(origin.y, camera.transform.position.y);
            var afterDrag = camera.transform.position;
            controller.enabled = false;
            MouseAt(start + new Vector2(90, 30), true);
            controller.enabled = true;
            Tick();
            Assert.That(Vector3.Distance(afterDrag, camera.transform.position), Is.LessThan(0.001f),
                "Reenable while middle button remains held must not resume the previous drag.");
            MouseAt(start, false);
            Tick();
            MouseAt(start, true);
            Tick();
            MouseAt(start + new Vector2(30, 10), true);
            Tick();
            Assert.Greater(Vector3.Distance(afterDrag, camera.transform.position), 0.001f);
            MouseAt(start, false);
            Tick();
        }

        [UnityTest]
        public IEnumerator UiPointerAndInputFieldFocusDoNotPanOrStartDrag()
        {
            var eventObject = Track(new GameObject("TacticalCameraEventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule)));
            var events = eventObject.GetComponent<EventSystem>();
            var uiModule = eventObject.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            var canvasObject = Track(new GameObject("TacticalCameraCanvas", typeof(Canvas), typeof(GraphicRaycaster)));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var panel = Track(new GameObject("CameraBlockingPanel", typeof(RectTransform), typeof(Image)));
            panel.transform.SetParent(canvasObject.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(120, 120);
            var fieldObject = Track(new GameObject("CameraInputFocus", typeof(RectTransform), typeof(Image), typeof(InputField)));
            fieldObject.transform.SetParent(canvasObject.transform, false);
            var input = fieldObject.GetComponent<InputField>();
            var label = Track(new GameObject("InputText", typeof(RectTransform), typeof(Text)));
            label.transform.SetParent(fieldObject.transform, false);
            input.textComponent = label.GetComponent<Text>();
            input.textComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            yield return null;
            Canvas.ForceUpdateCanvases();
            Vector3 origin = camera.transform.position;
            Vector2 center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            MouseAt(center, true);
            Keys(Key.D);
            uiModule.Process();
            Assert.IsTrue(events.IsPointerOverGameObject(mouse.deviceId), "Real UI input module must own this pointer.");
            Tick();
            Assert.That(Vector3.Distance(origin, camera.transform.position), Is.LessThan(0.001f));
            Keys();
            panel.SetActive(false);
            fieldObject.SetActive(false);
            MouseAt(center + new Vector2(100, 100), true);
            uiModule.Process();
            Tick();
            Assert.That(Vector3.Distance(origin, camera.transform.position), Is.LessThan(0.001f),
                "A drag begun over UI cannot activate after leaving UI while the button remains held.");
            MouseAt(Vector2.zero, false);
            Tick();
            fieldObject.SetActive(true);
            events.SetSelectedGameObject(fieldObject);
            input.ActivateInputField();
            yield return null;
            Assert.IsTrue(input.isFocused);
            Keys(Key.W);
            Tick();
            Assert.That(Vector3.Distance(origin, camera.transform.position), Is.LessThan(0.001f));
            events.SetSelectedGameObject(null);
            input.DeactivateInputField();
            Keys();
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator SavedAdventureSceneReopensWithSizeFourAndExplicitCameraReferences()
        {
            controller.enabled = false;
            var bootstrapObject = Track(new GameObject("CameraSceneBootstrap"));
            bootstrapObject.AddComponent<TianZhang.Bootstrap.GameBootstrap>();
            var runtime = TianZhang.Bootstrap.GameBootstrap.RequireRuntime();
            var config = UnityEditor.AssetDatabase.LoadAssetAtPath<TianZhang.Game.CharacterCreation.CharacterCreationPointBuyConfig>(
                "Assets/Resources/Data/CharacterCreation/CharacterCreationPointBuyConfig.asset");
            var profile = TianZhang.Features.CharacterCreation.CharacterCreationManager.CreateProfile(
                TianZhang.Features.CharacterCreation.CharacterCreationCatalog.CreateDefaultDraft(), config, GrowthRules);
            runtime.BeginNewGame(TianZhang.Character.CharacterRuntimeProfile.FromDefinition("player", profile, GrowthRules),
                TianZhang.Cultivation.CultivationState.FromDefinition(profile.foundationPurpleMansionState), "guanzhong_hub");
            Object.Destroy(profile);
            runtime.EnterWorld("guanzhong_hub");
            runtime.EnterSettlement("guanzhong_city");
            runtime.EnterAdventure("guanzhong_wild", TianZhang.Gameplay.Contracts.SceneReturnTarget.Settlement("guanzhong_city"));
            yield return SceneManager.LoadSceneAsync(TianZhang.Gameplay.Contracts.GameplaySceneNames.Adventure, LoadSceneMode.Additive);
            Scene adventure = SceneManager.GetSceneByName(TianZhang.Gameplay.Contracts.GameplaySceneNames.Adventure);
            AdventureCameraController saved = null;
            foreach (GameObject root in adventure.GetRootGameObjects())
            {
                saved = root.GetComponentInChildren<AdventureCameraController>(true);
                if (saved != null) break;
            }
            Assert.IsNotNull(saved, "Saved formal scene must explicitly serialize the camera owner.");
            var actualCamera = Field<Camera>(saved, "controlledCamera");
            Assert.IsNotNull(actualCamera);
            Assert.IsTrue(actualCamera.orthographic);
            Assert.AreEqual(4, actualCamera.orthographicSize);
            Assert.AreSame(actualCamera.gameObject, saved.gameObject);
            Assert.IsNotNull(Field<Transform>(saved, "battlefield"));
            Assert.AreEqual("GuanzhongBattlefield", Field<Transform>(saved, "battlefield").name);
            yield return SceneManager.UnloadSceneAsync(adventure);
        }
#endif

        private GameObject Track(GameObject value) { temporary.Add(value); return value; }
        private static T Field<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private void Tick() => typeof(AdventureCameraController).GetMethod("LateUpdate",
            BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
        private void Keys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }
        private void MouseAt(Vector2 position, bool held)
        {
            var state = new MouseState { position = position }.WithButton(MouseButton.Middle, held);
            InputSystem.QueueStateEvent(mouse, state);
            InputSystem.Update();
        }
        private Vector3 GroundFocus()
        {
            var plane = new Plane(Vector3.up, Vector3.zero);
            Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            Assert.IsTrue(plane.Raycast(ray, out float distance));
            return ray.GetPoint(distance);
        }
    }

    [DefaultExecutionOrder(1000)]
    public sealed class TacticalCameraFrameProbe : MonoBehaviour
    {
        private Camera observed;
        private Vector3 previous;
        public int Samples { get; private set; }
        public float ExpectedDistance { get; private set; }
        public float MeasuredDistance { get; private set; }
        public void Begin(Camera value) { observed = value; previous = value.transform.position; }
        private void LateUpdate()
        {
            if (observed == null) return;
            if (Keyboard.current != null && Keyboard.current.dKey.isPressed)
            {
                Samples++;
                ExpectedDistance += 4f * Time.unscaledDeltaTime;
                MeasuredDistance += Vector3.Distance(previous, observed.transform.position);
            }
            previous = observed.transform.position;
        }
    }
}
