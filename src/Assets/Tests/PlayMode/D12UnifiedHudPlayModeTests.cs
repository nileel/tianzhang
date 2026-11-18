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
                Assert.LessOrEqual(label.preferredHeight, label.rectTransform.rect.height + 1, button.name + " label clips");
                if (hint != null) Assert.LessOrEqual(hint.preferredHeight, hint.rectTransform.rect.height, button.name + " shortcut clips");
            }
            string folder = Path.GetFullPath("Logs/d12-hud"); Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, name + "-bounds.txt"), proof);
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
        { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png")); yield return null; }
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
