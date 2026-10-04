using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Features.CharacterCreation;
using TianZhang.Features.Settlement;
using TianZhang.Features.WorldMap;
using TianZhang.Gameplay.Contracts;
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
    // Production scenes/store; only pointer input and read-only observations drive navigation.
    public sealed class GuanzhongNavigationTextPlayModeTests
    {
        private Mouse mouse;
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
        private string directory;

        [UnityTest] public IEnumerator Navigation1920() => Navigate(1920, 1080);
        [UnityTest] public IEnumerator Navigation1366() => Navigate(1366, 768);

        private IEnumerator Navigate(int width, int height)
        {
            directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/navigation/" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + width));
            Directory.CreateDirectory(directory);
            background = InputSystem.settings.backgroundBehavior;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>();
            SetGameSize(width, height);
            var stale = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return SceneReady(GameplaySceneNames.StartMenu);
            Assert.AreEqual(width, Screen.width);
            Assert.AreEqual(height, Screen.height);
            string slot = "nav-" + width + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Log("Unity=" + Application.unityVersion + " size=" + width + "x" + height +
                " entry=Assets/Scenes/StartMenuScene.unity slot=" + slot +
                " store=" + Path.Combine(Application.persistentDataPath, "SaveSlots"));
            yield return Click(Button("NewPlayerButton"));
            yield return Click(Find<CharacterCreationView>().GetOriginButton("origin_minor_clan"));
            var draft = Find<CharacterCreationController>().Draft;
            CollectionAssert.AreEqual(new[] { 8, 8, 8, 8, 8 }, new[] { draft.Innate.RootBone, draft.Innate.Soul,
                draft.Innate.DivineSense, draft.Innate.Aptitude, draft.Innate.Fortune });
            Node("SlotIdInput").GetComponent<InputField>().text = slot;
            Node("CharacterNameInput").GetComponent<InputField>().text = "关中导航" + width;
            yield return Click(Button("CreatePlayerButton"));
            yield return SceneReady(GameplaySceneNames.World);
            yield return Click(Button("WorldNodeButton_1"));
            AssertWorld();
            yield return Capture("01-world");
            yield return Click(Button("EnterLocationButton"));
            yield return SceneReady(GameplaySceneNames.Settlement);
            AssertSettlement();
            yield return Capture("02-settlement");
            yield return Click(Button("ReturnToWorldButton"));
            yield return SceneReady(GameplaySceneNames.World);
            AssertWorld();
            yield return Capture("03-returned-world");
            yield return Click(Button("EnterLocationButton"));
            yield return SceneReady(GameplaySceneNames.Settlement);
            AssertSettlement();
            string saved = GameBootstrap.RequireRuntime().CaptureSaveJson();
            File.WriteAllText(Path.Combine(directory, "before-save.json"), saved);
            yield return Click(Button("SaveAndReturnButton"));
            yield return SceneReady(GameplaySceneNames.StartMenu);
            Button savedSlot = Button("SaveSlot_" + slot);
            yield return ScrollToSlot(savedSlot);
            yield return Click(savedSlot);
            yield return SceneReady(GameplaySceneNames.Settlement);
            AssertSettlement();
            Assert.AreEqual(saved, GameBootstrap.RequireRuntime().CaptureSaveJson());
            yield return Capture("04-loaded");
            // Failure display is checked in isolation after the formal persisted route completes.
            Find<SettlementView>().ShowFailure("settlement_missing", "guanzhong_hub");
            Assert.AreEqual("可返回主世界", Text("SettlementDetailText").text);
            Assert.AreEqual(saved, GameBootstrap.RequireRuntime().CaptureSaveJson());
            Log("PASS navigation, persisted JSON and failure-display isolation");
        }

        private static void AssertWorld()
        {
            Assert.AreEqual("guanzhong_hub", Find<WorldMapController>().SelectedNodeId);
            Assert.AreEqual("guanzhong_hub", GameBootstrap.RequireRuntime().Navigation.WorldNodeId);
            Assert.AreEqual("关陇玄域", Text("SelectedWorldNodeText").text);
            Assert.AreEqual("关陇玄域（区域枢纽）\n可进入据点", Text("SelectedWorldNodeDescription").text);
            Assert.IsTrue(Button("EnterLocationButton").interactable);
        }

        private static void AssertSettlement()
        {
            Assert.AreEqual("guanzhong_city", Find<SettlementController>().CurrentSettlementId);
            Assert.AreEqual("guanzhong_hub", Find<SettlementController>().ResolveReturnWorldNodeId());
            Assert.AreEqual("guanzhong_city", GameBootstrap.RequireRuntime().Navigation.SettlementId);
            Assert.AreEqual("关中城", Text("SettlementNameText").text);
            Assert.AreEqual("据点: 关中城\n区域: 关中", Text("SettlementDetailText").text);
            Assert.AreEqual("据点已加载 · 可返回主世界", Text("SettlementStatusText").text);
        }

        private IEnumerator Capture(string label)
        {
            // Allow scene activation, font atlas upload and rendered frames before screen capture.
            for (int frame = 0; frame < 8; frame++) yield return null;
            Canvas.ForceUpdateCanvases();
            string stem = Path.Combine(directory, label);
            var texts = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Text>()).ToArray();
            File.WriteAllText(stem + ".txt", "size=" + Screen.width + "x" + Screen.height + "\n" +
                string.Join("\n", texts.Select(t => t.name + "=" + t.text)));
            foreach (var text in texts.Where(t => t.name == "SelectedWorldNodeDescription" ||
                t.name == "SettlementStatusText" || t.name == "SettlementNameText" || t.name == "SettlementDetailText"))
            {
                Assert.That(text.text, Does.Not.Contain("guanzhong_"));
                Assert.LessOrEqual(text.preferredHeight, text.rectTransform.rect.height + 1, text.name);
                Log(text.name + " rect=" + text.rectTransform.rect + " preferredHeight=" + text.preferredHeight);
            }
            ScreenCapture.CaptureScreenshot(stem + ".png");
            for (int frame = 0; frame < 4; frame++) yield return null;
            Log("capture=" + stem);
        }

        private IEnumerator Click(Button button)
        {
            Canvas.ForceUpdateCanvases();
            Assert.IsTrue(button.gameObject.activeInHierarchy, button.name + " hidden");
            Assert.IsTrue(button.interactable, button.name + " disabled");
            var corners = new Vector3[4]; button.GetComponent<RectTransform>().GetWorldCorners(corners);
            Assert.GreaterOrEqual(corners[0].x, -1); Assert.GreaterOrEqual(corners[0].y, -1);
            Assert.LessOrEqual(corners[2].x, Screen.width + 1); Assert.LessOrEqual(corners[2].y, Screen.height + 1);
            Vector2 point = (corners[0] + corners[2]) * .5f;
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            Assert.IsNotEmpty(hits, button.name);
            Assert.AreEqual(button, hits[0].gameObject.GetComponentInParent<Button>(), button.name);
            Log("click=" + button.name + " point=" + point);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 1 });
            yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
            yield return null; yield return null;
        }

        private IEnumerator ScrollToSlot(Button button)
        {
            var scroll = button.GetComponentInParent<ScrollRect>();
            Assert.IsNotNull(scroll);
            for (int step = 0; step < 40; step++)
            {
                Canvas.ForceUpdateCanvases();
                var view = new Vector3[4]; scroll.viewport.GetWorldCorners(view);
                var row = new Vector3[4]; button.GetComponent<RectTransform>().GetWorldCorners(row);
                if (row[0].y >= view[0].y && row[2].y <= view[2].y) yield break;
                Vector2 point = (view[0] + view[2]) * .5f;
                float direction = row[0].y < view[0].y ? -1 : 1;
                Log("scroll-to-slot=" + button.name + " row=" + row[0] + "/" + row[2] +
                    " viewport=" + view[0] + "/" + view[2]);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point, scroll = new Vector2(0, direction) });
                yield return null; yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
                yield return null; yield return null;
            }
            Assert.Fail("Saved slot did not become visible through mouse scrolling");
        }

        private void Log(string message) => File.AppendAllText(Path.Combine(directory, "steps.log"), message + "\n");
        private static GameObject Node(string name) => SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Single(t => t.name == name).gameObject;
        private static Button Button(string name) => Node(name).GetComponent<Button>();
        private static Text Text(string name) => Node(name).GetComponent<Text>();
        private static T Find<T>() where T : Component => SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<T>(true)).Single();
        private static IEnumerator SceneReady(string scene)
        {
            float deadline = Time.realtimeSinceStartup + 25;
            while (SceneManager.GetActiveScene().name != scene)
            { Assert.Less(Time.realtimeSinceStartup, deadline, "Expected " + scene); yield return null; }
            yield return null; yield return null;
        }

        private static void SetGameSize(int width, int height)
        {
#if UNITY_EDITOR
            var assembly = typeof(UnityEditor.Editor).Assembly;
            var type = assembly.GetType("UnityEditor.GameViewSizes");
            var sizes = type.BaseType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var group = type.GetMethod("GetGroup").Invoke(sizes, new object[] { 0 });
            var size = Activator.CreateInstance(assembly.GetType("UnityEditor.GameViewSize"), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                new object[] { Enum.ToObject(assembly.GetType("UnityEditor.GameViewSizeType"), 1), width, height, "Navigation validation" }, null);
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
            int count = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null);
            var game = UnityEditor.EditorWindow.GetWindow(assembly.GetType("UnityEditor.GameView"));
            game.GetType().GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, count - 1);
#else
            Screen.SetResolution(width, height, false);
#endif
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            yield return null;
            // Every production save and evidence file is intentionally retained.
        }
    }
}
