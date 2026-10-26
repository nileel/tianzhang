using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    public sealed class D5JindanLedgerTests
    {
        private const string Scale = "SPACE_SCALE", Body = "scale_body";
        private const string Earth = "EARTH_MATERIALIZE", Gravity = "EARTH_GRAVITY";
        private const string Dormant = "fixture_dormant_scale";
        private const string SeatId = "demo-seat", Instance = "demo-guardian", EnemyId = "z_enemy";
        private static HexCoord Cell(int q) => new HexCoord(q, 0);
        private readonly CombatCommandService commands = new CombatCommandService();

        [Test]
        public void PreviewAndPreparationDoNotPayThenDeploymentPaysOneSharedReceipt()
        {
            var s = Ready(); var actor = Player(s); var seat = s.Jindan.Find(SeatId);
            var command = Deploy(); var quote = commands.Preview(s, command);
            Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
            Assert.AreEqual(250000, quote.ActionPointCostUnits);
            Assert.AreEqual(400, quote.SpiritCost);
            Assert.AreEqual(Instance, quote.AbilityInstanceId);
            Assert.IsTrue(commands.Prepare(s, command, quote, out _).Succeeded);
            Assert.AreEqual(500000, actor.ActionPointsRemainingUnits);
            Assert.AreEqual(1000, actor.CurrentSpirit);
            Assert.IsNull(seat.ActiveScale); Assert.IsEmpty(s.SceneObjects.All);
            int tick = s.TurnScheduler.CurrentTick;
            var result = Commit(s, command);
            Assert.AreEqual(250000, result.ActionPointSpentUnits);
            Assert.AreEqual(400, result.SpiritSpent);
            Assert.AreEqual(250000, actor.ActionPointsRemainingUnits);
            Assert.AreEqual(600, actor.CurrentSpirit);
            Assert.AreEqual(1, seat.SuccessfulDeployments);
            Assert.AreEqual(400, seat.TotalManaSpent);
            Assert.AreEqual(250000, seat.TotalActionPointSpent);
            Assert.AreEqual(0, seat.MaintenancePayments);
            Assert.AreEqual(tick, seat.ActiveScale.CreatedTick);
            Assert.AreEqual(1, actor.GetCooldown(Scale));
            Assert.AreEqual(1, actor.GetCooldown(Body), "Main and body share the same source cooldown.");
            Assert.AreEqual(200, seat.ActiveScale.Support.Durability);
            Assert.AreEqual("stone_support", seat.ActiveScale.Support.Definition.ContentType);
            for (int i = 0; i < 3; i++) commands.Preview(s, new CombatCommand(CombatCommandKind.Guard, "player"));
            Commit(s, new CombatCommand(CombatCommandKind.Guard, "player"));
            Assert.AreEqual(tick, s.TurnScheduler.CurrentTick);
            Assert.AreEqual(600, actor.CurrentSpirit);
            Assert.AreEqual(0, seat.MaintenancePayments);
        }

        [Test]
        public void SameTickOtherActorDoesNotMaintainButNextGlobalTickPaysFortyOnce()
        {
            var s = Ready(enemySpeed: 100); var seat = s.Jindan.Find(SeatId);
            Commit(s, Deploy()); int tick = s.TurnScheduler.CurrentTick;
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, "player"));
            var next = commands.AdvanceUntilAction(s);
            Assert.AreEqual(EnemyId, next.ActorId); Assert.AreEqual(0, next.TicksElapsed);
            Assert.AreEqual(tick, s.TurnScheduler.CurrentTick);
            Assert.AreEqual(600, Player(s).CurrentSpirit); Assert.AreEqual(0, seat.MaintenancePayments);
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, EnemyId));
            next = commands.AdvanceUntilAction(s);
            Assert.AreEqual("player", next.ActorId); Assert.AreEqual(1, next.TicksElapsed);
            Assert.AreEqual(560, Player(s).CurrentSpirit); Assert.AreEqual(1, seat.MaintenancePayments);
            Assert.AreEqual(tick + 1, seat.ActiveScale.LastPaidTick);
            commands.AdvanceUntilAction(s);
            Assert.AreEqual(560, Player(s).CurrentSpirit);
            Assert.AreEqual(0, Player(s).GetCooldown(Body));
        }

        [Test]
        public void MultiTickAdvanceChargesEachTickAndFailedUpkeepNeverPartiallyDebits()
        {
            var s = Ready(mana: 599, playerSpeed: 20); var seat = s.Jindan.Find(SeatId);
            Commit(s, Deploy());
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, "player"));
            Assert.AreEqual(5, commands.AdvanceUntilAction(s).TicksElapsed);
            Assert.AreEqual(39, Player(s).CurrentSpirit);
            Assert.AreEqual(4, seat.MaintenancePayments);
            Assert.AreEqual(560, seat.TotalManaSpent);
            Assert.IsNull(seat.ActiveScale);
            Assert.AreEqual("jindan_upkeep_failed", seat.LastEndReason);
            Assert.IsEmpty(s.SceneObjects.All);
            Assert.AreEqual(2, s.SceneObjects.Board.InspectEdge(Cell(1), Cell(2), SpatialQueryKind.Attack).MetricDistanceUnits);
        }

        [TestCase("mana")]
        [TestCase("ap")]
        [TestCase("bad_mode")]
        [TestCase("bad_edge")]
        [TestCase("range")]
        [TestCase("occupied_remote")]
        [TestCase("sealed")]
        public void RejectedDeploymentLeavesResourcesCooldownObjectsAndReceiptsUntouched(string failure)
        {
            var s = Ready(mana: failure == "mana" ? 399 : 1000, ap: failure == "ap" ? 249999 : 500000);
            var actor = Player(s); var seat = s.Jindan.Find(SeatId);
            if (failure == "sealed") actor.ApplyStatus(CombatStatusKind.SealSpirit, 1);
            var request = failure switch
            {
                "bad_mode" => new CombatJindanRequest(SeatId, new[] { Cell(1), Cell(2) }, 2),
                "bad_edge" => new CombatJindanRequest(SeatId, new[] { Cell(1), Cell(3) }, 1),
                "range" => new CombatJindanRequest(SeatId, new[] { Cell(7), Cell(8) }, 1),
                "occupied_remote" => new CombatJindanRequest(SeatId, new[] { Cell(1), Cell(2) }, 1, Cell(0)),
                _ => new CombatJindanRequest(SeatId, new[] { Cell(1), Cell(2) }, 1),
            };
            int ap = actor.ActionPointsRemainingUnits, mp = actor.CurrentSpirit;
            long state = s.StateVersion, revision = s.SceneObjects.Revision;
            Assert.IsFalse(commands.Execute(s, new CombatCommand(CombatCommandKind.JindanAbility,
                "player", profileId: Scale, jindan: request)).Succeeded);
            Assert.AreEqual(ap, actor.ActionPointsRemainingUnits); Assert.AreEqual(mp, actor.CurrentSpirit);
            Assert.AreEqual(state, s.StateVersion); Assert.AreEqual(revision, s.SceneObjects.Revision);
            Assert.AreEqual(0, actor.GetCooldown(Scale)); Assert.AreEqual(0, seat.TotalManaSpent);
            Assert.AreEqual(0, seat.TotalActionPointSpent); Assert.AreEqual(0, seat.SuccessfulDeployments);
            Assert.IsNull(seat.ActiveScale); Assert.IsEmpty(s.SceneObjects.All);
        }

        [Test]
        public void OldQuoteAndChangedSelectionCannotCommitOrPay()
        {
            var s = Ready(); var command = Deploy(); var quote = commands.Preview(s, command);
            var changed = Deploy(metric: 4);
            Assert.AreEqual("combat_quote_stale", commands.Execute(s, changed, quote).RejectionReason);
            Assert.AreEqual(1000, Player(s).CurrentSpirit);
            Commit(s, new CombatCommand(CombatCommandKind.Guard, "player"));
            int ap = Player(s).ActionPointsRemainingUnits;
            Assert.AreEqual("combat_quote_stale", commands.Execute(s, command, quote).RejectionReason);
            Assert.AreEqual(ap, Player(s).ActionPointsRemainingUnits);
            Assert.AreEqual(1000, Player(s).CurrentSpirit); Assert.IsNull(s.Jindan.Find(SeatId).ActiveScale);
        }

        [Test]
        public void ExistingInstanceConsumesCapacityAndCloseKeepsCooldownAndCountsAsBenefit()
        {
            var s = Ready(); Commit(s, Deploy());
            Assert.IsFalse(commands.Execute(s, Deploy()).Succeeded);
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, "player"));
            Assert.AreEqual("player", commands.AdvanceUntilAction(s).ActorId);
            Assert.AreEqual("jindan_capacity_full", commands.Execute(s, Deploy()).RejectionReason);
            var result = Commit(s, Close());
            Assert.AreEqual(25000, result.ActionPointSpentUnits); Assert.AreEqual(0, result.SpiritSpent);
            Assert.AreEqual("combat_wait_after_benefit",
                commands.Execute(s, new CombatCommand(CombatCommandKind.Wait, "player")).RejectionReason);
            Assert.AreEqual("jindan_instance_missing", commands.Execute(s, Close()).RejectionReason);
            Assert.AreEqual(1, s.Jindan.Find(SeatId).SuccessfulDeployments);
            s = Ready(); Commit(s, Deploy()); Commit(s, Close());
            Assert.AreEqual(1, Player(s).GetCooldown(Scale));
            Assert.AreEqual(1, Player(s).GetCooldown(Body));
            Assert.AreEqual("combat_ability_on_cooldown", commands.Execute(s, Deploy()).RejectionReason);
            Assert.IsEmpty(s.SceneObjects.All);
        }

        [Test]
        public void RemoteOriginAddsFiftyManaAndDoesNotMoveActorOrCreateASecondPool()
        {
            var s = Ready(); var command = Deploy(remote: Cell(4), path: new[] { Cell(7), Cell(8) });
            var quote = commands.Preview(s, command);
            Assert.IsTrue(quote.IsLegal, quote.RejectionReason); Assert.AreEqual(450, quote.SpiritCost);
            Commit(s, command);
            Assert.AreEqual(Cell(0), Player(s).Position); Assert.AreEqual(550, Player(s).CurrentSpirit);
            Assert.AreEqual(Cell(4), s.Jindan.Find(SeatId).ActiveScale.Origin);
        }

        [Test]
        public void StoneSupportUsesEnvironmentPermissionAndDestructionEndsOnlyItsRule()
        {
            var s = Ready(); Commit(s, Deploy()); var seat = s.Jindan.Find(SeatId);
            string id = seat.ActiveScale.SupportId;
            Commit(s, Attack("ordinary", Cell(1)));
            Assert.AreEqual(200, s.SceneObjects.Find(id).Durability);
            var strike = Attack("break-support", Cell(1));
            var quote = commands.Preview(s, strike);
            CollectionAssert.AreEqual(new[] { id }, quote.AttackArea.EnvironmentTargetIds);
            var result = Commit(s, strike);
            Assert.AreEqual(200, result.SceneObjectChanges.Single().Damage);
            Assert.IsNull(seat.ActiveScale); Assert.IsNull(s.SceneObjects.Find(id));
            Assert.AreEqual("jindan_support_destroyed", seat.LastEndReason);
            Assert.AreEqual(2, s.SceneObjects.Board.InspectEdge(Cell(1), Cell(2), SpatialQueryKind.Attack).MetricDistanceUnits);
            Assert.AreEqual(1, seat.SuccessfulDeployments); Assert.AreEqual(400, seat.TotalManaSpent);
        }

        [Test]
        public void SupportCanShareAnExistingNonblockingEdgeAndItsDestructionKeepsOriginalObject()
        {
            var residue = new CombatSceneObject("old-residue", new CombatSceneObjectDefinition("low_wall", "stone",
                30, 0, 0, false, false, "rubble"), Cell(1), Cell(2));
            var s = Ready(original: residue);
            Assert.IsTrue(commands.Preview(s, Deploy()).IsLegal);
            Commit(s, Deploy());
            Assert.AreEqual(2, s.SceneObjects.All.Count);
            Commit(s, Attack("break-support", Cell(1)));
            Assert.IsNull(s.Jindan.Find(SeatId).ActiveScale);
            Assert.AreEqual(30, s.SceneObjects.Find("old-residue").Durability);
            Assert.AreEqual(1, s.SceneObjects.All.Count);
        }

        [TestCase("owner")]
        [TestCase("opponent")]
        [TestCase("withdrawal")]
        public void TerminalCleanupAndFreshSessionDoNotReuseSupportReceiptsOrCooldown(string ending)
        {
            var s = Ready(); Commit(s, Deploy());
            if (ending == "withdrawal")
                typeof(CombatSession).GetMethod("Terminate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(s, null);
            else
            {
                s.Combatants.TryGet(ending == "owner" ? "player" : EnemyId, out var dead);
                dead.ReceiveDamage(dead.MaximumHealth);
                commands.AdvanceUntilAction(s);
            }
            Assert.IsNull(s.Jindan.Find(SeatId).ActiveScale); Assert.IsEmpty(s.SceneObjects.All);
            Assert.IsEmpty(s.SceneObjects.Board.MetricOverrides);
            var fresh = Ready(); var seat = fresh.Jindan.Find(SeatId);
            Assert.AreEqual(500000, Player(fresh).ActionPointsRemainingUnits);
            Assert.AreEqual(1000, Player(fresh).CurrentSpirit);
            Assert.AreEqual(0, Player(fresh).GetCooldown(Scale));
            Assert.AreEqual(0, seat.SuccessfulDeployments); Assert.AreEqual(0, seat.MaintenancePayments);
            Assert.AreEqual(0, seat.TotalManaSpent); Assert.IsNull(seat.ActiveScale);
        }

        [Test]
        public void EarthSwitchPaysTheUnusedWholeTurnPreservesSourceCooldownAndLocksUntilNextOwnTurn()
        {
            var s = Ready(earth: true); var actor = Player(s); var seat = s.Jindan.Find(SeatId);
            actor.SetCooldown(Earth, 7);
            var swap = new CombatCommand(CombatCommandKind.SwapJindan, "player", profileId: Gravity,
                jindan: new CombatJindanRequest(SeatId));
            var quote = commands.Preview(s, swap);
            Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
            Assert.AreEqual(500000, quote.ActionPointCostUnits); Assert.AreEqual(0, quote.SpiritCost);
            Assert.AreEqual(Earth, seat.EffectId); Assert.AreEqual(500000, actor.ActionPointsRemainingUnits);
            long turn = s.TurnSequence; int ownTurn = actor.OwnTurnsStarted, tick = s.TurnScheduler.CurrentTick;
            Commit(s, swap);
            Assert.AreEqual(0, actor.ActionPointsRemainingUnits); Assert.AreEqual(1000, actor.CurrentSpirit);
            Assert.AreEqual(Gravity, seat.EffectId); Assert.AreEqual(turn, seat.LastSwitchTurn);
            Assert.AreEqual(ownTurn, seat.LockedOwnTurn); Assert.AreEqual(turn, s.TurnSequence);
            Assert.AreEqual(CombatTurnPhase.Active, s.TurnPhase); Assert.AreEqual(tick, s.TurnScheduler.CurrentTick);
            Assert.AreEqual(7, actor.GetCooldown(Earth)); Assert.AreEqual(7, actor.GetCooldown(Gravity));
            Assert.IsNull(seat.ActiveScale); Assert.AreEqual(0, seat.SuccessfulDeployments);
            var back = new CombatCommand(CombatCommandKind.SwapJindan, "player", profileId: Earth,
                jindan: new CombatJindanRequest(SeatId));
            Assert.IsFalse(commands.Execute(s, back).Succeeded);
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, "player"));
            Assert.AreEqual("player", commands.AdvanceUntilAction(s).ActorId);
            Assert.Greater(actor.OwnTurnsStarted, seat.LockedOwnTurn);
            Assert.AreEqual(6, actor.GetCooldown(Gravity));
            Commit(s, back);
            Assert.AreEqual(6, actor.GetCooldown(Earth)); Assert.AreEqual(1000000, seat.TotalActionPointSpent);
        }

        [Test]
        public void BenefitCommandBlocksEarthSwitchEvenIfActionPointsAreRefundedToFull()
        {
            var s = Ready(earth: true); var actor = Player(s);
            Commit(s, new CombatCommand(CombatCommandKind.Guard, "player"));
            // Model a legitimate later refund without erasing the session's successful-benefit flag.
            typeof(CombatantSnapshot).GetMethod("SpendActionPoints", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(actor, new object[] { -50000 });
            Assert.AreEqual(actor.ActionPointLimitAtTurnStartUnits, actor.ActionPointsRemainingUnits);
            var swap = new CombatCommand(CombatCommandKind.SwapJindan, "player", profileId: Gravity,
                jindan: new CombatJindanRequest(SeatId));
            Assert.AreEqual("jindan_switch_requires_unused_full_turn", commands.Execute(s, swap).RejectionReason);
            Assert.AreEqual(Earth, s.Jindan.Find(SeatId).EffectId);
            Assert.AreEqual(1000, actor.CurrentSpirit);
        }

        [Test]
        public void SeatDefinitionsRejectDuplicatePositionOrSlotOrSourceAndReconfiguration()
        {
            var first = Seat(SeatId, "position", "SOURCE", Instance);
            foreach (var second in new[] { Seat("b", "position", "DOMAIN", "other"),
                Seat("b", "other-position", "SOURCE", "other"), Seat("b", "other-position", "DOMAIN", Instance) })
                Assert.Throws<ArgumentException>(() => new CombatJindanLoadout(new[] { first, second }, Definitions(false), 1));
            var s = Ready(start: false);
            Assert.Throws<InvalidOperationException>(() => s.ConfigureJindan(new CombatJindanLoadout(new[] { first }, Definitions(false), 1)));
        }

        [Test]
        public void ConfigureRejectsAnUnownedGuardianBindingBeforeStartingTheSession()
        {
            var s = Ready(start: false, configure: false);
            var wrong = Seat(SeatId, "position", "SOURCE", "not-the-actors-source");
            Assert.Throws<ArgumentException>(() => s.ConfigureJindan(
                new CombatJindanLoadout(new[] { wrong }, Definitions(false), 1)));
            Assert.IsNull(s.Jindan); Assert.AreEqual(0, s.TurnSequence);
        }

        [Test]
        public void SwitchingIntoEnabledScaleCannotDeployBeforeNextOwnTurn()
        {
            // Core lifecycle fixture: content compatibility itself is checked by the content importer.
            var s = Ready(initiallyDormant: true);
            Commit(s, new CombatCommand(CombatCommandKind.SwapJindan, "player", profileId: Scale,
                jindan: new CombatJindanRequest(SeatId)));
            Assert.AreEqual("jindan_effect_wait_next_own_turn", commands.Preview(s, Deploy()).RejectionReason);
            Assert.IsNull(s.Jindan.Find(SeatId).ActiveScale);
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, "player"));
            Assert.AreEqual("player", commands.AdvanceUntilAction(s).ActorId);
            Assert.IsTrue(commands.Preview(s, Deploy()).IsLegal);
            Commit(s, Deploy());
            Assert.AreEqual(1, s.Jindan.Find(SeatId).SuccessfulDeployments);
        }

        [Test]
        public void SourceDisableEndsTheExistingScaleWithoutChargingMaintenance()
        {
            var s = Ready(); Commit(s, Deploy());
            Player(s).ApplyStatus(CombatStatusKind.SealSpirit, 1);
            Commit(s, new CombatCommand(CombatCommandKind.Guard, "player"));
            var seat = s.Jindan.Find(SeatId);
            Assert.IsNull(seat.ActiveScale); Assert.AreEqual("jindan_source_disabled", seat.LastEndReason);
            Assert.AreEqual(600, Player(s).CurrentSpirit); Assert.AreEqual(0, seat.MaintenancePayments);
            Assert.IsEmpty(s.SceneObjects.All); Assert.IsEmpty(s.SceneObjects.Board.MetricOverrides);
        }

        private CombatSession Ready(int mana = 1000, int ap = 500000, int playerSpeed = 100,
            int enemySpeed = 1, bool earth = false, CombatSceneObject original = null, bool start = true,
            bool configure = true, bool initiallyDormant = false)
        {
            var cells = new Dictionary<HexCoord, SpatialCellRules>();
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            for (int q = 0; q <= 8; q++) cells.Add(Cell(q), new SpatialCellRules(0, false, false, false, 0));
            for (int q = 0; q < 8; q++)
            {
                edges.Add(new SpatialDirectedEdge(Cell(q), Cell(q + 1)), new SpatialEdgeRules(2, true, true));
                edges.Add(new SpatialDirectedEdge(Cell(q + 1), Cell(q)), new SpatialEdgeRules(2, true, true));
            }
            var environment = new CombatSceneObjects(new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(2, 16)),
                original == null ? Array.Empty<CombatSceneObject>() : new[] { original }, 10, 20);
            string[] candidates = earth ? new[] { Earth, Gravity } : initiallyDormant ? new[] { Dormant, Scale } : new[] { Scale };
            var binding = candidates.Concat(new[] { Body }).ToDictionary(id => id, id => Instance);
            var actor = new CombatantSnapshot("player", CombatTeam.Player, Cell(0), playerSpeed, 1000, 1000,
                100, 100, 10, 10, ap, 20000, equippedArtProfileIds: new[] { "ordinary", "break-support" },
                availableArtProfileIds: new[] { "ordinary", "break-support" }, abilityInstanceIds: binding,
                realmStage: "金丹", rawMind: 10);
            actor.SetSpirit(mana, mana);
            var enemy = new CombatantSnapshot(EnemyId, CombatTeam.Enemy, Cell(6), enemySpeed, 1000, 1000,
                20, 20, 10, 10, 500000, 20000, realmStage: "金丹", rawMind: 10);
            var policy = new SpatialAttackPolicy(SpatialMovementMode.Ground, SpatialMovementMode.Ground, 3);
            var profiles = new[] {
                new CombatAttackProfile("ordinary", CombatAttackKind.Art, CombatAttackEffect.Physical,
                    0, 8, 10000, 10000, 0, 1, physicalMultiplier: 1, spatialPolicy: policy),
                new CombatAttackProfile("break-support", CombatAttackKind.Art, CombatAttackEffect.Physical,
                    0, 8, 20000, 20000, 0, 1, physicalMultiplier: 1, spatialPolicy: policy,
                    environmentTargetTypes: new[] { "stone_support" }, environmentDamage: 200),
            };
            var ordinary = new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0),
                new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) };
            var session = new CombatSession(new[] { actor, enemy }, profiles, new Query(environment), ordinary, sceneObjects: environment);
            if (configure) session.ConfigureJindan(new CombatJindanLoadout(new[] {
                new CombatJindanSeatDefinition("player", SeatId, "position", "SOURCE", Instance, candidates, candidates[0]) },
                initiallyDormant ? new[] { Definition(Dormant, false), Definition(Scale, true) } : Definitions(earth), 1));
            if (start) Assert.AreEqual("player", commands.AdvanceUntilAction(session).ActorId);
            return session;
        }

        private static CombatJindanSeatDefinition Seat(string id, string position, string type, string source) =>
            new CombatJindanSeatDefinition("player", id, position, type, source, new[] { Scale }, Scale);
        private static CombatJindanAbilityDefinition[] Definitions(bool earth) => earth
            ? new[] { Definition(Earth, false), Definition(Gravity, false) } : new[] { Definition(Scale, true) };
        private static CombatJindanAbilityDefinition Definition(string id, bool enabled) =>
            new CombatJindanAbilityDefinition(id, enabled, 250000, 400, 1, 25000, 40, 4, 3, 2, 200, 50, 6,
                new[] { 1, 4 }, "jindan_effect_not_implemented");
        private static CombatantSnapshot Player(CombatSession s)
        { s.Combatants.TryGet("player", out var actor); return actor; }
        private static CombatCommand Deploy(int metric = 1, HexCoord? remote = null, HexCoord[] path = null) =>
            new CombatCommand(CombatCommandKind.JindanAbility, "player", profileId: Scale,
                jindan: new CombatJindanRequest(SeatId, path ?? new[] { Cell(1), Cell(2), Cell(3) }, metric, remote));
        private static CombatCommand Close() => new CombatCommand(CombatCommandKind.CloseJindan, "player",
            profileId: Scale, jindan: new CombatJindanRequest(SeatId));
        private static CombatCommand Attack(string profile, HexCoord target) =>
            new CombatCommand(CombatCommandKind.Art, "player", profileId: profile, targetCell: target,
                rolls: new CombatResolutionRolls(0, 100, 100, 100, 100));
        private CombatActionResult Commit(CombatSession session, CombatCommand command)
        { var result = commands.Execute(session, command); Assert.IsTrue(result.Succeeded, result.RejectionReason); return result; }

        private sealed class Query : ICombatSpatialQuery
        {
            private readonly CombatSceneObjects environment;
            public Query(CombatSceneObjects value) => environment = value;
            public SpatialContactResult QueryRange(SpatialPose source, SpatialPose target, int min, int max,
                SpatialAttackPolicy policy = null) => environment.Board.QueryContact(source, target, min, max, policy);
            public CombatMovementQueryResult QueryMovement(SpatialPose source, HexCoord destination, int budget, int efficiency,
                IReadOnlyCollection<HexCoord> occupied, int? destinationFacing = null)
            {
                var path = environment.Board.QueryMovementPath(source, destination, budget, efficiency, occupied, destinationFacing);
                return new CombatMovementQueryResult(path.Steps.Count > 0, path.Reason, path.Steps, path.UnitsPerRange, path.ActionPointCostUnits);
            }
            public IReadOnlyDictionary<SpatialPose, int> FindReachable(SpatialPose source, int budget, int efficiency,
                IReadOnlyCollection<HexCoord> occupied) => environment.Board.FindReachableWithinActionPoints(source, budget, efficiency, occupied);
        }
    }
}
