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
    public sealed class D11VisibilityPlayModeTests
    {
        private const string Yang = "demo_20261024_space_yang", Yin = "demo_20261024_seal_yin";
        private Mouse mouse;
        private Keyboard keyboard;
        private string saves, output;
        private EncounterCoordinator encounter;
        private CombatSession session;
        private JindanDemoView view;
        private JindanVisibilityView visibilityView;
        private InputSettings.BackgroundBehavior oldBackground;
        private InputSettings.EditorInputBehaviorInPlayMode oldInput;
        private bool configured;
        private readonly List<string> evidence = new();
        private Action<long, long, bool, bool, int> inputObserver;
        private Action<long, bool> skipObserver;
        private CombatantSnapshot Actor(string id) { session.Combatants.TryGet(id, out var actor); return actor; }
        private CombatJindanSeatState Seat(string effect) => session.Jindan.Seats.Single(seat => seat.EffectId == effect);

        [UnityTest, Timeout(600000)]
        public IEnumerator PaidDomainsAcceptRealHoldAndTwoWindowEdgesThenExitAndReenterCleanly()
        {
            yield return Setup(); yield return Enter();
            string saved = WorldRecord();
            yield return DeployBothThroughEntryAndButtons();
            var conflict = session.Visibility.Current;
            Assert.AreEqual("equal", conflict.PriorityStep); Assert.AreEqual(Yang, conflict.ObserverId); Assert.AreEqual(Yin, conflict.TargetId);
            Assert.AreEqual(Seat(CombatVisibility.Yang).ActiveVisibility.CreatedTick, Seat(CombatVisibility.Yin).ActiveVisibility.CreatedTick);
            string frozen = Clocks(); int yangMana = Actor(Yang).CurrentSpirit, yinMana = Actor(Yin).CurrentSpirit;
            yield return new WaitForSecondsRealtime(.25f);
            Assert.AreEqual(0, conflict.PulseCount); Assert.AreEqual(yangMana, Actor(Yang).CurrentSpirit); Assert.AreEqual(frozen, Clocks());
            Record("waiting: " + frozen); yield return Capture("01-waiting-before-space");
            var authority = new CombatCommandService();
            var rejected = authority.Execute(session, new CombatCommand(CombatCommandKind.EndTurn, session.CurrentActorId));
            Assert.AreEqual("combat_visibility_conflict_pending", rejected.RejectionReason);
            Assert.AreEqual(0, authority.AdvanceUntilAction(session).TicksElapsed); Assert.AreEqual(frozen, Clocks());
            string settledClocks = null;
            inputObserver = (id, time, pressed, released, keys) =>
            {
                if (id == conflict.EventId && !conflict.IsPending && settledClocks == null)
                { settledClocks = Clocks(); Record("input settled synchronously: " + settledClocks); }
            };
            visibilityView.InputRequested += inputObserver;
            yield return Keys(Key.Space);
            Assert.IsTrue(conflict.Started); Assert.AreEqual(1, conflict.PulseCount); Assert.AreEqual(50, conflict.LeftSpent); Assert.AreEqual(50, conflict.RightSpent);
            yield return WaitFor(() => conflict.PulseCount == 1 && conflict.WindowOpen, "first A window", 2);
            yield return Keys(Key.Space, Key.A);
            Assert.AreEqual(50, conflict.Gauge); Assert.IsTrue(conflict.IsPending); Assert.AreEqual(frozen, Clocks());
            Record("first keyboard edge +50; both pay50; " + Clocks()); yield return Capture("02-conflict-first-window");
            yield return Keys(Key.Space);
            yield return WaitFor(() => conflict.PulseCount == 2 && conflict.WindowOpen, "second D window", 2);
            yield return Keys(Key.Space, Key.D);
            Assert.AreEqual(CombatConflictOutcome.Left, conflict.Outcome); Assert.AreEqual("gauge", conflict.EndReason);
            Assert.AreEqual(100, conflict.Gauge); Assert.AreEqual(100, conflict.LeftSpent); Assert.AreEqual(100, conflict.RightSpent);
            Assert.AreEqual(frozen, settledClocks, "Only conflict MP and gauge may change before combat resumes.");
            visibilityView.InputRequested -= inputObserver; inputObserver = null;
            yield return Keys(); Record("keyboard result: " + string.Join(" | ", conflict.Log)); yield return Capture("03-manual-result");
            yield return LeaveAtTheNextLegalWindow();
            Assert.IsFalse(session.Visibility.IsPaused); Assert.AreEqual(saved, WorldRecord());
            yield return EnterAdventure(); yield return Enter();
            Assert.AreEqual(200, Seat(CombatVisibility.Yang).VisibilityLedger.ReserveRemaining);
            Assert.AreEqual(200, Seat(CombatVisibility.Yin).VisibilityLedger.ReserveRemaining);
            Assert.IsEmpty(session.Visibility.History); Assert.IsFalse(session.Visibility.AutoSkip);
            Record("reentry: both reserves200, no history, world identity unchanged"); yield return Capture("04-clean-reentry");
            yield return LeaveAtTheNextLegalWindow(); Assert.AreEqual(saved, WorldRecord());
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator MouseSkipAndWholeBattleSkipAreNeutralAndWaitingExitDoesNotSettle()
        {
            yield return Setup(); yield return Enter(); string saved = WorldRecord();
            yield return DeployBothThroughEntryAndButtons();
            var first = session.Visibility.Current;
            int receiptCount = -1, spentAfterRead = -1;
            skipObserver = (id, all) =>
            {
                if (id != first.EventId) return;
                int before = Seat(CombatVisibility.Yang).VisibilityLedger.ConflictManaSpent;
                receiptCount = session.Visibility.History.Count;
                session.Visibility.IsVisible(Yang, Yin); session.Visibility.Refresh();
                spentAfterRead = Seat(CombatVisibility.Yang).VisibilityLedger.ConflictManaSpent;
                Record("skip receipt: before=" + before + ", after reads=" + spentAfterRead + ", history=" + receiptCount);
            };
            visibilityView.SkipRequested += skipObserver;
            yield return Click(ConflictButton("skipButton"));
            Assert.AreEqual(CombatConflictOutcome.Neutral, first.Outcome); Assert.AreEqual(200, first.LeftSpent); Assert.AreEqual(200, first.RightSpent);
            Assert.IsTrue(first.Visible); Assert.AreEqual(1, receiptCount); Assert.AreEqual(200, spentAfterRead); Assert.IsFalse(session.Visibility.AutoSkip);
            visibilityView.SkipRequested -= skipObserver; skipObserver = null;
            Record("mouse skip neutral: " + string.Join(" | ", first.Log)); yield return Capture("01-skip-neutral");
            yield return WaitFor(() => encounter.AcceptsPlayerCommand || session.Visibility.IsPaused, "next command or observation");
            if (encounter.AcceptsPlayerCommand)
            {
                int paid = Seat(CombatVisibility.Yang).VisibilityLedger.ConflictManaSpent;
                encounter.RequestGuard(session.CurrentActorId); yield return null;
                Assert.AreEqual(paid, Seat(CombatVisibility.Yang).VisibilityLedger.ConflictManaSpent);
                Record("legal stationary guard does not recharge the completed observation");
            }
            yield return LeaveAtTheNextLegalWindow(); Assert.AreEqual(saved, WorldRecord());
            yield return EnterAdventure(); yield return Enter(); yield return DeployBothThroughEntryAndButtons();
            var automatic = session.Visibility.Current;
            yield return Click(ConflictButton("autoButton"));
            Assert.IsTrue(session.Visibility.AutoSkip); Assert.AreEqual(CombatConflictOutcome.Neutral, automatic.Outcome);
            Assert.AreEqual(200, automatic.LeftSpent); Assert.AreEqual(200, automatic.RightSpent);
            yield return WaitFor(() => encounter.AcceptsPlayerCommand, "auto skip resumes normal combat");
            Assert.IsFalse(session.Visibility.IsPaused);
            Assert.AreEqual(200, Seat(CombatVisibility.Yang).VisibilityLedger.ConflictManaSpent);
            Assert.AreEqual(200, Seat(CombatVisibility.Yin).VisibilityLedger.ConflictManaSpent);
            Record("whole-battle auto: " + string.Join(" | ", automatic.Log)); yield return Capture("02-auto-neutral");
            yield return LeaveAtTheNextLegalWindow();
            yield return EnterAdventure(); yield return Enter(); yield return DeployBothThroughEntryAndButtons();
            var waiting = session.Visibility.Current; var priorSession = session;
            int beforeYang = Actor(Yang).CurrentSpirit, beforeYin = Actor(Yin).CurrentSpirit;
            Assert.IsFalse(waiting.Started); yield return Capture("03-exit-while-waiting");
            yield return Click(ConflictButton("exitButton")); yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.IsFalse(priorSession.Visibility.IsPaused); Assert.AreEqual(CombatConflictOutcome.Cancelled, waiting.Outcome);
            Assert.AreEqual(0, waiting.LeftSpent); Assert.AreEqual(0, waiting.RightSpent);
            Assert.AreEqual(beforeYang, Actor(Yang).CurrentSpirit); Assert.AreEqual(beforeYin, Actor(Yin).CurrentSpirit);
            Assert.IsTrue(priorSession.Jindan.Seats.Where(seat => seat.VisibilityLedger != null).All(seat => seat.ActiveVisibility == null));
            Assert.AreEqual(saved, WorldRecord()); Record("waiting exit cancels, no pulse/payment/result commitment");
            yield return EnterAdventure(); yield return Enter(); Assert.IsFalse(session.Visibility.AutoSkip);
            Assert.IsEmpty(session.Visibility.History); Assert.AreEqual(200, Seat(CombatVisibility.Yang).VisibilityLedger.ReserveRemaining);
            yield return LeaveAtTheNextLegalWindow(); Assert.AreEqual(saved, WorldRecord());
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator HiddenTargetsStayOutOfHudAndBlindPreviewAndReleaseLosesWithoutClockAdvance()
        {
            yield return Setup(); yield return Enter();
            Assert.IsFalse(session.Visibility.IsVisible(Yang, Yin));
            var hud = Find<CombatHudPresenter>().Snapshot;
            Assert.IsFalse(hud.Enemies.Any(target => target.Id == Yin));
            Assert.IsFalse(hud.ArtActions.SelectMany(action => action.Targets).Any(target => target.Id == Yin));
            var candidate = new CombatActionCandidate(CombatActionKind.Art, Yang, null, "spell_guanshiji",
                hud.CommandContextVersion, targetCell: new CombatUnitPresentationHex(7, 14));
            var preview = encounter.PreviewAction(candidate);
            Assert.IsNotNull(preview.AttackArea);
            Assert.IsFalse(preview.AttackArea.Targets.Any(target => target.Id == Yin));
            // This demo's Guanshiji reaches only one weighted unit, while the witness is three away.
            // Legal blind damage is exercised with a range-six profile in D11ProtectionTests.
            Record("concealed target absent from HUD, art targets and displayed hit list");
            yield return DeployBothThroughEntryAndButtons();
            var conflict = session.Visibility.Current; string frozen = Clocks();
            yield return Keys(Key.Space); Assert.AreEqual(50, conflict.LeftSpent);
            yield return Keys();
            Assert.AreEqual(CombatConflictOutcome.Right, conflict.Outcome); Assert.AreEqual("released", conflict.EndReason);
            Assert.AreEqual(frozen, Clocks()); Assert.IsFalse(session.Visibility.IsVisible(Yang, Yin));
            Assert.IsFalse(Find<CombatHudPresenter>().Snapshot.Enemies.Any(target => target.Id == Yin));
            Record("hold release: yin wins; both pay50; clocks unchanged; target remains hidden");
            yield return Capture("01-hidden-after-release"); yield return LeaveAtTheNextLegalWindow();
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator PublicAltarRemainsWhenIndependentCastAndHitCrossConcealment()
        {
            yield return Setup(); yield return Enter();
            var provider = Find<Static3DCombatUnitPresentationProvider>();
            Assert.IsTrue(provider.TryGetPresentedUnit(Yin, out var yinBody));
            Assert.IsFalse(yinBody.activeInHierarchy);
            var altar = GameObject.Find("YinActive(Clone)");
            var range = GameObject.Find("YinDomainRange");
            Assert.IsNotNull(altar);
            Assert.IsTrue(altar.transform.Find("Intact/Glow/Glyph").gameObject.activeInHierarchy);
            Assert.IsNotNull(range);
            yield return Capture("01-hidden-actor-public-moon-altar");

            var near = new HexCoord(6, 14);
            encounter.RequestMove(Yang, near.Q, near.R);
            yield return Ready();
            Assert.AreEqual(near, Actor(Yang).Position);
            Assert.IsTrue(yinBody.activeInHierarchy);

            // Isolate overlapping playback: normal input waits for the presentation lock.
            // Both casts use the actual catalog carrier, and the committed move below changes real observation.
            CombatUnitPresentationHex Cell(string id) => new(Actor(id).Position.Q, Actor(id).Position.R);
            CombatAttackVfx Cast(string id, CombatUnitPresentationHex to)
            {
                var before = Object.FindObjectsByType<CombatAttackVfx>(FindObjectsSortMode.None);
                provider.Present(new CombatUnitPresentationEventProjection(id, CombatUnitPresentationEvent.Cast,
                    Cell(id), Cell(id), Actor(id).Facing, null, profileId: "spell_feixingzhi", sourceCell: Cell(id), targetCell: to));
                return Object.FindObjectsByType<CombatAttackVfx>(FindObjectsSortMode.None).Except(before).Single();
            }
            var privateShot = Cast(Yin, Cell(Yang));
            var publicShot = Cast(Yang, Cell(Yin));
            provider.Present(new CombatUnitPresentationEventProjection(Yin, CombatUnitPresentationEvent.Hit,
                Cell(Yin), Cell(Yin), Actor(Yin).Facing, null));
            var hit = GameObject.Find("GuanzhongCombatFeedback_Hit_Vfx");
            Assert.IsNotNull(hit);
            Assert.AreEqual(6, privateShot.GetComponentsInChildren<MeshRenderer>().Length, "Head, four tail quads and the ribbon");
            Assert.IsTrue(privateShot.transform.Find("Head").gameObject.activeInHierarchy);
            Assert.IsTrue(publicShot.transform.Find("Head").gameObject.activeInHierarchy);
            var retreat = new CombatCommand(CombatCommandKind.Move, Yang, destination: new HexCoord(4, 14));
            var result = new CombatCommandService().Execute(session, retreat);
            Assert.IsTrue(result.Succeeded, result.RejectionReason);
            Assert.IsFalse(session.Visibility.IsVisible(Yang, Yin));
            yield return null; // Installer.LateUpdate applies this observation before the next test frame.
            Assert.IsFalse(yinBody.activeInHierarchy);
            Assert.IsFalse(privateShot.gameObject.activeInHierarchy);
            Assert.IsEmpty(privateShot.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.gameObject.activeInHierarchy));
            Assert.IsFalse(hit.activeInHierarchy);
            Assert.IsTrue(publicShot.gameObject.activeInHierarchy, "A visible actor's cast at a public cell still plays.");
            Assert.IsTrue(altar.transform.Find("Intact/Glow/Glyph").gameObject.activeInHierarchy);
            Assert.IsTrue(range.activeInHierarchy);
            Record("Natural reveal then retreat; concealed caster Head/Tail/Ribbon and hit inactive. Public-cell cast, altar glyph and domain remain.");
            yield return Capture("02-retreat-public-altar-no-actor-effects");
            yield return LeaveAtTheNextLegalWindow();
            Assert.IsTrue(range == null && altar == null);
            yield return EnterAdventure(); yield return Enter();
            Assert.IsNotNull(GameObject.Find("YinDomainRange"));
            Assert.IsNull(GameObject.Find("YangDomainRange"));
            Assert.IsEmpty(Object.FindObjectsByType<CombatAttackVfx>(FindObjectsSortMode.None));
        }

        private IEnumerator DeployBothThroughEntryAndButtons()
        {
            Assert.AreEqual(Yang, session.CurrentActorId); yield return Press("visibility_tab");
            int ap = Actor(Yang).ActionPointsRemainingUnits, mp = Actor(Yang).CurrentSpirit;
            yield return Press("visibility_deploy"); Assert.IsFalse(view.GetButton("confirm").interactable);
            var altar = session.Jindan.Ability(CombatVisibility.Yang).Visibility.Center;
            var cell = new CombatUnitPresentationHex(altar.Q, altar.R);
            yield return D12PlayerStartPlayModeTests.LockAbilityCell(cell);
            Assert.IsTrue(view.GetButton("confirm").interactable);
            yield return Press("cancel_targeting"); Assert.AreEqual(ap, Actor(Yang).ActionPointsRemainingUnits); Assert.AreEqual(mp, Actor(Yang).CurrentSpirit);
            // Equal-speed CT ordering gives the seal/yin NPC the first turn. Its paid domain
            // already exists when the player's confirmation creates the observation conflict.
            Assert.AreEqual(1, Seat(CombatVisibility.Yin).SuccessfulDeployments);
            yield return Press("visibility_deploy");
            yield return D12PlayerStartPlayModeTests.LockAbilityCell(cell);
            yield return Press("confirm");
            yield return WaitFor(() => session.Visibility.IsPaused, "paid domains create conflict");
            Assert.AreEqual(ap - 250000, Actor(Yang).ActionPointsRemainingUnits); Assert.AreEqual(mp - 400, Actor(Yang).CurrentSpirit);
            Assert.AreEqual(1, Seat(CombatVisibility.Yang).SuccessfulDeployments);
            Assert.AreEqual(1, Seat(CombatVisibility.Yin).SuccessfulDeployments);
            Assert.AreEqual(400, Seat(CombatVisibility.Yin).TotalManaSpent);
            Assert.AreEqual(250000, Seat(CombatVisibility.Yin).TotalActionPointSpent);
            Record("formal domains: player and NPC each paid250AP/400MP; " + Clocks());
            yield return null;
        }

        private IEnumerator Enter()
        {
            Find<AdventureSceneInstaller>().OpenJindanPreparation();
            yield return null;
            view = Find<JindanDemoView>(); yield return Press("stage3"); yield return Press("enter");
            encounter = Find<EncounterCoordinator>(); session = encounter.CurrentSession; visibilityView = Find<JindanVisibilityView>();
            yield return Ready();
        }
        private IEnumerator LeaveAtTheNextLegalWindow()
        {
            yield return WaitFor(() => encounter.AcceptsPlayerCommand || session.Visibility.IsPaused, "withdrawal window");
            if (session.Visibility.IsPaused) yield return Click(ConflictButton("exitButton"));
            else
            {
                yield return Click(GameObject.Find("ReturnButton").GetComponent<Button>());
                yield return Click(GameObject.Find("ConfirmWithdrawalButton").GetComponent<Button>());
            }
            yield return SceneReady(GameplaySceneNames.Settlement);
        }
        private IEnumerator EnterAdventure()
        { Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild")); yield return SceneReady(GameplaySceneNames.Adventure); }
        private IEnumerator Setup()
        {
            evidence.Clear(); output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/d11-visibility/" + TestContext.CurrentContext.Test.Name));
            Directory.CreateDirectory(output); oldBackground = InputSystem.settings.backgroundBehavior;
            oldInput = InputSystem.settings.editorInputBehaviorInPlayMode; configured = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>(); keyboard = InputSystem.AddDevice<Keyboard>();
            GuanzhongCombatEquipmentPlayModeTests.SetGameSize(1366, 768);
            var stale = Object.FindFirstObjectByType<GameBootstrap>(); if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            saves = Path.Combine(Application.temporaryCachePath, "D11-" + Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saves));
            var creation = Find<CharacterCreationController>(); creation.Open(); Assert.IsTrue(creation.SelectOrigin("origin_minor_clan")); creation.Submit("d11", "显隐验证");
            yield return SceneReady(GameplaySceneNames.World); Assert.IsTrue(Find<WorldMapController>().SelectNode("guanzhong_hub"));
            Find<WorldMapController>().EnterSelectedLocation(); yield return SceneReady(GameplaySceneNames.Settlement); yield return EnterAdventure();
        }
        private IEnumerator Ready() => WaitFor(() => encounter.AcceptsPlayerCommand, "player command window");
        private static IEnumerator WaitFor(Func<bool> predicate, string reason, float seconds = 60)
        { for (float end = Time.realtimeSinceStartup + seconds; Time.realtimeSinceStartup < end && !predicate();) yield return null; Assert.IsTrue(predicate(), reason); yield return null; }
        private IEnumerator Keys(params Key[] keys) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); yield return null; }
        private IEnumerator Press(string key) => Click(view.GetButton(key));
        private static Button ConflictButton(string key) => GameObject.Find("JindanConflictRoot").transform.Find("ConflictDialog/" + key).GetComponent<Button>();
        private IEnumerator Click(Button button)
        {
            Canvas.ForceUpdateCanvases(); Assert.IsNotNull(button); Assert.IsTrue(button.interactable && button.gameObject.activeInHierarchy, button.name);
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = Center(button) }, hits);
            Assert.IsTrue(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button, button.name + " covered");
            Vector2 point = Center(button);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left)); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
        }
        private string Clocks()
        {
            var turns = session.TurnScheduler.GetSnapshot(session.Combatants.All);
            return string.Join(" | ", "tick=" + turns.CurrentTick, "turn=" + session.TurnSequence, "actor=" + session.CurrentActorId,
                "ct=" + string.Join("/", turns.Units.Select(unit => unit.Id + ":" + unit.ChargeTime + ":" + unit.NextActionThreshold)),
                "actors=" + string.Join("/", session.Combatants.All.Select(actor => actor.Id + ":hp" + actor.CurrentHealth + ":ap" + actor.ActionPointsRemainingUnits +
                    ":turns" + actor.OwnTurnsStarted + "," + actor.OwnTurnsEnded + ":cd=" + string.Join(",", actor.Cooldowns.Select(pair => pair.Key + ":" + pair.Value)) +
                    ":status=" + string.Join(",", actor.Statuses.Select(status => status.Kind + ":" + status.RemainingOwnTurnEnds)))),
                "domains=" + string.Join("/", session.Jindan.Seats.Select(seat => seat.Definition.SeatId + ":" + seat.MaintenancePayments + ":" + seat.ActiveVisibility?.LastPaidTick)),
                "surface=" + session.Surfaces.Revision + "," + session.Surfaces.SettlementSequence + "," + string.Join("/", session.Surfaces.FireSources.Select(fire => fire.ElapsedTicks + ":" + fire.RemainingFuel)),
                "flight=" + string.Join("/", session.Flight.States.Select(flight => flight.ActorId + ":" + flight.Active + ":" + flight.MaintenancePayments + ":" + flight.RemainingOwnTurnEnds)));
        }
        private static string WorldRecord() => JsonUtility.ToJson(CharacterRecord.Capture(GameBootstrap.RequireRuntime().Player.Capture()));
        private void Record(string text) { evidence.Add(text + " | tick=" + session?.TurnScheduler.CurrentTick); File.WriteAllLines(Path.Combine(output, "proof.txt"), evidence); }
        private IEnumerator Capture(string name) { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png")); yield return null; }
        private static Vector2 Center(Button b) { var r = (RectTransform)b.transform; return RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(r.rect.center)); }
        private static IEnumerator SceneReady(string name) { for (int i = 0; i < 300 && SceneManager.GetActiveScene().name != name; i++) yield return null; yield return null; yield return null; Assert.AreEqual(name, SceneManager.GetActiveScene().name); }
        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>() ?? throw new Exception(typeof(T).Name + " missing");
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(owner, value);
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (visibilityView != null)
            {
                if (inputObserver != null) visibilityView.InputRequested -= inputObserver;
                if (skipObserver != null) visibilityView.SkipRequested -= skipObserver;
            }
            inputObserver = null; skipObserver = null;
            if (mouse != null) InputSystem.RemoveDevice(mouse); if (keyboard != null) InputSystem.RemoveDevice(keyboard); mouse = null; keyboard = null;
            if (configured) { InputSystem.settings.backgroundBehavior = oldBackground; InputSystem.settings.editorInputBehaviorInPlayMode = oldInput; }
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>(); if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            if (saves != null && Directory.Exists(saves)) Directory.Delete(saves, true);
        }
    }
}
