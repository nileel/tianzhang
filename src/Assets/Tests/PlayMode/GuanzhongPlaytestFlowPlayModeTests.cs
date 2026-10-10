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
    // Formal scene interaction with explicit qualified-player and enemy-attack fixtures; not production playability evidence.
    public sealed class GuanzhongPlaytestFlowPlayModeTests
    {
        private Mouse mouse;
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
        private string directory;
        private CombatSession session;
        private CombatHudPresenter hud;
        private EncounterCoordinator encounter;
        private CombatActionBarView bar;
        private CombatActionPreviewView preview;
        private int captureIndex;
        private const string Bounty = "bounty_guanzhong_shijiahou";

        [UnityTest] public IEnumerator Victory1920() => Flow(1920, true);
        [UnityTest] public IEnumerator Victory1366() => Flow(1366, true);
        [UnityTest] public IEnumerator Defeat1920() => Flow(1920, false);
        [UnityTest] public IEnumerator Defeat1366() => Flow(1366, false);

        private IEnumerator Flow(int width, bool victory)
        {
            session = null;
            captureIndex = 0;
            directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/flow/" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + width + "-" + (victory ? "victory" : "defeat")));
            Directory.CreateDirectory(directory);
            background = InputSystem.settings.backgroundBehavior;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>();
            SetGameSize(width, width == 1920 ? 1080 : 768);
            var stale = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return SceneReady(GameplaySceneNames.StartMenu);
            Assert.AreEqual(width, Screen.width);
            string slot = "flow-" + width + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string saveDirectory = Path.Combine(directory, "saves");
            typeof(GameBootstrap).GetField("slotStore", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(GameBootstrap.RequireInstance(), new GameSaveSlotStore(saveDirectory));
            Log("fixtureSaveDirectory=" + saveDirectory + " slot=" + slot);
            yield return Click(Button("NewPlayerButton"));
            yield return Click(Find<CharacterCreationView>().GetOriginButton("origin_minor_clan"));
            var draft = Find<CharacterCreationController>().Draft;
            CollectionAssert.AreEqual(new[] { 8, 8, 8, 8, 8 }, new[] { draft.Innate.RootBone, draft.Innate.Soul,
                draft.Innate.DivineSense, draft.Innate.Aptitude, draft.Innate.Fortune });
            Node("SlotIdInput").GetComponent<InputField>().text = slot;
            Node("CharacterNameInput").GetComponent<InputField>().text = "关中流程" + width;
            yield return Capture("creation");
            yield return Click(Button("CreatePlayerButton"));
            yield return SceneReady(GameplaySceneNames.World);
            GuanzhongBasicAttackPlayModeTests.PrepareEquippedPlayerFixture();
            yield return Click(Button("WorldNodeButton_1"));
            yield return Capture("world-selected");
            yield return Click(Button("EnterLocationButton"));
            yield return SceneReady(GameplaySceneNames.Settlement);
            yield return Click(Button("SettlementFeature_bounty_board"));
            yield return Click(Button("AcceptBountyButton"));
            Assert.AreEqual("Accepted", GameBootstrap.RequireRuntime().Bounties.GetState(Bounty).Status.ToString());
            string rewardBaseline = RewardState();
            File.WriteAllText(Path.Combine(directory, "before-encounter.json"), GameBootstrap.RequireRuntime().CaptureSaveJson());
            Log("rewardBaseline=" + rewardBaseline);
            yield return Capture("bounty-accepted");
            yield return Click(Button("CloseBountyButton"));
            yield return Click(Button("SettlementAdventure_guanzhong_wild"));
            yield return SceneReady(GameplaySceneNames.Adventure);
            yield return Click(Button("AdventureNode_shijiahou_encounter"));
            encounter = Find<EncounterCoordinator>();
            hud = Find<CombatHudPresenter>();
            bar = Find<CombatActionBarView>();
            preview = Find<CombatActionPreviewView>();
            session = Get<CombatSession>(encounter, "session");
            Assert.IsNotNull(session);
            yield return Ready();
            Assert.IsTrue(session.Combatants.TryGet("player", out var player));
            Assert.IsTrue(session.Combatants.TryGet("enemy", out var enemy));
            GuanzhongBasicAttackPlayModeTests.ArmEnemyOutcomeFixture(enemy);
            // Keep the path/log/save scenario alive; a 99 roll alone still hits a zero-dodge actor.
            if (victory) player.DodgeRate = 100f;
            encounter.SetCombatResolutionRandomSource(new GuanzhongBasicAttackPlayModeTests.OutcomeFixtureRandom(session, victory));
            Assert.AreEqual(GameBootstrap.RequireRuntime().Player.Resources.MaximumHealth, player.MaximumHealth);
            Assert.AreEqual(657, enemy.MaximumHealth);
            Assert.AreEqual(78, enemy.PhysicalAttack);
            CollectionAssert.AreEquivalent(new[] { "spell_guanshiji", "spell_julingshu" }, player.EquippedArtProfileIds);
            Assert.That(Find<CombatBountyTrackerView>().Summary, Does.Contain("0/1"));
            Assert.IsFalse(Button("AdventureNode_return").interactable);
            yield return Capture("battle-entry");
            string state = State();
            var provider = Find<Static3DCombatUnitPresentationProvider>();
            Assert.IsTrue(provider.TryGetVisibleBounds("enemy", out var bounds));
            yield return Pointer(Camera.main.WorldToScreenPoint(bounds.center));
            Assert.AreEqual("enemy", hud.InspectedTargetId);
            var timeline = Find<CombatTimelineView>();
            int enemyIndex = hud.Snapshot.Timeline.Entries.ToList().FindIndex(e => e.Id == "enemy");
            yield return Click(Get<Button[]>(timeline, "entryButtons")[enemyIndex]);
            Assert.AreEqual("enemy", timeline.InspectedTargetId);
            Assert.AreEqual(state, State());
            yield return Capture("target-linked");
            yield return Click(Find<CombatTargetView>().CloseButton);

            if (victory)
            {
                foreach (var action in new[] { bar.ArtButton, bar.SecondArtButton })
                {
                    state = State();
                    yield return Click(action);
                    Assert.IsTrue(preview.IsVisible);
                    Assert.IsNotNull(timeline.Prediction);
                    Assert.That(Get<Text>(timeline, "predictionText").text, Does.Contain("顺位未定"));
                    yield return Capture("preview-" + action.name);
                    yield return Click(preview.CancelButton);
                    Assert.AreEqual(state, State(), "Unconfirmed candidate consumed authority state");
                }
                int mp = player.CurrentSpirit;
                yield return Action(bar.ArtButton);
                Assert.AreEqual(mp - 12, player.CurrentSpirit);
                yield return Ready();
                if (hud.Snapshot.CanEndTurn) yield return Action(bar.WaitButton);
                yield return Ready();
                mp = player.CurrentSpirit;
                yield return Action(bar.SecondArtButton);
                Assert.AreEqual(Math.Min(player.MaximumSpirit, mp + 5), player.CurrentSpirit);
                yield return Ready();
                if (hud.Snapshot.CanEndTurn) yield return Action(bar.WaitButton);
                yield return Ready();
                yield return MoveTo(1, 1);
                yield return FinishActionAndTurn();
                yield return MoveTo(2, 0);
                yield return FinishActionAndTurn();
                // The axial two-step route (2,0)->(2,1)->(2,2) is blocked by a static tile.
                // Observe the actual three-step detour in two legal turns; never inject a path.
                Assert.IsTrue(Node("GuanzhongObstacle_2_1").activeInHierarchy);
                Assert.IsFalse(hud.Snapshot.Movement.Options.Any(o => o.Destination.Q == 2 && o.Destination.R == 1));
                var detour = new List<CombatUnitPresentationHex>();
                yield return MoveTo(1, 2, detour);
                yield return FinishActionAndTurn();
                yield return MoveTo(2, 2, detour);
                Assert.AreEqual(3, detour.Count, "Static detour must exceed direct axial distance two");
                Assert.IsFalse(detour.Any(p => p.Q == 2 && p.R == 1));
                Log("STATIC DETOUR origin=2,0 blocked=2,1 destination=2,2 directDistance=2 actual=" +
                    string.Join(";", detour.Select(p => p.Q + "," + p.R)));
                yield return Capture("static-obstacle-detour-complete");
                yield return FinishActionAndTurn();
                // Return in budgeted turns: the enemy may have moved while the detour consumed AP.
                yield return MoveTo(1, 1);
                yield return FinishActionAndTurn();
                // Return through legal movement to the enemy's actual melee neighborhood.
                var approach = hud.Snapshot.Movement.Options.Where(o =>
                    HexDistance(o.Destination.Q, o.Destination.R, enemy.Position.Q, enemy.Position.R) == 1)
                    .OrderBy(o => o.Cost).FirstOrDefault();
                Assert.IsNotNull(approach, "No legal return to melee range after obstacle proof");
                yield return MoveTo(approach.Destination.Q, approach.Destination.R);
            }
            int commands = 0;
            bool longLogCaptured = false;
            while (SceneManager.GetActiveScene().name == GameplaySceneNames.Adventure && commands < 160)
            {
                yield return Ready();
                if (SceneManager.GetActiveScene().name != GameplaySceneNames.Adventure) break;
                if (!longLogCaptured && Get<Text>(Find<CombatLogView>(), "logText").text.Split('\n').Length >= 18)
                {
                    yield return CaptureLongLog();
                    longLogCaptured = true;
                }
                bool canAttack = session.ValidateCommand(new CombatCommand(CombatCommandKind.BasicAttack,
                    player.Id, enemy.Id, player.BasicAttackProfileId)).Succeeded;
                Button action = !victory ? bar.WaitButton : bar.ArtButton.interactable ? bar.ArtButton :
                    canAttack ? bar.BasicAttackButton : bar.WaitButton;
                yield return Action(action);
                commands++;
            }
            yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.IsTrue(longLogCaptured, "Natural long-log scrolling evidence missing");
            Assert.IsNull(Object.FindFirstObjectByType<CombatHudPresenter>());
            var runtime = GameBootstrap.RequireRuntime();
            Assert.AreEqual(victory ? 1 : 0, runtime.Bounties.GetState(Bounty).Progress);
            Assert.AreEqual(victory, enemy.CurrentHealth == 0);
            Assert.AreEqual(!victory, player.CurrentHealth == 0);
            if (!victory)
            {
                Assert.AreEqual(rewardBaseline, RewardState(), "Defeat changed inventory or bounty state");
                Log("DEFEAT no rewards/progress=" + RewardState());
            }
            File.WriteAllText(Path.Combine(directory, "after-encounter.json"), runtime.CaptureSaveJson());
            yield return Capture("returned");
            if (victory)
            {
                yield return Click(Button("SettlementFeature_bounty_board"));
                int before = Quantity("item_lingshi_low");
                yield return Click(Button("ClaimBountyButton"));
                Assert.AreEqual("Claimed", runtime.Bounties.GetState(Bounty).Status.ToString());
                Assert.AreEqual(before + 3, Quantity("item_lingshi_low"));
                string after = runtime.CaptureSaveJson();
                yield return Click(Button("ClaimBountyButton"));
                Assert.AreEqual(after, runtime.CaptureSaveJson());
                Log("duplicateReason=" + Find<BountyBoardView>().LastResultReason);
                yield return Capture("duplicate-rejected");
                yield return Click(Button("CloseBountyButton"));
            }
            string saved = runtime.CaptureSaveJson();
            File.WriteAllText(Path.Combine(directory, "before-save.json"), saved);
            yield return Click(Button("SaveAndReturnButton"));
            yield return SceneReady(GameplaySceneNames.StartMenu);
            var savedSlot = Button("SaveSlot_" + slot);
            yield return ScrollToSlot(savedSlot);
            yield return Click(savedSlot);
            yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.AreEqual(saved, GameBootstrap.RequireRuntime().CaptureSaveJson());
            if (!victory) Assert.AreEqual(rewardBaseline, RewardState());
            yield return Capture("loaded");
            Log("FLOW COMPLETE width=" + width + " victory=" + victory + " confirmedCommands=" + commands);
        }

        private IEnumerator MoveTo(int q, int r, List<CombatUnitPresentationHex> evidence = null)
        {
            yield return Ready();
            var move = hud.Snapshot.Movement.Options.SingleOrDefault(o => o.Destination.Q == q && o.Destination.R == r);
            Assert.IsNotNull(move, "Required formal path unavailable: " + q + "," + r + " state=" + State());
            Log("movement origin=" + hud.Snapshot.Movement.Origin + " path=" +
                string.Join(";", move.Path.Select(p => p.Q + "," + p.R)) + " cost=" + move.Cost + " before=" + State());
            foreach (var cell in move.Path)
            {
                Assert.IsFalse((cell.Q == 2 || cell.Q == 3) && (cell.R == 1 || cell.R == 4));
                Assert.IsFalse(session.Combatants.All.Any(u => u.Id != "player" && u.CurrentHealth > 0 &&
                    u.Position.Q == cell.Q && u.Position.R == cell.R));
            }
            yield return Click(bar.MoveButton);
            yield return Pointer(Camera.main.WorldToScreenPoint(Node("GuanzhongHex_" + q + "_" + r).transform.position));
            Assert.AreEqual(move, Find<CombatMovementView>().Selected);
            yield return Capture("movement-" + q + "-" + r);
            yield return Click(bar.ConfirmMoveButton);
            Assert.IsTrue(session.Combatants.TryGet("player", out var player));
            Assert.AreEqual(q, player.Position.Q); Assert.AreEqual(r, player.Position.R);
            evidence?.AddRange(move.Path);
            yield return Ready();
        }

        private IEnumerator FinishActionAndTurn()
        {
            yield return Ready();
            if (!session.HasCommittedBenefit && bar.ArtButton.interactable)
            {
                yield return Action(bar.ArtButton);
                yield return Ready();
            }
            if (session.TurnPhase == CombatTurnPhase.Active)
            {
                yield return Action(bar.WaitButton);
                yield return Ready();
            }
        }

        private IEnumerator CaptureLongLog()
        {
            string state = State();
            yield return Click(Button("ToggleLogButton"));
            yield return null; yield return null;
            Canvas.ForceUpdateCanvases();
            var scroll = Node("CombatLogViewport").GetComponent<ScrollRect>();
            Assert.Greater(scroll.content.rect.height, scroll.viewport.rect.height);
            float bottom = scroll.verticalNormalizedPosition;
            var corners = new Vector3[4]; scroll.viewport.GetWorldCorners(corners);
            Vector2 point = (corners[0] + corners[2]) * .5f;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point, scroll = Vector2.up });
            yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
            yield return null; yield return null;
            Assert.Greater(scroll.verticalNormalizedPosition, bottom + .001f, "Mouse wheel did not scroll natural combat log");
            Log("LONG LOG content=" + scroll.content.rect + " viewport=" + scroll.viewport.rect +
                " position=" + bottom + "->" + scroll.verticalNormalizedPosition);
            yield return Capture("long-log-scrolled");
            yield return Click(Button("CloseLogButton"));
            Assert.AreEqual(state, State(), "Reading log changed combat authority");
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
                if (step == 0) Log("slotBounds bottom=" + row[0].y.ToString("R") + "/" + view[0].y.ToString("R") +
                    " top=" + row[2].y.ToString("R") + "/" + view[2].y.ToString("R"));
                // At 1366px the aligned top edges differ by 0.000036px after canvas scaling.
                if (row[0].y >= view[0].y - .001f && row[2].y <= view[2].y + .001f) yield break;
                Vector2 point = (view[0] + view[2]) * .5f;
                float direction = row[0].y < view[0].y ? -1 : 1;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point, scroll = new Vector2(0, direction) });
                yield return null; yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
                yield return null; yield return null;
            }
            Assert.Fail("Saved slot did not become visible through mouse scrolling");
        }

        private static int HexDistance(int q, int r, int otherQ, int otherR) =>
            (Math.Abs(q - otherQ) + Math.Abs(r - otherR) + Math.Abs(q + r - otherQ - otherR)) / 2;
        private static string RewardState()
        {
            var save = GameBootstrap.RequireRuntime().CaptureSave();
            return string.Join(";", save.inventory.OrderBy(i => i.itemId).Select(JsonUtility.ToJson)) + "|" +
                string.Join(";", save.bounties.OrderBy(b => b.bountyId).Select(JsonUtility.ToJson));
        }

        private IEnumerator Action(Button button)
        {
            Log("action=" + button.name + " before=" + State());
            yield return Click(button);
            Assert.IsTrue(preview.IsVisible);
            yield return Click(preview.ConfirmButton);
        }
        private IEnumerator Ready()
        {
            float deadline = Time.realtimeSinceStartup + 25;
            while (SceneManager.GetActiveScene().name == GameplaySceneNames.Adventure &&
                (hud.Snapshot == null || !hud.Snapshot.AcceptsCommands || hud.Snapshot.IsPresenting || encounter.IsPresenting))
            { Assert.Less(Time.realtimeSinceStartup, deadline, "Formal encounter did not yield input"); yield return null; }
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
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            Assert.IsNotEmpty(hits, button.name);
            Assert.AreEqual(button, hits[0].gameObject.GetComponentInParent<Button>(), button.name);
            Log("click=" + button.name + " point=" + point);
            yield return Pointer(point);
        }
        private IEnumerator Pointer(Vector2 point)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 1 }); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
        }
        private IEnumerator Capture(string label)
        {
            for (int frame = 0; frame < 8; frame++) yield return null;
            Canvas.ForceUpdateCanvases();
            string stem = Path.Combine(directory, (++captureIndex).ToString("D2") + "-" + label);
            string text = string.Join("\n", SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Text>()).Select(t => t.name + "=" + t.text));
            File.WriteAllText(stem + ".txt", "size=" + Screen.width + "x" + Screen.height + "\n" + text + "\nSTATE=" + State());
            ScreenCapture.CaptureScreenshot(stem + ".png");
            for (int frame = 0; frame < 4; frame++) yield return null;
            Log("capture=" + stem);
        }
        private string State()
        {
            if (session == null) return SceneManager.GetActiveScene().name;
            var turns = session.TurnScheduler.GetSnapshot(session.Combatants.All);
            return session.CurrentActorId + ":" + session.TurnPhase + ":" + session.PendingCtPenalty + ":" + turns.CurrentTick + ":" +
                string.Join(";", turns.Units.Select(u => u.Id + ":" + u.ChargeTime + ":" + u.NextActionThreshold + ":" + u.IsReady)) +
                string.Join(";", session.Combatants.All.Select(u => u.Id + ":HP" + u.CurrentHealth + ":MP" + u.CurrentSpirit + ":move" +
                    u.ActionPointsRemainingUnits + ":" + u.Position + ":" + u.Facing + ":" + u.IsGuarding + ":" + string.Join(",", u.EquippedArtProfileIds.Select(u.GetCooldown))));
        }
        private void Log(string message)
        {
            Debug.Log("FLOW " + message);
            File.AppendAllText(Path.Combine(directory, "steps.log"), message + "\n");
        }
        private static int Quantity(string id) => GameBootstrap.RequireRuntime().CaptureSave().inventory.Where(r => r.itemId == id).Sum(r => r.quantity);
        private static GameObject Node(string name) => SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Single(t => t.name == name).gameObject;
        private static Button Button(string name) => Node(name).GetComponent<Button>();
        private static T Find<T>() where T : Component => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).Single();
        private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static IEnumerator SceneReady(string scene)
        {
            float deadline = Time.realtimeSinceStartup + 25;
            while (SceneManager.GetActiveScene().name != scene) { Assert.Less(Time.realtimeSinceStartup, deadline, "Expected " + scene); yield return null; }
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
                new object[] { Enum.ToObject(assembly.GetType("UnityEditor.GameViewSizeType"), 1), width, height, "Formal flow validation" }, null);
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
            if (directory != null) yield return Capture("final-state");
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            // Preserve every production save and evidence file.
        }
    }
}
