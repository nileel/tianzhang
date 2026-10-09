using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using TianZhang.Bootstrap;
using TianZhang.Combat;
using TianZhang.Gameplay.Contracts;
using TianZhang.Features.Adventure;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using NUnit.Framework;
using TianZhang.Features.CombatPresentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongCombatLayoutPlayModeTests
    {
        private GuanzhongCombatActionPreviewPlayModeTests entry;
        [UnitySetUp] public IEnumerator Enter() { entry = new GuanzhongCombatActionPreviewPlayModeTests(); yield return entry.EnterFormalEncounter(); }
        [UnityTearDown] public IEnumerator Leave() { yield return entry.Cleanup(); }
        [UnityTest] public IEnumerator RealControlsRemainReachableWithDetailsPreviewAndLongLog()
        {
            var presenter = Object.FindFirstObjectByType<CombatHudPresenter>();
            var bar = Object.FindFirstObjectByType<CombatActionBarView>();
            var preview = Object.FindFirstObjectByType<CombatActionPreviewView>();
            var bounty = Object.FindFirstObjectByType<CombatBountyTrackerView>();
            var log = Object.FindFirstObjectByType<CombatLogView>();
            string before = GameBootstrap.RequireRuntime().CaptureSaveJson();
            Assert.That(bounty.Summary, Does.Contain("0/1"));
            foreach (int width in new[] { 1920, 1366 })
            {
                SetGameSize(width, width == 1920 ? 1080 : 768);
                yield return null; yield return null; Canvas.ForceUpdateCanvases();
                Assert.AreEqual(width, Screen.width);
                presenter.InspectTarget("enemy"); bar.ArtButton.onClick.Invoke();
                yield return null; Canvas.ForceUpdateCanvases();
                AssertButtonReachable(preview.ConfirmButton); AssertButtonReachable(preview.CancelButton);
                AssertButtonReachable(Object.FindFirstObjectByType<CombatTargetView>().CloseButton);
                Assert.IsNull(Rect("CombatHudRoot").GetComponent<LayoutGroup>());
                var point = new Vector2(Screen.width * 0.5f, Screen.height * 0.52f);
                Assert.IsEmpty(Hits(point), "Central battlefield must remain open");
                foreach (Button button in new[] { bar.ArtButton, bar.SecondArtButton, bar.SecondArtTargetButton })
                {
                    Text label = button.GetComponentInChildren<Text>();
                    Assert.LessOrEqual(label.preferredHeight, label.rectTransform.rect.height + .1f, label.text);
                }
                Capture("layout-" + width);
                yield return Click(preview.CancelButton);
                Assert.IsFalse(preview.IsVisible);
                // Explicit presentation-only stress; never evidence of real combat state or text.
                var details = Rect("CombatTargetDetails").GetComponent<Text>();
                details.text += string.Concat(Enumerable.Repeat("\n超长中文目标说明用于滚动边界验证", 40));
                for (int line = 0; line < 60; line++) log.Append("布局压力样本 " + line + "：长中文战报保留滚动与固定关闭操作。");
                yield return Click(Rect("ToggleLogButton").GetComponent<Button>());
                yield return null; Canvas.ForceUpdateCanvases();
                var scroll = Rect("CombatLogViewport").GetComponent<ScrollRect>();
                Assert.Greater(scroll.content.rect.height, scroll.viewport.rect.height);
                scroll.verticalNormalizedPosition = 1; yield return null;
                Assert.Greater(scroll.verticalNormalizedPosition, .99f);
                AssertButtonReachable(Rect("CloseLogButton").GetComponent<Button>());
                AssertButtonReachable(Object.FindFirstObjectByType<CombatTargetView>().CloseButton);
                var targetScroll = Rect("TargetBody").GetComponent<ScrollRect>();
                Assert.Greater(targetScroll.content.rect.height, targetScroll.viewport.rect.height);
                targetScroll.verticalNormalizedPosition = 0; yield return null;
                Assert.Less(targetScroll.verticalNormalizedPosition, .01f);
                Capture("stress-" + width);
                yield return Click(Rect("CloseLogButton").GetComponent<Button>());
                Assert.IsFalse(Rect("CombatLogPanel").gameObject.activeSelf);
                Assert.AreEqual(before, GameBootstrap.RequireRuntime().CaptureSaveJson());
            }
            presenter.Hide(); yield return null;
            Assert.IsFalse(Rect("CombatHudRoot").gameObject.activeSelf);
            Assert.IsTrue(Rect("AdventurePanel").gameObject.activeSelf);
            Assert.IsFalse(preview.IsVisible);
            Assert.IsFalse(Rect("CombatTargetRoot").gameObject.activeSelf);
        }

        [UnityTest] public IEnumerator FocusPopupFollowsHoveredThenSelectedEntry()
        {
            var timeline = Object.FindFirstObjectByType<CombatTimelineView>();
            var buttons = (Button[])typeof(CombatTimelineView).GetField("entryButtons", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(timeline);
            EventSystem.current.SetSelectedGameObject(buttons[0].gameObject);
            yield return null;
            string selectedId = timeline.FocusedCombatantId;
            ExecuteEvents.Execute(buttons[1].gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
            yield return null;
            string hoveredId = timeline.FocusedCombatantId;
            Assert.AreNotEqual(selectedId, hoveredId);
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(buttons[0].gameObject);
            yield return null;
            Assert.AreEqual(hoveredId, timeline.FocusedCombatantId, "Hovered content wins over selection");
            ExecuteEvents.Execute(buttons[1].gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
            yield return null;
            Assert.AreEqual(selectedId, timeline.FocusedCombatantId, "Exit restores current selected content");
            EventSystem.current.SetSelectedGameObject(null);
            yield return null;
            Assert.IsFalse(Rect("TimelineFocus").gameObject.activeSelf);
        }

        [UnityTest] public IEnumerator VictoryReturnsAt1920() => CompleteFormalBattle(1920, true);
        [UnityTest] public IEnumerator VictoryReturnsAt1366() => CompleteFormalBattle(1366, true);
        [UnityTest] public IEnumerator DefeatReturnsAt1920() => CompleteFormalBattle(1920, false);
        [UnityTest] public IEnumerator DefeatReturnsAt1366() => CompleteFormalBattle(1366, false);

        private IEnumerator CompleteFormalBattle(int width, bool victory)
        {
            SetGameSize(width, width == 1920 ? 1080 : 768);
            yield return null; yield return null;
            var presenter = Object.FindFirstObjectByType<CombatHudPresenter>();
            var bar = Object.FindFirstObjectByType<CombatActionBarView>();
            var preview = Object.FindFirstObjectByType<CombatActionPreviewView>();
            var coordinator = Object.FindFirstObjectByType<EncounterCoordinator>();
            var session = (CombatSession)typeof(EncounterCoordinator).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(coordinator);
            // User-authorized test setup: weaken only this temporary encounter, never the enemy asset or save.
            if (victory)
            {
                Assert.IsTrue(session.Combatants.TryGet("enemy", out var enemy));
                var spawned = (AdventureSpawnSet)typeof(EncounterCoordinator).GetField("spawned", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(coordinator);
                string sourceBefore = JsonUtility.ToJson(spawned.EnemyData.combatTemplate);
                int maximumHealth = enemy.MaximumHealth;
                Assert.Greater(maximumHealth, 1);
                Assert.AreEqual(maximumHealth, enemy.CurrentHealth, "The formal enemy must start at full health");
                enemy.ReceiveDamage(enemy.CurrentHealth - 1);
                Assert.AreEqual(1, enemy.CurrentHealth);
                Assert.AreEqual(maximumHealth, enemy.MaximumHealth);
                Assert.AreEqual(sourceBefore, JsonUtility.ToJson(spawned.EnemyData.combatTemplate), "Temporary encounter damage must not change the formal enemy source");
                Debug.Log("LAYOUT controlled victory: temporary enemy HP " + maximumHealth + " -> 1; maximum remains " + maximumHealth);
            }
            coordinator.SetCombatResolutionRandomSource(new OutcomeCoverageRandom(session, victory));
            presenter.InspectTarget("enemy");
            yield return Click(Rect("ToggleLogButton").GetComponent<Button>());
            int commands = 0;
            bool capturedLongLog = false;
            for (int frame = 0; frame < 12000 && SceneManager.GetActiveScene().name == GameplaySceneNames.Adventure; frame++)
            {
                var snapshot = presenter.Snapshot;
                if (snapshot == null || !snapshot.AcceptsCommands || snapshot.IsPresenting || coordinator.IsPresenting)
                { yield return null; continue; }
                var scroll = Rect("CombatLogViewport").GetComponent<ScrollRect>();
                Canvas.ForceUpdateCanvases();
                if (!capturedLongLog && scroll.content.rect.height > scroll.viewport.rect.height)
                {
                    scroll.verticalNormalizedPosition = 1; yield return null;
                    Assert.Greater(scroll.verticalNormalizedPosition, .99f);
                    AssertButtonReachable(Rect("CloseLogButton").GetComponent<Button>());
                    Capture((victory ? "victory" : "defeat") + "-long-log-" + width);
                    capturedLongLog = true;
                }
                Button action = victory && bar.BasicAttackButton.interactable ? bar.BasicAttackButton : bar.WaitButton;
                yield return Click(action);
                Assert.IsTrue(preview.IsVisible);
                Assert.IsTrue(preview.ConfirmButton.interactable);
                yield return Click(preview.ConfirmButton);
                commands++;
                yield return null;
            }
            Assert.AreEqual(GameplaySceneNames.Settlement, SceneManager.GetActiveScene().name, "Battle must actually complete");
            yield return null; yield return null;
            if (victory) Assert.AreEqual(1, commands, "One real confirmed attack must finish the encounter");
            else Assert.IsTrue(capturedLongLog);
            Assert.IsNull(Object.FindFirstObjectByType<CombatHudPresenter>());
            Assert.AreEqual(victory ? 1 : 0, GameBootstrap.RequireRuntime().Bounties.GetState("bounty_guanzhong_shijiahou").Progress);
            Capture((victory ? "victory" : "defeat") + "-returned-" + width);
            Debug.Log("LAYOUT formal return width=" + width + " victory=" + victory + " confirmedCommands=" + commands);
        }

        private sealed class OutcomeCoverageRandom : ICombatResolutionRandomSource
        {
            private readonly CombatSession session;
            private readonly bool victory;
            private int roll;
            public OutcomeCoverageRandom(CombatSession session, bool victory) { this.session = session; this.victory = victory; }
            public float NextPercent()
            {
                bool hit = victory ? session.CurrentActorId == "player" : session.CurrentActorId == "enemy";
                return roll++ % 4 == 0 && hit ? 0 : 99;
            }
        }

        private static RectTransform Rect(string name) => SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<RectTransform>(true)).Single(r => r.name == name);
        private static List<RaycastResult> Hits(Vector2 point)
        {
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits); return hits;
        }
        private static void AssertButtonReachable(Button button)
        {
            var rect = button.GetComponent<RectTransform>(); var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Assert.That(corners[0].x, Is.GreaterThanOrEqualTo(0)); Assert.That(corners[0].y, Is.GreaterThanOrEqualTo(0));
            Assert.That(corners[2].x, Is.LessThanOrEqualTo(Screen.width)); Assert.That(corners[2].y, Is.LessThanOrEqualTo(Screen.height));
            var hits = Hits((corners[0] + corners[2]) * .5f); Assert.IsNotEmpty(hits);
            Assert.AreEqual(button, hits[0].gameObject.GetComponentInParent<Button>(), button.name);
        }
        private IEnumerator Click(Button button)
        {
            AssertButtonReachable(button);
            var mouse = (Mouse)typeof(GuanzhongCombatActionPreviewPlayModeTests).GetField("mouse", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(entry);
            var corners = new Vector3[4]; button.GetComponent<RectTransform>().GetWorldCorners(corners);
            Vector2 point = (corners[0] + corners[2]) * .5f;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 1 }); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
        }
        private static void SetGameSize(int width, int height)
        {
#if UNITY_EDITOR
            var assembly = typeof(UnityEditor.Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var sizes = sizesType.BaseType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { 0 });
            var sizeType = assembly.GetType("UnityEditor.GameViewSize");
            var kind = assembly.GetType("UnityEditor.GameViewSizeType");
            var size = Activator.CreateInstance(sizeType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                new object[] { Enum.ToObject(kind, 1), width, height, "Layout validation" }, null);
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
            int count = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null);
            var game = UnityEditor.EditorWindow.GetWindow(assembly.GetType("UnityEditor.GameView"));
            game.GetType().GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, count - 1);
#else
            Screen.SetResolution(width, height, false);
#endif
        }

        [UnityTest] [Explicit("Read-only pre-layout diagnostic; preserved baseline is not rerun after layout edits.")]
        public IEnumerator CaptureBaselineGeometry()
        {
            Object.FindFirstObjectByType<CombatHudPresenter>().InspectTarget("enemy");
            Object.FindFirstObjectByType<CombatActionBarView>().ArtButton.onClick.Invoke();
            yield return null;
            Canvas.ForceUpdateCanvases();
            Capture("baseline");
        }
