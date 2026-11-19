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
    public sealed class D8WaterTidePlayModeTests
    {
        private const string Earth = "demo_20261024_earth", Water = "demo_20261024_water";
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
        private CombatantSnapshot Actor(string id) { session.Combatants.TryGet(id, out var value); return value; }
        private CombatJindanSeatState Seat(string id) => session.Jindan.Seats.Single(value => value.Definition.ActorId == id);

        [UnityTest, Timeout(600000)] public IEnumerator ProductionTideMovesBurningSurfaceAndReturnsToShore() => TideLoop(false, false);
        [UnityTest, Timeout(600000)] public IEnumerator ProductionAnchorBlocksPushButStillTakesWaterDamage() => TideLoop(true, false);
        [UnityTest, Timeout(1800000)] public IEnumerator NativeAnchorTideAndWithdrawal() => TideLoop(true, true);

        [UnityTest, Timeout(600000)]
        public IEnumerator ProductionTwoWallsIsolateNaturalWaterDuringEbb()
        {
            yield return Setup(false); yield return Enter(true);
            var source = new HexCoord(4,2); var isolated = new HexCoord(4,1); var third = new HexCoord(5,1);
            var request = new CombatJindanRequest(Seat(Water).Definition.SeatId,
                tideAction: CombatTideAction.Ebb, tideSource: source);
            var before = CombatTideRules.Preview(session, Actor(Water), Seat(Water), request);
            Assert.IsTrue(before.IsLegal, before.Reason);
            CollectionAssert.AreEquivalent(new[] { source, isolated, third }, before.Changes.Select(value => value.Cell));
            Record("before walls: ebb changes " + string.Join(";", before.Changes.Select(value => value.Cell)));
            yield return Press("earth_tab"); yield return Wall(isolated, third);
            yield return EndCurrent(); yield return EndCurrent(); Assert.AreEqual(Earth, session.CurrentActorId);
            yield return Press("earth_tab"); yield return Wall(source, isolated);
            Assert.AreEqual(2, Seat(Earth).ActiveEarth.Objects.Count);
            yield return EndCurrent(); Assert.AreEqual(Water, session.CurrentActorId);
            yield return Press("tide_tab"); yield return Press("tide_ebb"); yield return SelectCell(source); yield return Press("tide_preview");
            var after = encounter.PreviewJindan(new CombatCommand(CombatCommandKind.JindanAbility, Water, profileId:"WATER_TIDE", jindan:request));
            Assert.IsTrue(after.IsLegal, after.RejectionReason);
            CollectionAssert.AreEquivalent(new[] { source, third }, after.Tide.Changes.Select(value => value.Cell));
            Record("two actual walls: ebb changes " + string.Join(";", after.Tide.Changes.Select(value => value.Cell)));
            yield return Capture("01-walled-ebb-preview"); yield return Press("confirm"); yield return Ready();
            Assert.AreEqual(CombatSurfaceKind.ShallowWater, session.Surfaces.Get(isolated).Kind);
            Assert.AreEqual(CombatSurfaceKind.Wet, session.Surfaces.Get(source).Kind);
            Assert.AreEqual(CombatSurfaceKind.Wet, session.Surfaces.Get(third).Kind);
            Assert.IsTrue(Seat(Earth).ActiveEarth.Objects.All(value => value.Durability == 750));
            Record("isolated (4,1) remains shallow; source and (5,1) become wet; both walls retain750");
            yield return Capture("02-walled-ebb-result");
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator ProductionSecondRiseErodesPlantAndBreaksFuelChain()
        {
            yield return Setup(false); yield return Enter(); yield return EndCurrent();
            var source = new HexCoord(5,5); var fuelCell = new HexCoord(4,6);
            var plant = session.SceneObjects.Find("LowPlant_4_6"); Assert.IsNotNull(plant); Assert.AreEqual(15, plant.Durability);
            for (int rise = 0; rise < 2; rise++)
            {
                Assert.AreEqual(Water, session.CurrentActorId);
                yield return Press("tide_tab"); yield return Press("tide_rise"); yield return SelectCell(source); yield return Press("tide_preview");
                Assert.IsTrue(view.GetButton("confirm").interactable);
                yield return Press("confirm"); yield return Ready();
                if (rise == 0)
                {
                    Assert.AreEqual(CombatSurfaceKind.Wet, session.Surfaces.Get(fuelCell).Kind);
                    Assert.IsFalse(plant.IsDestroyed); Assert.IsFalse(session.Surfaces.Cells.Any(value => value.Kind == CombatSurfaceKind.Burning));
                    Record("first rise extinguishes both burning cells without prematurely eroding wet-only plant");
                    yield return EndCurrent(); yield return EndCurrent();
                }
            }
            Assert.AreEqual(CombatSurfaceKind.ShallowWater, session.Surfaces.Get(fuelCell).Kind);
            Assert.IsTrue(plant.IsDestroyed); Assert.AreEqual(0, session.Surfaces.Get(fuelCell).Fuel);
            Assert.IsTrue(session.Surfaces.Settlements.Last().SceneObjectChanges.Any(value => value.Id == plant.Id));
            Record("second rise applies150 erosion: LowPlant_4_6 durability15->0; dependent fuel=0");
            yield return Capture("01-eroded-fuel");
            yield return EndCurrent(); yield return EndCurrent();
            Assert.IsFalse(session.Surfaces.Cells.Any(value => value.Kind == CombatSurfaceKind.Burning));
            Assert.AreEqual(CombatSurfaceKind.None, session.Surfaces.Get(new HexCoord(3,7)).Kind);
            Record("next environment cycle does not spread into (3,7)"); yield return Capture("02-fuel-chain-stopped");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator CommittedTideAssetsFollowTwoRisesTwoEbbsAndCleanup()
        {
            yield return Setup(false);
            yield return Enter();
            var caster = Actor(Water);
            var source = new HexCoord(5, 5);
            var target = new HexCoord(4, 6);
            var plant = GameObject.Find("LowPlant_4_6").transform;
            var surfaceRoot = Find<CombatSurfaceView>().transform;
            int CountFronts(string prefab) => surfaceRoot.Cast<Transform>().Count(item => item.name == prefab + "(Clone)");
            yield return Press("earth_tab");
            yield return Press("earth_anchor");
            yield return Click(Point(caster.Position));
            yield return Press("confirm");
            yield return Ready();
            Assert.AreEqual(CombatEarthMode.Anchor, CombatForceMovement.Gravity(caster).EarthMode);
            yield return EndCurrent();
            float previousDelta = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                var measured = new HashSet<Mesh>();
                for (int step = 0; step < 4; step++)
                {
                    bool rising = step < 2;
                    string front = rising ? "TideRise" : "TideEbb";
                    var action = rising ? CombatTideAction.Rise : CombatTideAction.Ebb;
                    var request = new CombatJindanRequest(Seat(Water).Definition.SeatId, tideAction: action, tideSource: source);
                    var quote = encounter.PreviewJindan(new CombatCommand(CombatCommandKind.JindanAbility, Water,
                        profileId: "WATER_TIDE", jindan: request));
                    Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
                    int health = caster.CurrentHealth;
                    var origin = caster.Position;
                    long sequence = session.Surfaces.SettlementSequence;
                    yield return Press("tide_tab");
                    yield return Press(rising ? "tide_rise" : "tide_ebb");
                    yield return Click(Point(source));
                    Assert.IsTrue(view.GetButton("confirm").interactable);
                    Assert.AreEqual(0, CountFronts(front));
                    Assert.AreEqual(sequence, session.Surfaces.SettlementSequence);
                    if (step == 0)
                    {
                        var stopped = quote.Tide.Pushes.Single(push => push.ActorId == Water);
                        Assert.IsEmpty(stopped.Movement.Path);
                        Assert.AreEqual("earth_anchored", stopped.Movement.Reason);
                        string hint = GameObject.Find("ActionHint").GetComponent<Text>().text;
                        StringAssert.Contains($"本人留在 ({origin.Q},{origin.R})", hint);
                        StringAssert.Contains("镇岳挡推，仍受水击", hint);
                        yield return Capture("01-locked-blocked-push");
                    }
                    yield return Press("confirm");
                    Assert.AreEqual(quote.Tide.Changes.Count, CountFronts(front));
                    Assert.AreEqual(quote.Tide.Changes.Count(change => change.Before == CombatSurfaceKind.Burning &&
                        change.After == CombatSurfaceKind.Wet), CountFronts("ExtinguishSteam"));
                    if (step == 1) Assert.AreEqual(1, CountFronts("LowPlantErode"));
                    yield return new WaitForSeconds(.24f);
                    var crest = surfaceRoot.Find(front + "(Clone)").GetComponentInChildren<MeshRenderer>();
                    var properties = new MaterialPropertyBlock();
                    crest.GetPropertyBlock(properties);
                    Assert.That(properties.GetFloat("_Progress"), Is.InRange(.15f, .85f));
                    Assert.Greater(properties.GetVector("_Flow").sqrMagnitude, .95f);
                    yield return Capture("0" + (step + 2) + "-" + front);
                    yield return Ready();
                    var receipt = session.Surfaces.Settlements.Single(value => value.Sequence > sequence && value.TideAction.HasValue);
                    Assert.AreEqual(source, receipt.TideSource);
                    Assert.AreEqual(action, receipt.TideAction);
                    CollectionAssert.AreEqual(quote.Tide.Changes, receipt.Changes);
                    foreach (var change in receipt.Changes)
                    {
                        Assert.AreEqual(change.After, session.Surfaces.Get(change.Cell).Kind);
                        if (change.After == CombatSurfaceKind.None) continue;
                        var sheet = surfaceRoot.Find("Surface_" + change.Cell.Q + "_" + change.Cell.R + "_" + change.After);
                        Assert.IsNotNull(sheet);
                        Assert.AreEqual("TianZhang/Jindan/Water Surface", sheet.GetComponentInChildren<MeshRenderer>().sharedMaterial.shader.name);
                    }
                    float supportError = 0f;
                    foreach (var cell in session.Surfaces.Cells.Where(cell => cell.Kind >= CombatSurfaceKind.Wet))
                    {
                        var sheet = surfaceRoot.Find("Surface_" + cell.Cell.Q + "_" + cell.Cell.R + "_" + cell.Kind);
                        var filter = sheet.GetComponentInChildren<MeshFilter>();
                        var mesh = filter.sharedMesh;
                        if (!measured.Add(mesh)) continue;
                        var points = mesh.vertices.Select(filter.transform.TransformPoint).ToArray();
                        var indices = mesh.triangles;
                        var support = surface.Cells.Single(item => item.q == cell.Cell.Q && item.r == cell.Cell.R);
                        for (int i = 0; i < indices.Length; i += 3)
                        {
                            Vector3 a = points[indices[i]], b = points[indices[i + 1]], c = points[indices[i + 2]];
                            if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-12f) continue;
                            Vector3 middle = (a + b + c) / 3f;
                            supportError = Mathf.Max(supportError, Mathf.Abs(middle.y - surface.Height(support, middle.z) - .018f));
                        }
                    }
                    Assert.Less(supportError, .0001f);
                    Record("tide " + action + " fronts=" + quote.Tide.Changes.Count + " supportError=" + supportError);
                    if (step == 0)
                    {
                        Assert.AreEqual(origin, caster.Position);
                        Assert.Less(caster.CurrentHealth, health);
                        Assert.IsTrue(plant.Find("Living").gameObject.activeInHierarchy);
                    }
                    if (step >= 1)
                    {
                        Assert.IsFalse(plant.Find("Living").gameObject.activeInHierarchy);
                        Assert.IsFalse(plant.Find("Charred").gameObject.activeInHierarchy);
                    }
                    yield return new WaitForSeconds(1.25f);
                    Assert.AreEqual(0, CountFronts(front));
                    Assert.AreEqual(0, CountFronts("ExtinguishSteam"));
                    if (step < 3) { yield return EndCurrent(); yield return EndCurrent(); }
                }
                Assert.AreEqual(CombatSurfaceKind.None, session.Surfaces.Get(target).Kind);
                yield return Press("close_tab"); yield return Press("tide_close"); yield return Press("confirm"); yield return Ready();
                Assert.AreEqual(CombatSurfaceKind.None, session.Surfaces.Get(source).Kind);
                yield return new WaitForSeconds(1.25f);
                Assert.IsNull(surfaceRoot.Find("Surface_5_5_ShallowWater"));
                Assert.AreEqual(0, CountFronts("WetToDry"));
                yield return Capture("06-tide-closed");
            }
            finally { Time.captureDeltaTime = previousDelta; }
        }

        private IEnumerator Wall(HexCoord a, HexCoord b)
        {
            yield return Press("earth_wall"); yield return Press("toggle_panel");
            yield return Click(Point(a)); yield return Click(Point(b)); yield return Press("toggle_panel"); yield return Press("earth_preview");
            Assert.IsTrue(view.GetButton("confirm").interactable, "wall quote failed: " + a + " -> " + b);
            yield return Press("confirm"); yield return Ready();
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator ProductionFlightAvoidsTideThenGravityLandingEnablesChargedWaterHit()
        {
            yield return Setup(false); yield return Enter(); yield return EndCurrent();
            Assert.AreEqual(Water, session.CurrentActorId); var water = Actor(Water); var source = new HexCoord(5,5);
            yield return Press("flight_tab"); yield return Press("flight_takeoff"); yield return Press("confirm"); yield return Ready();
            Assert.AreEqual(SpatialMovementMode.Fly, water.MovementMode);
            int hp = water.CurrentHealth;
            yield return Press("tide_tab"); yield return Press("tide_rise"); yield return SelectCell(source); yield return Press("tide_preview");
            var request = new CombatJindanRequest(Seat(Water).Definition.SeatId, tideAction: CombatTideAction.Rise, tideSource: source);
            var quote = encounter.PreviewJindan(new CombatCommand(CombatCommandKind.JindanAbility, Water, profileId:"WATER_TIDE", jindan:request));
            Assert.IsFalse(quote.Tide.WaterTargetIds.Contains(Water)); Assert.IsFalse(quote.Tide.Pushes.Any(value => value.ActorId == Water));
            yield return Press("confirm"); yield return Ready(); Assert.AreEqual(hp, water.CurrentHealth);
            Record("airborne tide immunity; altitude=" + water.AltitudeBand + " HP=" + hp); yield return Capture("01-flight-tide");
            yield return EndCurrent(); Assert.AreEqual(Earth, session.CurrentActorId);
            yield return Press("earth_tab"); yield return Press("earth_heavy"); yield return SelectCell(water.Position);
            yield return Press("earth_preview"); yield return Press("confirm"); yield return Ready();
            Assert.AreEqual(SpatialMovementMode.Ground, water.MovementMode);
            Record("ground source heavy gravity landed water actor at " + water.Position + " HP=" + water.CurrentHealth);
            yield return Capture("02-gravity-grounded");
            yield return EndCurrent(); Assert.AreEqual(Water, session.CurrentActorId);
            int ap = water.ActionPointsRemainingUnits, mp = water.CurrentSpirit;
            long before = session.Surfaces.SettlementSequence;
            yield return Press("tide_tab"); yield return Press("tide_charge"); yield return Press("tide_rise");
            yield return SelectCell(source); yield return Press("tide_preview"); yield return Capture("03-charge-preview");
            yield return Press("confirm"); yield return Ready();
            Assert.IsNotNull(Seat(Water).PendingTide); int due = Seat(Water).PendingTide.DueTick;
            Assert.AreEqual(before, session.Surfaces.SettlementSequence);
            Assert.AreEqual(ap - 375000, water.ActionPointsRemainingUnits); Assert.AreEqual(mp - 600, water.CurrentSpirit);
            Record("charge paid375AP600MP now=" + session.TurnScheduler.CurrentTick + " due=" + due + " without early surface result");
            yield return EndCurrent();
            Assert.IsNull(Seat(Water).PendingTide);
            var receipt = session.Surfaces.Settlements.Single(value => value.Sequence > before && value.SourceRef == Seat(Water).Definition.AbilityInstanceId);
            Assert.AreEqual(due, receipt.Tick); Assert.IsTrue(receipt.Damage.Any(value => value.TargetId == Water));
            Assert.AreEqual(CombatSurfaceKind.ShallowWater, session.Surfaces.Get(new HexCoord(4,5)).Kind);
            Record("charge resolved at exact tick=" + receipt.Tick + " grounded water hit=" + receipt.Damage.Single(value => value.TargetId == Water).FinalDamage);
            yield return Capture("04-charged-ground-hit");
        }

        private IEnumerator TideLoop(bool anchor, bool native)
        {
            yield return Setup(native); yield return Enter();
            var originalSession = session;
            string saved = JsonUtility.ToJson(CharacterRecord.Capture(GameBootstrap.RequireRuntime().Player.Capture()));
            var caster = Actor(Water);
            if (anchor)
            {
                yield return Press("earth_tab"); yield return Press("earth_anchor");
                yield return SelectCell(caster.Position); yield return Press("earth_preview"); yield return Press("confirm"); yield return Ready();
                Assert.AreEqual(3, CombatForceMovement.Gravity(caster).GravityShiftRank);
            }
            yield return EndCurrent(); Assert.AreEqual(Water, session.CurrentActorId);
            yield return Press("tide_tab");
            var source = new HexCoord(5,5);
            if (!anchor)
            {
                var board = session.SceneObjects.Board;
                var occupied = session.Combatants.All.Where(value => value.IsAlive && value.Id != Water).SelectMany(value => value.OccupiedCells).ToArray();
                var moves = source.Neighbors().Where(cell => cell != caster.Position).Select(cell => new
                {
                    Cell = cell, Direction = source.DirectionTo(cell),
                    Quote = encounter.PreviewJindan(new CombatCommand(CombatCommandKind.Move, Water, destination: cell)),
                }).Where(value => value.Quote.IsLegal && board.InspectGroundMoveEdge(new SpatialPose(value.Cell), value.Cell.Step(value.Direction), false).IsLegal &&
                    board.InspectPlacement(new SpatialPose(value.Cell.Step(value.Direction)), occupied).Length == 0)
                    .OrderBy(value => value.Quote.ActionPointCostUnits).ToArray();
                Assert.IsNotEmpty(moves, "No legal shoreline stance with an outward landing");
                encounter.RequestMove(Water, moves[0].Cell.Q, moves[0].Cell.R); yield return Ready();
                Assert.AreEqual(moves[0].Cell, caster.Position); Record("walked to actual tide stance " + caster.Position);
            }
            int hp = caster.CurrentHealth, ap = caster.ActionPointsRemainingUnits, mp = caster.CurrentSpirit;
            var origin = caster.Pose;
            var request = new CombatJindanRequest(Seat(Water).Definition.SeatId, tideAction: CombatTideAction.Rise, tideSource: source);
            var command = new CombatCommand(CombatCommandKind.JindanAbility, Water, profileId: "WATER_TIDE", jindan: request);
            var quote = encounter.PreviewJindan(command);
            Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
            var expectedPush = quote.Tide.Pushes.Single(value => value.ActorId == Water);
            Assert.AreEqual(anchor, expectedPush.Movement.Path.Count == 0);
            Assert.Contains(Water, quote.Tide.WaterTargetIds.ToArray());
            Record("before tide: " + string.Join(";", quote.Tide.Changes.Select(value => value.Cell + ":" + value.Before + "->" + value.After)) +
                " push=" + expectedPush.Movement.Origin.Anchor + "->" + expectedPush.Movement.Destination.Anchor + " reason=" + expectedPush.Movement.Reason);
            if (native)
            {
                long baseline = session.Surfaces.SettlementSequence;
                RemoveMouse();
                yield return NativeStep("tide", "点击涨潮，收起面板，点击潮源，展开后预演并确认。", source,
                    () => session.Surfaces.SettlementSequence > baseline);
                EnsureMouse();
            }
            else
            {
                yield return Press("tide_rise"); yield return SelectCell(source); yield return Press("tide_preview");
                Assert.IsTrue(view.GetButton("confirm").interactable);
                Assert.IsTrue(Object.FindObjectsByType<LineRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                    .Any(value => value.name == "TideQuoteLine" && value.positionCount > 0));
                Record(GameObject.Find("CombatQuote").GetComponent<Text>().text); yield return Capture("01-preview");
                yield return Press("confirm");
            }
            yield return Ready();
            Assert.AreEqual(ap - 250000, caster.ActionPointsRemainingUnits); Assert.AreEqual(mp - 400, caster.CurrentSpirit);
            Assert.Less(caster.CurrentHealth, hp); Assert.AreEqual(expectedPush.Movement.Destination, caster.Pose);
            Assert.AreEqual(origin.Facing, caster.Facing);
            Assert.AreEqual(CombatSurfaceKind.DeepWater, session.Surfaces.Get(source).Kind);
            Assert.AreEqual(CombatSurfaceKind.Wet, session.Surfaces.Get(new HexCoord(5,6)).Kind);
            Assert.IsFalse(session.Surfaces.Cells.Any(value => value.Kind == CombatSurfaceKind.Burning));
            var provider = Find<Static3DCombatUnitPresentationProvider>(); Assert.IsTrue(provider.TryGetPresentedUnit(Water, out var root));
            Assert.That(Vector3.Distance(root.transform.position, surface.Center(Hex(caster.Position))), Is.LessThan(.025f));
            Assert.IsNotNull(GameObject.Find("Surface_5_5_DeepWater")); Assert.IsNotNull(GameObject.Find("Surface_5_6_Wet"));
            float maxSupportError = 0;
            foreach (var cell in session.Surfaces.Cells.Where(value => value.Kind != CombatSurfaceKind.None))
            {
                var sheet = GameObject.Find("Surface_" + cell.Cell.Q + "_" + cell.Cell.R + "_" + cell.Kind);
                Assert.IsNotNull(sheet); var support = surface.Cells.Single(value => value.q == cell.Cell.Q && value.r == cell.Cell.R);
                foreach (var mesh in sheet.GetComponentsInChildren<MeshFilter>()) foreach (var vertex in mesh.sharedMesh.vertices)
                {
                    Vector3 p = mesh.transform.TransformPoint(vertex);
                    maxSupportError = Mathf.Max(maxSupportError, Mathf.Abs(p.y - surface.Height(support, p.z) - .018f));
                }
            }
            Assert.Less(maxSupportError, .00002f); Record("surface own-support max error=" + maxSupportError);
            Record("committed: " + caster.Position + " HP=" + hp + "->" + caster.CurrentHealth + " AP=" + caster.ActionPointsRemainingUnits +
                " MP=" + caster.CurrentSpirit + " visual=" + root.transform.position + " source=" + session.Surfaces.Get(source).Kind);
            yield return Capture("02-tide-result");
            if (!anchor)
            {
                encounter.RequestMove(Water, source.Q, source.R); yield return Ready(); Assert.AreEqual(source, caster.Position);
                Record("walked into deep water at " + source); yield return Capture("03-deep-water");
                var dry = session.Surfaces.Cells.Where(value => value.Kind == CombatSurfaceKind.None && value.Cell.TopologicalDistanceTo(source) >= 2)
                    .Select(value => encounter.PreviewJindan(new CombatCommand(CombatCommandKind.Move, Water, destination: value.Cell)))
                    .Where(value => value.IsLegal).OrderBy(value => value.ActionPointCostUnits).First();
                encounter.RequestMove(Water, dry.Destination.Value.Q, dry.Destination.Value.R); yield return Ready();
                Assert.AreEqual(CombatSurfaceKind.None, session.Surfaces.Get(caster.Position).Kind);
                Record("returned to shore " + caster.Position + " AP=" + caster.ActionPointsRemainingUnits); yield return Capture("04-shore");
            }
            yield return Press("tide_close"); yield return Press("confirm"); yield return Ready();
            Assert.AreEqual(0, session.Surfaces.OwnedCount(Seat(Water).Definition.AbilityInstanceId));
            Assert.AreEqual(CombatSurfaceKind.None, session.Surfaces.Get(source).Kind);
            yield return Press("flight_tab"); Assert.That(GameObject.Find("CombatDetails").GetComponent<Text>().text, Does.Contain("飞行·地面"));
            Assert.AreEqual(saved, JsonUtility.ToJson(CharacterRecord.Capture(GameBootstrap.RequireRuntime().Player.Capture())));
            yield return Click(GameObject.Find("ReturnButton").GetComponent<Button>());
            yield return Click(GameObject.Find("ConfirmWithdrawalButton").GetComponent<Button>());
            yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.IsTrue(originalSession.Surfaces.Cells.All(value => value.Kind == CombatSurfaceKind.None));
            Assert.IsTrue(originalSession.Jindan.Seats.All(value => value.PendingTide == null));
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild")); yield return SceneReady(GameplaySceneNames.Adventure);
            yield return Enter(); Assert.AreEqual(CombatSurfaceKind.ShallowWater, session.Surfaces.Get(source).Kind);
            Assert.AreEqual(CombatSurfaceKind.Burning, session.Surfaces.Get(new HexCoord(5,6)).Kind);
            Assert.AreEqual(6000, Actor(Water).CurrentSpirit); Record("withdrawal / reentry resets only new rehearsal; world record unchanged");
            File.WriteAllText(Path.Combine(output, "pending.json"), "{\"stage\":\"complete\"}");
        }

        private IEnumerator Enter(bool manifest = false)
        {
            yield return Click(GameObject.Find("JindanRehearsalButton").GetComponent<Button>());
            view = Find<JindanDemoView>(); yield return Press("stage2");
            if (manifest) yield return Press("earth_toggle");
            yield return Press("enter");
            encounter = Find<EncounterCoordinator>(); session = encounter.CurrentSession; surface = Find<CombatBattlefieldSurface>();
            yield return Ready(); Assert.AreEqual(Earth, session.CurrentActorId);
        }
        private IEnumerator EndCurrent()
        {
            long sequence = session.TurnSequence;
            yield return Click(Find<CombatActionBarView>().WaitButton); yield return Click(Find<CombatActionPreviewView>().ConfirmButton);
            for (float end = Time.realtimeSinceStartup + 60; session.TurnSequence == sequence && Time.realtimeSinceStartup < end;) yield return null;
            Assert.Greater(session.TurnSequence, sequence); yield return Ready();
        }
        private IEnumerator SelectCell(HexCoord cell)
        { yield return Press("toggle_panel"); yield return Click(Point(cell)); yield return Press("toggle_panel"); }
        private IEnumerator Setup(bool native)
        {
            evidence.Clear(); output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/d8/" + (native ? "native" : TestContext.CurrentContext.Test.Name)));
            Directory.CreateDirectory(output); oldBackground = InputSystem.settings.backgroundBehavior;
            oldInput = InputSystem.settings.editorInputBehaviorInPlayMode; configured = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            EnsureMouse(); GuanzhongCombatEquipmentPlayModeTests.SetGameSize(1366, 768);
            var stale = Object.FindFirstObjectByType<GameBootstrap>(); if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            saves = Path.Combine(Application.temporaryCachePath, "D8-" + Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saves));
            var creation = Find<CharacterCreationController>(); creation.Open(); Assert.IsTrue(creation.SelectOrigin("origin_minor_clan")); creation.Submit("d8", "水火验证");
            yield return SceneReady(GameplaySceneNames.World); Assert.IsTrue(Find<WorldMapController>().SelectNode("guanzhong_hub"));
            Find<WorldMapController>().EnterSelectedLocation(); yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild")); yield return SceneReady(GameplaySceneNames.Adventure);
        }
        private IEnumerator NativeStep(string stage, string instruction, HexCoord cell, Func<bool> condition)
        {
            int clicks = 0;
            for (float end = Time.realtimeSinceStartup + 600, update = 0; Time.realtimeSinceStartup < end;)
            {
                if (Time.realtimeSinceStartup >= update)
                {
                    File.WriteAllText(Path.Combine(output, "pending.json"), JsonUtility.ToJson(new Pending { stage = stage, instruction = instruction,
                        buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(value => value.interactable)
                            .Select(value => new ButtonPoint { name = value.name, position = Center(value) }).ToArray(),
                        cell = Point(cell), width = Screen.width, height = Screen.height }, true)); update = Time.realtimeSinceStartup + 1;
                }
                yield return null;
                if (Mouse.current?.native == true && Mouse.current.leftButton.wasPressedThisFrame) { clicks++; Record(stage + " native=" + Mouse.current.position.ReadValue()); }
                if (condition()) break;
            }
            Assert.IsTrue(condition(), stage + " timed out"); Assert.Greater(clicks, 0);
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
            Assert.IsNotNull(mouse); InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left)); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
        }
        private static Vector2 Center(Button b) { var r = (RectTransform)b.transform; return RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(r.rect.center)); }
        private Vector2 Point(HexCoord cell) => Camera.main.WorldToScreenPoint(surface.Center(Hex(cell)));
        private static CombatUnitPresentationHex Hex(HexCoord cell) => new(cell.Q, cell.R);
        private void EnsureMouse() { if (mouse == null) mouse = InputSystem.AddDevice<Mouse>(); }
        private void RemoveMouse() { if (mouse != null) InputSystem.RemoveDevice(mouse); mouse = null; }
        private void Record(string text) { evidence.Add(text + " | tick=" + session?.TurnScheduler.CurrentTick); File.WriteAllLines(Path.Combine(output, "proof.txt"), evidence); }
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
