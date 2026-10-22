using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    public sealed class F4CombatFootprintTests
    {
        private readonly CombatCommandService commands = new CombatCommandService();

        [Test]
        public void RearCellContactControlsRangeAndBackAttackWithoutTurningTheBody()
        {
            var player = Unit("player", CombatTeam.Player, new HexCoord(-2, 0));
            var beast = Unit("beast", CombatTeam.Enemy, new HexCoord(0, 0), true);
            beast.BlockRate = 100;
            var session = Ready(player, beast);
            var command = new CombatCommand(CombatCommandKind.BasicAttack, player.Id, beast.Id, "bite",
                new CombatResolutionRolls(0, 100, 0, 100));
            var quote = commands.Preview(session, command);
            Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
            Assert.AreEqual(new HexCoord(-1, 0), quote.Validation.TargetContacts.Single().Contact.TargetCell);
            var result = commands.Execute(session, command, quote);
            Assert.IsTrue(result.Succeeded, result.RejectionReason);
            Assert.IsFalse(result.Damage.Single().IsBlocked, "A rear-body contact is a back attack.");
            Assert.AreEqual(0, beast.Facing);
            Assert.AreEqual(2, beast.OccupiedCells.Count);
        }

        [Test]
        public void RangedAttackCannotPickFartherBodyCellToEscapeMinimumRange()
        {
            var player = Unit("player", CombatTeam.Player, new HexCoord(-2, 0));
            var beast = Unit("beast", CombatTeam.Enemy, new HexCoord(0, 0), true);
            var session = Ready(player, beast);
            var quote = commands.Preview(session, new CombatCommand(CombatCommandKind.Art, player.Id, beast.Id, "shot"));
            Assert.IsFalse(quote.IsLegal);
            Assert.AreEqual(SpatialQueryReasons.BelowMinimumRange, quote.RejectionReason);
        }

        [Test]
        public void AreaCoveringBothCellsDamagesOneIdentityOnce()
        {
            var player = Unit("player", CombatTeam.Player, new HexCoord(0, 0), basic: "sweep");
            var beast = Unit("beast", CombatTeam.Enemy, new HexCoord(2, 0), true);
            var session = Ready(player, beast);
            var area = session.QueryAttackArea(player.Id, "sweep", 0);
            CollectionAssert.AreEqual(new[] { beast.Id }, area.TargetIds);
            var result = commands.Execute(session, new CombatCommand(CombatCommandKind.BasicAttack, player.Id,
                profileId: "sweep", attackDirection: 0,
                targetRolls: new[] { new CombatTargetResolutionRolls(beast.Id, new CombatResolutionRolls(0, 100, 100, 100, 100)) }));
            Assert.IsTrue(result.Succeeded, result.RejectionReason);
            Assert.AreEqual(1, result.Damage.Count);
            Assert.AreEqual(beast.MaximumHealth - result.Damage[0].FinalDamage, beast.CurrentHealth);
        }

        [Test]
        public void MovingDoubleBodyUsesQuotedStepsAndChargesOnlyFrontEdges()
        {
            var actor = Unit("player", CombatTeam.Player, new HexCoord(0, 0), true);
            var target = Unit("beast", CombatTeam.Enemy, new HexCoord(4, 4));
            var session = Ready(actor, target);
            var move = new CombatCommand(CombatCommandKind.Move, actor.Id, destination: new HexCoord(2, 0), destinationFacing: 0);
            var quote = commands.Preview(session, move);
            Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
            Assert.AreEqual(40000, quote.ActionPointCostUnits);
            Assert.AreEqual(2, quote.MovementSteps.Count);
            var result = commands.Execute(session, move, quote);
            Assert.IsTrue(result.Succeeded, result.RejectionReason);
            CollectionAssert.AreEqual(quote.MovementSteps, result.MovementSteps);
            CollectionAssert.AreEquivalent(new[] { new HexCoord(2, 0), new HexCoord(1, 0) }, actor.OccupiedCells);
            Assert.AreEqual(60000, actor.ActionPointsRemainingUnits);
        }

        [Test]
        public void FacingChangesInvalidateAttackQuoteBeforeAnyPayment()
        {
            var actor = Unit("player", CombatTeam.Player, new HexCoord(-2, 0));
            var target = Unit("beast", CombatTeam.Enemy, new HexCoord(0, 0), true);
            var session = Ready(actor, target);
            var command = new CombatCommand(CombatCommandKind.BasicAttack, actor.Id, target.Id, "bite");
            var quote = commands.Preview(session, command);
            target.Facing = 1;
            var result = commands.Execute(session, command, quote);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual("combat_quote_stale", result.RejectionReason);
            Assert.AreEqual(100000, actor.ActionPointsRemainingUnits);
            Assert.AreEqual(target.MaximumHealth, target.CurrentHealth);
        }

        [Test]
        public void DeathReleasesFrontAndRearForTheSameSingleCellPlayer()
        {
            var player = Unit("player", CombatTeam.Player, new HexCoord(-2, 0));
            var beast = Unit("beast", CombatTeam.Enemy, new HexCoord(0, 0), true);
            var survivor = Unit("survivor", CombatTeam.Enemy, new HexCoord(4, 4));
            var session = Ready(player, beast, survivor);
            foreach (HexCoord cell in beast.OccupiedCells)
                Assert.IsFalse(commands.Preview(session, new CombatCommand(CombatCommandKind.Move, player.Id, destination: cell)).IsLegal);
            beast.ReceiveDamage(beast.CurrentHealth);
            foreach (HexCoord cell in beast.OccupiedCells)
                Assert.IsTrue(commands.Preview(session, new CombatCommand(CombatCommandKind.Move, player.Id, destination: cell)).IsLegal);
        }

        [Test]
        public void RangedDoubleBodyRetreatsToLegalFiringPoseAndDoesNotOverlapTarget()
        {
            var beast = Unit("beast", CombatTeam.Enemy, new HexCoord(0, 0), true, speed: 300);
            var player = Unit("player", CombatTeam.Player, new HexCoord(-2, 0));
            var session = Ready(beast, player);
            var legal = new CombatLegalActionService().GetLegalActions(session, beast.Id);
            var move = new RangedActionAI().ChooseAction(session, legal);
            Assert.AreEqual(CombatCommandKind.Move, move.Kind);
            var quote = commands.Preview(session, move);
            Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
            var finalPose = quote.MovementSteps.Last().To;
            Assert.IsFalse(finalPose.Contains(player.Position));
            Assert.IsTrue(session.SpatialQuery.QueryRange(finalPose, player.Pose, 2, 3).IsInRange);
            Assert.IsTrue(commands.Execute(session, move, quote).Succeeded);
            Assert.AreEqual(finalPose, beast.Pose);
        }

        [Test]
        public void SameAnchorCannotBeUsedAsFreeDoubleBodyTurn()
        {
            var actor = Unit("player", CombatTeam.Player, new HexCoord(0, 0), true);
            var session = Ready(actor, Unit("beast", CombatTeam.Enemy, new HexCoord(4, 4)));
            var quote = commands.Preview(session, new CombatCommand(CombatCommandKind.Move, actor.Id,
                destination: actor.Position, destinationFacing: 3));
            Assert.IsFalse(quote.IsLegal);
            Assert.AreEqual(0, actor.Facing);
            Assert.AreEqual(100000, actor.ActionPointsRemainingUnits);
        }

        [Test]
        public void ClearanceChangeInvalidatesMovementQuoteBeforePayment()
        {
            var actor = Unit("player", CombatTeam.Player, new HexCoord(0, 0), true);
            var session = Ready(actor, Unit("enemy", CombatTeam.Enemy, new HexCoord(4, 4)));
            var move = new CombatCommand(CombatCommandKind.Move, actor.Id,
                destination: new HexCoord(1, 0), destinationFacing: 0);
            var quote = commands.Preview(session, move);
            Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
            ((BoardQuery)session.SpatialQuery).SetClearance(new HexCoord(0, 0), new HexCoord(1, 0), 1);
            var result = commands.Execute(session, move, quote);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(SpatialQueryReasons.ClearanceInsufficient, result.RejectionReason);
            Assert.AreEqual(new HexCoord(0, 0), actor.Position);
            Assert.AreEqual(100000, actor.ActionPointsRemainingUnits);
        }

        [Test]
        public void ChangedSpeciesRankCannotReuseAnExistingQuote()
        {
            var actor = Unit("player", CombatTeam.Player, new HexCoord(0, 0), true);
            var session = Ready(actor, Unit("enemy", CombatTeam.Enemy, new HexCoord(4, 4)));
            var move = new CombatCommand(CombatCommandKind.Move, actor.Id, destination: new HexCoord(1, 0));
            var quote = commands.Preview(session, move);
            Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
            // Species rank is immutable in gameplay. Simulate replacement by an external data reload.
            typeof(CombatantSnapshot).GetField("<BodyScaleRank>k__BackingField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(actor, 3);
            Assert.AreEqual("combat_quote_stale", commands.Execute(session, move, quote).RejectionReason);
            Assert.AreEqual(100000, actor.ActionPointsRemainingUnits);
        }

        private CombatSession Ready(params CombatantSnapshot[] units)
        {
            var profiles = new[]
            {
                new CombatAttackProfile("bite", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, 10000, 10000, 0, 0, physicalMultiplier: 1),
                new CombatAttackProfile("shot", CombatAttackKind.Art, CombatAttackEffect.Physical, 2, 3, 10000, 10000, 0, 0, physicalMultiplier: 1),
                new CombatAttackProfile("sweep", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 2, 10000, 10000, 0, 0, physicalMultiplier: 1,
                    area: CombatAttackArea.ForwardPair),
            };
            var session = new CombatSession(units, profiles, new BoardQuery(), new[]
            {
                new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 10000, 10000, 0, 0),
                new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 10000, 10000, 0, 60),
            }, ruleSet: CombatActionPointKernelTests.Rules());
            Assert.AreEqual(units[0].Id, commands.AdvanceUntilAction(session).ActorId);
            return session;
        }

        private static CombatantSnapshot Unit(string id, CombatTeam team, HexCoord cell, bool pair = false,
            string basic = "bite", int? speed = null) => new CombatantSnapshot(id, team, cell,
                speed ?? (team == CombatTeam.Player ? 200 : 100), 1000, 1000, 100, 100, 10, 10, 100000, 5000,
                basicAttackProfileId: basic, equippedArtProfileIds: new[] { "shot" }, availableArtProfileIds: new[] { "shot" },
                footprint: pair ? SpatialFootprintKind.AdjacentPair : SpatialFootprintKind.Single, bodyScaleRank: pair ? 2 : 1);

        private sealed class BoardQuery : ICombatSpatialQuery
        {
            private SpatialQueryBoard board;
            private readonly Dictionary<HexCoord, SpatialCellRules> cells = new Dictionary<HexCoord, SpatialCellRules>();
            private readonly Dictionary<SpatialDirectedEdge, SpatialEdgeRules> edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            public BoardQuery()
            {
                for (int q = -4; q <= 4; q++) for (int r = -4; r <= 4; r++)
                    cells.Add(new HexCoord(q, r), new SpatialCellRules(0, false, false, false, 0));
                foreach (HexCoord cell in cells.Keys) foreach (HexCoord next in cell.Neighbors())
                    if (cells.ContainsKey(next)) edges.Add(new SpatialDirectedEdge(cell, next), new SpatialEdgeRules(1000, true, true));
                board = new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(1000, 16));
            }
            public void SetClearance(HexCoord from, HexCoord to, int limit)
            {
                edges[new SpatialDirectedEdge(from, to)] = new SpatialEdgeRules(1000, true, true, clearanceLimit: limit);
                board = new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(1000, 16));
            }
            public SpatialContactResult QueryRange(SpatialPose from, SpatialPose to, int min, int max) => board.QueryContact(from, to, min, max);
            public CombatMovementQueryResult QueryMovement(SpatialPose from, HexCoord to, int budget, int efficiency,
                IReadOnlyCollection<HexCoord> occupied, int? destinationFacing = null)
            {
                var path = board.QueryMovementPath(from, to, budget, efficiency, occupied, destinationFacing);
                return new CombatMovementQueryResult(path.Steps.Count > 0, path.Reason, path.Steps, path.UnitsPerRange, path.ActionPointCostUnits);
            }
            public IReadOnlyDictionary<SpatialPose, int> FindReachable(SpatialPose from, int budget, int efficiency,
                IReadOnlyCollection<HexCoord> occupied) => board.FindReachableWithinActionPoints(from, budget, efficiency, occupied);
        }
    }
}
