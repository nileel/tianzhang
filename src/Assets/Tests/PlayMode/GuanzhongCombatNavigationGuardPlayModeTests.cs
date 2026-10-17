#if UNITY_EDITOR
using System.Collections;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Character;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Cultivation;
using TianZhang.Entity;
using TianZhang.Features.Adventure;
using TianZhang.Features.CharacterCreation;
using TianZhang.Features.CombatPresentation;
using TianZhang.Features.Settlement;
using TianZhang.Features.WorldMap;
using TianZhang.Game.CharacterCreation;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.Infrastructure.Persistence;
using TianZhang.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongCombatNavigationGuardPlayModeTests
    {
        private const string Slot = "withdrawal-test", Bounty = "bounty_guanzhong_shijiahou";
        private string saveDirectory;
        private AdventureController adventure;
        private EncounterCoordinator encounter;
        private CombatSession combat;
        private AdventureSpawnSet spawned;
        private CombatCommandInput commands;
        private CombatHudPresenter combatHud;
        private AdventureHudPresenter adventureHud;
        private CombatActionBarView actions;
        private Mouse mouse;
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;

        [UnitySetUp]
        public IEnumerator PreparePointer()
        {
            background = InputSystem.settings.backgroundBehavior;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>();
            yield return null;
        }

        private static T Field<T>(object owner, string name) =>
            (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner);
        private static T Find<T>() where T : Component => SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).Single();
        private Button ReturnButton => Field<Button>(adventureHud, "returnButton");
        private Button ConfirmButton => Field<Button>(adventureHud, "confirmWithdrawalButton");
        private Button ContinueButton => Field<Button>(adventureHud, "continueCombatButton");

        [UnityTearDown]
        public IEnumerator CleanIsolatedSave()
        {
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (saveDirectory == null) yield break;
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return null;
            if (Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory, true);
            saveDirectory = null;
        }

        private static IEnumerator SceneReady(string scene)
        {
            for (float deadline = Time.realtimeSinceStartup + 20; Time.realtimeSinceStartup < deadline &&
                SceneManager.GetActiveScene().name != scene;) yield return null;
            Assert.AreEqual(scene, SceneManager.GetActiveScene().name);
            yield return null;
        }

        private IEnumerator EnterFormalAdventure()
        {
            var stale = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return SceneReady(GameplaySceneNames.StartMenu);
            saveDirectory = Path.Combine(Application.temporaryCachePath, "TianZhang-Withdrawal-" + Guid.NewGuid().ToString("N"));
            typeof(GameBootstrap).GetField("slotStore", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(GameBootstrap.RequireInstance(), new GameSaveSlotStore(saveDirectory));
            var creation = Find<CharacterCreationController>();
            creation.Open(); Assert.IsTrue(creation.SelectOrigin("origin_minor_clan"));
            creation.Draft.Innate = new InnateAttributeSet(15, 3, 6, 3, 3);
            creation.Submit(Slot, "撤离隔离存档验证");
            yield return SceneReady(GameplaySceneNames.World);
            // Production minor-clan grants and realm qualification, never a mortal starter fixture.
            GuanzhongBasicAttackPlayModeTests.PrepareEquippedPlayerFixture();
            Assert.IsTrue(GameBootstrap.RequireRuntime().Player.AbilityLoadout.UnequipSpell("spell_julingshu"));
            var world = Find<WorldMapController>(); Assert.IsTrue(world.SelectNode("guanzhong_hub"));
            world.EnterSelectedLocation(); yield return SceneReady(GameplaySceneNames.Settlement);
            var settlement = Find<SettlementController>();
            Find<BountyBoardView>().Show(Field<ContentCatalogData>(settlement, "contentCatalog"),
                "guanzhong_city", GameBootstrap.RequireRuntime().Bounties);
            Find<BountyBoardView>().SubmitAccept(Bounty);
            Assert.IsTrue(settlement.EnterAdventure("guanzhong_wild"));
            yield return SceneReady(GameplaySceneNames.Adventure);
            adventure = Find<AdventureController>(); encounter = Find<EncounterCoordinator>();
            adventureHud = Find<AdventureHudPresenter>(); combatHud = Find<CombatHudPresenter>();
            commands = Find<CombatCommandInput>(); actions = Find<CombatActionBarView>();
            for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            Assert.AreEqual(AdventureSceneState.Exploration, adventure.CurrentState);
            yield return null;
            Assert.IsTrue(ReturnButton.gameObject.activeInHierarchy);
            Assert.IsTrue(ReturnButton.interactable);
            Assert.AreEqual("返回关中城", ReturnButton.GetComponentInChildren<Text>().text);
            Assert.AreEqual("UICanvas", Field<GameObject>(adventureHud, "returnEntry").transform.parent.name);
        }

        private IEnumerator BeginRealEncounter()
        {
            Assert.IsTrue(Find<AdventureInputController>().SelectNode("shijiahou_encounter"));
            combat = Field<CombatSession>(encounter, "session"); spawned = Field<AdventureSpawnSet>(encounter, "spawned");
            encounter.SetCombatResolutionRandomSource(new GuanzhongBasicAttackPlayModeTests.OutcomeFixtureRandom(combat, false));
            yield return PlayerReady();
            Assert.IsFalse(Field<GameObject>(combatHud, "explorationPanel").activeSelf);
            Assert.IsTrue(ReturnButton.gameObject.activeInHierarchy);
            Assert.AreEqual("撤离战斗", ReturnButton.GetComponentInChildren<Text>().text);
            Assert.IsFalse(Find<AdventureInputController>().SelectNode("return"));
            var args = new object[] { null, null };
            Assert.IsFalse((bool)typeof(AdventureController).GetMethod("ReturnToSource", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(adventure, args));
            Assert.AreEqual("adventure_not_exploring", args[1]);
        }

        private IEnumerator PlayerReady()
        {
            for (float deadline = Time.realtimeSinceStartup + 25; Time.realtimeSinceStartup < deadline &&
                (combat.CurrentActorId != "player" || combat.TurnPhase != CombatTurnPhase.Active || encounter.IsPresenting);)
                yield return null;
            yield return null;
            Assert.IsTrue(encounter.CanWithdraw(out string reason), reason);
        }

        private string State() => GameBootstrap.RequireRuntime().CaptureSaveJson() + ":" +
            GameBootstrap.RequireRuntime().InventoryVersion + ":" + combat.StateVersion + ":" + combat.TurnSequence + ":" +
            combat.TurnScheduler.CurrentTick + ":" + spawned.Player.CurrentHealth + ":" + spawned.Player.CurrentSpirit + ":" +
            spawned.Player.ActionPointsRemainingUnits + ":" + combat.PendingCtPenalty;

        private static void PointerClick(Button button)
        {
            Canvas.ForceUpdateCanvases();
            var point = RectTransformUtility.WorldToScreenPoint(null, button.transform.TransformPoint(
                ((RectTransform)button.transform).rect.center));
            var data = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
            Assert.IsNotEmpty(hits, button.name);
            GameObject receiver = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
            Assert.AreSame(button.gameObject, receiver, "A different surface owns this click.");
            ExecuteEvents.Execute(receiver, data, ExecuteEvents.pointerClickHandler);
        }

        private IEnumerator Capture(string stage, int singleWidth = 0)
        {
            foreach (int width in singleWidth == 0 ? new[] { 1920, 1366 } : new[] { singleWidth })
            {
                GuanzhongCombatEquipmentPlayModeTests.SetGameSize(width, width == 1920 ? 1080 : 768);
                for (int frame = 0; frame < 8; frame++) yield return null;
                Canvas.ForceUpdateCanvases();
                Assert.AreEqual(width, Screen.width);
                Assert.AreEqual(width == 1920 ? 1080 : 768, Screen.height);
                foreach (Text text in Field<GameObject>(adventureHud, "returnEntry").GetComponentsInChildren<Text>())
                    AssertReadable(text);
                if (adventure.IsWithdrawalConfirmationOpen)
                    foreach (Text text in Field<GameObject>(adventureHud, "withdrawalConfirmation").GetComponentsInChildren<Text>())
                        AssertReadable(text);
                string directory = Path.Combine(Application.dataPath, "../Logs/q4-withdrawal"); Directory.CreateDirectory(directory);
                string screenshot = Path.Combine(directory, stage + "-" + width + ".png");
                File.Delete(screenshot);
                ScreenCapture.CaptureScreenshot(screenshot);
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.IsTrue(File.Exists(screenshot));
                var rect = ReturnButton.GetComponent<RectTransform>(); var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                var canvas = rect.GetComponentInParent<Canvas>();
                var panel = Field<GameObject>(combatHud, "explorationPanel").GetComponent<RectTransform>();
                var panelCorners = new Vector3[4]; panel.GetWorldCorners(panelCorners);
                AssertEntryLayout();
                GuanzhongCombatLayoutPlayModeTests.Capture("q4-" + stage + "-" + width);
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform,
                    RectTransformUtility.WorldToScreenPoint(null, panelCorners[2]), null, out Vector2 sourceTopRight);
                Debug.Log("[WithdrawalBounds] source=" + panel.name + "; sourceParent=" + panel.parent.name +
                    "; sourceVisualMin=" + panelCorners[0] + "; sourceVisualMax=" + panelCorners[2] +
                    "; sourceAnchors=" + panel.anchorMin + "/" + panel.anchorMax + "; sourceSize=" + panel.rect.size +
                    "; destination=" + canvas.name + "; mode=" + canvas.renderMode + "; camera=" + canvas.worldCamera +
                    "; scale=" + canvas.scaleFactor + "; sourceTopRightInCanvas=" + sourceTopRight +
                    "; entryMin=" + corners[0] + "; entryMax=" + corners[2] + "; sourceWriter=AdventureSceneBuilder/CombatHudPresenter.Present");
                Debug.Log("[WithdrawalUI] stage=" + stage + "; screen=" + Screen.width + "x" + Screen.height +
                    "; source=" + rect.name + "; parent=" + rect.parent.name + "; canvas=" + rect.GetComponentInParent<Canvas>().name +
                    "; min=" + corners[0] + "; max=" + corners[2] + "; writer=AdventureController.LateUpdate/AdventureHudPresenter.PresentReturnEntry");
            }
        }

        private static Rect ScreenRect(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private void AssertEntryLayout()
        {
            var rects = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<RectTransform>(true)).ToArray();
            var entry = rects.Single(rect => rect.name == "AdventureReturnEntry");
            var panel = Field<GameObject>(combatHud, "explorationPanel").GetComponent<RectTransform>();
            Assert.AreSame(panel.parent, entry.parent);
            var canvas = entry.GetComponentInParent<Canvas>();
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
            Rect bounds = ScreenRect(entry);
            Assert.GreaterOrEqual(bounds.xMin, 0); Assert.GreaterOrEqual(bounds.yMin, 0);
            Assert.LessOrEqual(bounds.xMax, Screen.width); Assert.LessOrEqual(bounds.yMax, Screen.height);
            if (adventure.CurrentState == AdventureSceneState.Combat)
            {
                Assert.Less(bounds.xMin, Screen.width * .05f, "Entry must use the left screen margin.");
                Assert.Greater(bounds.yMin, Screen.height * .8f, "Entry must stay in the upper-left corner.");
                foreach (string name in new[] { "CombatTimelineRoot", "TimelineFocus", "CombatHotbar" })
                {
                    var other = rects.Single(rect => rect.name == name);
                    if (other.gameObject.activeInHierarchy)
                        Assert.IsFalse(bounds.Overlaps(ScreenRect(other)), "Return entry overlaps " + name);
                }
            }
            else Assert.GreaterOrEqual(bounds.xMin, ScreenRect(panel).xMax, "Return entry overlaps exploration content.");
            Rect button = ScreenRect(ReturnButton.GetComponent<RectTransform>());
            Rect reason = ScreenRect(Field<Text>(adventureHud, "returnReason").rectTransform);
            Assert.IsTrue(bounds.Contains(reason.min) && bounds.Contains(reason.max));
            Assert.IsFalse(button.Overlaps(reason), "Disabled reason must not overlap the button.");
        }

        private IEnumerator ClickWithMouse(Button button)
        {
            Canvas.ForceUpdateCanvases();
            Vector2 point = ScreenRect(button.GetComponent<RectTransform>()).center;
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            Assert.IsNotEmpty(hits);
            Assert.AreSame(button.gameObject, ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 1 }); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
        }

        [UnityTest] public IEnumerator ReturnEntryMouseAndReentryAt1920() => VerifyReturnEntryMouseAndReentry(1920);
        [UnityTest] public IEnumerator ReturnEntryMouseAndReentryAt1366() => VerifyReturnEntryMouseAndReentry(1366);

        private IEnumerator VerifyReturnEntryMouseAndReentry(int width)
        {
            yield return EnterFormalAdventure(); yield return Capture("mouse-exploration", width);
            yield return BeginRealEncounter(); yield return Capture("mouse-combat", width);
            string before = State();
            yield return ClickWithMouse(ReturnButton);
            Assert.IsTrue(adventure.IsWithdrawalConfirmationOpen);
            yield return Capture("mouse-confirmation", width);
            yield return ClickWithMouse(ContinueButton);
            Assert.IsFalse(adventure.IsWithdrawalConfirmationOpen); Assert.AreEqual(before, State());
            AssertEntryLayout();
            yield return ClickWithMouse(ReturnButton); yield return ClickWithMouse(ConfirmButton);
            yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.AreEqual(0, GameBootstrap.RequireRuntime().Bounties.GetState(Bounty).Progress);
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild"));
            yield return SceneReady(GameplaySceneNames.Adventure); yield return null;
            adventure = Find<AdventureController>(); encounter = Find<EncounterCoordinator>();
            adventureHud = Find<AdventureHudPresenter>(); combatHud = Find<CombatHudPresenter>();
            commands = Find<CombatCommandInput>(); actions = Find<CombatActionBarView>();
            Assert.AreEqual(AdventureSceneState.Exploration, adventure.CurrentState);
            yield return Capture("mouse-reentered", width);
            string disk = File.ReadAllText(Directory.GetFiles(saveDirectory, "*.json").Single());
            yield return ClickWithMouse(ReturnButton);
            yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.AreEqual(disk, File.ReadAllText(Directory.GetFiles(saveDirectory, "*.json").Single()));
            Debug.Log("[Q4] Real mouse confirmation, cancellation, withdrawal, re-entry and exploration return passed at " + width);
        }

        private static void AssertReadable(Text text)
        {
            Assert.LessOrEqual(text.preferredHeight, text.rectTransform.rect.height + .1f, text.name);
            if (text.horizontalOverflow == HorizontalWrapMode.Overflow)
                Assert.LessOrEqual(text.preferredWidth, text.rectTransform.rect.width + .1f, text.name);
        }

        [UnityTest]
        public IEnumerator FormalWithdrawalCancelsPreviewsPreservesResourcesAndSavesOnTownRequest()
        {
            yield return EnterFormalAdventure(); yield return Capture("exploration");
            yield return BeginRealEncounter(); yield return Capture("combat");
            CharacterRecord initialPlayer = GameBootstrap.RequireRuntime().CaptureSave().player;
            yield return GuanzhongBasicAttackPlayModeTests.WaitForMeleeContact(encounter, combat);
            // All preview actions must be executable under Q1, including healing an injured actor.
            for (int turn = 0; turn < 10 && spawned.Player.CurrentHealth == spawned.Player.MaximumHealth; turn++)
            {
                encounter.RequestEndTurn("player"); yield return null; yield return PlayerReady();
            }
            Assert.Less(spawned.Player.CurrentHealth, spawned.Player.MaximumHealth);
            foreach (Button action in new[] { actions.BasicAttackButton, actions.ArtButton, actions.GuardButton,
                actions.SwapSpellButton, actions.SwapEquipmentButton, actions.UseItemButton })
            {
                action.onClick.Invoke(); Assert.IsNotNull(commands.Candidate, action.name);
                var old = commands.Candidate; string before = State();
                PointerClick(ReturnButton); ReturnButton.onClick.Invoke();
                Assert.IsTrue(adventure.IsWithdrawalConfirmationOpen);
                Assert.AreSame(ConfirmButton.gameObject, EventSystem.current.currentSelectedGameObject);
                for (int press = 0; press < 4; press++)
                {
                    ExecuteEvents.Execute(EventSystem.current.currentSelectedGameObject,
                        new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Right }, ExecuteEvents.moveHandler);
                    Assert.IsTrue(EventSystem.current.currentSelectedGameObject == ConfirmButton.gameObject ||
                        EventSystem.current.currentSelectedGameObject == ContinueButton.gameObject);
                }
                Assert.IsNull(commands.Candidate); Assert.IsNull(Field<CombatCommandPreview>(encounter, "swapPreview"));
                encounter.ConfirmAction(old); encounter.RequestEndTurn("player");
                for (int frame = 0; frame < 12; frame++) yield return null;
                Assert.AreEqual(before, State(), action.name + " confirmation advanced state");
                // Modal owns background raycasts; direct command bypasses are also refused.
                var point = RectTransformUtility.WorldToScreenPoint(null, actions.BasicAttackButton.transform.position);
                var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
                Assert.IsTrue(hits[0].gameObject.transform.IsChildOf(Field<GameObject>(adventureHud, "withdrawalConfirmation").transform));
                actions.BasicAttackButton.onClick.Invoke(); Assert.IsNull(commands.Candidate);
                PointerClick(ContinueButton); Assert.AreEqual(before, State());
                Assert.IsNull(commands.Candidate); Assert.IsFalse(adventure.IsWithdrawalConfirmationOpen);
            }
            actions.MoveButton.onClick.Invoke(); Assert.IsTrue(Find<CombatMovementView>().IsSelecting);
            string beforeMove = State(); PointerClick(ReturnButton);
            Assert.IsFalse(Find<CombatMovementView>().IsSelecting);
            Assert.IsTrue(Find<CombatMovementView>().Highlights.Values.All(value => !value.gameObject.activeSelf));
            yield return Capture("confirmation"); Assert.AreEqual(beforeMove, State());
            PointerClick(ContinueButton); Assert.AreEqual(beforeMove, State());

            // Let the production enemy actually act; no injury, realm or inventory is injected.
            for (int turn = 0; turn < 10 && spawned.Player.CurrentHealth == spawned.Player.MaximumHealth; turn++)
            {
                encounter.RequestEndTurn("player"); yield return null; yield return PlayerReady();
            }
            Assert.Less(spawned.Player.CurrentHealth, spawned.Player.MaximumHealth);
            var runtime = GameBootstrap.RequireRuntime();
            actions.ArtButton.onClick.Invoke();
            yield return GuanzhongCombatTargetSelectionPlayModeTests.PointArt(combat, spawned.Enemy.Id);
            Assert.IsTrue(commands.Preview.CanConfirm, commands.Preview.Reason);
            int spiritBeforeCast = spawned.Player.CurrentSpirit;
            yield return GuanzhongBasicAttackPlayModeTests.ClickPointedAttack(); yield return PlayerReady();
            Assert.Less(spawned.Player.CurrentSpirit, spiritBeforeCast);
            encounter.RequestEndTurn("player"); yield return null; yield return PlayerReady();
            actions.SwapEquipmentButton.onClick.Invoke(); Assert.IsTrue(commands.Preview.CanConfirm, commands.Preview.Reason);
            commands.ConfirmCandidate(); yield return PlayerReady();
            actions.UseItemButton.onClick.Invoke(); Assert.IsTrue(commands.Preview.CanConfirm, commands.Preview.Reason);
            commands.ConfirmCandidate(); yield return PlayerReady();
            Assert.AreEqual("item_iron_blade", runtime.Player.MainEquipmentItemId);
            Assert.AreEqual(2, runtime.GetItemQuantity("item_huoxue_dan"));
            encounter.RequestEndTurn("player"); yield return null; yield return PlayerReady();
            actions.SwapSpellButton.onClick.Invoke(); Assert.IsTrue(commands.Preview.CanConfirm, commands.Preview.Reason);
            commands.ConfirmCandidate(); yield return PlayerReady();
            CollectionAssert.AreNotEqual(initialPlayer.equippedSpells, spawned.Player.EquippedArtProfileIds);
            CollectionAssert.AreEqual(initialPlayer.equippedSpells, runtime.CaptureSave().player.equippedSpells);
            Assert.AreEqual(initialPlayer.currentHealth, runtime.CaptureSave().player.currentHealth);
            Assert.AreEqual(initialPlayer.currentSpirit, runtime.CaptureSave().player.currentSpirit);
            string persistentPlayer = JsonUtility.ToJson(runtime.CaptureSave().player);
            string originalDisk = File.ReadAllText(Directory.GetFiles(saveDirectory, "*.json").Single());
            var units = Find<Static3DCombatUnitPresentationProvider>();
            Find<CombatHudPresenter>().InspectTarget("enemy");
            int loads = 0;
            typeof(AdventureController).GetField("loadScene", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(adventure, (Action<string>)(scene => { loads++; SceneManager.LoadScene(scene); }));
            PointerClick(ReturnButton); yield return null; PointerClick(ConfirmButton);
            // These synchronous duplicates arrive before the old scene is unloaded.
            adventure.ConfirmWithdrawal(); adventure.RequestReturn();
            Assert.AreEqual(AdventureSceneState.Returning, adventure.CurrentState);
            Assert.IsFalse(encounter.IsRunning); Assert.IsNull(Field<AdventureSpawnSet>(encounter, "spawned"));
            Assert.IsNull(Field<Coroutine>(encounter, "combatRoutine"));
            Assert.IsNull(combatHud.Snapshot); Assert.IsNull(combatHud.InspectedTargetId); Assert.IsNull(commands.Candidate);
            Assert.IsFalse(units.TryGetPresentedUnit("player", out _)); Assert.IsFalse(units.TryGetPresentedUnit("enemy", out _));
            LogAssert.Expect(LogType.Error, new Regex(FormalEncounterRules.AlreadyConsumedReason));
            adventure.ResolveEncounter(CombatSessionOutcome.Victory, spawned.EnemyData);
            Assert.AreEqual(AdventureSceneState.Returning, adventure.CurrentState);
            Assert.IsNull(adventure.LastFormalEncounterResult); Assert.AreEqual(CombatSessionOutcome.Ongoing, adventure.LastEncounterOutcome);
            Assert.AreEqual(1, loads);
            typeof(EncounterCoordinator).GetMethod("Complete", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(encounter, new object[] { CombatSessionOutcome.Victory });
            Assert.AreEqual(1, loads);
            yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.AreEqual("guanzhong_city", runtime.Navigation.SettlementId); Assert.IsNull(runtime.Navigation.AdventureId);
            Assert.AreEqual(0, runtime.Bounties.GetState(Bounty).Progress);
            Assert.AreEqual(0, runtime.GetItemQuantity("item_shijia_piece")); Assert.AreEqual(0, runtime.GetItemQuantity("item_lingshi_low"));
            Assert.AreEqual(2, runtime.GetItemQuantity("item_huoxue_dan"));
            Assert.AreEqual(persistentPlayer, JsonUtility.ToJson(runtime.CaptureSave().player));
            Assert.AreEqual(1, loads);
            Assert.AreEqual(originalDisk, File.ReadAllText(Directory.GetFiles(saveDirectory, "*.json").Single()));
            string saved = runtime.CaptureSaveJson();
            Find<SettlementController>().SaveAndReturnToMenu(); yield return SceneReady(GameplaySceneNames.StartMenu);
            Assert.IsTrue(new GameSaveSlotStore(saveDirectory).Read(Slot).Succeeded);
            Find<StartMenuController>().LoadPlayer(Slot); yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.AreEqual(saved, GameBootstrap.RequireRuntime().CaptureSaveJson());
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild")); yield return SceneReady(GameplaySceneNames.Adventure);
            Assert.AreEqual(AdventureSceneState.Exploration, Find<AdventureController>().CurrentState);
            Assert.IsFalse(Find<EncounterCoordinator>().IsRunning); Assert.IsFalse(Find<AdventureController>().IsWithdrawalConfirmationOpen);
            Assert.IsNull(Find<CombatCommandInput>().Candidate);
            Assert.IsFalse(Find<Static3DCombatUnitPresentationProvider>().TryGetPresentedUnit("enemy", out _));
            Debug.Log("[Withdrawal] PASS formal entry, previews, modal pause, actual enemy injury, committed resources, no reward, single terminal guard, town disk save and re-entry; slot=" + saveDirectory);
        }

        [UnityTest]
        public IEnumerator FormalUnavailableClicksAreNotCachedAndFinalOutcomeWins()
        {
            yield return EnterFormalAdventure(); yield return BeginRealEncounter();
            CombatActionCandidate move = combatHud.Snapshot.Movement.Options.OrderByDescending(value => value.Path.Count)
                .First().Candidate;
            encounter.ConfirmAction(move);
            Assert.IsTrue(encounter.IsPresenting);
            adventure.RequestReturn(); Assert.IsFalse(adventure.IsWithdrawalConfirmationOpen);
            yield return null; Assert.IsFalse(ReturnButton.interactable);
            Assert.AreEqual("行动演出中，请稍后撤离", Field<Text>(adventureHud, "returnReason").text);
            AssertEntryLayout(); PointerClick(ReturnButton);
            Assert.IsFalse(adventure.IsWithdrawalConfirmationOpen);
            AssertReadable(Field<Text>(adventureHud, "returnReason"));
            yield return PlayerReady();
            Assert.IsFalse(adventure.IsWithdrawalConfirmationOpen);
            encounter.RequestEndTurn("player");
            Assert.AreEqual(CombatTurnPhase.Finished, combat.TurnPhase);
            Assert.IsFalse(encounter.CanWithdraw(out string transition)); Assert.AreEqual("回合切换中", transition);
            adventure.RequestReturn(); Assert.IsFalse(adventure.IsWithdrawalConfirmationOpen);
            for (float deadline = Time.realtimeSinceStartup + 20; Time.realtimeSinceStartup < deadline &&
                combat.CurrentActorId != "enemy" && !(encounter.IsPresenting && !Field<bool>(encounter, "acceptsPlayerCommand"));)
                yield return null;
            Assert.IsTrue(combat.CurrentActorId == "enemy" ||
                (encounter.IsPresenting && !Field<bool>(encounter, "acceptsPlayerCommand")));
            adventure.RequestReturn(); yield return null;
            Assert.IsFalse(ReturnButton.interactable); Assert.IsFalse(adventure.IsWithdrawalConfirmationOpen);
            Assert.AreEqual("敌方行动中，请等待你的行动", Field<Text>(adventureHud, "returnReason").text);
            AssertEntryLayout(); PointerClick(ReturnButton);
            Assert.IsFalse(adventure.IsWithdrawalConfirmationOpen);
            AssertReadable(Field<Text>(adventureHud, "returnReason"));
            yield return PlayerReady(); Assert.IsFalse(adventure.IsWithdrawalConfirmationOpen);
            // Existing isolated outcome fixture changes only this temporary enemy snapshot, not production content.
            GuanzhongBasicAttackPlayModeTests.ArmEnemyOutcomeFixture(spawned.Enemy);
            spawned.Player.ReceiveDamage(spawned.Player.CurrentHealth - 1);
            encounter.RequestEndTurn("player");
            bool observedFinalPresentation = false;
            for (float deadline = Time.realtimeSinceStartup + 30; Time.realtimeSinceStartup < deadline &&
                SceneManager.GetActiveScene().name == GameplaySceneNames.Adventure;)
            {
                if (!spawned.Player.IsAlive && encounter.IsPresenting)
                {
                    observedFinalPresentation = true;
                    Assert.IsFalse(encounter.CanWithdraw(out string finalReason));
                    Assert.AreEqual("胜负已定，等待结算", finalReason);
                    adventure.RequestReturn(); Assert.IsFalse(adventure.IsWithdrawalConfirmationOpen);
                }
                yield return null;
            }
            Assert.IsTrue(observedFinalPresentation);
            yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.AreEqual(0, GameBootstrap.RequireRuntime().Bounties.GetState(Bounty).Progress);
            Debug.Log("[Withdrawal] PASS real player presentation, turn transition, enemy action, no cached clicks, established defeat presentation priority; defeat uses existing isolated outcome fixture.");
        }

        [UnityTest]
        public IEnumerator FormalExplorationReturnUsesExistingTargetWithoutSaving()
        {
            yield return EnterFormalAdventure();
            var runtime = GameBootstrap.RequireRuntime();
            string disk = File.ReadAllText(Directory.GetFiles(saveDirectory, "*.json").Single());
            PointerClick(ReturnButton); adventure.RequestReturn();
            Assert.AreEqual(AdventureSceneState.Returning, adventure.CurrentState);
            yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.AreEqual("guanzhong_city", runtime.Navigation.SettlementId);
            Assert.IsNull(runtime.Navigation.AdventureId);
            Assert.AreEqual(disk, File.ReadAllText(Directory.GetFiles(saveDirectory, "*.json").Single()));
        }

        [UnityTest]
        public IEnumerator InitializationFailureKeepsOriginalReasonAndCannotReturnOrSettle()
        {
            yield return EnterFormalAdventure();
            var runtime = GameBootstrap.RequireRuntime();
            string disk = File.ReadAllText(Directory.GetFiles(saveDirectory, "*.json").Single());
            const string missingMap = "withdrawal_missing_map_fixture";
            runtime.EnterAdventure(missingMap, SceneReturnTarget.Settlement("guanzhong_city"));
            LogAssert.Expect(LogType.Error, "[AdventureInstaller] adventure_map_unresolved:" + missingMap);
            SceneManager.LoadScene(GameplaySceneNames.Adventure); yield return null; yield return null;
            var failed = Find<AdventureController>(); var hud = Find<AdventureHudPresenter>();
            Assert.IsFalse(failed.enabled); Assert.AreEqual(AdventureSceneState.Loading, failed.CurrentState);
            failed.RequestReturn(); failed.ConfirmWithdrawal();
            Assert.AreEqual(missingMap, runtime.Navigation.AdventureId);
            Assert.IsFalse(Field<GameObject>(hud, "returnEntry").activeSelf);
            Assert.IsFalse(Find<EncounterCoordinator>().IsRunning); Assert.IsNull(failed.LastFormalEncounterResult);
            Assert.AreEqual(disk, File.ReadAllText(Directory.GetFiles(saveDirectory, "*.json").Single()));
            Debug.Log("[Withdrawal] PASS isolated invalid-map initialization failure retains exact installer error, no navigation, reward or disk write.");
        }

        [UnityTest]
        public IEnumerator FormalVictoryPresentationWinsAndHistoricalRewardsSurviveNextWithdrawal()
        {
            yield return EnterFormalAdventure(); yield return BeginRealEncounter();
            for (int turn = 0; turn < 10; turn++)
            {
                actions.ArtButton.onClick.Invoke();
                yield return GuanzhongCombatTargetSelectionPlayModeTests.PointArt(combat, spawned.Enemy.Id);
                if (commands.Preview?.CanConfirm == true) break;
                commands.CancelCandidate();
                encounter.RequestEndTurn("player"); yield return null; yield return PlayerReady();
            }
            // Isolated last-hit fixture; real commands, presentation and settlement still own the result.
            foreach (var other in spawned.Enemies.Where(enemy => enemy != spawned.Enemy)) other.ReceiveDamage(other.CurrentHealth);
            spawned.Enemy.ReceiveDamage(spawned.Enemy.CurrentHealth - 1);
            encounter.SetCombatResolutionRandomSource(new GuanzhongBasicAttackPlayModeTests.OutcomeFixtureRandom(combat, true));
            actions.ArtButton.onClick.Invoke();
            yield return GuanzhongCombatTargetSelectionPlayModeTests.PointArt(combat, spawned.Enemy.Id);
            Assert.IsTrue(commands.Preview.CanConfirm, commands.Preview.Reason);
            yield return GuanzhongBasicAttackPlayModeTests.ClickPointedAttack();
            Assert.IsFalse(spawned.Enemy.IsAlive); Assert.IsTrue(encounter.IsPresenting);
            Assert.IsFalse(encounter.CanWithdraw(out string reason)); Assert.AreEqual("胜负已定，等待结算", reason);
            adventure.RequestReturn(); Assert.IsFalse(adventure.IsWithdrawalConfirmationOpen);
            yield return SceneReady(GameplaySceneNames.Settlement);
            var runtime = GameBootstrap.RequireRuntime();
            Assert.AreEqual(1, runtime.Bounties.GetState(Bounty).Progress);
            Assert.AreEqual(BountyStatus.ObjectiveCompleted, runtime.Bounties.GetState(Bounty).Status);
            Assert.AreEqual(1, runtime.GetItemQuantity("item_shijia_piece"));
            string prior = runtime.CaptureSaveJson();
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild"));
            yield return SceneReady(GameplaySceneNames.Adventure);
            adventure = Find<AdventureController>(); encounter = Find<EncounterCoordinator>();
            adventureHud = Find<AdventureHudPresenter>(); combatHud = Find<CombatHudPresenter>();
            commands = Find<CombatCommandInput>(); actions = Find<CombatActionBarView>();
            yield return BeginRealEncounter();
            PointerClick(ReturnButton); yield return null; PointerClick(ConfirmButton);
            yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.AreEqual(prior, runtime.CaptureSaveJson());
            Assert.AreEqual(BountyStatus.ObjectiveCompleted, runtime.Bounties.GetState(Bounty).Status);
            Debug.Log("[Withdrawal] PASS established victory priority and preservation of prior unclaimed bounty and drops; last-hit uses isolated combat fixture.");
        }

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

        [UnityTest]
        public IEnumerator VictoryReturnsThroughFormalSettlementOnce() => VerifyFormalReturn(CombatSessionOutcome.Victory);

        [UnityTest]
        public IEnumerator DefeatReturnsThroughFormalSettlementOnce() => VerifyFormalReturn(CombatSessionOutcome.Defeat);

        private static IEnumerator VerifyFormalReturn(CombatSessionOutcome outcome)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>("Assets/Data/ContentCatalog/ContentCatalog.asset");
            var environment = AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>(
                "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset");
            var config = AssetDatabase.LoadAssetAtPath<CharacterCreationPointBuyConfig>(
                "Assets/Resources/Data/CharacterCreation/CharacterCreationPointBuyConfig.asset");
            Assert.IsNotNull(catalog);
            Assert.IsNotNull(environment);
            Assert.IsNotNull(config);
            Assert.IsTrue(catalog.TryGetAdventureMap(FormalEncounterRules.GuanzhongWildAdventureId, out AdventureMapData map));
            Assert.IsTrue(catalog.TryGetEnemy(FormalEncounterRules.ShijiahouEnemyId, out EnemyData enemy));
            CharacterData profile = CharacterCreationManager.CreateProfile(CharacterCreationCatalog.CreateDefaultDraft(), config, GrowthRules);
            var host = new GameObject("FormalNavigationGuardRuntimeTest");
            try
            {
                var runtime = new GameRuntime();
                runtime.BeginNewGame(CharacterRuntimeProfile.FromDefinition("player", profile, GrowthRules),
                    CultivationState.FromDefinition(profile.foundationPurpleMansionState), "guanzhong_hub");
                runtime.EnterWorld("guanzhong_hub");
                runtime.EnterSettlement("guanzhong_city");
                var accepted = runtime.Bounties.Accept(catalog, "bounty_guanzhong_shijiahou", "guanzhong_city");
                Assert.IsTrue(accepted.Succeeded, accepted.FailureReason);
                runtime.EnterAdventure(map.adventureId, SceneReturnTarget.Settlement("guanzhong_city"));
                var controller = host.AddComponent<AdventureController>();
                controller.enabled = false;
                var input = host.AddComponent<AdventureInputController>();
                var hud = host.AddComponent<AdventureHudPresenter>();
                var spawner = host.AddComponent<AdventureUnitSpawner>();
                var encounters = host.AddComponent<EncounterCoordinator>();
                int loads = 0;
                string loadedScene = null;
                controller.Configure(catalog, map, runtime.Player.Capture(), environment,
                    System.Array.Empty<AttackProfileData>(), new AdventureMapLoader(), spawner, input, hud,
                    encounters, new CombatEntryAdapter(new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0), new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) }, UnityEditor.AssetDatabase.LoadAssetAtPath<TianZhang.Content.CombatRuleSetData>("Assets/Data/CombatRuleSet.asset")), runtime, runtime.Bounties, runtime.InventoryGrants,
                    scene => { loads++; loadedScene = scene; });
                controller.SetEncounterRandomSource(new ZeroRandomSource());
                typeof(AdventureController).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                Assert.AreEqual(AdventureSceneState.Exploration, controller.CurrentState);
                typeof(AdventureController).GetProperty("CurrentState").SetValue(controller, AdventureSceneState.Combat);
                var priorNode = controller.Session.CurrentNode;
                var priorNavigation = runtime.Navigation;
                Assert.IsFalse(input.SelectNode("return"));
                Assert.IsFalse(input.SelectNode("return"));
                Assert.AreEqual(0, loads);
                Assert.AreSame(priorNode, controller.Session.CurrentNode);
                Assert.AreSame(priorNavigation, runtime.Navigation);
                yield return null;

                controller.ResolveEncounter(outcome, enemy);
                Assert.AreEqual(1, loads);
                Assert.AreEqual(GameplaySceneNames.Settlement, loadedScene);
                Assert.AreEqual("guanzhong_city", runtime.Navigation.SettlementId);
                Assert.IsNull(runtime.Navigation.AdventureId);
                Assert.AreEqual(AdventureSceneState.Returning, controller.CurrentState);
                Assert.AreEqual(outcome, controller.LastEncounterOutcome);
                Assert.IsNotNull(controller.LastFormalEncounterResult);
                int expectedCount = outcome == CombatSessionOutcome.Victory ? 1 : 0;
                Assert.AreEqual(expectedCount, runtime.Bounties.GetState("bounty_guanzhong_shijiahou").Progress);
                Assert.AreEqual(expectedCount, Quantity(runtime, "item_shijia_piece"));
                Assert.AreEqual(expectedCount, Quantity(runtime, "item_lingshi_low"));
                var result = controller.LastFormalEncounterResult;
                var returnedNavigation = runtime.Navigation;
                LogAssert.Expect(LogType.Error, new Regex(FormalEncounterRules.AlreadyConsumedReason));
                controller.ResolveEncounter(outcome, enemy);
                Assert.AreEqual(FormalEncounterRules.AlreadyConsumedReason, controller.LastFailureReason);
                Assert.AreSame(result, controller.LastFormalEncounterResult);
                Assert.AreSame(returnedNavigation, runtime.Navigation);
                Assert.AreEqual(1, loads);
                Assert.AreEqual(expectedCount, runtime.Bounties.GetState("bounty_guanzhong_shijiahou").Progress);
                Assert.AreEqual(AdventureSceneState.Returning, controller.CurrentState);
                Assert.AreEqual(expectedCount, Quantity(runtime, "item_shijia_piece"));
                Assert.AreEqual(expectedCount, Quantity(runtime, "item_lingshi_low"));
            }
            finally
            {
                Object.Destroy(host);
                Object.Destroy(profile);
            }
            yield return null;
        }

        private static int Quantity(GameRuntime runtime, string itemId)
        {
            foreach (InventoryRecord record in runtime.CaptureSave().inventory)
                if (record.itemId == itemId) return record.quantity;
            return 0;
        }

        private sealed class ZeroRandomSource : IFormalEncounterRandomSource
        {
            public int NextPercent() => 0;
        }
    }
}
#endif
