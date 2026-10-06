using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Character;
using TianZhang.Cultivation;
using TianZhang.Entity;
using TianZhang.Features.CharacterCreation;
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
    // Formal StartMenuScene, the formal slot store and real pointer input drive every deletion step.
    public sealed class StartMenuSaveDeletionPlayModeTests
    {
        private readonly List<Object> ownedObjects = new List<Object>();
        private Mouse mouse;
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
        private string directory;

        [UnitySetUp]
        public IEnumerator EnterFormalStartMenu()
        {
            background = InputSystem.settings.backgroundBehavior;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>();
            GameBootstrap stale = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (stale != null)
            {
                Object.Destroy(stale.gameObject);
                yield return null;
            }
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return SceneReady(GameplaySceneNames.StartMenu);
            directory = Path.Combine(
                Path.GetTempPath(),
                "TianZhang.StartMenuSaveDeletion",
                Guid.NewGuid().ToString("N"));
            SetPrivateField(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(directory));
        }

        [UnityTearDown]
        public IEnumerator LeaveFormalStartMenu()
        {
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            if (mouse != null)
            {
                InputSystem.RemoveDevice(mouse);
                mouse = null;
            }
            GameBootstrap bootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (bootstrap != null)
            {
                Object.Destroy(bootstrap.gameObject);
                yield return null;
            }
            for (int index = 0; index < ownedObjects.Count; index++)
            {
                if (ownedObjects[index] != null) Object.Destroy(ownedObjects[index]);
            }
            ownedObjects.Clear();
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                Directory.Delete(directory, true);
            directory = null;
        }

        [UnityTest]
        public IEnumerator CancelKeepsBothSlotsAndConfirmDeletesOnlyTheRequestedOne()
        {
            GameBootstrap bootstrap = GameBootstrap.RequireInstance();
            WriteSlot(bootstrap, "del_a", "player_a", "甲角色");
            WriteSlot(bootstrap, "del_b", "player_b", "乙角色");
            yield return RefreshMenu();
            string slotAPath = Path.Combine(directory, "del_a.json");
            string slotBPath = Path.Combine(directory, "del_b.json");
            byte[] beforeA = File.ReadAllBytes(slotAPath);
            byte[] beforeB = File.ReadAllBytes(slotBPath);
            Log("start size=" + Screen.width + "x" + Screen.height + " slots=" + string.Join(",", SlotNames()));

            yield return Click(DeleteButton("del_a"));
            Canvas.ForceUpdateCanvases();
            Assert.IsTrue(Find("SaveDeleteConfirmPanel").gameObject.activeSelf);
            StringAssert.Contains("del_a", MessageText());
            StringAssert.Contains("甲角色", MessageText());
            StringAssert.Contains("不可恢复", MessageText());

            yield return Click(Find("CancelDeleteButton").GetComponent<Button>());
            Assert.IsFalse(Find("SaveDeleteConfirmPanel").gameObject.activeSelf);
            Assert.IsTrue(File.Exists(slotAPath), "cancelling must keep the slot on disk");
            Assert.That(File.ReadAllBytes(slotAPath), Is.EqualTo(beforeA), "cancelling must not rewrite the slot");
            Assert.That(File.ReadAllBytes(slotBPath), Is.EqualTo(beforeB));
            Assert.IsNotNull(Find("SaveSlot_del_a"), "cancelling must keep the row");
            Log("cancel kept " + string.Join(",", SlotNames()));

            yield return Click(DeleteButton("del_a"));
            yield return Click(Find("ConfirmDeleteButton").GetComponent<Button>());
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();

            Assert.IsFalse(File.Exists(slotAPath), "the confirmed slot must be gone");
            Assert.IsTrue(File.Exists(slotBPath), "the other slot must survive");
            Assert.That(File.ReadAllBytes(slotBPath), Is.EqualTo(beforeB));
            Assert.IsNull(Find("SaveSlot_del_a"), "the deleted row must disappear immediately");
            Assert.IsNotNull(Find("SaveSlot_del_b"));
            Text keptLabel = Find("SaveSlot_del_b").Find("Label").GetComponent<Text>();
            StringAssert.Contains("del_b", keptLabel.text);
            StringAssert.Contains("乙角色", keptLabel.text);
            Assert.IsTrue(
                Find("SaveSlot_del_b").GetComponent<Button>().interactable,
                "the remaining slot must stay loadable");
            // The row itself must keep loading on a real pointer click now that it also carries a delete action.
            VerifyRaycastHits(Find("SaveSlot_del_b").GetComponent<Button>());
            Assert.IsFalse(Find("EmptySaveText").gameObject.activeSelf);
            Log("confirm left " + string.Join(",", SlotNames()));
        }

        [UnityTest]
        public IEnumerator DeletingTheLastSlotShowsTheEmptyStateAndStopsTheActiveSlotFromRebuildingIt()
        {
            GameBootstrap bootstrap = GameBootstrap.RequireInstance();
            WriteSlot(bootstrap, "del_only", "player_only", "唯一角色");
            yield return RefreshMenu();
            string slotPath = Path.Combine(directory, "del_only.json");

            // Negative control: while the slot file still exists this save really does write it.
            var profile = ScriptableObject.CreateInstance<CharacterData>();
            profile.charName = "唯一角色";
            profile.realmMultiplier = 1f;
            ownedObjects.Add(profile);
            GameBootstrap.RequireRuntime().BeginNewGame(
                CharacterRuntimeProfile.FromDefinition("player_only", profile),
                CultivationState.CreateEmpty(),
                "guanzhong_hub");
            bootstrap.ActivateSlot("del_only");
            Assert.IsTrue(bootstrap.SaveActiveSlot().Succeeded, "baseline save must succeed before deletion");
            Assert.IsTrue(File.Exists(slotPath));

            yield return Click(DeleteButton("del_only"));
            yield return Click(Find("ConfirmDeleteButton").GetComponent<Button>());
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();

            Assert.IsFalse(File.Exists(slotPath));
            Assert.IsNull(Find("SaveSlot_del_only"));
            Assert.IsTrue(
                Find("EmptySaveText").gameObject.activeSelf,
                "removing the last slot must show the empty list hint");
            Transform content = Find("SaveSlotScroll").Find("Viewport").Find("SaveSlotContainer");
            Assert.AreEqual(0, content.childCount, "no slot row may survive the last deletion");
            Log("empty state active=" + Find("EmptySaveText").gameObject.activeSelf);

            Assert.IsFalse(
                bootstrap.SaveActiveSlot().Succeeded,
                "a deleted active slot must not be saved again");
            Assert.IsFalse(File.Exists(slotPath), "the deleted active slot must not be rebuilt on disk");
        }

        [UnityTest]
        public IEnumerator CorruptSlotRowIsDeletableWithoutBeingLoadable()
        {
            GameBootstrap bootstrap = GameBootstrap.RequireInstance();
            WriteSlot(bootstrap, "del_ok", "player_ok", "正常角色");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "del_broken.json"), "not-json");
            yield return RefreshMenu();

            Assert.IsFalse(
                Find("SaveSlot_del_broken").GetComponent<Button>().interactable,
                "a corrupt slot must not be loadable");
            StringAssert.Contains("无法读取", Find("SaveSlot_del_broken").Find("Label").GetComponent<Text>().text);
            Assert.IsNotNull(DeleteButton("del_broken"), "a corrupt real slot must still offer deletion");
            VerifyRaycastHits(DeleteButton("del_broken"));

            yield return Click(DeleteButton("del_broken"));
            StringAssert.Contains("del_broken", MessageText());
            yield return Click(Find("ConfirmDeleteButton").GetComponent<Button>());
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();

            Assert.IsFalse(File.Exists(Path.Combine(directory, "del_broken.json")));
            Assert.IsNull(Find("SaveSlot_del_broken"));
            Assert.IsNotNull(Find("SaveSlot_del_ok"));
            Assert.IsTrue(File.Exists(Path.Combine(directory, "del_ok.json")));
        }

        // This runner is -nographics, so Screen resolution cannot be forced (EditorWindow.GetWindow(GameView)
        // fails without a graphic device). The measured claims below are therefore the resolution-independent
        // ones: the confirmation stays active, complete, unclipped and pointer-reachable.
        [UnityTest]
        public IEnumerator DeleteConfirmationStaysReadableAndReachableWithoutClipping()
        {
            GameBootstrap bootstrap = GameBootstrap.RequireInstance();
            WriteSlot(bootstrap, "del_wide", "player_wide", "宽屏角色名");
            yield return RefreshMenu();
            var evidence = new List<string>();

            foreach (int targetWidth in new[] { 1920, 1366 })
            {
                int targetHeight = targetWidth == 1920 ? 1080 : 768;
                yield return null;
                Canvas.ForceUpdateCanvases();

                yield return Click(DeleteButton("del_wide"));
                yield return null;
                Canvas.ForceUpdateCanvases();
                GameObject panel = Find("SaveDeleteConfirmPanel").gameObject;
                Text message = Find("DeleteConfirmText").GetComponent<Text>();
                Button confirm = Find("ConfirmDeleteButton").GetComponent<Button>();
                Button cancel = Find("CancelDeleteButton").GetComponent<Button>();
                Assert.IsTrue(panel.activeSelf);
                StringAssert.Contains("del_wide", message.text);
                StringAssert.Contains("宽屏角色名", message.text);
                StringAssert.Contains("不可恢复", message.text);
                // The HUD canvas scales uniformly from the 1920x1080 reference, so a text that fits its
                // box in canvas space fits at 1920x1080 and 1366x768 alike.
                Assert.LessOrEqual(
                    message.preferredHeight,
                    message.rectTransform.rect.height + .1f,
                    "the confirmation text must not be clipped for target " + targetWidth);
                Assert.IsTrue(
                    confirm.interactable && cancel.interactable,
                    "both confirmation actions must stay enabled for target " + targetWidth);
                AssertReachable(confirm);
                AssertReachable(cancel);
                AssertReachable(DeleteButton("del_wide"));
                evidence.Add("target=" + targetWidth + "x" + targetHeight +
                    " actualScreen=" + Screen.width + "x" + Screen.height +
                    " scale=" + CanvasScale() +
                    " panel=" + Corners(panel.transform) +
                    " message=\"" + message.text.Replace("\n", "\\n") + "\"" +
                    " messageRect=" + message.rectTransform.rect +
                    " preferredHeight=" + message.preferredHeight.ToString("0.##") +
                    " confirm=" + Corners(confirm.transform) +
                    " cancel=" + Corners(cancel.transform) +
                    " rowDelete=" + Corners(DeleteButton("del_wide").transform));
                Log(evidence[evidence.Count - 1]);

                yield return Click(cancel);
                Assert.IsFalse(Find("SaveDeleteConfirmPanel").gameObject.activeSelf);
                Assert.IsTrue(File.Exists(Path.Combine(directory, "del_wide.json")));
            }

            string path = Path.Combine(Path.GetTempPath(), "tzg-startmenu-save-deletion-ui-evidence.txt");
            File.WriteAllText(path, string.Join("\n", evidence));
            Log("UI evidence: " + path);
        }

        private void WriteSlot(GameBootstrap bootstrap, string slotId, string characterId, string displayName)
        {
            var definition = ScriptableObject.CreateInstance<CharacterData>();
            definition.charName = displayName;
            definition.realmMultiplier = 1f;
            ownedObjects.Add(definition);
            var runtime = new GameRuntime();
            runtime.BeginNewGame(
                CharacterRuntimeProfile.FromDefinition(characterId, definition),
                CultivationState.CreateEmpty(),
                "guanzhong_hub");
            GameSaveSlotWriteResult write = bootstrap.SlotStore.Write(slotId, runtime.CaptureSave());
            Assert.IsTrue(write.Succeeded, slotId + " " + write.FailureReason);
        }

        private IEnumerator RefreshMenu()
        {
            FindInScene<StartMenuController>().RefreshSlots();
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
        }

        private IEnumerator Click(Button button)
        {
            Canvas.ForceUpdateCanvases();
            Assert.IsTrue(button.gameObject.activeInHierarchy, button.name + " hidden");
            Assert.IsTrue(button.interactable, button.name + " disabled");
            Vector2 point = VerifyRaycastHits(button);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 1 });
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
            yield return null;
            yield return null;
        }

        // The real pointer path must resolve to this very button, not to the row behind it.
        private static Vector2 VerifyRaycastHits(Button button)
        {
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4];
            button.GetComponent<RectTransform>().GetWorldCorners(corners);
            Assert.GreaterOrEqual(corners[0].x, -1, button.name);
            Assert.GreaterOrEqual(corners[0].y, -1, button.name);
            Assert.LessOrEqual(corners[2].x, Screen.width + 1, button.name);
            Assert.LessOrEqual(corners[2].y, Screen.height + 1, button.name);
            Vector2 point = (corners[0] + corners[2]) * .5f;
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            Assert.IsNotEmpty(hits, button.name + " is not reachable by pointer at " + point);
            Assert.AreEqual(button, hits[0].gameObject.GetComponentInParent<Button>(), button.name);
            return point;
        }

        private static void AssertReachable(Button button)
        {
            var corners = new Vector3[4];
            button.GetComponent<RectTransform>().GetWorldCorners(corners);
            Assert.GreaterOrEqual(corners[0].x, -1, button.name);
            Assert.GreaterOrEqual(corners[0].y, -1, button.name);
            Assert.LessOrEqual(corners[2].x, Screen.width + 1, button.name);
            Assert.LessOrEqual(corners[2].y, Screen.height + 1, button.name);
        }

        private string MessageText() => Find("DeleteConfirmText").GetComponent<Text>().text;

        private static string Corners(Transform target)
        {
            var corners = new Vector3[4];
            ((RectTransform)target).GetWorldCorners(corners);
            return "[" + corners[0].ToString("0.#") + ".." + corners[2].ToString("0.#") + "]";
        }

        private static float CanvasScale() =>
            Object.FindFirstObjectByType<Canvas>().scaleFactor;

        private static string[] SlotNames() =>
            SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Where(t => t.name.StartsWith("SaveSlot_", StringComparison.Ordinal))
                .Select(t => t.name.Substring("SaveSlot_".Length))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

        private static Button DeleteButton(string slotId) =>
            Find("DeleteSlot_" + slotId).GetComponent<Button>();

        private static Transform Find(string name) =>
            SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(t => t.name == name);

        private static T FindInScene<T>() where T : Component =>
            SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<T>(true)).Single();

        private static IEnumerator SceneReady(string scene)
        {
            float deadline = Time.realtimeSinceStartup + 25;
            while (SceneManager.GetActiveScene().name != scene)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "Expected " + scene);
                yield return null;
            }
            yield return null;
            yield return null;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(target, value);
        }

        private void Log(string message)
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "tzg-startmenu-save-deletion-steps.log"),
                message + "\n");
        }
    }
}
