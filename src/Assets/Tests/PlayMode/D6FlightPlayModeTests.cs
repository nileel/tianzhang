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
    public sealed class D6FlightPlayModeTests
    {
        private const string Earth = "demo_20261024_earth";
        private Mouse mouse;
        private string saves, output;
        private EncounterCoordinator encounter;
        private CombatSession session;
        private CombatBattlefieldSurface surface;
        private JindanDemoView view;
        private InputSettings.BackgroundBehavior oldBackground;
        private InputSettings.EditorInputBehaviorInPlayMode oldInput;
        private bool configured;
        private readonly List<string> evidence = new List<string>();
        private CombatantSnapshot Actor { get { session.Combatants.TryGet(Earth, out var a); return a; } }

        [UnityTest, Timeout(600000)] public IEnumerator ProductionFlightLoop() => Loop(false);
        [UnityTest, Timeout(1800000)] public IEnumerator NativeFlightLoop() => Loop(true);

        [UnityTest, Timeout(600000)]
        public IEnumerator AutomaticExpiryLandsBeforeNextGroundMove()
        {
            yield return Setup(false); yield return EnterFirstSegment();
            yield return Press("flight_takeoff"); yield return Press("confirm"); yield return Ready();
            yield return Press("flight_ascend"); yield return Press("confirm"); yield return Ready();
            for (int i = 0; i < 4; i++) yield return EndTurn();
            Assert.AreEqual("flight_expired", session.Flight.Find(Earth).LastEndReason);
            Assert.AreEqual(0, Actor.AltitudeBand); AssertFlightVisual(0);
            var provider = Find<Static3DCombatUnitPresentationProvider>();
            Assert.IsTrue(provider.TryGetPresentedPose(Earth, out var pose)); Assert.AreEqual(0, pose.AltitudeBand);
            var target = Actor.Position.Neighbors().First(cell => session.SceneObjects.Board.QueryMovementPath(Actor.Pose,
                cell, Actor.ActionPointsRemainingUnits, Actor.MovementEfficiencyMilliUnits, Occupied()).Steps.Count > 0);
            encounter.RequestMove(Earth, target.Q, target.R); yield return Ready(); Assert.AreEqual(target, Actor.Position);
            Record("third subsequent own end landed; following ground movement presented without stale altitude");
            yield return Capture("06-expired-ground-move");
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator FailedLandingFromUpkeepCanRecoverWithoutMovingColumn()
        {
            yield return Setup(false); yield return EnterFirstSegment();
            // Deliberate resource fixture in a normal production session; no production affordability bypass.
            Actor.SetSpirit(6000, 299);
            yield return Press("flight_takeoff"); yield return Press("confirm"); yield return Ready();
            var target = new HexCoord(4, 3);
            encounter.RequestMove(Earth, target.Q, target.R); yield return Ready(); Assert.AreEqual(target, Actor.Position);
            yield return Press("flight_land"); Assert.IsFalse(view.GetButton("confirm").interactable);
            Assert.AreEqual(99, Actor.CurrentSpirit); yield return Press("cancel_targeting");
            yield return EndTurn(); var state = session.Flight.Find(Earth);
            Assert.IsTrue(state.FailedLanding); Assert.AreEqual(99, Actor.CurrentSpirit);
            Assert.AreEqual(target, Actor.Position); AssertFlightVisual(1);
            Assert.LessOrEqual(Actor.CurrentHealth, Actor.MaximumHealth - (Actor.MaximumHealth + 9) / 10);
            Record("upkeep 99 < 100: no partial debit, illegal rock column retained with failed-landing process");
            yield return Capture("07-failed-landing");
            yield return Press("flight_ascend"); Assert.IsFalse(view.GetButton("confirm").interactable);
            yield return Press("cancel_targeting");
            // Restore only the test input balance so the real paid recovery command can be exercised.
            Actor.SetSpirit(6000, 500); int ap = Actor.ActionPointsRemainingUnits;
            yield return Press("flight_recover"); yield return Press("confirm"); yield return Ready();
            Assert.IsFalse(state.FailedLanding); Assert.IsTrue(state.Active); Assert.AreEqual(3, state.RemainingOwnTurnEnds);
            Assert.AreEqual(ap - 250000, Actor.ActionPointsRemainingUnits); Assert.AreEqual(300, Actor.CurrentSpirit);
            Assert.AreEqual(target, Actor.Position); AssertFlightVisual(1);
            Record("paid recovery AP250/MP200 restores low air in the original column"); yield return Capture("08-recovered-low-air");
        }

        private IEnumerator EnterFirstSegment()
        {
            yield return Click(GameObject.Find("JindanRehearsalButton").GetComponent<Button>());
            view = Find<JindanDemoView>(); yield return Press("stage1"); yield return Press("enter");
            encounter = Find<EncounterCoordinator>(); session = encounter.CurrentSession; surface = Find<CombatBattlefieldSurface>();
            yield return Ready(); yield return Press("flight_tab");
        }

        private IEnumerator Loop(bool native)
        {
            yield return Setup(native);
            string saved = JsonUtility.ToJson(CharacterRecord.Capture(GameBootstrap.RequireRuntime().Player.Capture()));
            yield return Click(GameObject.Find("JindanRehearsalButton").GetComponent<Button>());
            view = Find<JindanDemoView>(); yield return Press("stage1"); yield return Press("enter");
            encounter = Find<EncounterCoordinator>(); session = encounter.CurrentSession;
            surface = Find<CombatBattlefieldSurface>(); yield return Ready();
            yield return Press("flight_tab");
            Assert.AreEqual(1000000, Actor.ActionPointsRemainingUnits); Assert.AreEqual(6000, Actor.CurrentSpirit);
            var board = session.SceneObjects.Board; var origin = Actor.Pose; var destination = new HexCoord(4, 5);
            var occupied = Occupied();
            var ground = board.QueryMovementPath(origin, destination, 1000000, Actor.MovementEfficiencyMilliUnits, occupied);
            var low = board.QueryMovementPath(new SpatialPose(origin.Anchor, origin.Facing, movementMode: SpatialMovementMode.Fly, altitudeBand: 1),
                destination, 1000000, Actor.MovementEfficiencyMilliUnits, occupied);
            Assert.IsNotEmpty(ground.Steps); Assert.IsNotEmpty(low.Steps); Assert.Less(low.Steps.Count, ground.Steps.Count);
            Assert.Less(low.ActionPointCostUnits, ground.ActionPointCostUnits);
            Record("same destination " + destination + ": ground=" + ground.Steps.Count + "/" + ground.ActionPointCostUnits + " air=" + low.Steps.Count + "/" + low.ActionPointCostUnits);
            yield return Press("flight_takeoff"); yield return Press("cancel_targeting");
            Assert.AreEqual(1000000, Actor.ActionPointsRemainingUnits); Assert.AreEqual(6000, Actor.CurrentSpirit);
            if (native) RemoveMouse();
            yield return Action(native, "takeoff", "flight_takeoff", () => Actor.AltitudeBand == 1);
            Assert.AreEqual(750000, Actor.ActionPointsRemainingUnits); Assert.AreEqual(5800, Actor.CurrentSpirit);
            AssertFlightVisual(1); yield return Capture("01-low-air");
            yield return Action(native, "ascend", "flight_ascend", () => Actor.AltitudeBand == 2);
            Assert.AreEqual(625000, Actor.ActionPointsRemainingUnits); AssertFlightVisual(2);
            yield return Capture("02-high-air");
            yield return Action(native, "descend", "flight_descend", () => Actor.AltitudeBand == 1);
            Assert.AreEqual(500000, Actor.ActionPointsRemainingUnits);
            if (native)
                yield return NativeStep("move", "收起演武面板，点击移动按钮，悬停地图(4,5)预览，再左键落点提交。", () => Actor.Position == destination);
            else
            {
                yield return Press("toggle_panel"); yield return Click(Find<CombatActionBarView>().MoveButton);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = Point(destination) }); yield return null; yield return null;
                var movement = Find<CombatMovementView>(); Assert.IsNotNull(movement.Selected);
                var line = (LineRenderer)typeof(CombatMovementView).GetField("pathLine", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(movement);
                Assert.AreEqual(low.Steps.Count + 1, line.positionCount);
                Assert.That(line.GetPosition(0).y - surface.Center(new CombatUnitPresentationHex(origin.Anchor.Q, origin.Anchor.R)).y,
                    Is.EqualTo(.75f).Within(.01f));
                Record("hovered airborne path: " + line.positionCount + " points, first point 0.75m above origin");
                yield return Capture("00-air-path-preview"); yield return Click(Point(destination));
            }
            yield return Ready(); Assert.AreEqual(destination, Actor.Position);
            Assert.AreEqual(500000 - low.ActionPointCostUnits, Actor.ActionPointsRemainingUnits);
            AssertFlightVisual(1); Record("air path committed: " + string.Join(" -> ", low.Path));
            yield return Capture("03-cliff-shortcut");
            if (!native) yield return Press("toggle_panel");
            yield return Action(native, "land", "flight_land", () => Actor.AltitudeBand == 0);
            Assert.AreEqual(450000 - low.ActionPointCostUnits, Actor.ActionPointsRemainingUnits);
            Assert.AreEqual(5800, Actor.CurrentSpirit); AssertFlightVisual(0);
            yield return Capture("04-landed");

            // Real enemy AI reads the same anti-air contact policy. Approach a same-height column.
            if (native) EnsureMouse();
            yield return EndTurn();
            var enemy = session.Combatants.All.Single(a => a.Team == CombatTeam.Enemy);
            var adjacent = enemy.Position.Neighbors().Where(cell => board.TryGetCell(cell, out var r) &&
                board.TryGetCell(enemy.Position, out var er) && r.HeightLevel == er.HeightLevel &&
                board.InspectPlacement(new SpatialPose(cell), Occupied()) == "")
                .Select(cell => new { cell, path = board.QueryMovementPath(Actor.Pose, cell, Actor.ActionPointsRemainingUnits,
                    Actor.MovementEfficiencyMilliUnits, Occupied()) })
                .Where(p => p.cell == Actor.Position || p.path.Steps.Count > 0).OrderBy(p => p.path.ActionPointCostUnits).First();
            if (adjacent.cell != Actor.Position)
            { encounter.RequestMove(Earth, adjacent.cell.Q, adjacent.cell.R); yield return Ready(); }
            yield return Press("flight_takeoff"); yield return Press("confirm"); yield return Ready();
            var attack = session.AttackProfiles.Single(p => p.Id == enemy.BasicAttackProfileId);
            Assert.IsTrue(board.QueryContact(enemy.Pose, Actor.Pose, attack.MinimumRange, attack.MaximumRange, attack.SpatialPolicy).IsInRange);
            yield return Press("flight_ascend"); yield return Press("confirm"); yield return Ready();
            Assert.AreEqual(SpatialQueryReasons.VerticalReachExceeded,
                board.QueryContact(enemy.Pose, Actor.Pose, attack.MinimumRange, attack.MaximumRange, attack.SpatialPolicy).Reason);
            Record("same-height enemy: low reachable, high rejected by vertical reach 1");
            yield return Press("flight_descend"); yield return Press("confirm"); yield return Ready();
            int health = Actor.CurrentHealth, maintenance = session.Flight.Find(Earth).MaintenancePayments;
            yield return EndTurn();
            Assert.Less(Actor.CurrentHealth, health, "Ground enemy must actually hit the low-air player.");
            Assert.AreEqual(maintenance + 1, session.Flight.Find(Earth).MaintenancePayments);
            Assert.AreEqual(1, Actor.AltitudeBand); AssertFlightVisual(1);
            Record("enemy legally hit low-air player; own start paid one maintenance"); yield return Capture("05-anti-air-hit");
            Assert.AreEqual(saved, JsonUtility.ToJson(CharacterRecord.Capture(GameBootstrap.RequireRuntime().Player.Capture())));
            var old = session;
            yield return Click(GameObject.Find("ReturnButton").GetComponent<Button>());
            yield return Click(GameObject.Find("ConfirmWithdrawalButton").GetComponent<Button>());
            yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.IsTrue(old.Flight.States.All(s => !s.Active && !s.FailedLanding));
            Assert.IsTrue(old.Combatants.All.All(a => a.AltitudeBand == 0));
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild")); yield return SceneReady(GameplaySceneNames.Adventure);
            yield return Click(GameObject.Find("JindanRehearsalButton").GetComponent<Button>());
            view = Find<JindanDemoView>(); yield return Press("stage1"); yield return Press("enter");
            encounter = Find<EncounterCoordinator>(); session = encounter.CurrentSession; surface = Find<CombatBattlefieldSurface>(); yield return Ready();
            Assert.AreEqual(1000000, Actor.ActionPointsRemainingUnits); Assert.AreEqual(6000, Actor.CurrentSpirit);
            Assert.AreEqual(0, Actor.AltitudeBand); Assert.AreEqual(0, session.Flight.Find(Earth).MaintenancePayments);
            Record("withdrawal and fresh re-entry clear flight, source ledger and resources");
            File.WriteAllText(Path.Combine(output, "pending.json"), "{\"stage\":\"complete\"}");
        }

        private IEnumerator Action(bool native, string stage, string button, Func<bool> condition)
        {
            if (native) yield return NativeStep(stage, "展开飞行面板，点击" + button + "，再确认施放。", condition);
            else { yield return Press(button); yield return Press("confirm"); }
            yield return Ready(); Assert.IsTrue(condition()); Record(stage + " confirmed");
        }
        private void AssertFlightVisual(int band)
        {
            var provider = Find<Static3DCombatUnitPresentationProvider>();
            Assert.IsTrue(provider.TryGetPresentedUnit(Earth, out var root));
            float offset = root.transform.position.y - surface.Center(new CombatUnitPresentationHex(Actor.Position.Q, Actor.Position.R)).y;
            if (band > 0) Assert.That(offset, Is.EqualTo(.75f * band).Within(.01f));
            var ring = root.GetComponentsInChildren<LineRenderer>(true).Single(l => l.name == "FlightGroundProjection");
            var tether = root.GetComponentsInChildren<LineRenderer>(true).Single(l => l.name == "FlightAltitudeLine");
            Assert.AreEqual(band > 0, ring.enabled); Assert.AreEqual(band > 0, tether.enabled);
            Assert.IsTrue(ring.sharedMaterial.HasProperty("_BaseColor"), "Flight lines require the line material, not the swept unit overlay.");
            if (band > 0) Assert.Greater(tether.GetPosition(1).y, tether.GetPosition(0).y);
            Record("visual band=" + band + " root offset=" + offset + " projection=" + ring.enabled);
        }
        private IEnumerator Setup(bool native)
        {
            evidence.Clear(); output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/d6/" + (native ? "native" : TestContext.CurrentContext.Test.Name)));
            Directory.CreateDirectory(output); oldBackground = InputSystem.settings.backgroundBehavior;
            oldInput = InputSystem.settings.editorInputBehaviorInPlayMode; configured = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            EnsureMouse(); GuanzhongCombatEquipmentPlayModeTests.SetGameSize(1366, 768);
            var stale = Object.FindFirstObjectByType<GameBootstrap>(); if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            saves = Path.Combine(Application.temporaryCachePath, "D6-" + Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saves));
            var creation = Find<CharacterCreationController>(); creation.Open(); Assert.IsTrue(creation.SelectOrigin("origin_minor_clan")); creation.Submit("d6", "飞行验证");
            yield return SceneReady(GameplaySceneNames.World); Assert.IsTrue(Find<WorldMapController>().SelectNode("guanzhong_hub"));
            Find<WorldMapController>().EnterSelectedLocation(); yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild")); yield return SceneReady(GameplaySceneNames.Adventure);
        }
        private IEnumerator NativeStep(string stage, string instruction, Func<bool> condition)
        {
            Assert.IsNull(mouse); int clicks = 0;
            for (float end = Time.realtimeSinceStartup + 600, update = 0; Time.realtimeSinceStartup < end;)
            {
                if (Time.realtimeSinceStartup >= update)
                {
                    File.WriteAllText(Path.Combine(output, "pending.json"), JsonUtility.ToJson(new Pending { stage = stage, instruction = instruction,
                        buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                            .Where(b => b.interactable).Select(b => new ButtonPoint { name = b.name, position = Center(b) }).ToArray(),
                        cell = Point(new HexCoord(4, 5)), width = Screen.width, height = Screen.height }, true));
                    update = Time.realtimeSinceStartup + 1;
                }
                yield return null;
                if (Mouse.current?.native == true && Mouse.current.leftButton.wasPressedThisFrame) { clicks++; Record(stage + " native=" + Mouse.current.position.ReadValue()); }
                if (condition()) break;
            }
            Assert.IsTrue(condition(), stage + " timed out"); Assert.Greater(clicks, 0);
        }
        private IEnumerator EndTurn()
        {
            long turn = session.TurnSequence;
            yield return Click(Find<CombatActionBarView>().WaitButton); yield return Click(Find<CombatActionPreviewView>().ConfirmButton);
            for (float end = Time.realtimeSinceStartup + 60; session.TurnSequence == turn && Time.realtimeSinceStartup < end;) yield return null;
            Assert.Greater(session.TurnSequence, turn); yield return Ready();
        }
        private IEnumerator Ready()
        {
            for (float end = Time.realtimeSinceStartup + 60; Time.realtimeSinceStartup < end &&
                (!encounter.AcceptsPlayerCommand || session.CurrentActorId != Earth);) yield return null;
            Assert.IsTrue(encounter.AcceptsPlayerCommand); Assert.AreEqual(Earth, session.CurrentActorId); yield return null;
        }
        private IEnumerator Press(string key) => Click(view.GetButton(key));
        private HexCoord[] Occupied() => session.Combatants.All.Where(a => a.IsAlive && a.Id != Earth).SelectMany(a => a.Pose.OccupiedCells).ToArray();
        private IEnumerator Click(Button button)
        {
            Canvas.ForceUpdateCanvases(); Assert.IsNotNull(button); Assert.IsTrue(button.interactable && button.gameObject.activeInHierarchy, button.name);
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = Center(button) }, hits);
            Assert.IsTrue(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button, button.name + " covered");
            yield return Click(Center(button));
        }
        private IEnumerator Click(Vector2 point)
        {
            Assert.IsNotNull(mouse); InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left)); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
        }
        private static Vector2 Center(Button b) { var r = (RectTransform)b.transform; return RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(r.rect.center)); }
        private Vector2 Point(HexCoord cell) => Camera.main.WorldToScreenPoint(surface.Center(new CombatUnitPresentationHex(cell.Q, cell.R)));
        private void EnsureMouse() { if (mouse == null) mouse = InputSystem.AddDevice<Mouse>(); }
        private void RemoveMouse() { if (mouse != null) InputSystem.RemoveDevice(mouse); mouse = null; }
        private void Record(string text)
        { evidence.Add(text + " | " + (session == null ? "" : "tick=" + session.TurnScheduler.CurrentTick + " AP=" + Actor.ActionPointsRemainingUnits + " MP=" + Actor.CurrentSpirit + " HP=" + Actor.CurrentHealth)); File.WriteAllLines(Path.Combine(output, "proof.txt"), evidence); }
        private IEnumerator Capture(string name) { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png")); yield return null; }
        private static IEnumerator SceneReady(string name) { for (int i = 0; i < 300 && SceneManager.GetActiveScene().name != name; i++) yield return null; yield return null; yield return null; Assert.AreEqual(name, SceneManager.GetActiveScene().name); }
        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>() ?? throw new Exception(typeof(T).Name + " missing");
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(owner, value);
        [Serializable] private sealed class Pending { public string stage, instruction; public int width, height; public Vector2 cell; public ButtonPoint[] buttons; }
        [Serializable] private sealed class ButtonPoint { public string name; public Vector2 position; }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            RemoveMouse(); if (configured) { InputSystem.settings.backgroundBehavior = oldBackground; InputSystem.settings.editorInputBehaviorInPlayMode = oldInput; }
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>(); if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            if (saves != null && Directory.Exists(saves)) Directory.Delete(saves, true);
        }
    }
}
