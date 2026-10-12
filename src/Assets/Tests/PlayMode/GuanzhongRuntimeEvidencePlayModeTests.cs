using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Features.Adventure;
using TianZhang.Features.CharacterCreation;
using TianZhang.Features.CombatPresentation;
using TianZhang.Features.Settlement;
using TianZhang.Features.WorldMap;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.Persistence;
using TianZhang.Spatial;
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
    // Capability/baseline probes, not acceptance of the pending bounty/obstacle/spawn fixes.
    // Input goes through InputSystem -> the production EventSystem, never Button.onClick.Invoke.
    public sealed class GuanzhongRuntimeEvidencePlayModeTests
    {
        private const string BountyId = "bounty_guanzhong_shijiahou";
        private readonly List<string> saveDirectories = new List<string>();
        private Mouse mouse;
        private Keyboard keyboard;
        private InputSettings.BackgroundBehavior background;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
#endif
        private static string OutputDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/gz-r2-evidence"));

        [SetUp]
        public void ConfigureInput()
        {
            background = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            mouse = InputSystem.AddDevice<Mouse>();
            keyboard = InputSystem.AddDevice<Keyboard>();
            Directory.CreateDirectory(OutputDirectory);
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
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            yield return null;
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return null;
            foreach (string directory in saveDirectories)
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            saveDirectories.Clear();
        }

        [UnityTest]
        public IEnumerator BountyPointerPipelineAtBothResolutions()
        {
            foreach (Vector2Int size in Sizes())
            {
                yield return EnterSettlement();
                yield return SetGameSize(size);
                var board = Find<BountyBoardView>();
                var settlement = Find<SettlementController>();
                board.Show(Get<ContentCatalogData>(settlement, "contentCatalog"), "guanzhong_city", GameBootstrap.RequireRuntime().Bounties);
                yield return null;
                Canvas.ForceUpdateCanvases();
                Button accept = Get<Button>(board, "acceptButton");
                Button claim = Get<Button>(board, "claimButton");
                var trace = new List<string> { Header(), "input=InputSystem virtual Mouse -> production EventSystem; baseline only" };
                EventSystem.current.SetSelectedGameObject(accept.gameObject);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.DownArrow));
                yield return null; yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.Not.Null.And.Not.EqualTo(accept.gameObject), "Keyboard navigation must pass through the production input module.");
                trace.Add("keyboardDownFocus=" + EventSystem.current.currentSelectedGameObject.name);
                EventSystem.current.SetSelectedGameObject(null);
                int entered = 0, pressed = 0, released = 0, clicked = 0;
                var trigger = accept.gameObject.AddComponent<EventTrigger>();
                Observe(trigger, EventTriggerType.PointerEnter, () => entered++);
                Observe(trigger, EventTriggerType.PointerDown, () => pressed++);
                Observe(trigger, EventTriggerType.PointerUp, () => released++);
                Observe(trigger, EventTriggerType.PointerClick, () => clicked++);
                Vector2 center = RectTransformUtility.WorldToScreenPoint(accept.GetComponentInParent<Canvas>().worldCamera,
                    accept.transform.TransformPoint(((RectTransform)accept.transform).rect.center));
                yield return Pointer(new Vector2(2, 2), false);
                trace.Add(ButtonState("normal", accept, claim));
                yield return Pointer(center, false);
                Assert.That(entered, Is.GreaterThan(0), "Production raycast must reach AcceptBountyButton.");
                trace.Add(ButtonState("hover", accept, claim));
                yield return Capture("bounty-hover-" + size.x);
                yield return Pointer(center, true);
                Assert.That(pressed, Is.EqualTo(1));
                trace.Add(ButtonState("press", accept, claim));
                yield return Pointer(center, false);
                Assert.That(released, Is.EqualTo(1));
                Assert.That(clicked, Is.EqualTo(1));
                Assert.That(GameBootstrap.RequireRuntime().Bounties.GetState(BountyId).Status, Is.EqualTo(BountyStatus.Accepted));
                trace.Add(ButtonState("release-accepted", accept, claim));
                yield return Pointer(new Vector2(2, 2), false);
                trace.Add(ButtonState("leave", accept, claim));
                trace.Add($"pointerEnter={entered}; pointerDown={pressed}; pointerUp={released}; pointerClick={clicked}; focus={EventSystem.current.currentSelectedGameObject?.name}");
                Write("bounty-" + size.x, trace);
                yield return Capture("bounty-accepted-" + size.x);
            }
        }

        [UnityTest]
        public IEnumerator BattlefieldBoundsAndSpawnProjectionAtBothResolutions()
        {
            yield return EnterSettlement();
            var settlement = Find<SettlementController>();
            Assert.That(settlement.EnterAdventure("guanzhong_wild"), Is.True);
            yield return SceneReady(GameplaySceneNames.Adventure);
            var adventure = Find<AdventureController>();
            for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            Assert.That(adventure.CurrentState, Is.EqualTo(AdventureSceneState.Exploration));
            adventure.SetEncounterRandomSource(new EncounterRandom());
            Assert.That(Find<AdventureInputController>().SelectNode("shijiahou_encounter"), Is.True);
            yield return null;
            var encounter = Find<EncounterCoordinator>();
            AdventureSpawnSet spawned = Get<AdventureSpawnSet>(encounter, "spawned");
            CombatSession session = Get<CombatSession>(encounter, "session");
            Camera camera = Get<Camera>(Find<CombatMovementView>(), "battlefieldCamera");
            var objects = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            Transform[] cells = objects.Where(item => item.name.StartsWith("GuanzhongHex_", StringComparison.Ordinal)).ToArray();
            Assert.That(cells.Length, Is.EqualTo(36));
            string[] names = { "GuanzhongObstacle_2_1", "GuanzhongObstacle_3_1", "GuanzhongObstacle_2_4", "GuanzhongObstacle_3_4" };
            foreach (Vector2Int size in Sizes())
            {
                yield return SetGameSize(size);
                var lines = new List<string> { Header(), $"camera={PathOf(camera.transform)}; position={camera.transform.position:F5}; rotation={camera.transform.eulerAngles:F5}; ortho={camera.orthographicSize}; pixelRect={camera.pixelRect}; aspect={camera.aspect}" };
                foreach (Transform cell in cells.OrderBy(item => item.name))
                {
                    string[] coordinate = cell.name.Split('_');
                    var hex = new HexCoord(int.Parse(coordinate[1]), int.Parse(coordinate[2]));
                    // Read-only multi-turn reachability quote; does not grant AP or move either unit.
                    var path = session.SpatialQuery.QueryMovement(spawned.Player.Position, hex, 10000000,
                        spawned.Player.MovementEfficiencyMilliUnits, Array.Empty<HexCoord>()).Path;
                    lines.Add($"cell={cell.name}; world={cell.position:F5}; screen={camera.WorldToScreenPoint(cell.position):F3}; pathFromPlayer={string.Join("/", path)}");
                }
                foreach (string name in names)
                {
                    Transform obstacle = objects.Single(item => item.name == name);
                    lines.Add("obstacle=" + PathOf(obstacle) + "; local=" + obstacle.localPosition.ToString("F5") + "; scale=" + obstacle.lossyScale.ToString("F5"));
                    foreach (Renderer renderer in obstacle.GetComponentsInChildren<Renderer>()) lines.Add(RendererState(renderer, camera));
                    foreach (Renderer renderer in obstacle.parent.GetComponentsInChildren<Renderer>().Where(item => !item.transform.IsChildOf(obstacle)))
                        lines.Add("tile=" + RendererState(renderer, camera));
                }
                foreach (Transform occluder in objects.Where(item => item.name.Contains("VisualBaselineOccluder")))
                    lines.Add($"separateOccluder={PathOf(occluder)}; active={occluder.gameObject.activeInHierarchy}");
                foreach (var unit in new[] { spawned.Player, spawned.Enemy })
                {
                    Vector3 world = Static3DCombatUnitPresentationProvider.HexToWorld(new CombatUnitPresentationHex(unit.Position.Q, unit.Position.R));
                    lines.Add($"unit={unit.Id}; logic={unit.Position}; world={world:F5}; screen={camera.WorldToScreenPoint(world):F3}");
                }
                Write("battlefield-" + size.x, lines);
                yield return Capture("battlefield-" + size.x);
            }
        }

        private IEnumerator EnterSettlement()
        {
            var stale = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return SceneReady(GameplaySceneNames.StartMenu);
            string directory = Path.Combine(Application.temporaryCachePath, "TianZhang-R2Evidence-" + Guid.NewGuid().ToString("N"));
            saveDirectories.Add(directory);
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(directory));
            var creation = Find<CharacterCreationController>();
            creation.Open();
            Assert.That(creation.SelectOrigin("origin_minor_clan"), Is.True);
            creation.Submit("runtime-evidence", "运行时取证");
            yield return SceneReady(GameplaySceneNames.World);
            var world = Find<WorldMapController>();
            Assert.That(world.SelectNode("guanzhong_hub"), Is.True);
            world.EnterSelectedLocation();
            yield return SceneReady(GameplaySceneNames.Settlement);
        }

        private static IEnumerator SetGameSize(Vector2Int size)
        {
#if UNITY_EDITOR
            var assembly = typeof(UnityEditor.Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var sizes = sizesType.BaseType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { 0 });
            var value = Activator.CreateInstance(assembly.GetType("UnityEditor.GameViewSize"), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { Enum.ToObject(assembly.GetType("UnityEditor.GameViewSizeType"), 1), size.x, size.y, "R2 runtime evidence" }, null);
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { value });
            int count = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null);
            var game = UnityEditor.EditorWindow.GetWindow(assembly.GetType("UnityEditor.GameView"));
            game.GetType().GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, count - 1);
