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
    public sealed class D5JindanEntryPlayModeTests
    {
        private const string ScaleActor = "demo_20261024_space_yang";
        private const string ScaleSeat = "demo_20261024_space_yang_scale_seat";
        private Mouse mouse;
        private string saves, output;
        private EncounterCoordinator encounter;
        private CombatSession session;
        private CombatBattlefieldSurface surface;
        private JindanDemoView view;
        private InputSettings.BackgroundBehavior oldBackground;
        private InputSettings.EditorInputBehaviorInPlayMode oldEditorInput;
        private bool inputConfigured;
        private readonly List<string> evidence = new List<string>();

        [UnityTest, Timeout(600000)]
        public IEnumerator ProductionEntryAndLedger()
        {
            yield return Setup(false);
            string originalPlayer = PlayerSave();
            yield return OpenPreparation();
            var presets = new HashSet<string>();
            for (int i = 0; i < 5; i++)
            {
                presets.Add(Get<Text>(view, "preparationDetails").text);
                yield return Press("preset_next");
            }
            Assert.AreEqual(5, presets.Count, "Five preparation presets must be browseable.");
            foreach (string key in new[] { "stage1", "stage2", "stage3", "earth_toggle", "earth_toggle", "cancel_preparation" })
                yield return Press(key);
            Assert.AreEqual(originalPlayer, PlayerSave(), "Preparation must not mutate the formal character.");
            Assert.IsNull(Find<EncounterCoordinator>().CurrentSession);
            yield return OpenPreparation();
            yield return Press("enter");
            Bind(); yield return Ready(ScaleActor);
            AssertFreshScale();
            yield return Press("scale_tab");
            yield return Press("preview");
            Assert.IsFalse(view.GetButton("confirm").interactable, "Empty selection must fail without payment.");
            AssertFreshScale();
            yield return Press("cancel_targeting");
            yield return Deploy(false);
            yield return MaintainAndClose(false);
            Assert.AreEqual(originalPlayer, PlayerSave(), "Rehearsal resources must remain session-owned.");
            yield return Withdraw(); yield return EnterAdventure();
            yield return OpenPreparation(); yield return Press("enter");
            Bind(); yield return Ready(ScaleActor); AssertFreshScale();
            yield return Capture("03-fresh-reentry");
            yield return Withdraw(); yield return EnterAdventure();

            yield return OpenPreparation(); yield return Press("stage2"); yield return Press("enter");
            Bind(); yield return Ready();
            Assert.AreEqual(2, session.Combatants.All.Count(value => value.Team == CombatTeam.Player));
            Assert.AreEqual(1, session.Combatants.All.Count(value => value.Team == CombatTeam.Enemy));
            string first = encounter.ControlledActorId;
            yield return EndTurn();
            Assert.AreNotEqual(first, encounter.ControlledActorId, "Second allied actor must receive player input.");
            yield return Press("flight_tab");
            yield return Press("flight_takeoff");
            Assert.IsTrue(view.GetButton("confirm").interactable, "The second ally's equipped flight should have a legal quote.");
            yield return Press("cancel_targeting");
            int ap = Actor.ActionPointsRemainingUnits;
            yield return Guard();
            Assert.Less(Actor.ActionPointsRemainingUnits, ap);
            Record("stage2: both allies accept commands through the same coordinator");
            yield return Withdraw(); yield return EnterAdventure();

            yield return OpenPreparation(); yield return Press("stage1"); yield return Press("enter");
            Bind(); yield return Ready("demo_20261024_earth");
            var earth = session.Jindan.Seats.Single(value => value.Definition.ActorId == Actor.Id);
            string effect = earth.EffectId;
            int budget = Actor.ActionPointLimitAtTurnStartUnits, mana = Actor.CurrentSpirit;
            long turn = session.TurnSequence; int tick = session.TurnScheduler.CurrentTick;
            yield return Press("earth_tab"); yield return Press("earth_swap"); yield return Press("confirm"); yield return Ready();
            Assert.AreNotEqual(effect, earth.EffectId); Assert.AreEqual(0, Actor.ActionPointsRemainingUnits);
            Assert.AreEqual(budget, earth.TotalActionPointSpent); Assert.AreEqual(mana, Actor.CurrentSpirit);
            for (int i = 0; i < 4; i++) yield return null;
            Assert.AreEqual(turn, session.TurnSequence); Assert.AreEqual(tick, session.TurnScheduler.CurrentTick);
            Assert.AreEqual(CombatTurnPhase.Active, session.TurnPhase);
            Assert.IsTrue(encounter.AcceptsPlayerCommand, "Zero AP after swapping must retain manual end-turn control.");
            yield return Capture("04-earth-zero-ap"); yield return EndTurn();
            Assert.Greater(session.TurnSequence, turn);
            yield return Withdraw(); yield return EnterAdventure();

            Assert.IsFalse(Find<AdventureController>().IsTerrainRehearsal);
            Assert.IsTrue(Find<AdventureInputController>().SelectNode("shijiahou_encounter"));
            yield return null; Bind(); yield return Ready("player");
            Assert.IsNull(session.Jindan); Assert.IsNull(session.SceneObjects);
            ap = Actor.ActionPointsRemainingUnits; yield return Guard();
            Assert.Less(Actor.ActionPointsRemainingUnits, ap);
            Record("ordinary bounty: entered and guard paid AP without rehearsal state");
            File.WriteAllLines(Path.Combine(output, "proof.txt"), evidence);
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator NativeScaleLoop()
        {
            yield return Setup(true);
            string originalPlayer = PlayerSave();
            yield return OpenPreparation();
            yield return Capture("00-preparation");
            yield return Press("enter");
            Bind(); yield return Ready(ScaleActor); AssertFreshScale();
            yield return Deploy(true);
            yield return MaintainAndClose(true);
            Assert.AreEqual(originalPlayer, PlayerSave());
            yield return NativeStep("withdraw", "点击返回关中城，再确认撤离。", () =>
                SceneManager.GetActiveScene().name == GameplaySceneNames.Settlement);
            EnsureMouse(); yield return EnterAdventure(); yield return OpenPreparation();
            RemoveMouse();
            yield return NativeStep("reenter", "点击进入演武；确认第三段为全新资源与无方位标状态。", () =>
                Find<EncounterCoordinator>().CurrentSession?.Jindan != null);
            Bind(); yield return Ready(ScaleActor); AssertFreshScale();
            yield return Capture("03-native-fresh-reentry");
            File.WriteAllText(Path.Combine(output, "pending.json"), "{\"stage\":\"complete\"}");
            File.WriteAllLines(Path.Combine(output, "proof.txt"), evidence);
        }

        private IEnumerator Deploy(bool native)
        {
            var actor = Actor; int ap = actor.ActionPointsRemainingUnits, mp = actor.CurrentSpirit;
            long turn = session.TurnSequence; int tick = session.TurnScheduler.CurrentTick;
            if (native)
            {
                RemoveMouse();
                yield return NativeStep("deploy", "压缩边→选定相连格→点击 (4,14)、(5,14)→预演→确认施放。可收起面板后选格，再展开。",
                    () => Seat.ActiveScale != null, true);
            }
            else
            {
                yield return Press("scale_tab");
                yield return Press("compress"); yield return Press("pick_edges");
                yield return Click(Point(4, 14)); yield return Click(Point(5, 14));
                Assert.IsTrue(view.GetButton("confirm").interactable, GameObject.Find("CompactQuote").GetComponent<Text>().text);
                yield return Press("confirm");
            }
            yield return Ready(ScaleActor);
            Assert.IsNotNull(Seat.ActiveScale);
            Assert.AreEqual(250000, ap - actor.ActionPointsRemainingUnits); Assert.AreEqual(400, mp - actor.CurrentSpirit);
            Assert.AreEqual(200, Seat.ActiveScale.Support.Durability); Assert.AreEqual(1, Metric());
            Assert.AreEqual(turn, session.TurnSequence); Assert.AreEqual(tick, session.TurnScheduler.CurrentTick);
            Assert.AreEqual(0, Seat.MaintenancePayments);
            for (int i = 0; i < 4; i++) yield return null;
            Assert.AreEqual(0, Seat.MaintenancePayments); Assert.AreEqual(mp - 400, actor.CurrentSpirit);
            Assert.IsTrue(Get<GameObject>(Find<JindanScaleView>(), "support").activeInHierarchy);
            Record("deployed: AP -250, MP -400, support 200, metric 1; same tick has no upkeep");
            yield return Capture("01-scale-deployed");
            if (native)
            {
                GuanzhongCombatEquipmentPlayModeTests.SetGameSize(1920, 1080);
                yield return null; yield return null;
                yield return Capture("01-scale-deployed-1920");
                GuanzhongCombatEquipmentPlayModeTests.SetGameSize(1366, 768);
                yield return null; yield return null;
            }
        }

        private IEnumerator MaintainAndClose(bool native)
        {
            var actor = Actor; int mp = actor.CurrentSpirit, tick = session.TurnScheduler.CurrentTick;
            if (native) yield return NativeStep("maintain", "点击结束行动，并在动作预演中确认；等待本方重新行动。",
                () => Seat.MaintenancePayments > 0 && encounter.AcceptsPlayerCommand && session.CurrentActorId == ScaleActor);
            else yield return EndTurn(ScaleActor);
            int elapsed = session.TurnScheduler.CurrentTick - tick;
            Assert.Greater(elapsed, 0); Assert.AreEqual(elapsed, Seat.MaintenancePayments);
            Assert.AreEqual(40 * elapsed, mp - actor.CurrentSpirit);
            int ap = actor.ActionPointsRemainingUnits; mp = actor.CurrentSpirit;
            if (native) yield return NativeStep("close", "点击关闭方位定尺，再确认；方位标和受影响边线应消失。", () => Seat.ActiveScale == null);
            else { yield return Press("close_tab"); yield return Press("close_effect"); yield return Press("confirm"); }
            yield return Ready(ScaleActor);
            Assert.IsNull(Seat.ActiveScale); Assert.AreEqual(25000, ap - actor.ActionPointsRemainingUnits);
            Assert.AreEqual(mp, actor.CurrentSpirit); Assert.AreEqual(2, Metric());
            Assert.IsFalse(Get<GameObject>(Find<JindanScaleView>(), "support").activeInHierarchy);
            Record("maintained " + elapsed + " ticks × 40 MP; close AP -25, metric restored to 2");
            yield return Capture("02-scale-closed");
        }

        private IEnumerator Setup(bool native)
        {
            evidence.Clear(); encounter = null; session = null; surface = null; view = null;
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/d5/" + (native ? "native" : "production")));
            Directory.CreateDirectory(output);
            oldBackground = InputSystem.settings.backgroundBehavior;
            oldEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode; inputConfigured = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            EnsureMouse(); GuanzhongCombatEquipmentPlayModeTests.SetGameSize(1366, 768);
            var stale = Object.FindFirstObjectByType<GameBootstrap>();
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            saves = Path.Combine(Application.temporaryCachePath, "D5-" + Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saves));
            var creation = Find<CharacterCreationController>();
            creation.Open(); Assert.IsTrue(creation.SelectOrigin("origin_minor_clan")); creation.Submit("d5", "金丹演武验证");
            yield return SceneReady(GameplaySceneNames.World);
            Assert.IsTrue(Find<WorldMapController>().SelectNode("guanzhong_hub"));
            Find<WorldMapController>().EnterSelectedLocation(); yield return SceneReady(GameplaySceneNames.Settlement);
            yield return EnterAdventure();
        }

        private IEnumerator NativeStep(string stage, string instruction, Func<bool> condition, bool requireEdges = false)
        {
            Assert.IsNull(mouse, "Synthetic mouse must be removed for native acceptance.");
            int clicks = 0; var cells = new HashSet<string>(); float update = 0;
            for (float end = Time.realtimeSinceStartup + 600; Time.realtimeSinceStartup < end;)
            {
                if (Time.realtimeSinceStartup >= update)
                { PublishPending(stage, instruction); update = Time.realtimeSinceStartup + 1; }
                yield return null;
                Mouse real = Mouse.current;
                if (real != null && real.native && real.leftButton.wasPressedThisFrame)
                {
                    clicks++; Vector2 point = real.position.ReadValue(); string hit = "UI";
                    var movement = Object.FindFirstObjectByType<CombatMovementView>();
                    if (movement != null && !movement.UiOwnsPointer(point) && movement.TryPickCell(point, out var cell))
                    { hit = cell.Q + ":" + cell.R; cells.Add(hit); }
                    Record(stage + " native=" + point + " target=" + hit);
                }
                if (condition()) break;
            }
            Assert.IsTrue(condition(), stage + " timed out"); Assert.Greater(clicks, 0, stage + " requires real OS input");
            if (requireEdges) { Assert.IsTrue(cells.Contains("4:14")); Assert.IsTrue(cells.Contains("5:14")); }
            Record(stage + " completed");
        }

        private void PublishPending(string stage, string instruction)
        {
            var names = new HashSet<string>(new[] { "compress", "pick_edges", "pick_origin", "preview", "confirm",
                "toggle_panel", "close_effect", "enter", "WaitButton", "ConfirmActionButton", "ReturnButton", "ConfirmWithdrawalButton" });
            var points = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(button => names.Contains(button.name)).Select(button => new ButtonPoint { name = button.name,
                    position = ButtonCenter(button), active = button.gameObject.activeInHierarchy, interactable = button.interactable }).ToArray();
            var pending = new Pending { stage = stage, instruction = instruction, width = Screen.width, height = Screen.height,
                coordinates = "Game-view pixels; origin bottom-left", buttons = points, state = StateText(),
                cells = surface == null ? Array.Empty<CellPoint>() : new[] {
                    new CellPoint { cell = "4:14", position = Point(4, 14) }, new CellPoint { cell = "5:14", position = Point(5, 14) } } };
            File.WriteAllText(Path.Combine(output, "pending.json"), JsonUtility.ToJson(pending, true));
        }

        private void AssertFreshScale()
        {
            Assert.AreEqual(ScaleActor, encounter.ControlledActorId); Assert.AreEqual(1000000, Actor.ActionPointsRemainingUnits);
            Assert.AreEqual(6000, Actor.CurrentSpirit); Assert.AreEqual(0, Actor.GetCooldown("SPACE_SCALE"));
            Assert.IsNull(Seat.ActiveScale); Assert.AreEqual(0, Seat.SuccessfulDeployments);
            Assert.AreEqual(0, Seat.MaintenancePayments); Assert.AreEqual(0, Seat.TotalManaSpent); Assert.AreEqual(2, Metric());
            Assert.IsEmpty(session.SceneObjects.Board.MetricOverrides);
        }
        private void Bind()
        {
            encounter = Find<EncounterCoordinator>(); session = encounter.CurrentSession;
            Assert.IsNotNull(session); surface = Object.FindFirstObjectByType<CombatBattlefieldSurface>();
            view = Find<JindanDemoView>();
        }
        private CombatJindanSeatState Seat => session.Jindan.Find(ScaleSeat);
        private CombatantSnapshot Actor { get { session.Combatants.TryGet(encounter.ControlledActorId, out var value); return value; } }
        private int Metric() => session.SceneObjects.Board.InspectEdge(new HexCoord(4, 14), new HexCoord(5, 14), SpatialQueryKind.Attack).MetricDistanceUnits;
        private static string PlayerSave() => JsonUtility.ToJson(CharacterRecord.Capture(GameBootstrap.RequireRuntime().Player.Capture()));
        private IEnumerator OpenPreparation()
        { yield return Click(GameObject.Find("JindanRehearsalButton").GetComponent<Button>()); view = Find<JindanDemoView>(); }
        private IEnumerator EnterAdventure()
        { Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild")); yield return SceneReady(GameplaySceneNames.Adventure); }
        private IEnumerator Withdraw()
        {
            yield return null; yield return Click(GameObject.Find("ReturnButton").GetComponent<Button>());
            Assert.IsTrue(Find<AdventureController>().IsWithdrawalConfirmationOpen);
            yield return Click(GameObject.Find("ConfirmWithdrawalButton").GetComponent<Button>());
            yield return SceneReady(GameplaySceneNames.Settlement);
        }
        private IEnumerator EndTurn(string actor = null)
        {
            long before = session.TurnSequence;
            yield return Click(Find<CombatActionBarView>().WaitButton);
            yield return Click(Find<CombatActionPreviewView>().ConfirmButton);
            for (float end = Time.realtimeSinceStartup + 60; session.TurnSequence == before && Time.realtimeSinceStartup < end;) yield return null;
            Assert.Greater(session.TurnSequence, before); yield return Ready(actor);
        }
        private IEnumerator Guard()
        { yield return Click(Find<CombatActionBarView>().GuardButton); yield return Click(Find<CombatActionPreviewView>().ConfirmButton); yield return Ready(); }
        private IEnumerator Ready(string actor = null)
        {
            for (float end = Time.realtimeSinceStartup + 60; Time.realtimeSinceStartup < end &&
                (!encounter.AcceptsPlayerCommand || session.TurnPhase != CombatTurnPhase.Active || actor != null && session.CurrentActorId != actor);) yield return null;
            Assert.IsTrue(encounter.AcceptsPlayerCommand, StateText()); Assert.AreEqual(CombatTurnPhase.Active, session.TurnPhase);
            if (actor != null) Assert.AreEqual(actor, session.CurrentActorId);
            yield return null;
        }
        private IEnumerator Press(string key) => Click(view.GetButton(key));
        private IEnumerator Click(Button button)
        {
            Canvas.ForceUpdateCanvases(); Assert.IsNotNull(button);
            Assert.IsTrue(button.interactable && button.gameObject.activeInHierarchy, button.name);
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = ButtonCenter(button) }, hits);
            Assert.IsTrue(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button,
                button.name + " is covered by " + string.Join(",", hits.Select(value => value.gameObject.name)));
            yield return Click(ButtonCenter(button));
        }
        private IEnumerator Click(Vector2 point)
        {
            Assert.IsNotNull(mouse); InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left)); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
        }
        private static Vector2 ButtonCenter(Button button)
        { var rect = (RectTransform)button.transform; return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)); }
        private Vector2 Point(int q, int r) => Camera.main.WorldToScreenPoint(surface.Center(new CombatUnitPresentationHex(q, r)));
        private void EnsureMouse() { if (mouse == null) mouse = InputSystem.AddDevice<Mouse>(); }
        private void RemoveMouse() { if (mouse != null) InputSystem.RemoveDevice(mouse); mouse = null; }
        private string StateText() => session == null ? "no session" : "actor=" + session.CurrentActorId + " tick=" + session.TurnScheduler.CurrentTick +
            " turn=" + session.TurnSequence + " units=" + string.Join(";", session.Combatants.All.Select(a => a.Id + ":AP" + a.ActionPointsRemainingUnits + ":MP" + a.CurrentSpirit + ":HP" + a.CurrentHealth));
        private void Record(string value) { evidence.Add(value + " | " + StateText()); File.WriteAllLines(Path.Combine(output, "proof.txt"), evidence); }
        private IEnumerator Capture(string name)
        { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png")); yield return null; }
        private static IEnumerator SceneReady(string name)
        { for (int i = 0; i < 300 && SceneManager.GetActiveScene().name != name; i++) yield return null; yield return null; yield return null; Assert.AreEqual(name, SceneManager.GetActiveScene().name); }
        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>() ?? throw new Exception(typeof(T).Name + " missing");
        private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(owner, value);
        [Serializable] private sealed class Pending { public string stage, instruction, coordinates, state; public int width, height; public ButtonPoint[] buttons; public CellPoint[] cells; }
        [Serializable] private sealed class ButtonPoint { public string name; public Vector2 position; public bool active, interactable; }
        [Serializable] private sealed class CellPoint { public string cell; public Vector2 position; }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            RemoveMouse();
            if (inputConfigured) { InputSystem.settings.backgroundBehavior = oldBackground; InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorInput; }
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>(); if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            if (saves != null && Directory.Exists(saves)) Directory.Delete(saves, true);
        }
    }
}
