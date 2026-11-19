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
    public sealed class D9SpacePlayModeTests
    {
        private const string Player = "demo_20261024_space_yang", Enemy = "demo_20261024_seal_yin";
        private static readonly HexCoord GateFrom = new HexCoord(8, 12), GateTo = new HexCoord(9, 12);
        private Mouse mouse;
        private string saves, output;
        private EncounterCoordinator encounter;
        private CombatSession session;
        private CombatBattlefieldSurface surface;
        private JindanDemoView view;
        private InputSettings.BackgroundBehavior oldBackground;
        private InputSettings.EditorInputBehaviorInPlayMode oldInput;
        private bool configured;
        private readonly List<string> evidence = new();
        private CombatantSnapshot Actor(string id) { session.Combatants.TryGet(id, out var actor); return actor; }
        private CombatJindanSeatState SealSeat => session.Jindan.Seats.Single(seat => seat.EffectId == "SPACE_SEAL");

        [UnityTest, Timeout(600000)]
        public IEnumerator ProductionScaleChangesReachAndInvalidatesOldQuotes()
        {
            yield return Setup(); yield return Enter();
            yield return Move(new HexCoord(5, 14));
            var target = Actor(Enemy); Assert.AreEqual(new HexCoord(7, 14), target.Position);
            var attack = new CombatCommand(CombatCommandKind.BasicAttack, Player, Enemy, Actor(Player).BasicAttackProfileId);
            Assert.IsFalse(encounter.PreviewJindan(attack).IsLegal);
            var move = new CombatCommand(CombatCommandKind.Move, Player, destination: new HexCoord(6, 14));
            var emptyAttack = new CombatCommand(CombatCommandKind.BasicAttack, Player,
                profileId: Actor(Player).BasicAttackProfileId, targetCell: new HexCoord(6, 14));
            var oldMove = encounter.PreviewJindan(move); var oldAttack = encounter.PreviewJindan(emptyAttack);
            Assert.IsTrue(oldMove.IsLegal); Assert.IsTrue(oldAttack.IsLegal);
            yield return Scale(false, new HexCoord(5, 14), new HexCoord(6, 14), new HexCoord(7, 14));
            int ap = Actor(Player).ActionPointsRemainingUnits, mp = Actor(Player).CurrentSpirit;
            Assert.IsFalse(encounter.ConfirmJindan(move, oldMove).Succeeded);
            Assert.IsFalse(encounter.ConfirmJindan(emptyAttack, oldAttack).Succeeded);
            Assert.AreEqual(ap, Actor(Player).ActionPointsRemainingUnits); Assert.AreEqual(mp, Actor(Player).CurrentSpirit);
            var quote = encounter.PreviewJindan(attack); Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
            Assert.AreEqual(2, session.SpatialQuery.QueryRange(Actor(Player).Pose, target.Pose, 1, 1).DistanceUnits);
            Record("compressed two topology edges to range1; old move/attack rejected without payment");
            int healthBefore = Actor(Enemy).CurrentHealth;
            yield return Click(Find<CombatActionBarView>().BasicAttackButton); yield return Click(Point(target.Position));
            Assert.AreEqual(healthBefore, Actor(Enemy).CurrentHealth);
            Assert.AreEqual(ap, Actor(Player).ActionPointsRemainingUnits);
            yield return Press("confirm");
            yield return Ready(); Assert.Less(Actor(Enemy).CurrentHealth, healthBefore);
            yield return Capture("01-compressed-real-attack");
            yield return Press("close_tab"); yield return Press("close_effect"); yield return Press("confirm"); yield return Ready();
            Assert.IsFalse(encounter.PreviewJindan(attack).IsLegal);
            // A fresh entry isolates the expansion witness and avoids refreshing the same source's CD.
            yield return Withdraw(); yield return EnterAdventure(); yield return Enter();
            yield return Move(new HexCoord(6, 14));
            attack = new CombatCommand(CombatCommandKind.BasicAttack, Player, Enemy, Actor(Player).BasicAttackProfileId);
            Assert.IsTrue(encounter.PreviewJindan(attack).IsLegal);
            yield return Scale(true, new HexCoord(6, 14), new HexCoord(7, 14));
            Assert.IsFalse(encounter.PreviewJindan(attack).IsLegal);
            Record("expanded adjacent edge to range2; adjacent basic attack unavailable"); yield return Capture("02-expanded-adjacent");
            yield return Press("cancel_targeting");
            yield return Press("toggle_panel");
            var details = GameObject.Find("CombatDetails").GetComponent<Text>();
            Assert.IsTrue(details.text.Contains("扩张2") && details.text.Contains("内100/100"));
            var scroll = details.GetComponentInParent<ScrollRect>();
            Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 0; yield return null;
            Assert.Less(scroll.verticalNormalizedPosition, .01f); yield return Capture("02b-scale-ledger-scrolled");
            yield return Press("toggle_panel");
            yield return Press("close_tab"); yield return Press("close_effect"); yield return Press("confirm"); yield return Ready();
            Assert.IsTrue(encounter.PreviewJindan(attack).IsLegal); Record("closing restores adjacent attack");
            yield return Capture("03-restored");
        }

        [UnityTest, Timeout(600000)] public IEnumerator ProductionGateDetourAndTwoLayerBreak() => Gate(false);
        [UnityTest, Timeout(900000)] public IEnumerator NativeGateFirstStrikeAndCleanup() => Gate(true);

        private IEnumerator Gate(bool native)
        {
            yield return Setup(); yield return Enter();
            string save = JsonUtility.ToJson(CharacterRecord.Capture(GameBootstrap.RequireRuntime().Player.Capture()));
            var seat = SealSeat; var seal = seat.ActiveSeal;
            Assert.IsNotNull(seal); Assert.AreEqual(400, seat.TotalManaSpent); Assert.AreEqual(250000, seat.TotalActionPointSpent);
            Assert.AreEqual(session.TurnScheduler.CurrentTick, seal.CreatedTick);
            yield return Move(GateFrom);
            var direct = encounter.PreviewJindan(new CombatCommand(CombatCommandKind.Move, Player, destination: GateTo));
            Assert.IsTrue(direct.IsLegal); Assert.Greater(direct.MovementSteps.Count, 1);
            Assert.IsFalse(direct.MovementSteps.Any(step => step.From.Anchor == GateFrom && step.To.Anchor == GateTo));
            Record("enemy paid deployment; gate arrow and layers shown; ground route detours " + direct.MovementSteps.Count + " steps");
            Assert.IsNotNull(GameObject.Find("JindanSealDirection"));
            yield return Capture("01-gate-intact");
            if (!native)
            {
                yield return Press("flight_tab"); yield return Press("flight_takeoff"); yield return Press("confirm"); yield return Ready();
                var flying = encounter.PreviewJindan(new CombatCommand(CombatCommandKind.Move, Player, destination: GateTo));
                Assert.IsTrue(flying.IsLegal); Assert.Greater(flying.MovementSteps.Count, 1);
                Assert.IsEmpty(CombatForceMovement.Query(session, Player, 0, 1, 2).Path);
                Record("flight still detours; forced flight crossing stopped"); yield return Capture("02-flight-sealed");
                yield return Press("flight_land"); yield return Press("confirm"); yield return Ready();
                yield return Move(GateTo); Assert.AreEqual(GateTo, Actor(Player).Position);
                yield return Move(GateFrom); Record("walked around gate then left in allowed reverse direction");
                yield return Press("scale_tab");
            }
            yield return Press("toggle_panel");
            // Use the existing camera pan surface so the right-hand attack preview does not cover the gate cell.
            Camera.main.transform.position = surface.Center(new CombatUnitPresentationHex(GateTo.Q, GateTo.R)) - Camera.main.transform.forward * 20;
            yield return null;
            int initialAp = Actor(Player).ActionPointsRemainingUnits, initialMp = Actor(Player).CurrentSpirit;
            if (native)
            {
                RemoveMouse();
                yield return NativeStrike(() => seal.Support.Durability == 170);
                EnsureMouse(); yield return Ready();
            }
            else yield return Strike();
            Assert.AreEqual(170, seal.Support.Durability);
            if (native) yield return Capture("02-native-first-strike");
            for (int i = 1; i < 7; i++)
            {
                yield return Strike();
                Record("strike " + (i + 1) + " durability=" + seal.Support.Durability + " expiry=" + seal.ExpiresTick);
                if (i == 3)
                {
                    Assert.AreEqual(80, seal.Support.Durability); Assert.AreEqual(seal.CreatedTick + 2, seal.ExpiresTick);
                    Assert.IsNotNull(seat.ActiveSeal); Assert.IsEmpty(CombatForceMovement.Query(session, Player, 0, 1, 2).Path);
                    yield return Capture("03-outer-broken-still-sealed");
                }
            }
            Assert.IsNull(seat.ActiveSeal); Assert.AreEqual(0, seal.Support.Durability);
            var opened = encounter.PreviewJindan(new CombatCommand(CombatCommandKind.Move, Player, destination: GateTo));
            Assert.IsTrue(opened.IsLegal); Assert.AreEqual(1, opened.MovementSteps.Count);
            Record("7 strikes: AP=" + initialAp + "->" + Actor(Player).ActionPointsRemainingUnits + " MP=" + initialMp + "->" + Actor(Player).CurrentSpirit);
            yield return Move(GateTo); Record("walked through opened edge"); yield return Capture("04-gate-open");
            Assert.AreEqual(save, JsonUtility.ToJson(CharacterRecord.Capture(GameBootstrap.RequireRuntime().Player.Capture())));
            yield return Withdraw(); Assert.IsNull(seat.ActiveSeal); Assert.IsEmpty(session.SceneObjects.Board.MetricOverrides);
            yield return EnterAdventure(); yield return Enter(); Assert.AreEqual(200, SealSeat.ActiveSeal.Support.Durability);
            Assert.AreEqual(1, SealSeat.SuccessfulDeployments); Record("fresh reentry pays a fresh deployment; world character unchanged");
            yield return Capture("05-reentry");
        }

        private IEnumerator Strike()
        {
            yield return Click(Find<CombatActionBarView>().ArtButton);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = Point(GateTo) }); yield return null; yield return null;
            var preview = Find<CombatCommandInput>().Preview;
            Record("strike preview cell=" + preview?.Candidate.TargetCell + " legal=" + preview?.CanConfirm + " reason=" + preview?.Reason);
            if (preview?.CanConfirm != true) yield return Capture("strike-unavailable");
            Assert.IsTrue(preview?.CanConfirm == true, preview?.Reason);
            yield return Click(Point(GateTo));
            yield return Press("confirm");
            yield return Ready();
        }
        private IEnumerator Scale(bool expand, params HexCoord[] path)
        {
            yield return Press("scale_tab"); yield return Press(expand ? "expand" : "compress"); yield return Press("pick_edges");
            foreach (var cell in path) yield return Click(Point(cell));
            Assert.IsTrue(view.GetButton("confirm").interactable);
            yield return Press("confirm"); yield return Ready();
        }
        private IEnumerator Move(HexCoord cell)
        {
            var quote = encounter.PreviewJindan(new CombatCommand(CombatCommandKind.Move, Player, destination: cell));
            Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
            encounter.RequestMove(Player, cell.Q, cell.R); yield return Ready(); Assert.AreEqual(cell, Actor(Player).Position);
        }
        private IEnumerator Enter()
        {
            yield return Click(GameObject.Find("JindanRehearsalButton").GetComponent<Button>());
            view = Find<JindanDemoView>(); yield return Press("stage3"); yield return Press("enter");
            encounter = Find<EncounterCoordinator>(); session = encounter.CurrentSession; surface = Find<CombatBattlefieldSurface>();
            yield return Ready(); Assert.AreEqual(Player, session.CurrentActorId);
        }
        private IEnumerator Withdraw()
        {
            yield return Click(GameObject.Find("ReturnButton").GetComponent<Button>());
            yield return Click(GameObject.Find("ConfirmWithdrawalButton").GetComponent<Button>());
            yield return SceneReady(GameplaySceneNames.Settlement);
        }
        private IEnumerator EnterAdventure()
        { Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild")); yield return SceneReady(GameplaySceneNames.Adventure); }
        private IEnumerator Setup()
        {
            evidence.Clear();
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/d9/" + TestContext.CurrentContext.Test.Name));
            Directory.CreateDirectory(output); oldBackground = InputSystem.settings.backgroundBehavior;
            oldInput = InputSystem.settings.editorInputBehaviorInPlayMode; configured = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            EnsureMouse(); GuanzhongCombatEquipmentPlayModeTests.SetGameSize(1366, 768);
            var stale = Object.FindFirstObjectByType<GameBootstrap>(); if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            saves = Path.Combine(Application.temporaryCachePath, "D9-" + Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saves));
            var creation = Find<CharacterCreationController>(); creation.Open(); Assert.IsTrue(creation.SelectOrigin("origin_minor_clan")); creation.Submit("d9", "空间验证");
            yield return SceneReady(GameplaySceneNames.World); Assert.IsTrue(Find<WorldMapController>().SelectNode("guanzhong_hub"));
            Find<WorldMapController>().EnterSelectedLocation(); yield return SceneReady(GameplaySceneNames.Settlement); yield return EnterAdventure();
        }
        private IEnumerator NativeStrike(Func<bool> done)
        {
            int clicks = 0;
            for (float end = Time.realtimeSinceStartup + 600, update = 0; Time.realtimeSinceStartup < end && !done();)
            {
                if (Time.realtimeSinceStartup >= update)
                {
                    File.WriteAllText(Path.Combine(output, "pending.json"), JsonUtility.ToJson(new Pending {
                        instruction = "点击贯石击，左键锁定支点格，再点击确认施行，核对耐久200→170", cell = Point(GateTo), width = Screen.width, height = Screen.height,
                        buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(b => b.interactable)
                            .Select(b => new ButtonPoint { name = b.name, position = Center(b) }).ToArray() }, true)); update = Time.realtimeSinceStartup + 1;
                }
                yield return null;
                if (Mouse.current?.native == true && Mouse.current.leftButton.wasPressedThisFrame) { clicks++; Record("native mouse " + Mouse.current.position.ReadValue()); }
            }
            Assert.IsTrue(done(), "Native strike timeout"); Assert.Greater(clicks, 0);
        }
        private IEnumerator Ready()
        { for (float end = Time.realtimeSinceStartup + 60; Time.realtimeSinceStartup < end && !encounter.AcceptsPlayerCommand;) yield return null;
            Assert.IsTrue(encounter.AcceptsPlayerCommand); yield return null; }
        private IEnumerator Press(string key) => Click(view.GetButton(key));
        private IEnumerator Click(Button button)
        {
            Canvas.ForceUpdateCanvases(); Assert.IsNotNull(button); Assert.IsTrue(button.interactable && button.gameObject.activeInHierarchy, button.name);
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = Center(button) }, hits);
            Assert.IsTrue(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button, button.name + " covered"); yield return Click(Center(button));
        }
        private IEnumerator Click(Vector2 point)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left)); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
        }
        private static Vector2 Center(Button b) { var r = (RectTransform)b.transform; return RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(r.rect.center)); }
        private Vector2 Point(HexCoord cell) => Camera.main.WorldToScreenPoint(surface.Center(new CombatUnitPresentationHex(cell.Q, cell.R)));
        private void EnsureMouse() { if (mouse == null) mouse = InputSystem.AddDevice<Mouse>(); }
        private void RemoveMouse() { if (mouse != null) InputSystem.RemoveDevice(mouse); mouse = null; }
        private void Record(string text) { evidence.Add(text + " | tick=" + session?.TurnScheduler.CurrentTick); File.WriteAllLines(Path.Combine(output, "proof.txt"), evidence); }
        private IEnumerator Capture(string name) { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png")); yield return null; }
        private static IEnumerator SceneReady(string name) { for (int i = 0; i < 300 && SceneManager.GetActiveScene().name != name; i++) yield return null; yield return null; yield return null; Assert.AreEqual(name, SceneManager.GetActiveScene().name); }
        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>() ?? throw new Exception(typeof(T).Name + " missing");
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(owner, value);
        [Serializable] private sealed class Pending { public string instruction; public int width, height; public Vector2 cell; public ButtonPoint[] buttons; }
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
