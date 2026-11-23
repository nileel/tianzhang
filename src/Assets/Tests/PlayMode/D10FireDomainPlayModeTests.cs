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
    public sealed class D10FireDomainPlayModeTests
    {
        private const string FireId = "demo_20261024_fire", EarthId = "demo_20261024_earth", WaterId = "demo_20261024_water";
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
        private CombatJindanSeatState FireSeat => session.Jindan.Seats.Single(seat => seat.FireDomain != null);

        [UnityTest, Timeout(600000)]
        public IEnumerator PlayerDeploysMaintainsClosesAndReentersWithoutWorldChanges()
        {
            yield return Setup(); yield return Enter(true);
            string save = JsonUtility.ToJson(CharacterRecord.Capture(GameBootstrap.RequireRuntime().Player.Capture()));
            Assert.AreEqual(FireId, session.CurrentActorId); Assert.AreEqual(CombatTeam.Player, Actor(FireId).Team);
            Assert.IsNull(FireSeat.FireDomain.Main);
            CheckFireView();
            yield return Capture("00-defender-public-center");
            yield return Move(new HexCoord(7, 11)); yield return Press("fire_tab");
            int ap = Actor(FireId).ActionPointsRemainingUnits, mp = Actor(FireId).CurrentSpirit;
            yield return Press("fire_preview"); Assert.IsFalse(view.GetButton("confirm").interactable);
            var center = FireSeat.FireDomain.Support.Cell;
            yield return D12PlayerStartPlayModeTests.LockAbilityCell(new CombatUnitPresentationHex(center.Q, center.R));
            Assert.IsTrue(view.GetButton("confirm").interactable);
            yield return Capture("01-eligible-surfaces-preview"); yield return Press("cancel_targeting");
            Assert.AreEqual(ap, Actor(FireId).ActionPointsRemainingUnits); Assert.AreEqual(mp, Actor(FireId).CurrentSpirit);
            yield return Press("fire_preview");
            yield return D12PlayerStartPlayModeTests.LockAbilityCell(new CombatUnitPresentationHex(center.Q, center.R));
            yield return Press("confirm"); yield return Ready();
            Assert.AreEqual(ap - 500000, Actor(FireId).ActionPointsRemainingUnits); Assert.AreEqual(mp - 1000, Actor(FireId).CurrentSpirit);
            Assert.AreEqual(6, FireSeat.FireDomain.ReservedFuel); Record("player paid 500AP/1000MP, center600, free9/reserved6");
            CheckFireView(); Assert.AreEqual(0, CombatFireDomainRules.BurningCount(session, FireSeat));
            yield return Capture("02-deployed");
            yield return End(); Assert.AreEqual(0, FireSeat.MaintenancePayments);
            yield return End(); Assert.AreEqual(1, FireSeat.MaintenancePayments);
            Assert.Greater(CombatFireDomainRules.BurningCount(session, FireSeat), 0);
            Record("one later own-turn-end maintenance 200MP; source burning=" + CombatFireDomainRules.BurningCount(session, FireSeat));
            CheckFireView(); yield return Capture("03-maintained-fire");
            yield return Press("close_tab"); yield return Press("fire_close"); yield return Press("confirm"); yield return Ready();
            Assert.IsNull(FireSeat.FireDomain.Main); Assert.AreEqual(2, FireSeat.FireDomain.Tails.Single().RemainingCycles);
            Record("paid 25AP close; finite two-cycle tail"); CheckFireView(); yield return Capture("04-paid-tail");
            Assert.IsFalse(view.GetButton("close_tab").interactable);
            Record("withdraw while paid tail is active; natural expiry covered by own-turn rule tests");
            var previous = FireSeat; yield return Withdraw(); Assert.IsNull(previous.FireDomain.Main); Assert.IsEmpty(previous.FireDomain.Tails);
            Assert.AreEqual(save, JsonUtility.ToJson(CharacterRecord.Capture(GameBootstrap.RequireRuntime().Player.Capture())));
            yield return EnterAdventure(); yield return Enter(true);
            Assert.AreEqual(15, FireSeat.FireDomain.RemainingFuel); Assert.AreEqual(0, FireSeat.SuccessfulDeployments);
            Assert.AreEqual(600, FireSeat.FireDomain.Support.Durability); Record("fresh entry resets field node and leaves world identity unchanged");
            CheckFireView(); yield return Capture("06-clean-reentry");
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator PlayerCountersPaidEnemyWithTideAndCenterBreakWhileGravityCoexists()
        {
            yield return Setup(); yield return Enter(false);
            for (int i = 0; i < 8 && FireSeat.MaintenancePayments == 0; i++) yield return End();
            Assert.IsNotNull(FireSeat.FireDomain.Main); Assert.Greater(FireSeat.MaintenancePayments, 0);
            Assert.AreEqual(1, FireSeat.SuccessfulDeployments); Assert.GreaterOrEqual(FireSeat.TotalManaSpent, 1200);
            Record("NPC moved into range, deployed and maintained through shared ledger");
            yield return UntilActor(EarthId);
            var earthSeat = session.Jindan.Seats.Single(value => value.Definition.ActorId == EarthId);
            yield return Execute(new CombatCommand(CombatCommandKind.JindanAbility, EarthId, profileId: "EARTH_GRAVITY",
                jindan: new CombatJindanRequest(earthSeat.Definition.SeatId, earthMode: CombatEarthMode.Anchor,
                    earthTargetId: EarthId, selectedTargetCell: Actor(EarthId).Position)));
            Assert.IsNotNull(earthSeat.ActiveEarth); Assert.IsNotNull(FireSeat.FireDomain.Main);
            Record("earth anchoring and fire temperature coexist, no conflict/QTE");
            yield return Capture("01-coexisting-rules");
            yield return Execute(new CombatCommand(CombatCommandKind.CloseJindan, EarthId, profileId: "EARTH_GRAVITY",
                jindan: new CombatJindanRequest(earthSeat.Definition.SeatId)));
            yield return UntilActor(EarthId);
            yield return Execute(new CombatCommand(CombatCommandKind.Flight, EarthId,
                profileId: CombatFlight.ProfileId, flightAction: CombatFlightAction.Takeoff));
            yield return Move(new HexCoord(7, 11));
            yield return Execute(new CombatCommand(CombatCommandKind.Flight, EarthId,
                profileId: CombatFlight.ProfileId, flightAction: CombatFlightAction.Land));
            Record("legal flight approach and landing at (7,11); earth AP=" + Actor(EarthId).ActionPointsRemainingUnits);
            Assert.IsNotNull(FireSeat.FireDomain.Main);
            var old = encounter.PreviewJindan(new CombatCommand(CombatCommandKind.Guard, EarthId));
            for (int i = 0; i < 2; i++)
            {
                yield return Execute(new CombatCommand(CombatCommandKind.Art, EarthId,
                    profileId: "demo_20261026_polei", targetCell: new HexCoord(7, 12)));
                CheckFireView();
                if (i == 0)
                {
                    Assert.AreEqual(225, FireSeat.FireDomain.Support.Durability);
                    yield return Capture("01-center-damaged-still-warm");
                }
            }
            Assert.IsTrue(FireSeat.FireDomain.Support.IsDestroyed); Assert.IsNull(FireSeat.FireDomain.Main);
            Assert.Greater(FireSeat.FireDomain.Tails.Count, 0);
            Assert.IsFalse(encounter.ConfirmJindan(new CombatCommand(CombatCommandKind.Guard, EarthId), old).Succeeded);
            Assert.AreEqual(3, session.Jindan.Seats.Count); Record("two legal polei attacks destroy600 center; seat identity remains and old quote rejects");
            yield return Capture("02-center-broken");
            yield return UntilActor(WaterId); yield return Move(new HexCoord(8, 7));
            var waterSeat = session.Jindan.Seats.Single(value => value.Definition.ActorId == WaterId);
            var burning = new HexCoord(9, 8);
            Assert.AreEqual(CombatSurfaceKind.Burning, session.Surfaces.Get(burning).Kind);
            yield return Execute(new CombatCommand(CombatCommandKind.JindanAbility, WaterId, profileId: "WATER_TIDE",
                jindan: new CombatJindanRequest(waterSeat.Definition.SeatId, tideAction: CombatTideAction.Rise,
                    tideSource: new HexCoord(9, 7), createTideEye: true)));
            Assert.AreEqual(CombatSurfaceKind.Wet, session.Surfaces.Get(burning).Kind);
            Assert.AreEqual(0, FireSeat.FireDomain.Support.Durability);
            CheckFireView();
            Record("paid tide eye/rise extinguished (9,8); paid tail extinguished after center break"); yield return Capture("03-tide-extinguished");
            var previous = FireSeat; yield return Withdraw(); Assert.IsEmpty(previous.FireDomain.Tails);
            yield return EnterAdventure(); yield return Enter(false); Assert.AreEqual(600, FireSeat.FireDomain.Support.Durability);
            Record("counter-side withdrawal and reentry reset node"); CheckFireView(); yield return Capture("04-counter-reentry");
        }

        private void CheckFireView()
        {
            var fire = FireSeat.FireDomain;
            var root = Find<JindanFireDomainView>().transform;
            var center = root.Find("FireCenter(Clone)");
            Assert.IsNotNull(center);
            var cell = fire.Support.Cell;
            Assert.That(Vector3.Distance(surface.Center(new CombatUnitPresentationHex(cell.Q, cell.R)), center.position),
                Is.LessThan(.001f), "The public stele must stand on the actual support cell.");
            Assert.AreEqual(!fire.Support.IsDestroyed, center.gameObject.activeInHierarchy);
            Assert.AreEqual(fire.Main != null, center.Find("Intact/Glow/CenterSpiritFlame").gameObject.activeInHierarchy);
            var warmth = root.Find("FireDomainWarmth");
            Assert.AreEqual(fire.Main != null, warmth != null && warmth.gameObject.activeInHierarchy);
            if (fire.Main != null) Assert.AreEqual(surface.Cells.Count, warmth.childCount);

            var tongues = Find<CombatSurfaceView>().GetComponentsInChildren<MeshRenderer>()
                .Where(renderer => renderer.gameObject.name == "LowTongues").ToArray();
            var burning = session.Surfaces.Cells.Where(slot => slot.Kind == CombatSurfaceKind.Burning).ToArray();
            Assert.AreEqual(burning.Length, tongues.Length, "Exactly one low fire per burning surface.");
            var tail = fire.Tails.SelectMany(value => value.Cells).ToHashSet();
            foreach (var slot in burning)
            {
                var ground = surface.Center(new CombatUnitPresentationHex(slot.Cell.Q, slot.Cell.R));
                var flame = tongues.Single(renderer => Vector3.Distance(renderer.transform.position, ground) < .1f);
                var block = new MaterialPropertyBlock(); flame.GetPropertyBlock(block);
                float strength = block.HasFloat("_Strength") ? block.GetFloat("_Strength") : flame.sharedMaterial.GetFloat("_Strength");
                Assert.AreEqual(tail.Contains(slot.Cell) ? .65f : 1f, strength, .001f);
            }
            Assert.AreEqual(tail.Count, root.GetComponentsInChildren<ParticleSystem>()
                .Count(particle => particle.transform.parent.name == "FiniteEmbers(Clone)"));
            var paidMarks = Object.FindObjectsByType<Text>(FindObjectsSortMode.None)
                .Where(label => label.name == "PublicFieldLabel" && label.text == "余火").ToArray();
            Canvas.ForceUpdateCanvases();
            foreach (Text label in paidMarks)
            {
                var corners = new Vector3[4];
                label.rectTransform.GetWorldCorners(corners);
                Record($"ember label '{label.text}' screen {corners[0]} to {corners[2]}, " +
                    $"rect {label.rectTransform.rect}, preferred {label.preferredWidth}x{label.preferredHeight}, " +
                    $"vertices {label.cachedTextGenerator.vertexCount}, canvas {label.canvas.renderMode} scale {label.canvas.scaleFactor}");
                Assert.LessOrEqual(label.preferredHeight, label.rectTransform.rect.height, evidence.Last());
            }
            foreach (var cellWithTail in tail)
            {
                Vector3 point = Camera.main.WorldToViewportPoint(surface.Center(new CombatUnitPresentationHex(cellWithTail.Q, cellWithTail.R)));
                if (point.z > 0 && point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1)
                    Assert.IsTrue(paidMarks.Any(mark => mark.text == "余火" && Vector2.Distance(mark.rectTransform.anchorMin, (Vector2)point) < .001f));
            }
            if (tail.Count == 0) Assert.IsEmpty(paidMarks);
            Record($"public center={cell}, durability={fire.Support.Durability}, main={fire.Main != null}, " +
                $"burning={burning.Length}, paid tails={string.Join(",", tail)}, camera size={Camera.main.orthographicSize}");
        }

        private IEnumerator Enter(bool controlFire)
        {
            Find<AdventureSceneInstaller>().OpenJindanPreparation();
            yield return null;
            view = Find<JindanDemoView>(); yield return Press("stage2");
            if (controlFire) yield return Press("fire_side");
            yield return Press("enter"); encounter = Find<EncounterCoordinator>(); session = encounter.CurrentSession;
            surface = Find<CombatBattlefieldSurface>(); yield return Ready();
        }
        private IEnumerator Execute(CombatCommand command)
        {
            var quote = encounter.PreviewJindan(command); Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
            var result = encounter.ConfirmJindan(command, quote); Assert.IsTrue(result.Succeeded, result.RejectionReason);
            yield return Ready();
        }
        private IEnumerator End()
        {
            encounter.RequestEndTurn(session.CurrentActorId); yield return null; yield return Ready();
        }
        private IEnumerator UntilActor(string id)
        {
            for (int i = 0; i < 6 && session.CurrentActorId != id; i++) yield return End();
            Assert.AreEqual(id, session.CurrentActorId);
        }
        private IEnumerator Move(HexCoord cell)
        {
            string actor = session.CurrentActorId;
            var quote = encounter.PreviewJindan(new CombatCommand(CombatCommandKind.Move, actor, destination: cell));
            Assert.IsTrue(quote.IsLegal, quote.RejectionReason + " actor=" + actor + " from=" + Actor(actor).Position +
                " to=" + cell + " AP=" + Actor(actor).ActionPointsRemainingUnits);
            encounter.RequestMove(actor, cell.Q, cell.R); yield return Ready(); Assert.AreEqual(cell, Actor(actor).Position);
            var camera = Camera.main;
            camera.transform.position = surface.Center(new CombatUnitPresentationHex(cell.Q,cell.R)) - camera.transform.forward * 20;
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
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/d10/" + TestContext.CurrentContext.Test.Name));
            Directory.CreateDirectory(output); oldBackground = InputSystem.settings.backgroundBehavior;
            oldInput = InputSystem.settings.editorInputBehaviorInPlayMode; configured = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            EnsureMouse(); GuanzhongCombatEquipmentPlayModeTests.SetGameSize(1366, 768);
            var stale = Object.FindFirstObjectByType<GameBootstrap>(); if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            saves = Path.Combine(Application.temporaryCachePath, "D10-" + Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saves));
            var creation = Find<CharacterCreationController>(); creation.Open(); Assert.IsTrue(creation.SelectOrigin("origin_minor_clan")); creation.Submit("d10", "领域验证");
            yield return SceneReady(GameplaySceneNames.World); Assert.IsTrue(Find<WorldMapController>().SelectNode("guanzhong_hub"));
            Find<WorldMapController>().EnterSelectedLocation(); yield return SceneReady(GameplaySceneNames.Settlement); yield return EnterAdventure();
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
        [UnityTearDown] public IEnumerator Cleanup()
        {
            RemoveMouse(); if (configured) { InputSystem.settings.backgroundBehavior = oldBackground; InputSystem.settings.editorInputBehaviorInPlayMode = oldInput; }
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>(); if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            if (saves != null && Directory.Exists(saves)) Directory.Delete(saves, true);
        }
    }
}
