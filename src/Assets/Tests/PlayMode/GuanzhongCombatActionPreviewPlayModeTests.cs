using System;
using System.Collections;
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
    public sealed class GuanzhongCombatActionPreviewPlayModeTests
    {
        private string saveDirectory;
        private Mouse mouse;
        private Keyboard keyboard;
        private InputSettings.BackgroundBehavior background;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
#endif
        private CombatHudPresenter presenter;
        private CombatTargetView target;
        private CombatTimelineView timeline;
        private Static3DCombatUnitPresentationProvider provider;
        private EncounterCoordinator encounter;
        private CombatSession session;
        private CombatRandom random;
        private AdventureSpawnSet spawned;
        private readonly System.Collections.Generic.List<string> layoutFailures = new System.Collections.Generic.List<string>();

        [UnitySetUp]
        public IEnumerator EnterFormalEncounter()
        {
            background = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            GameBootstrap stale = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return SceneReady(GameplaySceneNames.StartMenu);
            saveDirectory = Path.Combine(Application.temporaryCachePath, "TianZhang-Preview-" + Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saveDirectory));
            CharacterCreationController creation = Find<CharacterCreationController>();
            creation.Open();
            Assert.AreEqual("origin_loose", creation.Draft.OriginId);
            Assert.IsTrue(creation.SelectOrigin("origin_minor_clan"));
            Assert.That(new[] { creation.Draft.Innate.RootBone, creation.Draft.Innate.Soul, creation.Draft.Innate.DivineSense,
                creation.Draft.Innate.Aptitude, creation.Draft.Innate.Fortune }, Is.EqualTo(new[] { 8, 8, 8, 8, 8 }));
            creation.Submit("formal-preview", "行动预览验证");
            yield return SceneReady(GameplaySceneNames.World);
            WorldMapController world = Find<WorldMapController>();
            Assert.IsTrue(world.SelectNode("guanzhong_hub"));
            world.EnterSelectedLocation();
            yield return SceneReady(GameplaySceneNames.Settlement);
            yield return EnterAdventure();
            mouse = InputSystem.AddDevice<Mouse>();
            keyboard = InputSystem.AddDevice<Keyboard>();
        }

        private IEnumerator EnterAdventure()
        {
            SettlementController settlement = Find<SettlementController>();
            BountyBoardView board = Find<BountyBoardView>();
            board.Show(Get<ContentCatalogData>(settlement, "contentCatalog"), "guanzhong_city", GameBootstrap.RequireRuntime().Bounties);
            board.SubmitAccept("bounty_guanzhong_shijiahou");
            Assert.IsTrue(settlement.EnterAdventure("guanzhong_wild"));
            yield return SceneReady(GameplaySceneNames.Adventure);
            AdventureController adventure = Find<AdventureController>();
            for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            Assert.AreEqual(AdventureSceneState.Exploration, adventure.CurrentState);
            presenter = Find<CombatHudPresenter>();
            target = Find<CombatTargetView>();
            timeline = Find<CombatTimelineView>();
            provider = Find<Static3DCombatUnitPresentationProvider>();
            Assert.IsNull(presenter.InspectedTargetId);
            adventure.SetEncounterRandomSource(new EncounterRandom());
            encounter = Find<EncounterCoordinator>();
            random = new CombatRandom();
            encounter.SetCombatResolutionRandomSource(random);
            Assert.IsTrue(Find<AdventureInputController>().SelectNode("shijiahou_encounter"));
            session = Get<CombatSession>(encounter, "session");
            spawned = Get<AdventureSpawnSet>(encounter, "spawned");
            for (int frame = 0; frame < 600 && !presenter.CanInspect; frame++) yield return null;
            Assert.IsTrue(presenter.CanInspect);
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
            GameBootstrap bootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return null;
            // Keep task validation save evidence.
        }

        [UnityTest]
        public IEnumerator HudShowsAuthoritativeCostsAndInterleavedActionsAtBothResolutions()
        {
            foreach (int width in new[] { 1920, 1366 })
            {
                if (width == 1366) { yield return Cleanup(); yield return EnterFormalEncounter(); }
                SetGameSize(width, width == 1920 ? 1080 : 768);
                for (int frame = 0; frame < 8; frame++) yield return null;
                Canvas.ForceUpdateCanvases();
                Assert.That(Screen.width, Is.EqualTo(width));
                var bar = Find<CombatActionBarView>();
                var input = Find<CombatCommandInput>();
                var view = Find<CombatActionPreviewView>();
                var move = Find<CombatMovementView>();
                var service = new CombatCommandService();
                long turn = session.TurnSequence;
                int tick = session.TurnScheduler.CurrentTick;
                string initial = State();
                Assert.That(presenter.Snapshot.Movement.RemainingPoints, Is.EqualTo(spawned.Player.ActionPointsRemainingUnits));
                Assert.That(Get<Text>(bar, "movementLabel").text, Does.Contain("AP 100/100"));
                Assert.That(bar.BasicAttackButton.interactable && bar.GuardButton.interactable && bar.ArtButton.interactable, Is.True);
                Assert.That(bar.WaitButton.interactable && presenter.Snapshot.CanEndTurn, Is.True);
                bar.ArtButton.onClick.Invoke();
                CombatCommandPreview authority = service.Preview(session, new CombatCommand(CombatCommandKind.Art,
                    "player", "enemy", input.Candidate.ProfileId));
                Assert.That(input.Preview.ActionPointCostUnits, Is.EqualTo(authority.ActionPointCostUnits));
                Assert.That(input.Preview.SpiritCost, Is.EqualTo(authority.SpiritCost));
                Assert.That(input.Preview.CtPenalty, Is.EqualTo(authority.CtPenalty));
                Assert.That(input.Preview.ReuseCooldownTicks, Is.EqualTo(authority.ReuseCooldownTicks));
                Assert.That(Get<Text>(view, "summary").text, Does.Contain(
                    CombatActionPreviewView.FormatActionPoints(authority.ActionPointCostUnits) + " AP / " + authority.SpiritCost + " MP"));
                Assert.That(Get<Text>(timeline, "predictionText").text, Does.Contain("完成并结束"));
                Assert.That(input.Preview.Threshold, Is.EqualTo(110));
                yield return CaptureHud("initial-" + width);
                view.CancelButton.onClick.Invoke();
                bar.MoveButton.onClick.Invoke();
                Assert.That(move.IsSelecting, Is.True);
                bar.CancelMoveButton.onClick.Invoke();
                Assert.That(State(), Is.EqualTo(initial), "All preview/selection/cancel operations must be read-only.");
                CombatActionCandidate oldMove = presenter.Snapshot.Movement.Options.First().Candidate;

                if (width == 1366) yield return MoveThroughHud();
                bar.SecondArtButton.onClick.Invoke();
                Assert.That(input.Preview.ActionPointCostUnits, Is.EqualTo(25000));
                Assert.That(input.Preview.SpiritCost, Is.Zero);
                int ap = spawned.Player.ActionPointsRemainingUnits;
                var oldAction = input.Candidate;
                input.ConfirmCandidate();
                Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.EqualTo(ap - 25000));
                Assert.That(presenter.Snapshot.IsPresenting, Is.True);
                Assert.That(bar.ArtButton.interactable || bar.GuardButton.interactable || bar.MoveButton.interactable, Is.False);
                string busy = State();
                input.ConfirmCandidate(); encounter.ConfirmAction(oldAction); encounter.ConfirmAction(oldMove);
                Assert.That(State(), Is.EqualTo(busy));
                yield return Ready();
                Assert.That(bar.SecondArtButton.interactable, Is.False);
                Assert.That(presenter.Snapshot.ArtActions[1].Targets[0].Quote.RemainingCooldownTicks, Is.EqualTo(5));
                if (width == 1920) yield return MoveThroughHud();
                Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.EqualTo(25000));
                Assert.That(Get<Text>(bar, "movementLabel").text, Does.Contain("AP 25/100"));
                Assert.That(presenter.Snapshot.PendingCtPenalty, Is.EqualTo(5));
                Assert.That(bar.BasicAttackButton.interactable || bar.ArtButton.interactable || bar.GuardButton.interactable, Is.False);
                Assert.That(bar.WaitButton.interactable, Is.True, "End turn legality must not disable or enable other actions.");
                Assert.That(spawned.Player.GetCooldown("spell_julingshu"), Is.EqualTo(5));
                Assert.That(session.TurnSequence, Is.EqualTo(turn));
                Assert.That(session.TurnScheduler.CurrentTick, Is.EqualTo(tick));
                string settledActions = State();
                encounter.ConfirmAction(oldMove); encounter.ConfirmAction(oldAction);
                Assert.That(State(), Is.EqualTo(settledActions), "Old action and movement confirmations cannot be repriced/submitted.");
                bar.WaitButton.onClick.Invoke();
                Assert.That(input.Preview.ActionPointCostUnits, Is.Zero);
                Assert.That(input.Preview.Threshold, Is.EqualTo(105));
                yield return CaptureHud("after-actions-" + width);
                view.CancelButton.onClick.Invoke();
                Assert.That(State(), Is.EqualTo(settledActions));
                TestContext.WriteLine($"AP HUD {Screen.width}x{Screen.height}: turn={turn}, tick={tick}, AP=25000, CT penalty=5, cooldown=5; busy/cancel/stale/repeated inputs unchanged.");
            }
            Assert.That(layoutFailures, Is.Empty, string.Join("\n", layoutFailures));
        }

        private IEnumerator MoveThroughHud()
        {
            var bar = Find<CombatActionBarView>();
            var move = Find<CombatMovementView>();
            var option = presenter.Snapshot.Movement.Options.Single(value => value.Destination.Q == 0 && value.Destination.R == 1);
            CombatCommandPreview quote = new CombatCommandService().Preview(session,
                new CombatCommand(CombatCommandKind.Move, "player", destination: new HexCoord(0, 1)));
            Assert.That(option.Cost, Is.EqualTo(quote.ActionPointCostUnits));
            Assert.That(option.Candidate.MovementPath.Select(p => new HexCoord(p.Q, p.R)), Is.EqualTo(quote.MovementPath));
            string before = State();
            int ap = spawned.Player.ActionPointsRemainingUnits;
            bar.MoveButton.onClick.Invoke();
            Vector3 world = Get<System.Collections.Generic.Dictionary<Vector2Int, Vector3>>(move, "centers")[new Vector2Int(0, 1)];
            Vector2 point = Get<Camera>(move, "battlefieldCamera").WorldToScreenPoint(world);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 1 }); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
            Assert.That(move.Selected, Is.SameAs(option));
            Assert.That(Get<Text>(bar, "movementLabel").text, Does.Contain("消耗 50 AP"));
            Assert.That(State(), Is.EqualTo(before));
            bar.ConfirmMoveButton.onClick.Invoke(); bar.ConfirmMoveButton.onClick.Invoke();
            Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.EqualTo(ap - quote.ActionPointCostUnits));
            Assert.That(spawned.Player.Position, Is.EqualTo(new HexCoord(0, 1)));
            yield return Ready();
        }

        private IEnumerator CaptureHud(string label)
        {
            Canvas.ForceUpdateCanvases();
            var bar = Find<CombatActionBarView>();
            var preview = Find<CombatActionPreviewView>();
            var texts = new[] { Get<Text>(bar, "movementLabel"), Get<Text>(bar, "actionStatusLabel"),
                bar.BasicAttackButton.GetComponentInChildren<Text>(), bar.GuardButton.GetComponentInChildren<Text>(),
                bar.WaitButton.GetComponentInChildren<Text>(), bar.ArtButton.GetComponentInChildren<Text>(),
                bar.SecondArtButton.GetComponentInChildren<Text>(), Get<Text>(timeline, "predictionText") };
            foreach (Text text in texts.Where(text => text.gameObject.activeInHierarchy))
                if (text.preferredHeight > text.rectTransform.rect.height + .1f)
                    layoutFailures.Add(Screen.width + ": " + TextGeometry(text));
            string directory = Path.Combine(Application.dataPath, "../Logs/ap-hud");
            Directory.CreateDirectory(directory);
            string stem = Path.Combine(directory, label);
            File.WriteAllText(stem + ".txt", "formal scene=" + SceneManager.GetActiveScene().name +
                "; resolution=" + Screen.width + "x" + Screen.height + "; state=" + State() + "\n" +
                string.Join("\n", texts.Select(TextGeometry)) + "\npreview=" + Get<Text>(preview, "summary").text);
            ScreenCapture.CaptureScreenshot(stem + ".png");
            for (int frame = 0; frame < 4; frame++) yield return null;
        }

        private static string TextGeometry(Text text)
        {
            string hierarchy = text.name;
            for (Transform parent = text.transform.parent; parent != null; parent = parent.parent) hierarchy = parent.name + "/" + hierarchy;
            var corners = new Vector3[4]; text.rectTransform.GetWorldCorners(corners);
            var settings = text.GetGenerationSettings(Vector2.zero);
            float compactWidth = text.cachedTextGeneratorForLayout.GetPreferredWidth("防御50AP", settings) / text.pixelsPerUnit;
            return hierarchy + "=" + text.text + "; rect=" + text.rectTransform.rect + "; preferredHeight=" + text.preferredHeight +
                "; preferredWidth=" + text.preferredWidth + "; font=" + text.font.name + "; fontSize=" + text.fontSize +
                "; compactGuardWidth=" + compactWidth + "; canvas=" + text.canvas.name + "/" + text.canvas.renderMode +
                "; scale=" + text.canvas.scaleFactor + "; screenCorners=" + string.Join("/", corners.Select(corner => corner.ToString("F2")));
        }

        private static void SetGameSize(int width, int height)
        {
#if UNITY_EDITOR
            var assembly = typeof(UnityEditor.Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var sizes = sizesType.BaseType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { 0 });
            var size = Activator.CreateInstance(assembly.GetType("UnityEditor.GameViewSize"),
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                new object[] { Enum.ToObject(assembly.GetType("UnityEditor.GameViewSizeType"), 1), width, height, "AP HUD validation" }, null);
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
            int count = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null);
            var game = UnityEditor.EditorWindow.GetWindow(assembly.GetType("UnityEditor.GameView"));
            game.GetType().GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, count - 1);
