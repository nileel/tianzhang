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
    public sealed class D12UnifiedHudPlayModeTests
    {
        private Mouse mouse;
        private Keyboard keyboard;
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
        private readonly string output = Path.GetFullPath("Logs/d12-hud");

        [UnityTest, Timeout(240000)]
        public IEnumerator ActiveHudSurvivesResolutionAndAspectChanges()
        {
            string folder = Path.GetFullPath("Logs/d12-resize"); Directory.CreateDirectory(folder);
            background = InputSystem.settings.backgroundBehavior;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>(); keyboard = InputSystem.AddDevice<Keyboard>();
            var stale = Find<GameBootstrap>(); if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            GuanzhongCombatEquipmentPlayModeTests.SetGameSize(1920, 1080);
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu); yield return null;
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(Path.Combine(Application.temporaryCachePath, "D12Resize-" + Guid.NewGuid().ToString("N"))));
            var creation = Find<CharacterCreationController>(); creation.Open();
            Assert.IsTrue(creation.SelectOrigin("origin_minor_clan")); creation.Submit("resize", "适配测试");
            yield return ReadyScene(GameplaySceneNames.World);
            Assert.IsTrue(Find<WorldMapController>().SelectNode("guanzhong_hub"));
            Find<WorldMapController>().EnterSelectedLocation(); yield return ReadyScene(GameplaySceneNames.Settlement);
            yield return EnterAdventure();
            yield return Click(GameObject.Find("TerrainRehearsalButton").GetComponent<Button>());
            yield return Ready();
            var canvas = GameObject.Find("UICanvas").GetComponent<Canvas>();
            var hud = (RectTransform)canvas.transform.Find("CombatHudRoot");
            var samples = new List<string>(); var failures = new List<string>();
            var sizes = new[] { new Vector2Int(1920,1080), new Vector2Int(1366,768), new Vector2Int(1920,1080),
                new Vector2Int(1920,1200), new Vector2Int(1600,1000), new Vector2Int(2560,1440), new Vector2Int(1366,768), new Vector2Int(1920,1080) };
            Canvas.WillRenderCanvases drawProbe = () =>
            {
                samples.Add(ResizeSample(canvas, hud, "drawn"));
                Rect drawn = ScreenRect(hud);
                if (Mathf.Abs(drawn.xMin) > 1 || Mathf.Abs(drawn.yMin) > 1 || Mathf.Abs(drawn.width - Screen.width) > 1 || Mathf.Abs(drawn.height - Screen.height) > 1)
                    failures.Add("render frame " + Time.frameCount + " HUD=" + drawn + " screen=" + Screen.width + "x" + Screen.height);
            };
            Canvas.willRenderCanvases += drawProbe;
            try
            {
                for (int step = 0; step < sizes.Length; step++)
                {
                    Vector2Int size = sizes[step]; GuanzhongCombatEquipmentPlayModeTests.SetGameSize(size.x, size.y);
                    for (int frame = 0; frame < 5; frame++)
                    {
                        yield return null;
                        samples.Add(ResizeSample(canvas, hud, step + "/" + frame + " before-render"));
                    }
                    Assert.AreEqual(size.x, Screen.width); Assert.AreEqual(size.y, Screen.height);
                    var bar = Find<CombatActionBarView>();
                    var buttons = bar.BasicAttackButton.transform.parent.GetComponentsInChildren<Button>()
                        .Where(b => b.gameObject.activeInHierarchy).Concat(new[] { bar.WaitButton });
                    foreach (Button button in buttons) CheckResizeButton(button);
                    foreach (string name in new[] { "CombatResourceCard", "CombatTimelineRoot", "CombatBountyTrackerRoot" })
                        CheckInsideScreen(ScreenRect((RectTransform)hud.Find(name)), name);
                    var menu = canvas.GetComponentsInChildren<Button>().Single(b => b.name == "ReturnButton");
                    var log = hud.Find("ToggleLogButton").GetComponent<Button>();
                    CheckResizeButton(menu); CheckResizeButton(log);
                    Assert.That(ScreenRect((RectTransform)menu.transform).center.y, Is.EqualTo(ScreenRect((RectTransform)log.transform).center.y).Within(1));
                    yield return Click(bar.ArtButton);
                    Assert.IsNotNull(Find<CombatCommandInput>().Candidate);
                    CheckInsideScreen(ScreenRect((RectTransform)hud.Find("CombatActionPreviewRoot")), "quote");
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder, step + "-" + size.x + "x" + size.y + ".png"));
                    yield return null;
                    yield return PressKey(Key.Escape);
                    yield return Click(bar.MoveButton);
                    Assert.IsTrue(Find<CombatMovementView>().IsSelecting);
                    yield return PressKey(Key.Escape);
                    yield return Click(bar.WaitButton);
                    Assert.IsTrue(Find<CombatActionPreviewView>().ConfirmButton.interactable);
                    yield return PressKey(Key.Escape);
                    yield return Click(menu);
                    Assert.IsTrue(canvas.GetComponentsInChildren<Button>().Any(b => b.name == "ContinueCombatButton" && b.interactable));
                    Find<AdventureController>().CancelWithdrawal(); yield return null;
                }
            }
            finally { Canvas.willRenderCanvases -= drawProbe; File.WriteAllLines(Path.Combine(folder, "resize-proof.txt"), samples); }
            Assert.IsEmpty(failures, string.Join("\n", failures));
            yield return Withdraw();
            yield return EnterAdventure();
            yield return Click(GameObject.Find("TerrainRehearsalButton").GetComponent<Button>());
            yield return Ready();
            CheckSlots("ordinary-reentered-1920");
        }

        private static Rect ScreenRect(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        private static void CheckInsideScreen(Rect rect, string name)
        {
            Assert.Greater(rect.width, 0, name); Assert.Greater(rect.height, 0, name);
            Assert.GreaterOrEqual(rect.xMin, -.5f, name); Assert.GreaterOrEqual(rect.yMin, -.5f, name);
            Assert.LessOrEqual(rect.xMax, Screen.width + .5f, name); Assert.LessOrEqual(rect.yMax, Screen.height + .5f, name);
        }
        private static void CheckResizeButton(Button button)
        {
            Rect rect = ScreenRect((RectTransform)button.transform); CheckInsideScreen(rect, button.name);
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = rect.center }, hits);
            Assert.IsTrue(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button, button.name + " hit blocked after resize");
        }

        private static string ResizeSample(Canvas canvas, RectTransform hud, string phase)
        {
            var corners = new Vector3[4]; hud.GetWorldCorners(corners);
            return phase + " frame=" + Time.frameCount + " Screen=" + Screen.width + "x" + Screen.height +
                " canvas=" + canvas.name + "/" + canvas.renderMode + " camera=" + canvas.worldCamera +
                " pixelRect=" + canvas.pixelRect + " canvasScale=" + canvas.scaleFactor +
                " root=" + hud.parent.name + "/" + hud.name + " local=" + hud.rect + " localScale=" + hud.localScale +
                " drawn=" + Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y) +
                " vignette=" + hud.Find("CombatVignette").GetComponent<Image>().sprite.name;
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator OrdinaryAndScaleYangUseEquipmentDrivenBarAtBothSizes()
        {
            Directory.CreateDirectory(output);
            background = InputSystem.settings.backgroundBehavior;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>(); keyboard = InputSystem.AddDevice<Keyboard>();
            foreach (int width in new[] { 1920, 1366 })
            {
                var stale = Find<GameBootstrap>(); if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
                GuanzhongCombatEquipmentPlayModeTests.SetGameSize(width, width == 1920 ? 1080 : 768);
                yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu); yield return null;
                Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(Path.Combine(Application.temporaryCachePath, "D12Hud-" + Guid.NewGuid().ToString("N"))));
                var creation = Find<CharacterCreationController>(); creation.Open();
                Assert.IsTrue(creation.SelectOrigin("origin_minor_clan")); creation.Submit("hud", "沈行舟");
                yield return ReadyScene(GameplaySceneNames.World);
                Assert.IsTrue(Find<WorldMapController>().SelectNode("guanzhong_hub"));
                Find<WorldMapController>().EnterSelectedLocation(); yield return ReadyScene(GameplaySceneNames.Settlement);
                yield return EnterAdventure();
                yield return Click(GameObject.Find("TerrainRehearsalButton").GetComponent<Button>());
                yield return Ready();
                var bar = Find<CombatActionBarView>(); var view = Find<JindanDemoView>();
                Assert.IsTrue(bar.ArtButton.gameObject.activeInHierarchy);
                Assert.IsTrue(bar.SecondArtButton.gameObject.activeInHierarchy);
                Assert.IsTrue(bar.ThirdArtButton.gameObject.activeInHierarchy);
                Assert.IsFalse(bar.FourthArtButton.gameObject.activeInHierarchy);
                Assert.IsFalse(view.GetButton("fire_tab").gameObject.activeInHierarchy);
                Assert.IsFalse(bar.DivineButton.gameObject.activeInHierarchy);
                yield return Capture("ordinary-initial-" + width);
                CheckSlots("ordinary-" + width);
                yield return Click(bar.ThirdArtButton);
                Assert.AreEqual("spell_feixingzhi", Find<CombatCommandInput>().Candidate.ProfileId);
                yield return Capture("ordinary-spell-" + width);
                yield return PressKey(Key.Tab);
                Assert.IsNotNull(EventSystem.current.currentSelectedGameObject);
                var focused = EventSystem.current.currentSelectedGameObject.transform.Find("FocusFrame");
                Assert.IsNotNull(focused); Assert.IsTrue(focused.gameObject.activeSelf);
                yield return PressKey(Key.Escape);
                Assert.IsNull(Find<CombatCommandInput>().Candidate);
                yield return PressKey(Key.M);
                Assert.IsTrue(Find<CombatMovementView>().IsSelecting);
                yield return PressKey(Key.Escape);
                Find<AdventureController>().RequestReturn(); yield return null;
                yield return PressKey(Key.M);
                Assert.IsFalse(Find<CombatMovementView>().IsSelecting, "Withdrawal dialog owns keyboard input.");
                Find<AdventureController>().CancelWithdrawal(); yield return null;
                var encounter = Find<EncounterCoordinator>();
                var actor = encounter.CurrentSession.Combatants.All.Single(a => a.Id == encounter.ControlledActorId);
                actor.SetCooldown("spell_feixingzhi", 60); Refresh(encounter); yield return null;
                StringAssert.Contains("60", bar.ThirdArtButton.transform.Find("Cooldown").GetComponent<Text>().text);
                yield return Capture("ordinary-cooldown-" + width);
                actor.SetCooldown("spell_feixingzhi", 0);
                typeof(CombatantSnapshot).GetMethod("SpendActionPoints", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(actor, new object[] { actor.ActionPointsRemainingUnits });
                Refresh(encounter); yield return null;
                Assert.IsTrue(bar.WaitButton.interactable, "AP zero must retain the end-turn entry.");
                yield return Click(bar.WaitButton);
                Assert.IsTrue(Find<CombatActionPreviewView>().ConfirmButton.interactable);
                yield return Capture("ordinary-zero-ap-" + width);
                yield return Withdraw(); yield return EnterAdventure();
                yield return Click(GameObject.Find("JindanRehearsalButton").GetComponent<Button>());
                view = Find<JindanDemoView>(); yield return Click(view.GetButton("enter")); yield return Ready();
                encounter = Find<EncounterCoordinator>(); bar = Find<CombatActionBarView>();
                Assert.AreEqual("demo_20261024_space_yang", encounter.ControlledActorId);
                Assert.IsFalse(bar.DivineButton.gameObject.activeInHierarchy, "An equipped art must not be duplicated as a divine ability.");
                foreach (string key in new[] { "scale_tab", "visibility_tab", "flight_tab" }) Assert.IsTrue(view.GetButton(key).gameObject.activeInHierarchy, key);
                foreach (string key in new[] { "earth_tab", "fire_tab", "tide_tab" }) Assert.IsFalse(view.GetButton(key).gameObject.activeInHierarchy, key);
                yield return Capture("scale-initial-" + width);
                CheckSlots("scale-yang-" + width);
                yield return Click(view.GetButton("scale_tab"));
                Assert.AreEqual("scale_tab", view.SelectedKey);
                yield return Capture("scale-selected-" + width);
                yield return Click(view.GetButton("visibility_tab"));
                yield return Click(view.GetButton("visibility_deploy"));
                Assert.IsTrue(view.GetButton("confirm").interactable);
                yield return Click(bar.ArtButton);
                Assert.IsNull(view.SelectedKey);
                Assert.IsFalse(view.GetButton("confirm").gameObject.activeInHierarchy);
                yield return Capture("scale-ordinary-switch-" + width);
                yield return Withdraw();
                Assert.IsNull(Find<JindanDemoView>());
            }
        }

        public static void CheckSlots(string name)
        {
            Canvas.ForceUpdateCanvases();
            CaptureLayout(name);
            CheckV3Bounds(name);
            var bar = Find<CombatActionBarView>();
            var row = bar.BasicAttackButton.transform.parent;
            var slots = row.GetComponentsInChildren<Button>().Where(b => b.gameObject.activeInHierarchy).ToArray();
            Assert.AreEqual("MoveButton", slots.First().name);
            Assert.AreEqual("BagButton", slots.Last().name);
            var bounds = new List<Rect>(); var proof = new List<string>();
            foreach (Button button in slots)
            {
                var corners = new Vector3[4]; ((RectTransform)button.transform).GetWorldCorners(corners);
                var rect = Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
                Assert.Greater(rect.width, 40); Assert.Greater(rect.height, 40);
                Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(0)); Assert.That(rect.xMax, Is.LessThanOrEqualTo(Screen.width));
                Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(0)); Assert.That(rect.yMax, Is.LessThanOrEqualTo(Screen.height));
                foreach (Rect previous in bounds) Assert.IsFalse(rect.Overlaps(previous), name + " overlapping slots");
                bounds.Add(rect);
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = rect.center }, hits);
                Assert.IsTrue(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button, button.name + " hit blocked");
                Text label = button.GetComponentInChildren<Text>();
                proof.Add(button.name + " rect=" + rect + " textHeight=" + label.preferredHeight + "/" + label.rectTransform.rect.height + " label=" + label.text.Replace('\n', '/'));
                Text hint = button.transform.Find("Shortcut")?.GetComponent<Text>();
                if (hint != null) proof.Add(" shortcut=" + hint.text + " rect=" + hint.rectTransform.rect + " preferredHeight=" + hint.preferredHeight);
                string probeFolder = Path.GetFullPath("Logs/d12-hud"); Directory.CreateDirectory(probeFolder);
                File.WriteAllLines(Path.Combine(probeFolder, name + "-bounds.txt"), proof);
                Assert.LessOrEqual(label.preferredHeight, label.rectTransform.rect.height, button.name + " label clips");
                if (hint != null) Assert.LessOrEqual(hint.preferredHeight, hint.rectTransform.rect.height, button.name + " shortcut clips");
            }
            string folder = Path.GetFullPath("Logs/d12-hud"); Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, name + "-bounds.txt"), proof);
        }

        private static void CheckV3Bounds(string name)
        {
            var canvas = SceneManager.GetActiveScene().GetRootGameObjects().Single(go => go.name == "UICanvas");
            RectTransform dock = canvas.GetComponentsInChildren<RectTransform>(true).Single(r => r.name == "CombatHotbar");
            var corners = new Vector3[4]; dock.GetWorldCorners(corners);
            float width = corners[2].x - corners[0].x;
            bool small = Screen.width == 1366;
            // Measured from the approved browser design, not from the Unity layout constants.
            float expected = name.StartsWith("ordinary") ? (small ? 835.08f : 944.31f) :
                // The current fixed scale preset owns only Guanshiji: omit V3's extra Feixingzhi slot and gap.
                name.StartsWith("scale") ? (small ? 867.38f : 1055.11f) : (small ? 716.23f : 867.45f);
            Assert.That(width, Is.EqualTo(expected).Within(2), "Dock must match V3's content width.");
            Assert.That((corners[0].x + corners[2].x) / 2, Is.EqualTo(Screen.width / 2f).Within(1));
            Assert.That(corners[0].y, Is.EqualTo(32).Within(1));
            Assert.That(corners[2].y - corners[0].y, Is.EqualTo(small ? 126 : 139.77f).Within(1));
            RectTransform card = canvas.GetComponentsInChildren<RectTransform>(true).Single(r => r.name == "CombatResourceCard");
            card.GetWorldCorners(corners);
            Assert.That(corners[0].x, Is.EqualTo(Screen.width * .02f).Within(1));
            Assert.That(corners[2].y, Is.EqualTo(Screen.height * .95f).Within(1));
            Assert.That(corners[2].x - corners[0].x, Is.EqualTo(small ? 396.13f : 439.28f).Within(1));
        }

        public static void CaptureLayout(string name)
        {
            var canvas = SceneManager.GetActiveScene().GetRootGameObjects().SingleOrDefault(go => go.name == "UICanvas")?.GetComponent<Canvas>();
            if (canvas == null) return;
            Canvas.ForceUpdateCanvases();
            var proof = new List<string> { "screen=" + Screen.width + "x" + Screen.height + " canvas=" + canvas.name +
                " renderMode=" + canvas.renderMode + " scaleFactor=" + canvas.scaleFactor + " camera=" + canvas.worldCamera,
                "conversion=RectTransform.GetWorldCorners -> RectTransformUtility.WorldToScreenPoint (overlay camera=null); screen origin=bottom-left" };
            string[] names = { "CombatHudRoot", "CombatResourceCard", "CombatHotbar", "ActionContent", "CombatTimelineRoot", "TimelineEntry0",
                "CombatBountyTrackerRoot", "JindanStatusRoot", "CombatActionPreviewRoot", "JindanDemoCombatRoot", "CombatSlotDetailsRoot", "ReturnButton" };
            foreach (RectTransform rect in canvas.GetComponentsInChildren<RectTransform>(true).Where(r => names.Contains(r.name)))
            {
                string path = rect.name;
                for (Transform p = rect.parent; p != null; p = p.parent) path = p.name + "/" + path;
                var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                Vector2 min = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, corners[0]);
                Vector2 max = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, corners[2]);
                var drawn = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                var image = rect.GetComponent<Image>();
                proof.Add(path + " active=" + rect.gameObject.activeInHierarchy + " local=" + rect.rect + " anchors=" + rect.anchorMin + "/" + rect.anchorMax +
                    " pivot=" + rect.pivot + " position=" + rect.anchoredPosition + " scale=" + rect.lossyScale + " finalScreen=" + drawn +
                    " sprite=" + (image != null && image.sprite != null ? image.sprite.name : "none") + " color=" + (image != null ? image.color.ToString() : "none"));
            }
            foreach (Text label in canvas.GetComponentsInChildren<Text>(true).Where(t => t.gameObject.activeInHierarchy &&
                (new[] { "CompactQuote", "ActionPreviewHeading", "ActionHint", "BoardActionPrompt", "StatusLine", "JindanQuoteTitle" }.Contains(t.name) ||
                 t.transform.parent.name is "CancelActionButton" or "ConfirmActionButton" or "ReturnButton" or "ToggleLogButton" or "MoveButton")))
            {
                proof.Add("TEXT " + label.transform.parent.name + "/" + label.name + " rect=" + label.rectTransform.rect +
                    " preferred=" + label.preferredWidth + "x" + label.preferredHeight + " font=" + label.font.name + " size=" + label.fontSize +
                    " overflow=" + label.horizontalOverflow + "/" + label.verticalOverflow + " text=" + label.text.Replace('\n', '/'));
                Assert.LessOrEqual(label.preferredHeight, label.rectTransform.rect.height, label.transform.parent.name + "/" + label.name + " clips vertically");
                Assert.LessOrEqual(label.preferredWidth, label.rectTransform.rect.width, label.transform.parent.name + "/" + label.name + " clips horizontally");
            }
            string folder = Path.GetFullPath("Logs/d12-hud/layout"); Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, name + ".txt"), proof);
        }

        private IEnumerator EnterAdventure()
        { Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild")); yield return ReadyScene(GameplaySceneNames.Adventure); }
        private IEnumerator Withdraw()
        { var controller = Find<AdventureController>(); controller.RequestReturn(); controller.ConfirmWithdrawal(); yield return ReadyScene(GameplaySceneNames.Settlement); }
        private IEnumerator Ready()
        {
            for (float end = Time.realtimeSinceStartup + 30; !Find<EncounterCoordinator>().AcceptsPlayerCommand && Time.realtimeSinceStartup < end;) yield return null;
            Assert.IsTrue(Find<EncounterCoordinator>().AcceptsPlayerCommand); yield return null; yield return null;
        }
        private IEnumerator Click(Button button)
        {
            Canvas.ForceUpdateCanvases(); Assert.IsTrue(button.gameObject.activeInHierarchy && button.interactable, button.name);
            var rect = (RectTransform)button.transform;
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            Assert.IsTrue(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button, button.name + " blocked");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left)); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
        }
        private IEnumerator PressKey(Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
        }
        private IEnumerator Capture(string name)
        { yield return new WaitForEndOfFrame(); CaptureLayout(name); ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png")); yield return null; }
        private static IEnumerator ReadyScene(string name)
        { for (int i = 0; i < 300 && SceneManager.GetActiveScene().name != name; i++) yield return null; yield return null; yield return null; Assert.AreEqual(name, SceneManager.GetActiveScene().name); }
        private static void Refresh(EncounterCoordinator encounter) => typeof(EncounterCoordinator).GetMethod("Present", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(encounter, new object[] { "你的行动", true, null });
        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior = background; InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            var bootstrap = Find<GameBootstrap>(); if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            yield return null;
        }
    }
}
