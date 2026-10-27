using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    public sealed class D10FireDomainTests
    {
        private const string Owner = "a_fire", Enemy = "z_water", Seat = "fire_seat", Source = "fire_guardian";
        private readonly CombatCommandService commands = new CombatCommandService();
        private static readonly HexCoord Center = new HexCoord(0, 0), A = new HexCoord(2, 1), B = new HexCoord(1, 1), C = new HexCoord(2, 0);
        private static CombatantSnapshot Actor(CombatSession s, string id = Owner) { s.Combatants.TryGet(id, out var a); return a; }
        private static CombatJindanSeatState State(CombatSession s) => s.Jindan.Find(Seat);
        private static CombatFireDomainState Fire(CombatSession s) => State(s).FireDomain;
        private static CombatCommand Deploy() => new CombatCommand(CombatCommandKind.JindanAbility, Owner,
            profileId: CombatFireDomainRules.Effect, jindan: new CombatJindanRequest(Seat));
        private static CombatCommand Close() => new CombatCommand(CombatCommandKind.CloseJindan, Owner,
            profileId: CombatFireDomainRules.Effect, jindan: new CombatJindanRequest(Seat));
        private void Commit(CombatSession s, CombatCommand command)
        { var result = commands.Execute(s, command); Assert.IsTrue(result.Succeeded, result.RejectionReason); }
        private void Next(CombatSession s)
        {
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, s.CurrentActorId));
            for (int i = 0; i < 10; i++)
            {
                var next = commands.AdvanceUntilAction(s);
                if (next.ActorId == Owner) return;
                Commit(s, new CombatCommand(CombatCommandKind.EndTurn, next.ActorId));
            }
            Assert.Fail("fire_turn_not_ready");
        }
        private void FirstCycle(CombatSession s) { Commit(s, Deploy()); Next(s); Next(s); }

        [Test]
        public void PrepareIsPureAndDeploymentUsesTheSharedLedgerAndReservesRealFuel()
        {
            var s = Ready(); var quote = commands.Preview(s, Deploy());
            Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
            Assert.IsTrue(commands.Prepare(s, Deploy(), quote, out _).Succeeded);
            Assert.AreEqual(5000, Actor(s).CurrentSpirit); Assert.IsNull(Fire(s).Main); Assert.AreEqual(15, Fire(s).RemainingFuel);
            Assert.IsNull(s.SceneObjects.Find(Fire(s).Support.Id));
            Commit(s, Deploy()); Assert.AreEqual(4000, Actor(s).CurrentSpirit);
            Assert.AreEqual(500000, Actor(s).ActionPointsRemainingUnits); Assert.AreEqual(1000, State(s).TotalManaSpent);
            Assert.AreEqual(9, Fire(s).RemainingFuel); Assert.AreEqual(6, Fire(s).ReservedFuel);
            Assert.AreEqual(600, Fire(s).Support.Durability); Assert.AreEqual(1, s.Jindan.Seats.Count(value => value.Definition.ActorId == Owner));
            Assert.AreSame(Fire(s).Support, s.SceneObjects.Find(Fire(s).Support.Id));
            Assert.AreEqual(1, Actor(s).GetCooldown(CombatFireDomainRules.Effect)); Assert.IsFalse(commands.Preview(s, Deploy()).IsLegal);
        }

        [Test]
        public void ThreeLaterOwnEndsAndTwoTailEndsNeverTickOnDeploymentOrGlobalTicks()
        {
            var s = Ready(); Commit(s, Deploy()); Next(s);
            Assert.AreEqual(0, State(s).MaintenancePayments); Assert.AreEqual(0, CombatFireDomainRules.BurningCount(s, State(s)));
            for (int i = 1; i <= 3; i++) { Next(s); Assert.AreEqual(i, State(s).MaintenancePayments); }
            Assert.IsNull(Fire(s).Main); Assert.AreEqual(2, Fire(s).Tails.Single().RemainingCycles);
            Assert.AreEqual(0, Fire(s).RemainingFuel); Assert.AreEqual(1600, State(s).TotalManaSpent);
            Next(s); Assert.AreEqual(1, Fire(s).Tails.Single().RemainingCycles);
            Next(s); Assert.IsEmpty(Fire(s).Tails); Assert.AreEqual(0, Fire(s).ReservedFuel);
            Assert.AreEqual(0, CombatFireDomainRules.BurningCount(s, State(s)));
            Assert.IsTrue(new[] { A, B, C }.All(cell => s.Surfaces.Get(cell).Kind == CombatSurfaceKind.Scorched));
            Assert.AreEqual(3, State(s).MaintenancePayments);
        }

        [TestCase("mana")] [TestCase("ap")] [TestCase("sealed")] [TestCase("center")] [TestCase("selection")]
        public void InvalidDeploymentPaysNothing(string kind)
        {
            var s = Ready(mana: kind == "mana" ? 999 : 5000, ap: kind == "ap" ? 499999 : 1000000);
            if (kind == "sealed") Actor(s).ApplyStatus(CombatStatusKind.SealSpirit, 2);
            if (kind == "center") Destroy(Fire(s).Support);
            var command = kind == "selection" ? new CombatCommand(CombatCommandKind.JindanAbility, Owner,
                profileId: CombatFireDomainRules.Effect, jindan: new CombatJindanRequest(Seat, new[] { A, B })) : Deploy();
            int ap = Actor(s).ActionPointsRemainingUnits, mana = Actor(s).CurrentSpirit;
            Assert.IsFalse(commands.Execute(s, command).Succeeded);
            Assert.AreEqual(ap, Actor(s).ActionPointsRemainingUnits); Assert.AreEqual(mana, Actor(s).CurrentSpirit);
            Assert.AreEqual(15, Fire(s).RemainingFuel); Assert.IsNull(Fire(s).Main);
        }

        [Test]
        public void InsufficientUpkeepIsAtomicAndConvertsOnlyExistingFireToTail()
        {
            var s = Ready(); FirstCycle(s); Actor(s).SetSpirit(5000, 199);
            int fuel = Fire(s).RemainingFuel; Next(s);
            Assert.AreEqual(199, Actor(s).CurrentSpirit); Assert.AreEqual(fuel, Fire(s).RemainingFuel);
            Assert.AreEqual(1, State(s).MaintenancePayments); Assert.IsNull(Fire(s).Main);
            Assert.AreEqual(2, Fire(s).Tails.Single().RemainingCycles); Assert.AreEqual("jindan_upkeep_failed", State(s).LastEndReason);
        }

        [Test]
        public void CloseCostsTwentyFiveAPOnceAndOldTailBlocksCapacityWithoutRefresh()
        {
            var s = Ready(); FirstCycle(s); int ap = Actor(s).ActionPointsRemainingUnits;
            Commit(s, Close()); Assert.AreEqual(ap - 25000, Actor(s).ActionPointsRemainingUnits);
            Assert.IsNull(s.SceneObjects.Find(Fire(s).Support.Id));
            var tail = Fire(s).Tails.Single(); Assert.AreEqual(2, tail.RemainingCycles);
            Assert.IsFalse(commands.Execute(s, Close()).Succeeded);
            Assert.AreEqual("fire_tail_capacity_full", commands.Preview(s, Deploy()).RejectionReason);
            Assert.AreEqual(2, tail.RemainingCycles); Assert.AreEqual(6, tail.ReservedFuel);
            Assert.AreEqual(1, State(s).SuccessfulDeployments);
        }

        [TestCase(false)] [TestCase(true)]
        public void GroundPairIsHitOnceAndAirAvoidsTheSameSurface(bool flying)
        {
            var s = Ready(pair: !flying, flying: flying); FirstCycle(s);
            var damage = s.Surfaces.Settlements.Where(value => value.SourceRef == Source).SelectMany(value => value.Damage).ToArray();
            Assert.AreEqual(flying ? 0 : 1, damage.Count(value => value.TargetId == Enemy));
            if (!flying) { Assert.Greater(damage.Single().FinalDamage, 0); Assert.LessOrEqual(damage.Single().FinalDamage, 24); }
            else Assert.AreEqual(10000, Actor(s, Enemy).CurrentHealth);
        }

        [TestCase(CombatSurfaceKind.DeepWater, CombatSurfaceKind.ShallowWater)]
        [TestCase(CombatSurfaceKind.ShallowWater, CombatSurfaceKind.Wet)]
        [TestCase(CombatSurfaceKind.Wet, CombatSurfaceKind.None)]
        public void HeatNeverSkipsTheUniqueWaterSurface(CombatSurfaceKind before, CombatSurfaceKind after)
        {
            var s = Ready(surface: before); FirstCycle(s);
            Assert.AreEqual(after, s.Surfaces.Get(A).Kind);
            Assert.AreEqual(6, Fire(s).RemainingFuel); Assert.AreEqual(1, State(s).MaintenancePayments);
        }

        [Test]
        public void TideExtinguishesPaidTailAndASecondDeploymentCannotRefreshItsOldDeadline()
        {
            var s = Ready(); FirstCycle(s); Commit(s, Close()); var old = Fire(s).Tails.Single();
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, Owner)); Assert.AreEqual(Enemy, commands.AdvanceUntilAction(s).ActorId);
            var tide = new CombatCommand(CombatCommandKind.JindanAbility, Enemy, profileId: "WATER_TIDE",
                jindan: new CombatJindanRequest("water_seat", tideAction: CombatTideAction.Rise, tideSource: new HexCoord(3, 1)));
            Commit(s, tide); Assert.AreEqual(CombatSurfaceKind.Wet, s.Surfaces.Get(A).Kind);
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, Enemy)); Assert.AreEqual(Owner, commands.AdvanceUntilAction(s).ActorId);
            long deadlineOrigin = old.CreatedOwnTurn; Commit(s, Deploy());
            Assert.AreSame(old, Fire(s).Tails.Single()); Assert.AreEqual(deadlineOrigin, old.CreatedOwnTurn);
            Assert.LessOrEqual(CombatFireDomainRules.BurningCount(s, State(s)), 3);
            Assert.AreEqual(CombatSurfaceKind.Wet, s.Surfaces.Get(A).Kind);
            Next(s); Next(s); Assert.IsFalse(Fire(s).Tails.Contains(old));
            Assert.LessOrEqual(CombatFireDomainRules.BurningCount(s, State(s)), 3);
        }

        [TestCase("center")] [TestCase("sealed")]
        public void InvalidCenterOrCarrierEndsMainButPreservesOnlyPaidTail(string reason)
        {
            var s = Ready(); FirstCycle(s);
            if (reason == "center") Destroy(Fire(s).Support); else Actor(s).ApplyStatus(CombatStatusKind.SealSpirit, 2);
            Reconcile(s); Assert.IsNull(Fire(s).Main); Assert.AreEqual(1, Fire(s).Tails.Count);
            Assert.AreEqual(6, Fire(s).ReservedFuel); Assert.AreEqual(2, s.Jindan.Seats.Count);
            Assert.IsFalse(commands.Preview(s, Deploy()).IsLegal);
        }

        [TestCase(false)] [TestCase(true)]
        public void DeathAndSessionEndRemoveMainAndTail(bool finish)
        {
            var s = Ready(); FirstCycle(s); Commit(s, Close());
            if (finish) s.Jindan.Clear(s); else { Actor(s).ReceiveDamage(10000); Reconcile(s); }
            Assert.IsNull(Fire(s).Main); Assert.IsEmpty(Fire(s).Tails); Assert.AreEqual(0, CombatFireDomainRules.BurningCount(s, State(s)));
        }

        [Test]
        public void PublicLivingPlantCanUseCenterFuelWithoutRefillingD8Fuel()
        {
            var s = Ready(plantOnly: true); Assert.AreEqual(0, s.Surfaces.Get(A).Fuel);
            FirstCycle(s); Assert.AreEqual(CombatSurfaceKind.Burning, s.Surfaces.Get(A).Kind); Assert.AreEqual(0, s.Surfaces.Get(A).Fuel);
            Destroy(s.SceneObjects.Find("plant")); Reconcile(s);
            Assert.AreEqual(CombatSurfaceKind.Scorched, s.Surfaces.Get(A).Kind);
            Assert.IsFalse(CombatFireDomainRules.IsBurnable(s, s.Surfaces.Get(A)));
        }

        [Test]
        public void ExistingForeignFireIsNotReattributedAndWarmthDoesNotChargeItsPool()
        {
            var s = Ready(foreign: true); FirstCycle(s);
            Assert.AreEqual("environment", s.Surfaces.Get(A).SourceRef);
            Assert.AreEqual(12, s.Surfaces.FireSources.Single().RemainingFuel);
            Assert.IsFalse(Fire(s).Main.Cells.Contains(A));
        }

        [Test]
        public void FuelCannotPartiallyPayAPeriodAndDeathInsideTheFireCycleEndsTheSession()
        {
            var s = Ready(fuelBudget: 8); Commit(s, Deploy()); Next(s); Next(s);
            Assert.IsNull(Fire(s).Main); Assert.AreEqual(0, State(s).MaintenancePayments);
            Assert.AreEqual(1000, State(s).TotalManaSpent); Assert.AreEqual(8, Fire(s).RemainingFuel);
            Assert.AreEqual(0, CombatFireDomainRules.BurningCount(s, State(s)));
            var death = Ready(); Commit(death, Deploy()); Next(death);
            Actor(death).SetPosition(B); Actor(death).ReceiveDamage(9999);
            Commit(death, new CombatCommand(CombatCommandKind.EndTurn, Owner)); commands.AdvanceUntilAction(death);
            Assert.IsFalse(Actor(death).IsAlive); Assert.AreEqual(CombatTurnPhase.Finished, death.TurnPhase);
            Assert.IsNull(Fire(death).Main); Assert.IsEmpty(Fire(death).Tails);
        }

        private static void Destroy(CombatSceneObject value) => typeof(CombatSceneObject).GetMethod("ApplyDurability", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(value, new object[] { 0 });
        private static void Reconcile(CombatSession s) => typeof(CombatJindanLoadout).GetMethod("Reconcile", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(s.Jindan, new object[] { s });

        private CombatSession Ready(int mana = 5000, int ap = 1000000, bool pair = false, bool flying = false,
            CombatSurfaceKind surface = CombatSurfaceKind.None, bool plantOnly = false, bool foreign = false, int fuelBudget = 15)
        {
            var cells = new Dictionary<HexCoord, SpatialCellRules>();
            for (int q = 0; q <= 4; q++) for (int r = 0; r <= 2; r++) cells.Add(new HexCoord(q, r), new SpatialCellRules(0, false, false, false, 0, true, 4));
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            foreach (var cell in cells.Keys) for (int direction = 0; direction < 6; direction++)
                if (cells.ContainsKey(cell.Step(direction))) edges.Add(new SpatialDirectedEdge(cell, cell.Step(direction)), new SpatialEdgeRules(2, true, true, allowsFlight: true));
            var board = new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(2, 16));
            var objects = plantOnly ? new[] { new CombatSceneObject("plant", new CombatSceneObjectDefinition("low_plant", "plant", 15, 0, 0, false, false, "charred"), A) } : Array.Empty<CombatSceneObject>();
            var environment = new CombatSceneObjects(board, objects, 10, 20);
            var owner = new CombatantSnapshot(Owner, CombatTeam.Player, new HexCoord(0, 1), 100, 10000, 10000, 100, 10000, 1, 1, ap, 20000,
                abilityInstanceIds: new Dictionary<string, string> { [CombatFireDomainRules.Effect] = Source }, realmStage: "金丹");
            owner.SetSpirit(5000, mana);
            var enemy = new CombatantSnapshot(Enemy, CombatTeam.Enemy, A, 100, 10000, 10000, 100, 2400, 1, 1, 1000000, 20000,
                footprint: pair ? SpatialFootprintKind.AdjacentPair : SpatialFootprintKind.Single,
                abilityInstanceIds: new Dictionary<string, string> { ["WATER_TIDE"] = "water_guardian" }, realmStage: "金丹",
                movementMode: flying ? SpatialMovementMode.Fly : SpatialMovementMode.Ground, altitudeBand: flying ? 1 : 0);
            enemy.SetSpirit(5000, 5000);
            var s = new CombatSession(new[] { owner, enemy }, Array.Empty<CombatAttackProfile>(), new Query(environment),
                new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0),
                    new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) },
                ruleSet: CombatActionPointKernelTests.Rules(), sceneObjects: environment);
            s.ConfigureJindan(new CombatJindanLoadout(new[] {
                new CombatJindanSeatDefinition(Owner, Seat, "fire", "Domain", Source, new[] { CombatFireDomainRules.Effect }, CombatFireDomainRules.Effect),
                new CombatJindanSeatDefinition(Enemy, "water_seat", "water", "Source", "water_guardian", new[] { "WATER_TIDE" }, "WATER_TIDE") },
                new[] { new CombatJindanAbilityDefinition(CombatFireDomainRules.Effect, true, 500000, 1000, 1, 25000, 200, 4, 3, 3, 600, 0, 6,
                    Array.Empty<int>(), "", 3, fireDomain: new CombatFireDomainDefinition(Center, "center", fuelBudget, 6, 2, 2400, .01f)),
                    new CombatJindanAbilityDefinition("WATER_TIDE", true, 250000, 400, 1, 25000, 0, 4, 3, 7, 0, 0, 6, Array.Empty<int>(), "",
                        tide: new CombatTideDefinition(1, 1, 2, 1, 125000, 200, 1, 125000, 200, 2400, 13f/280, 1.25f, 150)) }, 1));
            var definitions = cells.Keys.Select(cell => new CombatSurfaceDefinition(cell,
                cell == new HexCoord(3, 1) ? CombatSurfaceKind.ShallowWater : cell == A && foreign ? CombatSurfaceKind.Burning :
                new[] { A, B, C }.Contains(cell) ? surface : CombatSurfaceKind.None,
                !plantOnly && new[] { A, B, C }.Contains(cell) ? 4 : 0, cell == A && foreign ? "environment" : null));
            s.ConfigureSurfaces(new CombatSurfaces(definitions, foreign ? new[] { new CombatFireDefinition("environment", Enemy, 1000, 2000, 3, 12, 2400, .01f) } : null));
            Assert.AreEqual(Owner, commands.AdvanceUntilAction(s).ActorId); return s;
        }

        private sealed class Query : ICombatSpatialQuery
        {
            private readonly CombatSceneObjects environment;
            public Query(CombatSceneObjects value) => environment = value;
            public SpatialContactResult QueryRange(SpatialPose a, SpatialPose b, int min, int max, SpatialAttackPolicy policy = null) => environment.Board.QueryContact(a, b, min, max, policy);
            public CombatMovementQueryResult QueryMovement(SpatialPose a, HexCoord b, int budget, int efficiency, IReadOnlyCollection<HexCoord> occupied, int? facing = null)
            { var p = environment.Board.QueryMovementPath(a, b, budget, efficiency, occupied, facing); return new CombatMovementQueryResult(p.Steps.Count > 0, p.Reason, p.Steps, p.UnitsPerRange, p.ActionPointCostUnits); }
            public IReadOnlyDictionary<SpatialPose, int> FindReachable(SpatialPose a, int budget, int efficiency, IReadOnlyCollection<HexCoord> occupied) => environment.Board.FindReachableWithinActionPoints(a, budget, efficiency, occupied);
        }
    }
}