#else
            Screen.SetResolution(width, height, false);
#endif
        }

        [UnityTest]
        public IEnumerator CandidateCancelAndTargetChangesNeverMutateAuthority()
        {
            CombatCommandInput input = Find<CombatCommandInput>();
            CombatActionBarView bar = Find<CombatActionBarView>();
            CombatActionPreviewView view = Find<CombatActionPreviewView>();
            string before = State();
            foreach (Button button in new[] { bar.BasicAttackButton, bar.ArtButton, bar.SecondArtButton, bar.GuardButton, bar.WaitButton })
            {
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    button.onClick.Invoke();
                    Assert.IsNotNull(input.Candidate);
                    Assert.IsTrue(view.IsVisible);
                    Assert.IsTrue(input.Preview.CanConfirm);
                    Assert.IsNotNull(timeline.Prediction);
                    presenter.InspectTarget(spawned.Enemy.Id);
                    Assert.AreEqual("player", input.Candidate.ActorId);
                    view.CancelButton.onClick.Invoke();
                    Assert.IsNull(input.Candidate);
                    Assert.IsNull(timeline.Prediction);
                    Assert.AreEqual(before, State());
                }
            }
            bar.SecondArtButton.onClick.Invoke();
            Assert.AreEqual("player", input.Candidate.TargetId);
            Assert.AreEqual(0, input.Preview.SpiritCost);
            bar.SecondArtTargetButton.onClick.Invoke();
            Assert.AreEqual("enemy", input.Candidate.TargetId);
            Assert.AreEqual(20, input.Preview.SpiritCost);
            Assert.AreEqual(before, State());
            view.CancelButton.onClick.Invoke();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ConfirmOnceAndRejectStaleOrUnaffordableCandidates()
        {
            CombatCommandInput input = Find<CombatCommandInput>();
            CombatActionBarView bar = Find<CombatActionBarView>();
            CombatActionPreviewView view = Find<CombatActionPreviewView>();
            int mp = spawned.Player.CurrentSpirit, rolls = random.Count;
            bar.ArtButton.onClick.Invoke();
            CombatActionCandidate old = input.Candidate;
            var hud = Get<CombatHudSnapshot>(input, "snapshot");
            Assert.IsNotNull(input.Preview, "AP-start: actor=" + session.CurrentActorId +
                "; AP=" + spawned.Player.ActionPointsRemainingUnits + "; accepts=" + hud.AcceptsCommands +
                "; canEnd=" + hud.CanEndTurn + "; artEnabled=" + bar.ArtButton.interactable);
            Assert.AreEqual(12, input.Preview.SpiritCost);
            Assert.AreEqual(110, input.Preview.Threshold);
            view.ConfirmButton.onClick.Invoke();
            view.ConfirmButton.onClick.Invoke();
            Assert.AreEqual(mp - 12, spawned.Player.CurrentSpirit);
            Assert.AreEqual(rolls + 4, random.Count);
            Assert.AreEqual(CombatTurnPhase.Active, session.TurnPhase);
            Assert.IsNull(input.Candidate);
            Assert.IsNull(timeline.Prediction);
            string after = State();
            encounter.ConfirmAction(old);
            Assert.AreEqual(after, State());
            yield return Ready();
            bar.WaitButton.onClick.Invoke();
            Assert.AreEqual(CombatActionKind.EndTurn, input.Candidate.Kind);
            CombatActionPreviewSnapshot end = input.Preview;
            view.ConfirmButton.onClick.Invoke();
            Assert.AreEqual(CombatTurnPhase.Finished, session.TurnPhase);
            var settled = session.TurnScheduler.GetSnapshot(session.Combatants.All).Units.Single(u => u.Id == "player");
            Assert.AreEqual(end.SettledCt, settled.ChargeTime);
            Assert.AreEqual(end.Threshold, settled.NextActionThreshold);
            yield return Ready();
            bar.SecondArtButton.onClick.Invoke();
            Assert.AreEqual("player", input.Candidate.TargetId);
            mp = spawned.Player.CurrentSpirit; rolls = random.Count;
            view.ConfirmButton.onClick.Invoke();
            view.ConfirmButton.onClick.Invoke();
            Assert.Greater(spawned.Player.CurrentSpirit, mp);
            Assert.AreEqual(rolls, random.Count);
            yield return Ready();
            encounter.RequestEndTurn("player");
            yield return Ready();
            bar.SecondArtTargetButton.onClick.Invoke();
            bar.SecondArtButton.onClick.Invoke();
            CombatActionCandidate costly = input.Candidate;
            Assert.IsNotNull(costly);
            spawned.Player.TryConsumeSpirit(spawned.Player.CurrentSpirit);
            after = State();
            Assert.IsFalse(encounter.PreviewAction(costly).CanConfirm);
            view.ConfirmButton.onClick.Invoke();
            Assert.AreEqual(after, State());
            Assert.IsNull(input.Candidate);
        }

        [UnityTest]
        public IEnumerator MoveAndLifecycleClearOnlyUncommittedCandidates()
        {
            CombatCommandInput input = Find<CombatCommandInput>();
            CombatActionBarView bar = Find<CombatActionBarView>();
            CombatMovementView move = Find<CombatMovementView>();
            string before = State();
            bar.ArtButton.onClick.Invoke();
            bar.MoveButton.onClick.Invoke();
            Assert.IsNull(input.Candidate);
            Assert.IsTrue(move.IsSelecting);
            bar.SecondArtButton.onClick.Invoke();
            Assert.IsFalse(move.IsSelecting);
            Assert.IsNotNull(input.Candidate);
            input.enabled = false;
            bar.ArtButton.onClick.Invoke();
            bar.MoveButton.onClick.Invoke();
            input.ConfirmCandidate();
            Assert.IsFalse(move.IsSelecting);
            Assert.IsNull(input.Candidate);
            Assert.IsNull(timeline.Prediction);
            input.enabled = true;
            input.SetContext(presenter.Snapshot);
            bar.ArtButton.onClick.Invoke();
            presenter.enabled = false;
            Assert.IsNull(input.Candidate);
            presenter.enabled = true;
            Assert.AreEqual(before, State());
            yield return null;
        }

        [UnityTest]
        public IEnumerator HideEndTurnAndDeadTargetUseRealValidation()
        {
            CombatCommandInput input = Find<CombatCommandInput>();
            CombatActionBarView bar = Find<CombatActionBarView>();
            CombatHudSnapshot prior = presenter.Snapshot;
            bar.ArtButton.onClick.Invoke();
            string before = State();
            presenter.Hide();
            Assert.IsNull(input.Candidate);
            Assert.IsNull(timeline.Prediction);
            Assert.AreEqual(before, State());
            presenter.Present(prior);
            bar.WaitButton.onClick.Invoke();
            Assert.AreEqual(CombatActionKind.EndTurn, input.Candidate.Kind);
            var expected = input.Preview;
            Assert.IsNotNull(timeline.Prediction);
            StringAssert.Contains("顺位未定", Get<Text>(timeline, "predictionText").text);
            input.ConfirmCandidate();
            var settled = session.TurnScheduler.GetSnapshot(session.Combatants.All).Units.Single(u => u.Id == "player");
            Assert.AreEqual(expected.SettledCt, settled.ChargeTime);
            Assert.AreEqual(expected.Threshold, settled.NextActionThreshold);
            Assert.AreEqual(CombatTurnPhase.Finished, session.TurnPhase);
            yield return Ready();
            bar.ArtButton.onClick.Invoke();
            var attack = input.Candidate;
            Assert.IsNotNull(attack);
            spawned.Enemy.ReceiveDamage(spawned.Enemy.CurrentHealth);
            before = State();
            Assert.IsFalse(encounter.PreviewAction(attack).CanConfirm);
            input.ConfirmCandidate();
            Assert.IsNull(input.Candidate);
            Assert.IsNull(timeline.Prediction);
            Assert.AreEqual(before, State());
        }

        private IEnumerator Ready()
        {
            for (float deadline = Time.realtimeSinceStartup + 15f; Time.realtimeSinceStartup < deadline &&
                (session.CurrentActorId != "player" || session.TurnPhase == CombatTurnPhase.Finished || encounter.IsPresenting);) yield return null;
            yield return null;
            Assert.AreEqual("player", session.CurrentActorId);
            Assert.IsFalse(encounter.IsPresenting);
        }

        private string State()
        {
            var turns = session.TurnScheduler.GetSnapshot(session.Combatants.All);
            return session.CurrentActorId + ":" + session.TurnPhase + ":" + session.PendingCtPenalty + ":" +
                turns.CurrentTick + ":" + random.Count + ":" +
                string.Join(";", turns.Units.Select(u => u.Id + ":" + u.ChargeTime + ":" + u.NextActionThreshold + ":" + u.IsReady)) +
                string.Join(",", turns.ReadyQueueIds) + string.Join(",", turns.ChargingUnitIds) +
                string.Join(";", session.Combatants.All.Select(u => u.Id + ":" + u.CurrentHealth + ":" + u.CurrentSpirit + ":" +
                    u.ActionPointsRemainingUnits + ":" + u.Position + ":" + u.Facing + ":" + u.IsGuarding + ":" +
                    string.Join(",", u.EquippedArtProfileIds.Select(u.GetCooldown))));
        }

        private static IEnumerator SceneReady(string scene)
        {
            for (float deadline = Time.realtimeSinceStartup + 15f; Time.realtimeSinceStartup < deadline && SceneManager.GetActiveScene().name != scene;) yield return null;
            Assert.AreEqual(scene, SceneManager.GetActiveScene().name);
            yield return null;
        }
        private static T Find<T>() where T : Component => SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).Single();
        private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
        private sealed class EncounterRandom : IFormalEncounterRandomSource { public int NextPercent() => 0; }
        private sealed class CombatRandom : ICombatResolutionRandomSource
        {
            public int Count { get; private set; }
            public float NextPercent() => Count++ % 4 == 0 ? 0f : 99f;
        }
