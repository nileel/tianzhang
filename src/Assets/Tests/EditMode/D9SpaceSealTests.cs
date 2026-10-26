using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    public sealed class D9SpaceSealTests
    {
        private const string Owner = "a_owner", Enemy = "z_enemy", Seat = "seal-seat", Effect = "SPACE_SEAL";
        private static readonly HexCoord From = new HexCoord(8, 12), To = new HexCoord(9, 12);
        private readonly CombatCommandService commands = new CombatCommandService();

        [Test]
        public void DeploymentIsOnePaidInstanceAndNoTriggerFeeOrExtraObject()
        {
            var s = Ready(); var owner = Actor(s, Owner);
            var command = Deploy(); var quote = commands.Preview(s, command);
            Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
            Assert.AreEqual(250000, quote.ActionPointCostUnits); Assert.AreEqual(400, quote.SpiritCost);
            Assert.IsTrue(commands.Prepare(s, command, quote, out _).Succeeded);
            Assert.IsNull(s.Jindan.Find(Seat).ActiveSeal); Assert.AreEqual(1000, owner.CurrentSpirit);
            Commit(s, command); var seal = s.Jindan.Find(Seat).ActiveSeal;
            Assert.AreEqual(200, seal.Support.Durability); Assert.AreEqual(100, seal.OuterDurability);
            Assert.AreEqual(100, seal.InnerDurability); Assert.AreEqual(1, s.SceneObjects.All.Count);
            Assert.IsFalse(seal.Support.Definition.BlocksMovement); Assert.IsFalse(seal.Support.Definition.BlocksSight);
            Assert.AreEqual(3, owner.GetCooldown(Effect));
            for (int i = 0; i < 3; i++) CombatForceMovement.Query(s, Enemy, 0, 1, 2);
            Assert.AreEqual(600, owner.CurrentSpirit); Assert.AreEqual(1, s.Jindan.Find(Seat).SuccessfulDeployments);
            Assert.IsFalse(commands.Preview(s, Deploy()).IsLegal);
        }

        [TestCase(false)] [TestCase(true)]
        public void EnemyGroundAndFlightDetourButAttacksAndSightCross(bool fly)
        {
            var s = Ready(fly: fly); Commit(s, Deploy()); var enemy = Actor(s, Enemy);
            var shortPath = s.SpatialQuery.QueryMovement(enemy.Pose, To, 5000, 20000, Occupied(s, Enemy));
            Assert.IsFalse(shortPath.IsReachable);
            var detour = s.SpatialQuery.QueryMovement(enemy.Pose, To, 20000, 20000, Occupied(s, Enemy));
            Assert.IsTrue(detour.IsReachable, detour.Reason); Assert.Greater(detour.Steps.Count, 1);
            Assert.IsFalse(detour.Steps.Any(step => step.From.Anchor == From && step.To.Anchor == To));
            Assert.IsTrue(s.SceneObjects.Board.QueryContact(new SpatialPose(From), new SpatialPose(To), 0, 1).IsInRange);
            Assert.IsTrue(s.SceneObjects.Board.QueryLineOfSight(From, To).HasLineOfSight);
            int ap = enemy.ActionPointsRemainingUnits;
            Assert.IsEmpty(CombatForceMovement.Apply(s, Enemy, 0, 1, 2).Path);
            Assert.AreEqual(ap, enemy.ActionPointsRemainingUnits); Assert.AreEqual(From, enemy.Position);
        }

        [Test]
        public void SameFactionAndReverseDirectionRemainOpen()
        {
            var s = Ready(enemyTeam: CombatTeam.Player); Commit(s, Deploy());
            var actor = Actor(s, Enemy);
            Assert.IsEmpty(CombatSealRules.BlockedEdges(s, actor));
            Assert.AreEqual(1, s.SpatialQuery.QueryMovement(actor.Pose, To, 5000, 20000,
                Occupied(s, Enemy)).Steps.Count);
            var reverse = Ready(enemyPosition: To); Commit(reverse, Deploy());
            actor = Actor(reverse, Enemy);
            Assert.AreEqual(1, reverse.SpatialQuery.QueryMovement(actor.Pose, From, 5000, 20000,
                Occupied(reverse, Enemy)).Steps.Count);
        }

        [TestCase("mana")] [TestCase("ap")] [TestCase("outside")] [TestCase("condition")] [TestCase("disabled")]
        public void InvalidDeploymentDoesNotPayOrLeaveHalfInstance(string failure)
        {
            var s = Ready(mana: failure == "mana" ? 399 : 1000, ap: failure == "ap" ? 249999 : 1000000);
            var owner = Actor(s, Owner);
            if (failure == "outside") owner.SetPosition(new HexCoord(7, 11));
            if (failure == "disabled") owner.ApplyStatus(CombatStatusKind.SealSpirit, 2);
            var command = failure == "condition" ? new CombatCommand(CombatCommandKind.JindanAbility, Owner,
                profileId: Effect, jindan: new CombatJindanRequest(Seat, new[] { To, From })) : Deploy();
            int ap = owner.ActionPointsRemainingUnits, mp = owner.CurrentSpirit;
            Assert.IsFalse(commands.Execute(s, command).Succeeded);
            Assert.AreEqual(ap, owner.ActionPointsRemainingUnits); Assert.AreEqual(mp, owner.CurrentSpirit);
            Assert.IsNull(s.Jindan.Find(Seat).ActiveSeal); Assert.IsEmpty(s.SceneObjects.All);
        }

        [Test]
        public void FirstBrokenLayerShortensOnceAndStillBlocksThenSecondLayerOpens()
        {
            var s = Ready(); Commit(s, Deploy()); Turn(s, Enemy);
            var seal = s.Jindan.Find(Seat).ActiveSeal; int tick = s.TurnScheduler.CurrentTick;
            for (int i = 0; i < 4; i++) Commit(s, Break());
            Assert.AreEqual(80, seal.Support.Durability); Assert.IsTrue(seal.OuterLayerBroken);
            Assert.AreEqual(tick + 2, seal.ExpiresTick);
            Assert.AreEqual("forced_movement_space_sealed", CombatForceMovement.Query(s, Enemy, 0, 1, 2).Reason);
            Commit(s, Break()); Assert.AreEqual(tick + 2, seal.ExpiresTick);
            Commit(s, Break());
            var destroyed = commands.Execute(s, Break()); Assert.IsTrue(destroyed.Succeeded, destroyed.RejectionReason);
            Assert.IsNull(s.Jindan.Find(Seat).ActiveSeal); Assert.IsEmpty(s.SceneObjects.All);
            Assert.AreEqual("stone_support", destroyed.SceneObjectChanges.Single().ContentType);
            Assert.AreEqual(0, destroyed.SceneObjectChanges.Single().After);
            Assert.AreEqual(1, CombatForceMovement.Query(s, Enemy, 0, 1, 2).Path.Count);
        }

        [TestCase(1000, 5, 400, "seal_expired")]
        [TestCase(599, 4, 39, "jindan_upkeep_failed")]
        public void MultiTickAdvanceMaintainsExactlyUntilExpiryOrInsufficientMana(int mana, int payments, int remaining, string reason)
        {
            var s = Ready(mana: mana, speed: 10); Commit(s, Deploy());
            int tick = s.TurnScheduler.CurrentTick;
            Turn(s, Enemy); Commit(s, new CombatCommand(CombatCommandKind.EndTurn, Enemy));
            commands.AdvanceUntilAction(s);
            Assert.AreEqual(tick + 10, s.TurnScheduler.CurrentTick);
            Assert.AreEqual(payments, s.Jindan.Find(Seat).MaintenancePayments);
            Assert.AreEqual(remaining, Actor(s, Owner).CurrentSpirit);
            Assert.AreEqual(reason, s.Jindan.Find(Seat).LastEndReason); Assert.IsNull(s.Jindan.Find(Seat).ActiveSeal);
        }

        [TestCase("leave")] [TestCase("source")] [TestCase("close")] [TestCase("finish")]
        public void EndPathsRemoveOnlyTheRuntimeGate(string kind)
        {
            var s = Ready(); Commit(s, Deploy());
            if (kind == "leave") Commit(s, new CombatCommand(CombatCommandKind.Move, Owner, destination: new HexCoord(10, 14)));
            if (kind == "source")
            { Actor(s, Owner).ApplyStatus(CombatStatusKind.SealSpirit, 2); Commit(s, new CombatCommand(CombatCommandKind.Guard, Owner)); }
            if (kind == "close") Commit(s, new CombatCommand(CombatCommandKind.CloseJindan, Owner, profileId: Effect, jindan: new CombatJindanRequest(Seat)));
            if (kind == "finish") s.Jindan.Clear(s);
            Assert.IsNull(s.Jindan.Find(Seat).ActiveSeal); Assert.IsEmpty(s.SceneObjects.All);
            Assert.AreEqual(1, s.Jindan.Seats.Count);
        }

        [Test]
        public void AIInstallsThroughTheLegalCommandAndRespectsItsDomain()
        {
            var s = Ready(); var legal = new CombatLegalActionService(commands).GetLegalActions(s, Owner);
            var chosen = new LegalActionAI().ChooseAction(s, legal);
            Assert.AreEqual(Effect, chosen.ProfileId); Commit(s, chosen);
            legal = new CombatLegalActionService(commands).GetLegalActions(s, Owner);
            chosen = new LegalActionAI().ChooseAction(s, legal);
            if (chosen.Kind == CombatCommandKind.Move)
                Assert.IsTrue(s.Jindan.Find(Seat).ActiveSeal.Definition.ContainsOwner(chosen.Destination.Value));
        }

        [Test]
        public void PairRearAndPivotUseDirectedSeals()
        {
            var board = Board(); var start = new SpatialPose(new HexCoord(9, 12), 0, SpatialFootprintKind.AdjacentPair, 2);
            var policy = SpatialMovementPolicy.Normal.WithSealedEdges(new[] { new SpatialDirectedEdge(From, To) });
            Assert.IsEmpty(board.QueryMovementPath(start, new HexCoord(10, 12), 5000, 20000, policy: policy).Steps);
            var pivot = SpatialMovementPolicy.Normal.WithSealedEdges(new[] {
                new SpatialDirectedEdge(new HexCoord(8, 12), new HexCoord(8, 13)) });
            Assert.IsEmpty(board.QueryMovementPath(start, new HexCoord(10, 11), 5000, 20000, policy: pivot).Steps);
        }

        [TestCase(1)] [TestCase(4)]
        public void SameDirectionSourcesDoNotMultiplyAndOneRemovalKeepsTheOther(int metric)
        {
            var a = new SpatialMetricOverride("a", From, To, metric);
            var b = new SpatialMetricOverride("b", To, From, metric);
            var board = Board().WithMetricOverrides(new[] { a, b });
            Assert.AreEqual(2, board.MetricOverrides.Count);
            Assert.AreEqual(metric, board.InspectEdge(From, To, SpatialQueryKind.Attack).MetricDistanceUnits);
            Assert.AreEqual(metric, board.WithMetricOverrides(new[] { b }).InspectEdge(To, From, SpatialQueryKind.Sight).MetricDistanceUnits);
        }

        private CombatSession Ready(int mana = 1000, int ap = 1000000, int speed = 100, bool fly = false,
            CombatTeam enemyTeam = CombatTeam.Enemy, HexCoord? enemyPosition = null)
        {
            var environment = new CombatSceneObjects(Board(), Array.Empty<CombatSceneObject>(), 10, 20);
            var owner = new CombatantSnapshot(Owner, CombatTeam.Player, new HexCoord(10, 13), speed, 2000, 2000,
                100, 100, 10, 10, ap, 20000, abilityInstanceIds: new Dictionary<string, string> { [Effect] = "guardian" }, realmStage: "金丹");
            owner.SetSpirit(mana, mana);
            var enemy = new CombatantSnapshot(Enemy, enemyTeam, enemyPosition ?? From, speed, 2000, 2000, 100, 100, 10, 10,
                1000000, 20000, equippedArtProfileIds: new[] { "break" }, availableArtProfileIds: new[] { "break" },
                realmStage: "金丹", movementMode: fly ? SpatialMovementMode.Fly : SpatialMovementMode.Ground, altitudeBand: fly ? 1 : 0);
            var hostile = new CombatantSnapshot("zz_observer", CombatTeam.Enemy, new HexCoord(7, 14), 1, 2000, 2000, 1, 1, 1, 1, 100000, 1000);
            var profile = new CombatAttackProfile("break", CombatAttackKind.Art, CombatAttackEffect.Physical, 0, 4,
                10000, 10000, 0, 0, physicalMultiplier: 1, spatialPolicy: new SpatialAttackPolicy(SpatialMovementMode.Ground, SpatialMovementMode.Ground, 3),
                environmentTargetTypes: new[] { "stone_support" }, environmentDamage: 30);
            var s = new CombatSession(new[] { owner, enemy, hostile }, new[] { profile }, new Query(environment),
                new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0),
                    new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) },
                ruleSet: CombatActionPointKernelTests.Rules(), sceneObjects: environment);
            s.ConfigureJindan(new CombatJindanLoadout(new[] { new CombatJindanSeatDefinition(Owner, Seat, "seal", "Transformation", "guardian", new[] { Effect }, Effect) },
                new[] { new CombatJindanAbilityDefinition(Effect, true, 250000, 400, 3, 25000, 40, 4, 3, 1, 200, 0, 6,
                    Array.Empty<int>(), "", seal: new CombatSealDefinition(From, To, "space_lock_support", 2, 100, 2, 6)) }, 1));
            Assert.AreEqual(Owner, commands.AdvanceUntilAction(s).ActorId); return s;
        }
        private static SpatialQueryBoard Board()
        {
            var cells = new Dictionary<HexCoord, SpatialCellRules>();
            for (int q = 7; q <= 10; q++) for (int r = 11; r <= 14; r++) cells.Add(new HexCoord(q, r), new SpatialCellRules(0, false, false, false, 0, true, 4));
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            foreach (var cell in cells.Keys) for (int direction = 0; direction < 6; direction++)
                if (cells.ContainsKey(cell.Step(direction))) edges.Add(new SpatialDirectedEdge(cell, cell.Step(direction)), new SpatialEdgeRules(2, true, true, allowsFlight: true));
            return new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(2, 16));
        }
        private static CombatantSnapshot Actor(CombatSession s, string id) { s.Combatants.TryGet(id, out var actor); return actor; }
        private static HexCoord[] Occupied(CombatSession s, string id) => s.Combatants.All.Where(actor => actor.IsAlive && actor.Id != id).SelectMany(actor => actor.OccupiedCells).ToArray();
        private static CombatCommand Deploy() => new CombatCommand(CombatCommandKind.JindanAbility, Owner, profileId: Effect, jindan: new CombatJindanRequest(Seat));
        private static CombatCommand Break() => new CombatCommand(CombatCommandKind.Art, Enemy, profileId: "break", targetCell: To);
        private void Commit(CombatSession s, CombatCommand command) { var result = commands.Execute(s, command); Assert.IsTrue(result.Succeeded, result.RejectionReason); }
        private void Turn(CombatSession s, string id) { Commit(s, new CombatCommand(CombatCommandKind.EndTurn, s.CurrentActorId)); Assert.AreEqual(id, commands.AdvanceUntilAction(s).ActorId); }
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
