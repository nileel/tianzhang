using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    public sealed class D6FlightTests
    {
        private readonly CombatCommandService commands = new CombatCommandService();
        private static HexCoord Cell(int q) => new HexCoord(q, 0);
        private static SpatialPose Air(int q, int band = 1) => new SpatialPose(Cell(q), movementMode: SpatialMovementMode.Fly, altitudeBand: band);

        [Test]
        public void PreviewCancelAndStaleQuotesNeverPayAndTransformsHaveFixedSharedCosts()
        {
            var s = Ready(); var a = Player(s); var command = Flight(CombatFlightAction.Takeoff);
            var quote = commands.Preview(s, command);
            Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
            Assert.AreEqual(250000, quote.ActionPointCostUnits); Assert.AreEqual(200, quote.SpiritCost);
            Assert.IsTrue(commands.Prepare(s, command, quote, out _).Succeeded);
            Assert.AreEqual(1000000, a.ActionPointsRemainingUnits); Assert.AreEqual(1000, a.CurrentSpirit);
            Assert.AreEqual(SpatialMovementMode.Ground, a.MovementMode);
            Assert.IsFalse(commands.Execute(s, Flight(CombatFlightAction.Ascend), quote).Succeeded);
            Commit(s, command); int ap = a.ActionPointsRemainingUnits;
            Assert.AreEqual("combat_quote_stale", commands.Execute(s, command, quote).RejectionReason);
            Assert.AreEqual(ap, a.ActionPointsRemainingUnits); Assert.AreEqual(800, a.CurrentSpirit);
            Assert.AreEqual(1, a.GetCooldown(CombatFlight.ProfileId));
            foreach (var action in new[] { CombatFlightAction.Ascend, CombatFlightAction.Descend, CombatFlightAction.Ascend, CombatFlightAction.Descend })
            { var result = Commit(s, Flight(action)); Assert.AreEqual(125000, result.ActionPointSpentUnits); Assert.AreEqual(0, result.SpiritSpent); }
            Commit(s, Flight(CombatFlightAction.Land));
            Assert.AreEqual(200000, a.ActionPointsRemainingUnits); Assert.AreEqual(800, a.CurrentSpirit);
            Assert.AreEqual(SpatialMovementMode.Ground, a.MovementMode); Assert.AreEqual(0, a.AltitudeBand);
            Assert.AreEqual("combat_ability_on_cooldown", commands.Execute(s, command).RejectionReason);
            Assert.AreEqual(800000, s.Flight.Find(a.Id).TotalActionPointsSpent);
            Assert.AreEqual(200, s.Flight.Find(a.Id).TotalManaSpent);
        }

        [Test]
        public void MaintenanceUsesSubsequentOwnStartsAfterRecoveryAndDurationUsesThreeSubsequentEnds()
        {
            var s = Ready(mana: 200); var a = Player(s); var state = s.Flight.Find(a.Id);
            Commit(s, Flight(CombatFlightAction.Takeoff)); a.SpiritRecoveryPerOwnTurn = 100;
            int tick = s.TurnScheduler.CurrentTick;
            for (int i = 0; i < 4; i++) commands.Preview(s, Flight(CombatFlightAction.Ascend));
            Assert.AreEqual(tick, s.TurnScheduler.CurrentTick); Assert.AreEqual(0, state.MaintenancePayments);
            Next(s); Assert.AreEqual(3, state.RemainingOwnTurnEnds); Assert.AreEqual(1, state.MaintenancePayments);
            Next(s); Assert.AreEqual(2, state.RemainingOwnTurnEnds); Assert.AreEqual(2, state.MaintenancePayments);
            Next(s); Assert.AreEqual(1, state.RemainingOwnTurnEnds); Assert.AreEqual(3, state.MaintenancePayments);
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, a.Id));
            Assert.IsFalse(state.Active); Assert.AreEqual("flight_expired", state.LastEndReason);
            Assert.AreEqual(900, a.CurrentHealth); Assert.AreEqual(0, a.CurrentSpirit);
            Assert.AreEqual(SpatialMovementMode.Ground, a.MovementMode); Assert.AreEqual(500, state.TotalManaSpent);
        }

        [Test]
        public void InsufficientUpkeepNeverPartiallyPaysAndLandingDamageOccursOnce()
        {
            var s = Ready(mana: 299); var a = Player(s); Commit(s, Flight(CombatFlightAction.Takeoff));
            Next(s); Assert.AreEqual(99, a.CurrentSpirit); Assert.AreEqual(900, a.CurrentHealth);
            Assert.AreEqual("flight_upkeep_failed", s.Flight.Find(a.Id).LastEndReason);
            Next(s); Assert.AreEqual(900, a.CurrentHealth); Assert.AreEqual(0, s.Flight.Find(a.Id).MaintenancePayments);
        }

        [TestCase("mana")]
        [TestCase("ap")]
        [TestCase("source")]
        [TestCase("sealed")]
        [TestCase("immobilized")]
        public void RejectedTakeoffDoesNotChangePoseResourcesOrCooldown(string reason)
        {
            var s = Ready(mana: reason == "mana" ? 199 : 1000, ap: reason == "ap" ? 249999 : 1000000); var a = Player(s);
            if (reason == "source") s.Flight.SetSourceAvailable(s, a.Id, false);
            if (reason == "sealed") a.ApplyStatus(CombatStatusKind.SealSpirit, 2);
            if (reason == "immobilized") a.ApplyStatus(CombatStatusKind.Immobilize, 2);
            int ap = a.ActionPointsRemainingUnits, mp = a.CurrentSpirit; long version = s.StateVersion;
            Assert.IsFalse(commands.Execute(s, Flight(CombatFlightAction.Takeoff)).Succeeded);
            Assert.AreEqual(ap, a.ActionPointsRemainingUnits); Assert.AreEqual(mp, a.CurrentSpirit);
            Assert.AreEqual(SpatialMovementMode.Ground, a.MovementMode); Assert.AreEqual(version, s.StateVersion);
            Assert.AreEqual(0, a.GetCooldown(CombatFlight.ProfileId)); Assert.IsFalse(s.Flight.Find(a.Id).Active);
        }

        [Test]
        public void FailedLandingKeepsColumnAllowsOtherActionsAndChargesOnlyLaterOwnEndsUntilDeath()
        {
            var s = Ready(blockedGround: 1); var a = Player(s); Commit(s, Flight(CombatFlightAction.Takeoff));
            Commit(s, new CombatCommand(CombatCommandKind.Move, a.Id, destination: Cell(1)));
            Assert.IsFalse(commands.Execute(s, Flight(CombatFlightAction.Land)).Succeeded);
            s.Flight.SetSourceAvailable(s, a.Id, false); var state = s.Flight.Find(a.Id);
            Assert.IsTrue(state.FailedLanding); Assert.AreEqual(Cell(1), a.Position); Assert.AreEqual(1, a.AltitudeBand);
            Assert.AreEqual(900, a.CurrentHealth); Assert.AreEqual("demo_20261024_flight_failed_landing", state.ProcessState);
            Assert.AreEqual("flight_not_active", commands.Execute(s, new CombatCommand(CombatCommandKind.Move, a.Id, destination: Cell(2))).RejectionReason);
            Assert.IsFalse(commands.Execute(s, Flight(CombatFlightAction.Ascend)).Succeeded);
            Commit(s, new CombatCommand(CombatCommandKind.Guard, a.Id));
            Next(s); Assert.AreEqual(900, a.CurrentHealth, "Creation turn end does not add 20%.");
            Next(s); Assert.AreEqual(700, a.CurrentHealth);
            Next(s); Next(s); Next(s); Assert.AreEqual(100, a.CurrentHealth);
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, a.Id));
            Assert.IsFalse(a.IsAlive); Assert.IsFalse(state.FailedLanding); Assert.AreEqual(0, a.AltitudeBand);
        }

        [Test]
        public void RecoveryRequiresSourceAndCooldownThenRestoresLowAirInSameColumn()
        {
            var s = Ready(blockedGround: 1); var a = Player(s); Commit(s, Flight(CombatFlightAction.Takeoff));
            Commit(s, Flight(CombatFlightAction.Ascend)); Commit(s, new CombatCommand(CombatCommandKind.Move, a.Id, destination: Cell(1)));
            s.Flight.SetSourceAvailable(s, a.Id, false);
            Assert.IsFalse(commands.Execute(s, Flight(CombatFlightAction.Recover)).Succeeded);
            s.Flight.SetSourceAvailable(s, a.Id, true);
            Assert.AreEqual("combat_ability_on_cooldown", commands.Execute(s, Flight(CombatFlightAction.Recover)).RejectionReason);
            Next(s); int ap = a.ActionPointsRemainingUnits, mp = a.CurrentSpirit;
            Commit(s, Flight(CombatFlightAction.Recover));
            Assert.AreEqual(ap - 250000, a.ActionPointsRemainingUnits); Assert.AreEqual(mp - 200, a.CurrentSpirit);
            Assert.AreEqual(Cell(1), a.Position); Assert.AreEqual(1, a.AltitudeBand);
            Assert.IsTrue(s.Flight.Find(a.Id).Active); Assert.IsFalse(s.Flight.Find(a.Id).FailedLanding);
            Assert.AreEqual(3, s.Flight.Find(a.Id).RemainingOwnTurnEnds); Assert.AreEqual(900, a.CurrentHealth);
        }

        [Test]
        public void OccupiedColumnRejectsLandingAndSourceLossRescuesOnlyAfterColumnBecomesFree()
        {
            var s = Ready(); var a = Player(s); s.Combatants.TryGet("z_enemy", out var enemy);
            Commit(s, Flight(CombatFlightAction.Takeoff));
            Assert.IsFalse(commands.Execute(s, new CombatCommand(CombatCommandKind.Move, a.Id, destination: enemy.Position)).Succeeded);
            // Simulate an external forced occupancy change; voluntary movement never permits this overlap.
            enemy.SetPosition(a.Position);
            Assert.IsFalse(commands.Execute(s, Flight(CombatFlightAction.Land)).Succeeded);
            s.Flight.SetSourceAvailable(s, a.Id, false);
            Assert.IsTrue(s.Flight.Find(a.Id).FailedLanding); Assert.AreEqual(900, a.CurrentHealth);
            enemy.SetPosition(Cell(3)); Commit(s, new CombatCommand(CombatCommandKind.Guard, a.Id));
            Assert.AreEqual(Cell(0), a.Position); Assert.AreEqual(0, a.AltitudeBand);
            Assert.IsFalse(s.Flight.Find(a.Id).FailedLanding); Assert.AreEqual(900, a.CurrentHealth);
        }

        [Test]
        public void SealReconcilesAndTerminalCleanupDoesNotChargeFallOrCarryFlightToFreshSession()
        {
            var s = Ready(); var a = Player(s); Commit(s, Flight(CombatFlightAction.Takeoff));
            a.ApplyStatus(CombatStatusKind.SealSpirit, 2); Commit(s, new CombatCommand(CombatCommandKind.Guard, a.Id));
            Assert.AreEqual("combat_actor_sealed", s.Flight.Find(a.Id).LastEndReason); Assert.AreEqual(900, a.CurrentHealth);
            s = Ready(blockedGround: 1); a = Player(s); Commit(s, Flight(CombatFlightAction.Takeoff));
            Commit(s, new CombatCommand(CombatCommandKind.Move, a.Id, destination: Cell(1)));
            typeof(CombatSession).GetMethod("Terminate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(s, null);
            Assert.IsFalse(s.Flight.Find(a.Id).Active); Assert.IsFalse(s.Flight.Find(a.Id).FailedLanding);
            Assert.AreEqual(0, a.AltitudeBand); Assert.AreEqual(1000, a.CurrentHealth);
            var fresh = Ready(); Assert.AreEqual(0, Player(fresh).GetCooldown(CombatFlight.ProfileId));
            Assert.AreEqual(0, fresh.Flight.Find("player").TotalManaSpent);
        }

        [Test]
        public void AirEdgesIgnoreGroundBurdenButKeepMetricCeilingsCliffsWallsAndOccupancy()
        {
            var b = Board(groundBurden: 3); var ground = Path(b, new SpatialPose(Cell(0)), 1); var low = Path(b, Air(0), 1);
            Assert.AreEqual(25000, ground.ActionPointCostUnits); Assert.AreEqual(10000, low.ActionPointCostUnits);
            var scaled = b.WithMetricOverrides(new[] { new SpatialMetricOverride("scale", Cell(0), Cell(1), 1) });
            Assert.AreEqual(5000, Path(scaled, Air(0), 1).ActionPointCostUnits);
            var cliff = Board(height: 2, edgeKind: SpatialEdgeKind.OpenCliff);
            Assert.IsEmpty(Path(cliff, new SpatialPose(Cell(0)), 1).Steps); Assert.IsEmpty(Path(cliff, Air(0), 1).Steps);
            Assert.AreEqual(1, Path(cliff, Air(0, 2), 1).Steps.Count);
            Assert.IsEmpty(Path(Board(edgeKind: SpatialEdgeKind.EntityWall), Air(0, 2), 1).Steps);
            Assert.IsEmpty(Path(Board(allowsFlight: false), Air(0), 1).Steps);
            Assert.IsEmpty(Path(Board(ceiling: 1), Air(0, 2), 1).Steps);
            Assert.IsEmpty(b.QueryMovementPath(Air(0), Cell(1), 1000000, 10000, new[] { Cell(1) }).Steps);
            Assert.AreEqual(SpatialQueryReasons.MissingCell, b.InspectPlacement(Air(99)));
            Assert.IsNotEmpty(b.InspectPlacement(new SpatialPose(Cell(0), footprint: SpatialFootprintKind.AdjacentPair,
                movementMode: SpatialMovementMode.Fly, altitudeBand: 1)));
        }

        [Test]
        public void ExplicitAntiAirPolicyCanReachLowButNotHighAndDoesNotGrantUnlistedSources()
        {
            var b = Board(); var both = SpatialMovementMode.Ground | SpatialMovementMode.Fly;
            var policy = new SpatialAttackPolicy(both, both, 1, true);
            Assert.IsTrue(b.QueryContact(new SpatialPose(Cell(0)), Air(1), 0, 1, policy).IsInRange);
            Assert.AreEqual(SpatialQueryReasons.VerticalReachExceeded, b.QueryContact(new SpatialPose(Cell(0)), Air(1, 2), 0, 1, policy).Reason);
            Assert.IsTrue(b.QueryContact(Air(0), new SpatialPose(Cell(1)), 0, 1, policy).IsInRange);
            var ordinary = new SpatialAttackPolicy(SpatialMovementMode.Ground, SpatialMovementMode.Ground, 1, true);
            Assert.AreEqual(SpatialQueryReasons.TargetModeNotAllowed, b.QueryContact(new SpatialPose(Cell(0)), Air(1), 0, 1, ordinary).Reason);
            Assert.AreEqual(SpatialQueryReasons.SourceModeNotAllowed, b.QueryContact(Air(0), new SpatialPose(Cell(1)), 0, 1, ordinary).Reason);
        }

        [Test]
        public void EnemyUsesSameFlightQuoteAndUpkeepDeathProducesVictory()
        {
            var s = Ready(enemyFlight: true); s.Combatants.TryGet("z_enemy", out var enemy);
            Assert.IsTrue(new CombatLegalActionService(commands).GetLegalActions(s, "player").Any(c => c.FlightAction == CombatFlightAction.Takeoff));
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, "player"));
            Assert.AreEqual(enemy.Id, commands.AdvanceUntilAction(s).ActorId);
            var takeoff = new CombatCommand(CombatCommandKind.Flight, enemy.Id, profileId: CombatFlight.ProfileId, flightAction: CombatFlightAction.Takeoff);
            var quote = commands.Preview(s, takeoff); Assert.IsTrue(quote.IsLegal);
            Assert.AreEqual(250000, quote.ActionPointCostUnits); Assert.AreEqual(200, quote.SpiritCost);
            Commit(s, takeoff); enemy.ReceiveDamage(900);
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, enemy.Id));
            Assert.AreEqual("player", commands.AdvanceUntilAction(s).ActorId);
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, "player"));
            Assert.IsFalse(commands.AdvanceUntilAction(s).HasActor);
            Assert.IsFalse(enemy.IsAlive); Assert.AreEqual(99, enemy.CurrentSpirit);
            Assert.AreEqual(CombatSessionOutcome.Victory, new CombatResultBuilder().Build(s).Outcome);
        }

        private CombatSession Ready(int mana = 1000, int ap = 1000000, int blockedGround = -1, bool enemyFlight = false)
        {
            var environment = new CombatSceneObjects(Board(blockedGround: blockedGround), Array.Empty<CombatSceneObject>(), 10, 20);
            var actor = new CombatantSnapshot("player", CombatTeam.Player, Cell(0), 100, 1000, 1000,
                100, 100, 10, 10, ap, 10000, abilityInstanceIds: new Dictionary<string, string> { { CombatFlight.ProfileId, "player_flight" } });
            actor.SetSpirit(1000, mana);
            var enemy = new CombatantSnapshot("z_enemy", CombatTeam.Enemy, Cell(3), enemyFlight ? 100 : 1, 1000, 1000, 20, 20, 10, 10, 1000000, 10000,
                abilityInstanceIds: enemyFlight ? new Dictionary<string, string> { { CombatFlight.ProfileId, "enemy_flight" } } : null);
            if (enemyFlight) enemy.SetSpirit(1000, 299);
            var profile = new CombatAttackProfile("fixture", CombatAttackKind.Art, CombatAttackEffect.Physical, 0, 1, 10000, 10000, 0, 1, physicalMultiplier: 1);
            var ordinary = new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0),
                new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) };
            var s = new CombatSession(new[] { actor, enemy }, new[] { profile }, new Query(environment), ordinary, sceneObjects: environment);
            var sources = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("player", "player_flight") };
            if (enemyFlight) sources.Add(new KeyValuePair<string, string>("z_enemy", "enemy_flight"));
            s.ConfigureFlight(new CombatFlight(sources, new[] {
                new CombatFlightCost(CombatFlightAction.Takeoff, 250000, 200, 1), new CombatFlightCost(CombatFlightAction.Ascend, 125000, 0, 0),
                new CombatFlightCost(CombatFlightAction.Descend, 125000, 0, 0), new CombatFlightCost(CombatFlightAction.Land, 50000, 0, 0),
                new CombatFlightCost(CombatFlightAction.Recover, 250000, 200, 1) }, 100, 3));
            Assert.AreEqual("player", commands.AdvanceUntilAction(s).ActorId); return s;
        }
        private static SpatialQueryBoard Board(int blockedGround = -1, int groundBurden = 0, int height = 0,
            SpatialEdgeKind edgeKind = SpatialEdgeKind.Flat, bool allowsFlight = true, int ceiling = 4)
        {
            var cells = new Dictionary<HexCoord, SpatialCellRules>(); var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            for (int q = 0; q < 4; q++) cells.Add(Cell(q), new SpatialCellRules(q == 1 ? height : 0, q == blockedGround,
                false, false, groundBurden, allowsFlight, ceiling));
            for (int q = 0; q < 3; q++) foreach (var pair in new[] { new[] { q, q + 1 }, new[] { q + 1, q } })
                edges.Add(new SpatialDirectedEdge(Cell(pair[0]), Cell(pair[1])), new SpatialEdgeRules(2,
                    edgeKind == SpatialEdgeKind.Flat, true, edgeKind: edgeKind, allowsFlight: allowsFlight));
            return new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(2, 16));
        }
        private static SpatialMovementPathResult Path(SpatialQueryBoard b, SpatialPose source, int q) => b.QueryMovementPath(source, Cell(q), 1000000, 10000);
        private static CombatantSnapshot Player(CombatSession s) { s.Combatants.TryGet("player", out var a); return a; }
        private static CombatCommand Flight(CombatFlightAction action) => new CombatCommand(CombatCommandKind.Flight, "player", profileId: CombatFlight.ProfileId, flightAction: action);
        private CombatActionResult Commit(CombatSession s, CombatCommand c) { var result = commands.Execute(s, c); Assert.IsTrue(result.Succeeded, result.RejectionReason); return result; }
        private void Next(CombatSession s)
        {
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, "player"));
            var next = commands.AdvanceUntilAction(s);
            while (next.ActorId == "z_enemy") { Commit(s, new CombatCommand(CombatCommandKind.EndTurn, next.ActorId)); next = commands.AdvanceUntilAction(s); }
            Assert.AreEqual("player", next.ActorId);
        }
        private sealed class Query : ICombatSpatialQuery
        {
            private readonly CombatSceneObjects environment;
            public Query(CombatSceneObjects value) => environment = value;
            public SpatialContactResult QueryRange(SpatialPose source, SpatialPose target, int min, int max, SpatialAttackPolicy policy = null) => environment.Board.QueryContact(source, target, min, max, policy);
            public CombatMovementQueryResult QueryMovement(SpatialPose source, HexCoord destination, int budget, int efficiency, IReadOnlyCollection<HexCoord> occupied, int? destinationFacing = null)
            { var path = environment.Board.QueryMovementPath(source, destination, budget, efficiency, occupied, destinationFacing); return new CombatMovementQueryResult(path.Steps.Count > 0, path.Reason, path.Steps, path.UnitsPerRange, path.ActionPointCostUnits); }
            public IReadOnlyDictionary<SpatialPose, int> FindReachable(SpatialPose source, int budget, int efficiency, IReadOnlyCollection<HexCoord> occupied) => environment.Board.FindReachableWithinActionPoints(source, budget, efficiency, occupied);
        }
    }
}