#if UNITY_EDITOR
        [UnityEditor.MenuItem("天章/验证/记录行动预览现场 %&p")]
        public static void CaptureVisiblePreview()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Requires formal Play Mode.");
            var input = Find<CombatCommandInput>();
            var hud = Find<CombatHudPresenter>();
            var preview = Find<CombatActionPreviewView>();
            var summary = Get<Text>(preview, "summary");
            var coordinator = Find<EncounterCoordinator>();
            if (Get<ICombatResolutionRandomSource>(coordinator, "combatRandomSource").GetType() != typeof(SystemCombatResolutionRandomSource))
                throw new InvalidOperationException("Requires ordinary random source.");
            CombatSession live = Get<CombatSession>(coordinator, "session");
            var value = input.Preview;
            var evidence = new VisibleEvidence
            {
                width = Screen.width, height = Screen.height, tick = hud.Snapshot.Timeline.CurrentTick,
                actor = live.CurrentActorId, phase = live.TurnPhase.ToString(), viewed = hud.InspectedTargetId,
                candidate = value == null ? null : value.Candidate.Kind + ":" + value.Candidate.ProfileId + ":" + value.Candidate.TargetId,
                cost = value?.SpiritCost ?? -1, ct = value?.SettledCt ?? -1, threshold = value?.Threshold ?? -1,
                ticks = value?.TicksUntilReady ?? -1, summary = summary.text,
                textSize = summary.rectTransform.rect.size, preferredHeight = summary.preferredHeight,
                resources = string.Join(";", live.Combatants.All.Select(u => u.Id + ":HP" + u.CurrentHealth + ":MP" + u.CurrentSpirit + ":move" + u.ActionPointsRemainingUnits)),
                turns = string.Join(";", live.TurnScheduler.GetSnapshot(live.Combatants.All).Units.Select(u => u.Id + ":" + u.ChargeTime + "/" + u.NextActionThreshold)),
                previewCorners = new Vector3[4], confirmCorners = new Vector3[4], cancelCorners = new Vector3[4],
                canvasScale = summary.canvas.scaleFactor,
            };
            Get<GameObject>(preview, "root").GetComponent<RectTransform>().GetWorldCorners(evidence.previewCorners);
            preview.ConfirmButton.GetComponent<RectTransform>().GetWorldCorners(evidence.confirmCorners);
            preview.CancelButton.GetComponent<RectTransform>().GetWorldCorners(evidence.cancelCorners);
            string directory = Path.Combine(Application.dataPath, "../Logs/preview");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "visible-preview-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(evidence, true));
            Debug.Log("Read-only preview evidence: " + path);
        }
        [Serializable]
        private sealed class VisibleEvidence
        {
            public int width, height, tick, cost;
            public long ticks;
            public float ct, threshold, preferredHeight, canvasScale;
            public string actor, phase, viewed, candidate, summary, resources, turns;
            public Vector2 textSize;
            public Vector3[] previewCorners, confirmCorners, cancelCorners;
        }
#endif
    }
}
