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
using TianZhang.Game.CharacterCreation;
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
            WriteSlot(bootstrap, "del_a", "player", "甲角色");
            WriteSlot(bootstrap, "del_b", "player", "乙角色");
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
        public IEnumerator CreationReturnKeepsDraftAndExistingSaveAvailableAtCommonResolutions()
        {
            GameBootstrap bootstrap = GameBootstrap.RequireInstance();
            WriteSlot(bootstrap, "return_read", "player", "已有存档");
            yield return RefreshMenu();
            string slotPath = Path.Combine(directory, "return_read.json");
            string draftSlotPath = Path.Combine(directory, "slot1.json");
            byte[] savedSlotBefore = File.ReadAllBytes(slotPath);
            CharacterCreationController controller = FindInScene<CharacterCreationController>();
            CharacterCreationView view = FindInScene<CharacterCreationView>();
            InputField nameInput = Find("CharacterNameInput").GetComponent<InputField>();
            GameObject creationPanel = Find("CharacterCreationPanel").gameObject;
            GameObject menuPanel = Find("StartMenuPanel").gameObject;
            bool hasGraphics = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            Vector2Int[] sizes = hasGraphics
                ? new[] { new Vector2Int(1920, 1080), new Vector2Int(1366, 768) }
                : new[] { new Vector2Int(Screen.width, Screen.height) };

            foreach (Vector2Int size in sizes)
            {
                if (hasGraphics) SetGameSize(size.x, size.y);
                yield return null;
                yield return null;
                Assert.AreEqual(size.x, Screen.width);
                Assert.AreEqual(size.y, Screen.height);

                yield return Click(Find("NewPlayerButton").GetComponent<Button>());
                Canvas.ForceUpdateCanvases();
                Assert.IsTrue(creationPanel.activeSelf);
                Assert.IsTrue(menuPanel.activeSelf, "the existing start menu stays behind the creation panel");
                AssertReachable(Find("ReturnToMenuButton").GetComponent<Button>());
                nameInput.text = "暂存角色" + size.x;
                yield return Click(view.GetOriginButton("origin_debt_exile"));
                CharacterCreationDraft draft = controller.Draft;
                Assert.AreEqual("origin_debt_exile", draft.OriginId);

                yield return Click(Find("ReturnToMenuButton").GetComponent<Button>());
                Assert.IsFalse(creationPanel.activeSelf);
                Assert.IsTrue(menuPanel.activeSelf);
                Assert.IsNotNull(Find("SaveSlot_return_read"));
                Assert.That(File.ReadAllBytes(slotPath), Is.EqualTo(savedSlotBefore),
                    "returning from an unfinished draft must not write or change an existing save");
                Assert.IsFalse(File.Exists(draftSlotPath), "an unfinished draft must not create a save slot");

                yield return Click(Find("NewPlayerButton").GetComponent<Button>());
                Assert.AreSame(draft, controller.Draft, "reopening must keep the same session draft");
                Assert.AreEqual("origin_debt_exile", controller.Draft.OriginId);
                Assert.AreEqual("暂存角色" + size.x, nameInput.text);
                yield return Click(Find("ReturnToMenuButton").GetComponent<Button>());
                Assert.That(File.ReadAllBytes(slotPath), Is.EqualTo(savedSlotBefore));
                Assert.IsFalse(File.Exists(draftSlotPath));
            }

            string retainedOrigin = controller.Draft.OriginId;
            yield return Click(Find("SaveSlot_return_read").GetComponent<Button>());
            yield return SceneReady(GameplaySceneNames.World);
            Assert.That(File.ReadAllBytes(slotPath), Is.EqualTo(savedSlotBefore),
                "loading the preserved save must not rewrite it");
            Log("creation return resolutions=" + string.Join(",", sizes.Select(size => size.x + "x" + size.y)) +
                " loaded=return_read draft=" + retainedOrigin);
        }

        [UnityTest]
        public IEnumerator DeletingTheLastSlotShowsTheEmptyStateAndStopsTheActiveSlotFromRebuildingIt()
        {
            GameBootstrap bootstrap = GameBootstrap.RequireInstance();
            WriteSlot(bootstrap, "del_only", "player", "唯一角色");
            yield return RefreshMenu();
            string slotPath = Path.Combine(directory, "del_only.json");

            // Negative control: while the slot file still exists this save really does write it.
            var profile = ScriptableObject.CreateInstance<CharacterData>();
            profile.unarmedAttackFamilyId = "body_human";
            profile.realmStage = "凡人";
            profile.charName = "唯一角色";
            profile.realmMultiplier = 1f;
            profile.actionPointMaximumUnits = 100000;
            profile.movementEfficiencyMilliUnits = 2000;
            ownedObjects.Add(profile);
            GameBootstrap.RequireRuntime().BeginNewGame(
                CharacterRuntimeProfile.FromDefinition("player", profile, GrowthRules),
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
            WriteSlot(bootstrap, "del_ok", "player", "正常角色");
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

        // Headless runs validate only their actual viewport; graphical runs also retain both required UI frames.
        [UnityTest]
        public IEnumerator DeleteConfirmationStaysReadableAndReachableWithoutClipping()
        {
            GameBootstrap bootstrap = GameBootstrap.RequireInstance();
            WriteSlot(bootstrap, "del_wide", "player", "宽屏角色名");
            yield return RefreshMenu();
            var evidence = new List<string>();

            bool hasGraphics = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            Vector2Int[] sizes = hasGraphics
                ? new[] { new Vector2Int(1920, 1080), new Vector2Int(1366, 768) }
                : new[] { new Vector2Int(Screen.width, Screen.height) };
            string evidenceDirectory = Path.Combine(Path.GetTempPath(),
                "TianZhang.StartMenuSaveDeletionEvidence", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(evidenceDirectory);

            foreach (Vector2Int size in sizes)
            {
                int targetWidth = size.x;
                int targetHeight = size.y;
                if (hasGraphics) SetGameSize(targetWidth, targetHeight);
                yield return null;
                yield return null;
                Assert.AreEqual(targetWidth, Screen.width, "Actual viewport width must match the evidence.");
                Assert.AreEqual(targetHeight, Screen.height, "Actual viewport height must match the evidence.");
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

                if (hasGraphics)
                {
                    string framePath = Path.Combine(evidenceDirectory,
                        "confirmation-" + targetWidth + "x" + targetHeight + ".png");
                    ScreenCapture.CaptureScreenshot(framePath);
                    float deadline = Time.realtimeSinceStartup + 10;
                    while (!File.Exists(framePath))
                    {
                        Assert.Less(Time.realtimeSinceStartup, deadline, "Expected actual Game View screenshot.");
                        yield return null;
                    }
                    TestContext.WriteLine("UI frame: " + framePath);
                }

                yield return Click(cancel);
                Assert.IsFalse(Find("SaveDeleteConfirmPanel").gameObject.activeSelf);
                Assert.IsTrue(File.Exists(Path.Combine(directory, "del_wide.json")));
            }

            string path = Path.Combine(evidenceDirectory, "ui-evidence.txt");
            File.WriteAllText(path, string.Join("\n", evidence));
            Log("UI evidence: " + path);
            TestContext.WriteLine("UI evidence: " + path);
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
                new object[] { Enum.ToObject(kind, 1), width, height, "Save deletion validation" }, null);
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
            int count = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null);
            var game = UnityEditor.EditorWindow.GetWindow(assembly.GetType("UnityEditor.GameView"));
            game.GetType().GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(game, count - 1);
#else
            Screen.SetResolution(width, height, false);
#endif
        }

        private void WriteSlot(GameBootstrap bootstrap, string slotId, string characterId, string displayName)
        {
            var definition = ScriptableObject.CreateInstance<CharacterData>();
            definition.unarmedAttackFamilyId = "body_human";
            definition.realmStage = "凡人";
            definition.charName = displayName;
            definition.realmMultiplier = 1f;
            definition.actionPointMaximumUnits = 100000;
            definition.movementEfficiencyMilliUnits = 2000;
            ownedObjects.Add(definition);
            var runtime = new GameRuntime();
            runtime.BeginNewGame(
                CharacterRuntimeProfile.FromDefinition(characterId, definition, GrowthRules),
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
