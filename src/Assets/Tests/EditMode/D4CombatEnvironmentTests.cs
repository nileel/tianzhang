using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    public sealed class D4CombatEnvironmentTests
    {
        private readonly CombatCommandService commands = new CombatCommandService();
        private static readonly CombatResolutionRolls Hit = new CombatResolutionRolls(0, 100, 100, 100, 100);

        [Test]
        public void OneAreaDamagesEachObjectOnceAndNeverRetargetsBehindTheBrokenWall()
        {
            var session = Ready();
            var sweep = new CombatCommand(CombatCommandKind.BasicAttack, "player", profileId: "sweep", attackDirection: 0);
            var quote = commands.Preview(session, sweep);
            Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
            CollectionAssert.AreEqual(new[] { "wall" }, quote.AttackArea.EnvironmentTargetIds);
            Assert.IsEmpty(quote.AttackArea.TargetIds);
            Assert.IsTrue(commands.Prepare(session, sweep, quote, out _).Succeeded);
            Assert.AreEqual(60, session.SceneObjects.Find("wall").Durability, "Preparation/cancellation cannot publish destruction.");
            var first = commands.Execute(session, sweep, quote);
            Assert.AreEqual(1, first.SceneObjectChanges.Count);
            Assert.AreEqual(30, session.SceneObjects.Find("wall").Durability);
            Assert.IsTrue(session.SceneObjects.Find("wall").IsDamaged);
            Assert.IsEmpty(first.Damage);
            Assert.AreEqual(1, session.SceneObjects.Revision);
            var oldMove = new CombatCommand(CombatCommandKind.Move, "player", destination: new HexCoord(2, 1));
            var oldQuote = commands.Preview(session, oldMove);
            var second = commands.Execute(session, sweep);
            Assert.IsTrue(second.Succeeded, second.RejectionReason);
            Assert.IsEmpty(second.Damage, "The second action breaks the wall but keeps its original target set.");
            Assert.AreEqual(0, session.SceneObjects.Find("wall").Durability);
            Assert.AreEqual(2, session.SceneObjects.Revision);
            Assert.AreEqual("combat_quote_stale", commands.Execute(session, oldMove, oldQuote).RejectionReason);
            var third = commands.Preview(session, sweep);
            Assert.AreEqual(2, third.CommittedUseCount);
            CollectionAssert.AreEqual(new[] { "enemy" }, third.AttackArea.TargetIds);
            Assert.IsEmpty(third.AttackArea.EnvironmentTargetIds);
        }

        [Test]
        public void OrdinaryEmptyCastCannotDamageAnObjectAndPermittedArtPaysItsExistingCosts()
        {
            var session = Ready();
            var actor = session.Combatants.All.Single(value => value.Id == "player");
            var empty = new CombatCommand(CombatCommandKind.Art, "player", profileId: "ordinary", targetCell: new HexCoord(1, 0));
            var result = commands.Execute(session, empty);
            Assert.IsTrue(result.Succeeded, result.RejectionReason);
            Assert.IsEmpty(result.SceneObjectChanges);
            Assert.AreEqual(60, session.SceneObjects.Find("wall").Durability);
            Assert.IsEmpty(result.Damage);
            var strike = new CombatCommand(CombatCommandKind.Art, "player", profileId: "strike", targetCell: new HexCoord(1, 0));
            int beforeAp = actor.ActionPointsRemainingUnits, beforeMp = actor.CurrentSpirit;
            result = commands.Execute(session, strike);
            Assert.IsTrue(result.Succeeded, result.RejectionReason);
            Assert.AreEqual(50000, beforeAp - actor.ActionPointsRemainingUnits);
            Assert.AreEqual(12, beforeMp - actor.CurrentSpirit);
            Assert.AreEqual(30, result.SceneObjectChanges.Single().Damage);
            Assert.IsEmpty(result.Damage);
        }

        [Test]
        public void AreaHitsOnePairUnitOnce()
        {
            var session = Ready(wall: false, pair: true);
            var command = new CombatCommand(CombatCommandKind.BasicAttack, "player", profileId: "sweep", attackDirection: 0,
                targetRolls: new[] { new CombatTargetResolutionRolls("enemy", Hit) });
            var quote = commands.Preview(session, command);
            CollectionAssert.AreEqual(new[] { "enemy" }, quote.AttackArea.TargetIds);
            Assert.AreEqual(1, commands.Execute(session, command, quote).Damage.Count);
        }

        [Test]
        public void CoverChangesHitChanceButNotDamageAndHeightDoesNotAddDamage()
        {
            var covered = Ready(wall: false, cover: true);
            var clear = Ready(wall: false);
            var missRoll = new CombatResolutionRolls(90, 100, 100, 100, 100);
            var aim = new CombatCommand(CombatCommandKind.Art, "player", "enemy", "shot", missRoll);
            Assert.AreEqual(2, commands.Preview(covered, aim).Validation.TargetContacts.Single().Contact.CoverTier);
            Assert.IsFalse(commands.Execute(covered, aim).Damage.Single().IsHit);
            Assert.IsTrue(commands.Execute(clear, aim).Damage.Single().IsHit);
            covered = Ready(wall: false, cover: true);
            clear = Ready(wall: false);
            var high = Ready(wall: false, cover: true, sourceHeight: 1);
            aim = new CombatCommand(CombatCommandKind.Art, "player", "enemy", "shot", Hit);
            int damage = commands.Execute(clear, aim).Damage.Single().FinalDamage;
            Assert.AreEqual(damage, commands.Execute(covered, aim).Damage.Single().FinalDamage);
            Assert.AreEqual(0, commands.Preview(high, aim).Validation.TargetContacts.Single().Contact.CoverTier);
            Assert.AreEqual(damage, commands.Execute(high, aim).Damage.Single().FinalDamage);
        }

        private CombatSession Ready(bool wall = true, bool pair = false, bool cover = false, int sourceHeight = 0)
        {
            var cells = new Dictionary<HexCoord, SpatialCellRules>();
            for (int q = -1; q <= 4; q++) for (int r = -1; r <= 2; r++)
                cells.Add(new HexCoord(q, r), new SpatialCellRules(q == 0 && r == 0 ? sourceHeight : 0, false, false, false, 0));
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            foreach (var cell in cells.Keys) foreach (var next in cell.Neighbors())
                if (cells.ContainsKey(next)) edges.Add(new SpatialDirectedEdge(cell, next), new SpatialEdgeRules(2, true, true,
                    edgeKind: cells[cell].HeightLevel == cells[next].HeightLevel ? SpatialEdgeKind.Flat : SpatialEdgeKind.OpenCliff));
            var objects = new List<CombatSceneObject>();
            if (wall || cover) objects.Add(new CombatSceneObject("wall", new CombatSceneObjectDefinition("high_wall", "stone", 60,
                cover ? 2 : 0, 2, wall, wall, "rubble"), new HexCoord(1, 0), new HexCoord(2, 0)));
            var environment = new CombatSceneObjects(new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(2, 16)), objects, 10, 20);
            var actor = new CombatantSnapshot("player", CombatTeam.Player, new HexCoord(0, 0), 200,
                1000, 1000, 100, 100, 10, 10, 500000, 20, basicAttackProfileId: "sweep",
                equippedArtProfileIds: new[] { "ordinary", "strike", "shot" }, availableArtProfileIds: new[] { "ordinary", "strike", "shot" });
            actor.SetSpirit(100, 100);
            var enemy = new CombatantSnapshot("enemy", CombatTeam.Enemy, new HexCoord(2, 0), 100,
                1000, 1000, 100, 100, 10, 10, 500000, 20,
                facing: pair ? 0 : 3, footprint: pair ? SpatialFootprintKind.AdjacentPair : SpatialFootprintKind.Single, bodyScaleRank: pair ? 2 : 1);
            var policy = new SpatialAttackPolicy(SpatialMovementMode.Ground, SpatialMovementMode.Ground, 1);
            var profiles = new[] {
                new CombatAttackProfile("sweep", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 2, 40000, 40000, 0, 0,
                    physicalMultiplier: 1, area: CombatAttackArea.ForwardPair, spatialPolicy: policy, environmentTargetTypes: new[] { "high_wall" }, environmentDamage: 30),
                new CombatAttackProfile("ordinary", CombatAttackKind.Art, CombatAttackEffect.Physical, 1, 1, 50000, 25000, 0, 0, physicalMultiplier: 1, spatialPolicy: policy),
                new CombatAttackProfile("strike", CombatAttackKind.Art, CombatAttackEffect.Physical, 1, 1, 50000, 25000, 0, 0,
                    physicalMultiplier: 1.2f, spiritCost: 12, spatialPolicy: policy, environmentTargetTypes: new[] { "high_wall" }, environmentDamage: 30),
                new CombatAttackProfile("shot", CombatAttackKind.Art, CombatAttackEffect.Physical, 1, 4, 50000, 25000, 0, 0, physicalMultiplier: 1, spatialPolicy: policy),
            };
            var session = new CombatSession(new[] { actor, enemy }, profiles, new Query(environment),
                CombatActionPointKernelTests.Ordinary(), ruleSet: CombatActionPointKernelTests.Rules(), sceneObjects: environment);
            Assert.AreEqual("player", commands.AdvanceUntilAction(session).ActorId);
            return session;
        }

        private sealed class Query : ICombatSpatialQuery
        {
            private readonly CombatSceneObjects environment;
            public Query(CombatSceneObjects value) => environment = value;
            public SpatialContactResult QueryRange(SpatialPose source, SpatialPose target, int min, int max, SpatialAttackPolicy policy = null) =>
                environment.Board.QueryContact(source, target, min, max, policy);
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