#if UNITY_EDITOR
        [UnityEditor.MenuItem("天章/验证/记录整屏布局现场")]
        public static void CaptureVisible() { if (!Application.isPlaying) throw new InvalidOperationException("Requires Play Mode."); Capture("visible"); }
#endif
        public static void Capture(string label)
        {
            Canvas.ForceUpdateCanvases();
            var rects = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RectTransform>(true));
            var canvas = Object.FindFirstObjectByType<Canvas>();
            var evidence = new Evidence { width = Screen.width, height = Screen.height, scale = canvas.scaleFactor,
                canvas = canvas.GetComponent<RectTransform>().rect, cameraViewport = Camera.main.rect,
                rects = rects.Select(r => {
                    var corners = new Vector3[4]; r.GetWorldCorners(corners);
                    var text = r.GetComponent<Text>();
                    return new RectEvidence { path = PathOf(r), active = r.gameObject.activeInHierarchy, local = r.rect,
                        anchorMin = r.anchorMin, anchorMax = r.anchorMax, corners = corners,
                        layout = string.Join(",", r.GetComponents<Component>().Where(c => c is LayoutGroup || c is ContentSizeFitter || c is RectMask2D).Select(c => c.GetType().Name)),
                        text = text == null ? null : text.text, preferredHeight = text == null ? 0 : text.preferredHeight,
                        raycast = r.GetComponent<Graphic>() != null && r.GetComponent<Graphic>().raycastTarget };
                }).ToArray() };
            var directory = Path.Combine(Application.dataPath, "../Logs/layout"); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, label + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(evidence, true)); Debug.Log("Layout evidence: " + path);
        }
        private static string PathOf(Transform value) => value.parent == null ? value.name : PathOf(value.parent) + "/" + value.name;
        [Serializable] private sealed class Evidence { public int width, height; public float scale; public Rect canvas, cameraViewport; public RectEvidence[] rects; }
        [Serializable] private sealed class RectEvidence { public string path, layout, text; public bool active, raycast; public float preferredHeight; public Rect local; public Vector2 anchorMin, anchorMax; public Vector3[] corners; }
    }
}