#else
            Screen.SetResolution(size.x, size.y, false);
#endif
            for (int frame = 0; frame < 60 && (Screen.width != size.x || Screen.height != size.y); frame++) yield return null;
            Assert.That(new Vector2Int(Screen.width, Screen.height), Is.EqualTo(size), "Requested size must be the actual rendered size.");
            yield return null;
            Canvas.ForceUpdateCanvases();
        }

        private IEnumerator Pointer(Vector2 position, bool down)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = (ushort)(down ? 1 : 0) });
            yield return null; yield return null;
        }

        private static void Observe(EventTrigger trigger, EventTriggerType type, Action action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => action());
            trigger.triggers.Add(entry);
        }

        private static string ButtonState(string phase, params Button[] buttons) => phase + "; bounty=" + GameBootstrap.RequireRuntime().Bounties.GetState(BountyId).Status + "; " +
            string.Join("; ", buttons.Select(button => $"{PathOf(button.transform)}: interactable={button.interactable}, sprite={button.image.overrideSprite?.name}, rect={((RectTransform)button.transform).rect}, canvasScale={button.GetComponentInParent<Canvas>().scaleFactor}"));

        private static string RendererState(Renderer renderer, Camera camera)
        {
            Bounds bounds = renderer.bounds;
            var corners = new List<Vector3>();
            for (int i = 0; i < 8; i++) corners.Add(new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x, (i & 2) == 0 ? bounds.min.y : bounds.max.y, (i & 4) == 0 ? bounds.min.z : bounds.max.z));
            Vector3[] screen = corners.Select(camera.WorldToScreenPoint).ToArray();
            string top = "";
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                Vector3[] vertices = filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();
                int[] triangles = filter.sharedMesh.triangles;
                var surface = new List<Vector3>();
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                    if (Vector3.Dot(Vector3.Cross(b - a, c - a).normalized, Vector3.up) < .8f) continue;
                    surface.Add(a); surface.Add(b); surface.Add(c);
                }
                Assert.That(surface, Is.Not.Empty, "Expected upward mesh surface: " + PathOf(renderer.transform));
                var topBounds = new Bounds(surface[0], Vector3.zero);
                foreach (Vector3 vertex in surface) topBounds.Encapsulate(vertex);
                Vector3[] projected = surface.Select(camera.WorldToScreenPoint).ToArray();
                // Upward cliff ledges may be included: keep the raw transformed mesh so design can
                // identify the actual tile cap instead of treating this envelope as its footprint.
                string meshFile = PathOf(renderer.transform).Replace('/', '_') + "-" + Screen.width + ".json";
                File.WriteAllText(Path.Combine(OutputDirectory, meshFile), JsonUtility.ToJson(new MeshEvidence
                {
                    hierarchy = PathOf(renderer.transform), worldVertices = vertices,
                    screenVertices = vertices.Select(camera.WorldToScreenPoint).ToArray(), triangles = triangles
                }));
                top = $"; upwardSurfaceNormalDotMin=0.8; upwardWorldMin={topBounds.min:F5}; upwardWorldMax={topBounds.max:F5}; upwardScreenMin=({projected.Min(point => point.x):F3},{projected.Min(point => point.y):F3}); upwardScreenMax=({projected.Max(point => point.x):F3},{projected.Max(point => point.y):F3}); rawMesh={meshFile}";
            }
            return $"{PathOf(renderer.transform)}; enabled={renderer.enabled}; boundsMin={bounds.min:F5}; boundsMax={bounds.max:F5}; center={bounds.center:F5}; screenMin=({screen.Min(point => point.x):F3},{screen.Min(point => point.y):F3}); screenMax=({screen.Max(point => point.x):F3},{screen.Max(point => point.y):F3})" + top;
        }

        private static IEnumerator Capture(string label)
        {
#if UNITY_EDITOR
            // A fresh worktree compiles shader variants on first render. Never capture their cyan placeholder.
            for (float deadline = Time.realtimeSinceStartup + 120f; UnityEditor.ShaderUtil.anythingCompiling && Time.realtimeSinceStartup < deadline;) yield return null;
            Assert.That(UnityEditor.ShaderUtil.anythingCompiling, Is.False, "Shader compilation must finish before visual evidence.");
            yield return null;
#endif
            string path = Path.Combine(OutputDirectory, label + ".png");
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            for (int frame = 0; frame < 120 && !File.Exists(path); frame++) yield return null;
            Assert.That(File.Exists(path), Is.True, "Graphics capture did not produce " + path);
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(100));
        }

        private static void Write(string label, IEnumerable<string> lines)
        {
            string text = string.Join("\n", lines);
            File.WriteAllText(Path.Combine(OutputDirectory, label + ".txt"), text);
            TestContext.WriteLine(text);
        }
        private static string Header() => $"scene={SceneManager.GetActiveScene().name}; resolution={Screen.width}x{Screen.height}; project={Application.dataPath}; utc={DateTime.UtcNow:O}";
        private static Vector2Int[] Sizes() => new[] { new Vector2Int(1920, 1080), new Vector2Int(1366, 768) };
        private static string PathOf(Transform value) => value.parent == null ? value.name : PathOf(value.parent) + "/" + value.name;
        private static T Find<T>() where T : Component => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).Single();
        private static T Get<T>(object owner, string field) => (T)owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
        private static IEnumerator SceneReady(string scene)
        {
            for (int frame = 0; frame < 180 && SceneManager.GetActiveScene().name != scene; frame++) yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(scene));
            yield return null;
        }
        private sealed class EncounterRandom : IFormalEncounterRandomSource { public int NextPercent() => 0; }
        [Serializable]
        private sealed class MeshEvidence
        {
            public string hierarchy;
            public Vector3[] worldVertices;
            public Vector3[] screenVertices;
            public int[] triangles;
        }
    }
}
