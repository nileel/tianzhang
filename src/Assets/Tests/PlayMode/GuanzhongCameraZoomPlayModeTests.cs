using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Combat;
using TianZhang.Features.Adventure;
using TianZhang.Features.CharacterCreation;
using TianZhang.Features.CombatPresentation;
using TianZhang.Features.Settlement;
using TianZhang.Features.WorldMap;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.Persistence;
using TianZhang.World;
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
    public sealed class GuanzhongCameraZoomPlayModeTests
    {
        private Mouse mouse;
        private Keyboard keyboard;
        private string saveDirectory;
        private InputSettings.BackgroundBehavior background;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
#endif
        private static string Evidence => Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/f2-camera"));

        [UnitySetUp]
        public IEnumerator EnterSettlement()
        {
            background = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            mouse = InputSystem.AddDevice<Mouse>();
            keyboard = InputSystem.AddDevice<Keyboard>();
            var stale = Object.FindFirstObjectByType<GameBootstrap>();
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            saveDirectory = Path.Combine(Application.temporaryCachePath, "TianZhang-F2-" + Guid.NewGuid().ToString("N"));
            typeof(GameBootstrap).GetField("slotStore", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(GameBootstrap.RequireInstance(), new GameSaveSlotStore(saveDirectory));
            var creation = Find<CharacterCreationController>();
            creation.Open();
            Assert.IsTrue(creation.SelectOrigin("origin_minor_clan"));
            creation.Submit("f2-camera", "镜头缩放验证");
            yield return SceneReady(GameplaySceneNames.World);
            var world = Find<WorldMapController>();
            Assert.IsTrue(world.SelectNode("guanzhong_hub"));
            world.EnterSelectedLocation();
            yield return SceneReady(GameplaySceneNames.Settlement);
            Directory.CreateDirectory(Evidence);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            InputSystem.settings.backgroundBehavior = background;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
#endif
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            if (bootstrap != null) { Object.Destroy(bootstrap.gameObject); yield return null; }
            Scene previous = SceneManager.GetActiveScene();
            Scene empty = SceneManager.CreateScene("F2CameraCleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(previous);
            if (saveDirectory != null && Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory, true);
        }

        [UnityTest, Timeout(300000)] public IEnumerator FormalZoomAt1920() => Verify(1920, 1080);
        [UnityTest, Timeout(300000)] public IEnumerator FormalZoomAt1366() => Verify(1366, 768);

        private IEnumerator Verify(int width, int height)
        {
            GuanzhongCombatEquipmentPlayModeTests.SetGameSize(width, height);
            for (int frame = 0; frame < 60 && (Screen.width != width || Screen.height != height); frame++) yield return null;
            Assert.AreEqual(width, Screen.width); Assert.AreEqual(height, Screen.height);
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild"));
            yield return SceneReady(GameplaySceneNames.Adventure);
            var adventure = Find<AdventureController>();
            for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            Assert.AreEqual(AdventureSceneState.Exploration, adventure.CurrentState);
            var camera = Camera.main;
            Vector3 origin = camera.transform.position;
            Vector3 focus = GroundFocus(camera);
            Vector2 center = new Vector2(width * .5f, height * .5f);
            Assert.AreEqual(5f, camera.orthographicSize);
            yield return Scroll(center, 1f);
            Assert.AreEqual(4.5f, camera.orthographicSize, .001f, "Exploration must allow zoom.");
            yield return Scroll(center, -1f);
            adventure.SetEncounterRandomSource(new EncounterRandom());
            Assert.IsTrue(Find<AdventureInputController>().SelectNode("shijiahou_encounter"));
            var encounter = Find<EncounterCoordinator>();
            yield return Ready(encounter);
            var hud = Find<CombatHudPresenter>();
            var session = Field<CombatSession>(encounter, "session");
            var spawned = Field<AdventureSpawnSet>(encounter, "spawned");
            var provider = Find<Static3DCombatUnitPresentationProvider>();
            var bar = Find<CombatActionBarView>();
            var movement = Find<CombatMovementView>();
            var input = Find<CombatCommandInput>();
            var preview = Find<CombatActionPreviewView>();
            Assert.IsTrue(provider.TryGetVisibleBounds("player", out Bounds playerBounds));
            Vector3 playerCenter = camera.WorldToViewportPoint(playerBounds.center);
            Assert.That(playerCenter.x, Is.InRange(.09f, .9f));
            Assert.That(playerCenter.y, Is.InRange(.2f, .8f), "Default view must keep the player above the hotbar.");
            Assert.Greater(camera.WorldToScreenPoint(playerBounds.min).y, ScreenRect(Rect("ActionViewport")).yMax,
                "The full player body must remain above the hotbar.");
            yield return Capture("default", camera);
            Rect bountyArea = ScreenRect(Rect("CombatBountyTrackerRoot"));
            foreach (var healthBar in Field<CombatUnitHealthBarView.Bar[]>(Find<CombatUnitHealthBarView>(), "bars"))
                if (healthBar.root.gameObject.activeInHierarchy)
                    Assert.IsFalse(ScreenRect(healthBar.root).Overlaps(bountyArea),
                        "Default framing must keep head health bars clear of the bounty panel.");

            yield return Scroll(center, 100f);
            Assert.AreEqual(3f, camera.orthographicSize);
            Assert.Less(Vector3.Distance(focus, GroundFocus(camera)), .001f);
            yield return Capture("nearest", camera);
            yield return Scroll(center, -100f);
            Assert.AreEqual(7f, camera.orthographicSize);
            Assert.Less(Vector3.Distance(focus, GroundFocus(camera)), .001f);
            yield return Capture("farthest", camera);

            // Actual held-key input at the closest zoom exercises all four pan corners.
            yield return Scroll(center, 100f);
            Bounds board = BoardBounds();
            foreach (var corner in new[]
            {
                ("sw", board.min.x, board.min.z), ("se", board.max.x, board.min.z),
                ("ne", board.max.x, board.max.z), ("nw", board.min.x, board.max.z),
            })
            {
                yield return PanTo(camera, new Vector3(corner.Item2, .34f, corner.Item3));
                yield return Capture("near-pan-" + corner.Item1, camera);
            }
            // Restore only the camera fixture after recording real keyboard panning.
            camera.transform.position = origin;
            yield return Scroll(center, -4f);
            Assert.AreEqual(5f, camera.orthographicSize);

            yield return Click(bar.MoveButton);
            Assert.IsTrue(movement.IsSelecting);
            int ap = spawned.Player.ActionPointsRemainingUnits;
            yield return Scroll(center, 1f);
            Assert.AreEqual(4.5f, camera.orthographicSize);
            Assert.IsTrue(movement.IsSelecting, "Zoom must not cancel movement selection.");
            Assert.AreEqual(ap, spawned.Player.ActionPointsRemainingUnits);
            var centers = Field<Dictionary<Vector2Int, Vector3>>(movement, "centers");
            CombatMovementOption chosen = null;
            Vector2 destination = default;
            foreach (var option in hud.Snapshot.Movement.Options.OrderBy(value => value.Cost))
            {
                Vector2 point = camera.WorldToScreenPoint(centers[new Vector2Int(option.Destination.Q, option.Destination.R)]);
                if (point.x < 0 || point.x >= width || point.y < 0 || point.y >= height) continue;
                yield return Pointer(point);
                if (movement.Selected == null || !movement.Selected.Destination.Equals(option.Destination)) continue;
                chosen = option; destination = point; break;
            }
            Assert.NotNull(chosen, "A legal ground cell must remain clickable after zoom.");
            yield return Capture("move-preview", camera);
            yield return Click(destination);
            yield return Ready(encounter);
            Assert.AreEqual(chosen.Destination.Q, spawned.Player.Position.Q);
            Assert.AreEqual(chosen.Destination.R, spawned.Player.Position.R);
            Assert.AreEqual(ap - chosen.Cost, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual(4.5f, camera.orthographicSize);

            Assert.IsTrue(provider.TryGetVisibleBounds("player", out playerBounds));
            yield return Click((Vector2)camera.WorldToScreenPoint(playerBounds.center));
            Assert.AreEqual("player", hud.InspectedTargetId, "Body picking must use the zoomed camera.");
            yield return Scroll(Center(Rect("TargetBody")), -1f);
            Assert.AreEqual(4.5f, camera.orthographicSize, "Details own their wheel input.");
            yield return Scroll(Center(Rect("ActionViewport")), -1f);
            Assert.AreEqual(4.5f, camera.orthographicSize, "The action bar owns its wheel input.");
            hud.ClearInspectedTarget();

            // Presentation-only long-log fixture proves the wheel reaches the real ScrollRect.
            var log = Find<CombatLogView>();
            for (int line = 0; line < 32; line++) log.Append("镜头滚轮与战报滚动隔离验证 " + line);
            yield return Click(Rect("ToggleLogButton").GetComponent<Button>());
            var logScroll = Rect("CombatLogViewport").GetComponent<ScrollRect>();
            yield return null;
            float beforeScroll = logScroll.verticalNormalizedPosition;
            yield return Scroll(Center(logScroll.viewport), 1f);
            Assert.Greater(logScroll.verticalNormalizedPosition, beforeScroll);
            Assert.AreEqual(4.5f, camera.orthographicSize);
            yield return Capture("ui-scroll", camera);
            yield return Click(Rect("CloseLogButton").GetComponent<Button>());

            Assert.IsTrue(bar.SecondArtButton.interactable, "The self-targeting spell must have a legal target.");
            yield return Click(bar.SecondArtButton);
            Assert.IsTrue(input.IsSelectingAttack);
            yield return Scroll(center, -1f);
            Assert.AreEqual(5f, camera.orthographicSize);
            Assert.IsTrue(input.IsSelectingAttack, "Zoom must not cancel spell selection.");
            Assert.IsTrue(movement.TryPickCell(center, out _), "Ground picking remains aligned during targeting.");
            yield return Capture("spell-preview", camera);
            yield return Click(preview.CancelButton);

            encounter.RequestEndTurn("player");
            bool sawEnemy = false;
            for (float deadline = Time.realtimeSinceStartup + 30f; Time.realtimeSinceStartup < deadline;)
            {
                if (session.CurrentActorId != null && session.CurrentActorId != "player" && provider.IsPresenting)
                { sawEnemy = true; break; }
                yield return null;
            }
            Assert.IsTrue(sawEnemy, "Observe a real enemy presentation before testing its zoom input.");
            yield return Scroll(center, 1f);
            Assert.AreEqual(4.5f, camera.orthographicSize, "Enemy presentation must not lock the camera.");
            yield return Capture("enemy-action", camera);
            yield return Ready(encounter);
            Assert.AreEqual(4.5f, camera.orthographicSize, "The next player turn must keep the selected zoom.");
        }

        private IEnumerator Scroll(Vector2 point, float ticks)
        {
            yield return Pointer(point);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point, scroll = new Vector2(0f, ticks) });
            yield return null;
            yield return Pointer(point);
        }

        private IEnumerator Pointer(Vector2 point, bool down = false)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left, down));
            yield return null; yield return null;
        }

        private IEnumerator Click(Vector2 point)
        {
            yield return Pointer(point);
            yield return Pointer(point, true);
            yield return Pointer(point);
        }

        private IEnumerator Click(Button button) => Click(Center(button.GetComponent<RectTransform>()));

        private IEnumerator PanTo(Camera camera, Vector3 target)
        {
            for (float deadline = Time.realtimeSinceStartup + 20f; Time.realtimeSinceStartup < deadline;)
            {
                Vector3 delta = target - GroundFocus(camera);
                if (Mathf.Abs(delta.x) < .1f && Mathf.Abs(delta.z) < .1f) break;
                var keys = new List<Key>();
                if (Mathf.Abs(delta.x) >= .1f) keys.Add(delta.x > 0 ? Key.D : Key.A);
                if (Mathf.Abs(delta.z) >= .1f) keys.Add(delta.z > 0 ? Key.W : Key.S);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys.ToArray()));
                yield return null;
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null; yield return null;
            Assert.Less(Vector3.Distance(target, GroundFocus(camera)), .16f, "Real keyboard input must reach the pan corner.");
        }

        private static IEnumerator Capture(string label, Camera camera)
        {
#if UNITY_EDITOR
            for (float deadline = Time.realtimeSinceStartup + 120f;
                UnityEditor.ShaderUtil.anythingCompiling && Time.realtimeSinceStartup < deadline;) yield return null;
            Assert.IsFalse(UnityEditor.ShaderUtil.anythingCompiling);
#endif
            yield return null;
            Canvas.ForceUpdateCanvases();
            Transform backdrop = camera.transform.Find("GuanzhongEnvironment/Environment_DistantCanyon");
            Assert.NotNull(backdrop);
            Vector3 min = camera.WorldToViewportPoint(backdrop.TransformPoint(new Vector3(-.5f, -.5f, 0f)));
            Vector3 max = camera.WorldToViewportPoint(backdrop.TransformPoint(new Vector3(.5f, .5f, 0f)));
            Assert.LessOrEqual(min.x, 0f); Assert.LessOrEqual(min.y, 0f);
            Assert.GreaterOrEqual(max.x, 1f); Assert.GreaterOrEqual(max.y, 1f);
            string path = Path.Combine(Evidence, label + "-" + Screen.width);
            var provider = Find<Static3DCombatUnitPresentationProvider>();
            provider.TryGetVisibleBounds("player", out Bounds body);
            File.WriteAllText(path + ".txt", $"scene={SceneManager.GetActiveScene().name}; size={Screen.width}x{Screen.height}; " +
                $"camera={camera.name}; position={camera.transform.position:F4}; pitch={camera.transform.eulerAngles:F4}; " +
                $"ortho={camera.orthographicSize:F4}; focus={GroundFocus(camera):F4}; board={BoardBounds()}; " +
                $"playerBounds={body}; playerScreen={camera.WorldToScreenPoint(body.center):F2}; " +
                $"backgroundViewportMin={min:F4}; backgroundViewportMax={max:F4}\n" +
                "bountyScreen=" + ScreenRect(Rect("CombatBountyTrackerRoot")) + "\n" +
                string.Join("\n", Field<CombatUnitHealthBarView.Bar[]>(Find<CombatUnitHealthBarView>(), "bars")
                    .Where(bar => bar.root.gameObject.activeInHierarchy)
                    .Select(bar => bar.root.name + "=" + ScreenRect(bar.root))));
            if (File.Exists(path + ".png")) File.Delete(path + ".png");
            ScreenCapture.CaptureScreenshot(path + ".png");
            for (int frame = 0; frame < 120 && !File.Exists(path + ".png"); frame++) yield return null;
            Assert.IsTrue(File.Exists(path + ".png"));
            yield return null;
        }

        private static Vector3 GroundFocus(Camera camera)
        {
            var plane = new Plane(Vector3.up, new Vector3(0f, .34f, 0f));
            Ray ray = camera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
            Assert.IsTrue(plane.Raycast(ray, out float distance));
            return ray.GetPoint(distance);
        }

        private static Bounds BoardBounds()
        {
            Renderer[] renderers = GameObject.Find("GuanzhongBattlefield").GetComponentsInChildren<Renderer>();
            Bounds result = renderers[0].bounds;
            foreach (Renderer renderer in renderers) result.Encapsulate(renderer.bounds);
            return result;
        }

        private static IEnumerator Ready(EncounterCoordinator encounter)
        {
            for (float deadline = Time.realtimeSinceStartup + 45f;
                !encounter.AcceptsPlayerCommand && Time.realtimeSinceStartup < deadline;) yield return null;
            Assert.IsTrue(encounter.AcceptsPlayerCommand);
            yield return null;
        }

        private static IEnumerator SceneReady(string name)
        {
            for (int frame = 0; frame < 300 && SceneManager.GetActiveScene().name != name; frame++) yield return null;
            Assert.AreEqual(name, SceneManager.GetActiveScene().name);
            yield return null;
        }

        private static T Field<T>(object owner, string name) =>
            (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();
        private static RectTransform Rect(string name) => Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include,
            FindObjectsSortMode.None).Single(rect => rect.gameObject.scene == SceneManager.GetActiveScene() && rect.name == name);
        private static Vector2 Center(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        private static Rect ScreenRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
            return UnityEngine.Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        private sealed class EncounterRandom : IFormalEncounterRandomSource { public int NextPercent() => 0; }
    }
}
