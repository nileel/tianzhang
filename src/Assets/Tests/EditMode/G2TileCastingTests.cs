using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    public sealed class G2TileCastingTests
    {
        private readonly CombatCommandService commands = new CombatCommandService();
        private static readonly CombatResolutionRolls Hit = new CombatResolutionRolls(0, 100, 100, 100, 100);

        [TestCase("bite", 25000, 0, 0, 0)]
        [TestCase("strike", 50000, 12, 0, 0)]
        [TestCase("shot", 50000, 10, 0, 0)]
        [TestCase("recovery", 25000, 20, 5, 8)]
        public void EmptyCellPaysTheActionWithoutCreatingAnyTargetResult(string profile, int ap, int mp, int ct, int cd)
        {
            var session = Ready();
            session.Combatants.TryGet("player", out var actor);
            actor.IsGuarding = true;
            var command = Cell(profile, new HexCoord(1, 0));
            var quote = commands.Preview(session, command);
            Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
            Assert.AreEqual(mp, quote.SpiritCost);
            Assert.AreEqual(-1, quote.AttackArea.Direction);
            Assert.IsEmpty(quote.AttackArea.TargetIds);
            Assert.AreEqual(command.TargetCell, quote.AttackArea.CellContacts.Single().TargetCell);
            Assert.IsTrue(commands.Prepare(session, command, quote, out _).Succeeded);
            Assert.AreEqual(500000, actor.ActionPointsRemainingUnits, "Preparation can be cancelled without payment.");
            Assert.AreEqual(100, actor.CurrentSpirit);
            Assert.AreEqual(0, session.PendingCtPenalty);
            var result = commands.Execute(session, command, quote);
            Assert.IsTrue(result.Succeeded, result.RejectionReason);
            Assert.AreEqual(ap, result.ActionPointSpentUnits);
            Assert.AreEqual(mp, result.SpiritSpent);
            Assert.AreEqual(500000 - ap, actor.ActionPointsRemainingUnits);
            Assert.AreEqual(100 - mp, actor.CurrentSpirit);
            Assert.AreEqual(ct, session.PendingCtPenalty);
            Assert.AreEqual(cd, actor.GetCooldown(profile));
            Assert.IsFalse(actor.IsGuarding, "A committed empty cast retains the ordinary action side effects.");
            Assert.IsEmpty(result.Damage);
            Assert.IsEmpty(result.SpiritRecoveries);
            Assert.IsEmpty(result.TargetContacts);
            Assert.AreEqual("combat_quote_stale", commands.Execute(session, command, quote).RejectionReason);
        }

        [Test]
        public void EmptyCastsUseTheExistingRepeatLedgerAndKeepTheZeroApTurnOpen()
        {
            var session = Ready();
            var command = Cell("shot", new HexCoord(1, 0));
            Assert.IsTrue(commands.Execute(session, command).Succeeded);
            Assert.IsTrue(commands.Execute(session, command).Succeeded);
            var third = commands.Preview(session, command);
            Assert.AreEqual(2, third.CommittedUseCount);
            Assert.AreEqual(1250, third.RepeatMultiplierPermille);
            Assert.AreEqual(62500, third.ActionPointCostUnits);
            Assert.IsTrue(commands.Execute(session, command, third).Succeeded);

            session = Ready(ap: 50000);
            Assert.IsTrue(commands.Execute(session, command).Succeeded);
            session.Combatants.TryGet("player", out var actor);
            Assert.AreEqual(0, actor.ActionPointsRemainingUnits);
            Assert.AreEqual(CombatTurnPhase.Active, session.TurnPhase);
            Assert.AreEqual("player", session.CurrentActorId);
        }

        [TestCase(0)] [TestCase(-1)]
        public void RecoveryOnEitherOwnBodyCellUsesSelfCostAndCommitsTheRecovery(int q)
        {
            var session = Ready(playerPair: true);
            session.Combatants.TryGet("player", out var actor);
            actor.SetSpirit(100, 0);
            var command = Cell("recovery", new HexCoord(q, 0));
            var quote = commands.Preview(session, command);
            Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
            Assert.AreEqual(0, quote.SpiritCost);
            CollectionAssert.AreEqual(new[] { actor.Id }, quote.AttackArea.TargetIds);
            Assert.AreEqual(command.TargetCell, quote.Validation.TargetContacts.Single().Contact.TargetCell);
            var result = commands.Execute(session, command, quote);
            Assert.IsTrue(result.Succeeded, result.RejectionReason);
            Assert.AreEqual(0, result.SpiritSpent);
            Assert.AreEqual(10, actor.CurrentSpirit);
            Assert.AreEqual(10, result.SpiritRecoveries.Single().ActualRecovery);
        }

        [Test]
        public void RecoveryOnAnotherUnitKeepsItsExistingTargetQualificationAndOtherCost()
        {
            var session = Ready(enemyCell: new HexCoord(1, 0));
            session.Combatants.TryGet("player", out var actor);
            session.Combatants.TryGet("enemy", out var enemy);
            enemy.SetSpirit(100, 0);
            var result = commands.Execute(session, Cell("recovery", enemy.Position));
            Assert.IsTrue(result.Succeeded, result.RejectionReason);
            Assert.AreEqual(20, result.SpiritSpent);
            Assert.AreEqual(80, actor.CurrentSpirit);
            Assert.AreEqual(5, enemy.CurrentSpirit);
            Assert.AreEqual(enemy.Id, result.SpiritRecoveries.Single().TargetId);
        }

        [TestCase(1)] [TestCase(2)]
        public void SelectedBodyCellIsTheOnlyCoveredCellAndExactDamageContact(int q)
        {
            var session = Ready(enemyCell: new HexCoord(2, 0), enemyPair: true);
            var cell = new HexCoord(q, 0);
            var command = Cell("shot", cell);
            var quote = commands.Preview(session, command);
            Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
            CollectionAssert.AreEqual(new[] { cell }, quote.AttackArea.Cells.Select(value => value.Coord));
            CollectionAssert.AreEqual(new[] { "enemy" }, quote.AttackArea.TargetIds);
            Assert.AreEqual(cell, quote.Validation.TargetContacts.Single().Contact.TargetCell);
            Assert.AreEqual(q * 1000, quote.AttackArea.CellContacts.Single().DistanceUnits);
            var result = commands.Execute(session, command, quote);
            Assert.IsTrue(result.Succeeded, result.RejectionReason);
            Assert.AreEqual(1, result.Damage.Count);
            Assert.AreEqual(cell, result.TargetContacts.Single().Contact.TargetCell);
        }

        [Test]
        public void ExplicitFarBodyCellUsesItsOwnMinimumAndMaximumRangeWhileUnitSelectionStaysNearest()
        {
            var session = Ready(enemyCell: new HexCoord(2, 0), enemyPair: true);
            Assert.AreEqual(SpatialQueryReasons.BelowMinimumRange,
                commands.Preview(session, Cell("deadzone", new HexCoord(1, 0))).RejectionReason);
            Assert.IsTrue(commands.Preview(session, Cell("deadzone", new HexCoord(2, 0))).IsLegal);
            var unitCommand = new CombatCommand(CombatCommandKind.Art, "player", "enemy", "deadzone");
            Assert.AreEqual(SpatialQueryReasons.BelowMinimumRange, commands.Preview(session, unitCommand).RejectionReason);
            Assert.IsTrue(commands.Preview(session, Cell("strike", new HexCoord(1, 0))).IsLegal);
            Assert.AreEqual(SpatialQueryReasons.AboveMaximumRange,
                commands.Preview(session, Cell("strike", new HexCoord(2, 0))).RejectionReason);
        }

        [Test]
        public void DamageAtFriendlyCellIsEmptyAndDirectionAreaStillSettlesEachUnitOnlyOnce()
        {
            var ally = Unit("ally", CombatTeam.Player, new HexCoord(1, 0));
            var session = Ready(extra: ally);
            var result = commands.Execute(session, Cell("shot", ally.Position));
            Assert.IsTrue(result.Succeeded, result.RejectionReason);
            Assert.IsEmpty(result.Damage);
            Assert.AreEqual(ally.MaximumHealth, ally.CurrentHealth);

            session = Ready(enemyCell: new HexCoord(2, 0), enemyPair: true, basic: "sweep");
            var command = new CombatCommand(CombatCommandKind.BasicAttack, "player", profileId: "sweep", attackDirection: 0,
                targetRolls: new[] { new CombatTargetResolutionRolls("enemy", Hit) }, targetCell: new HexCoord(2, 0));
            result = commands.Execute(session, command);
            Assert.IsTrue(result.Succeeded, result.RejectionReason);
            Assert.AreEqual(1, result.Damage.Count);
        }

        [Test]
        public void EmptyDirectionIsValidButIsNotAnAiAttackCandidate()
        {
            var session = Ready(basic: "sweep");
            var command = new CombatCommand(CombatCommandKind.BasicAttack, "player", profileId: "sweep", attackDirection: 0);
            Assert.IsTrue(commands.Preview(session, command).IsLegal);
            Assert.IsFalse(new CombatLegalActionService().GetLegalActions(session, "player")
                .Any(value => value.Kind == CombatCommandKind.BasicAttack));
            var result = commands.Execute(session, command);
            Assert.IsTrue(result.Succeeded, result.RejectionReason);
            Assert.IsEmpty(result.Damage);
        }

        [TestCase("missing")] [TestCase("sight")] [TestCase("effects")] [TestCase("weighted")]
        public void InvalidSpatialCellCannotConsumeResources(string cause)
        {
            var board = new TileQuery();
            var cell = new HexCoord(2, 0);
            switch (cause)
            {
                case "missing": cell = new HexCoord(99, 0); break;
                case "sight": board.SetCell(new HexCoord(1, 0), blocksSight: true); break;
                case "effects": board.SetEffects(false); break;
                case "weighted": board.SetMetric(3000); break;
            }
            var session = Ready(board);
            var result = commands.Execute(session, Cell("shot", cell));
            Assert.IsFalse(result.Succeeded);
            AssertUnspent(session);
        }

        [Test]
        public void MovementOnlyObstacleRemainsAValidCastingCell()
        {
            var board = new TileQuery();
            var cell = new HexCoord(1, 0);
            board.SetCell(cell, blocksMovement: true);
            var session = Ready(board);
            Assert.IsTrue(commands.Execute(session, Cell("shot", cell)).Succeeded);
        }

        [TestCase("cooldown")] [TestCase("seal")] [TestCase("spirit")] [TestCase("divine")]
        public void EmptyCastingDoesNotBypassExistingRestrictionsOrExpandDivine(string restriction)
        {
            var session = Ready();
            session.Combatants.TryGet("player", out var actor);
            var command = Cell("shot", new HexCoord(1, 0));
            string expected = "";
            switch (restriction)
            {
                case "cooldown": actor.SetCooldown("shot", 5); expected = "attack_profile_cooldown_active"; break;
                case "seal": actor.ApplyStatus(CombatStatusKind.SealSpirit, 1); expected = "combat_spirit_sealed"; break;
                case "spirit": actor.SetSpirit(100, 9); expected = "spirit_insufficient"; break;
                case "divine": command = new CombatCommand(CombatCommandKind.Divine, "player", profileId: "divine", targetCell: new HexCoord(1, 0)); expected = "combat_session_target_invalid"; break;
            }
            int beforeSpirit = actor.CurrentSpirit;
            Assert.AreEqual(expected, commands.Execute(session, command).RejectionReason);
            Assert.AreEqual(500000, actor.ActionPointsRemainingUnits);
            Assert.AreEqual(beforeSpirit, actor.CurrentSpirit);
            Assert.AreEqual(0, session.PendingCtPenalty);
        }

        [TestCase("cell")] [TestCase("occupant")] [TestCase("distance")] [TestCase("blocked")]
        public void EmptyQuoteRejectsChangedSelectionOccupancyAndEnvironmentBeforeCommit(string change)
        {
            var board = new TileQuery();
            var session = Ready(board);
            var command = Cell("shot", new HexCoord(1, 0));
            var quote = commands.Preview(session, command);
            Assert.IsTrue(quote.IsLegal);
            switch (change)
            {
                case "cell": command = Cell("shot", new HexCoord(2, 0)); break;
                case "occupant": session.Combatants.TryGet("enemy", out var enemy); enemy.SetPosition(new HexCoord(1, 0)); break;
                case "distance": board.SetMetric(1500); break;
                case "blocked": board.SetEffects(false); break;
            }
            Assert.AreEqual("combat_quote_stale", commands.ValidateConfirmation(session, command, quote).RejectionReason);
            Assert.AreEqual("combat_quote_stale", commands.Execute(session, command, quote).RejectionReason);
            AssertUnspent(session);
        }

        [Test]
        public void DirectionWithNoLegalCoverageOrInvalidAimCellCannotEmptyCast()
        {
            var board = new TileQuery();
            board.SetEffects(false);
            var session = Ready(board, basic: "sweep");
            Assert.IsFalse(commands.Execute(session, new CombatCommand(CombatCommandKind.BasicAttack, "player",
                profileId: "sweep", attackDirection: 0)).Succeeded);
            AssertUnspent(session);
            session = Ready(basic: "sweep");
            Assert.IsFalse(commands.Execute(session, new CombatCommand(CombatCommandKind.BasicAttack, "player",
                profileId: "sweep", attackDirection: 0, targetCell: new HexCoord(5, 0))).Succeeded);
            AssertUnspent(session);
        }

        private static CombatCommand Cell(string profile, HexCoord cell) => new CombatCommand(
            profile == "bite" ? CombatCommandKind.BasicAttack : CombatCommandKind.Art, "player", profileId: profile,
            rolls: Hit, targetCell: cell);

        private static void AssertUnspent(CombatSession session)
        {
            session.Combatants.TryGet("player", out var actor);
            Assert.AreEqual(500000, actor.ActionPointsRemainingUnits);
            Assert.AreEqual(100, actor.CurrentSpirit);
            Assert.AreEqual(0, session.PendingCtPenalty);
            Assert.IsEmpty(actor.Cooldowns);
        }

        private CombatSession Ready(TileQuery query = null, int ap = 500000, bool playerPair = false,
            HexCoord? enemyCell = null, bool enemyPair = false, string basic = "bite", CombatantSnapshot extra = null)
        {
            var units = new List<CombatantSnapshot>
            {
                Unit("player", CombatTeam.Player, new HexCoord(0, 0), playerPair, ap, basic),
                Unit("enemy", CombatTeam.Enemy, enemyCell ?? new HexCoord(4, 4), enemyPair),
            };
            if (extra != null)
            {
                units.Add(extra);
                units.Add(Unit("enemy_2", CombatTeam.Enemy, new HexCoord(5, 4)));
            }
            var profiles = new[]
            {
                new CombatAttackProfile("bite", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, 25000, 25000, 0, 0, physicalMultiplier: 1),
                new CombatAttackProfile("sweep", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 2, 40000, 40000, 0, 0, physicalMultiplier: 1, area: CombatAttackArea.ForwardPair),
                new CombatAttackProfile("strike", CombatAttackKind.Art, CombatAttackEffect.Physical, 1, 1, 50000, 25000, 0, 0, physicalMultiplier: 1, spiritCost: 12),
                new CombatAttackProfile("shot", CombatAttackKind.Art, CombatAttackEffect.Physical, 1, 5, 50000, 25000, 0, 0, physicalMultiplier: 1, spiritCost: 10),
                new CombatAttackProfile("deadzone", CombatAttackKind.Art, CombatAttackEffect.Physical, 2, 3, 50000, 25000, 0, 0, physicalMultiplier: 1),
                new CombatAttackProfile("recovery", CombatAttackKind.Art, CombatAttackEffect.RestoreSpirit, 0, 2, 25000, 12500, 5, 8,
                    spiritRecoveryMultiplier: .5f, minimumSpiritRecovery: 5, otherTargetSpiritCost: 20, otherTargetSpiritRecoveryMultiplier: .5f),
                new CombatAttackProfile("divine", CombatAttackKind.Divine, CombatAttackEffect.Physical, 1, 5, 50000, 25000, 0, 0, physicalMultiplier: 1),
            };
            var session = new CombatSession(units, profiles, query ?? new TileQuery(), CombatActionPointKernelTests.Ordinary(),
                ruleSet: CombatActionPointKernelTests.Rules());
            Assert.AreEqual("player", commands.AdvanceUntilAction(session).ActorId);
            return session;
        }

        private static CombatantSnapshot Unit(string id, CombatTeam team, HexCoord cell, bool pair = false,
            int ap = 500000, string basic = "bite")
        {
            var unit = new CombatantSnapshot(id, team, cell, id == "player" ? 200 : 100,
                1000, 1000, 100, 100, 10, 10, ap, 5000, basicAttackProfileId: basic,
                equippedArtProfileIds: new[] { "strike", "shot", "deadzone", "recovery" },
                availableArtProfileIds: new[] { "strike", "shot", "deadzone", "recovery" },
                equippedDivineProfileIds: new[] { "divine" }, availableDivineProfileIds: new[] { "divine" },
                rawSpirit: 20, footprint: pair ? SpatialFootprintKind.AdjacentPair : SpatialFootprintKind.Single,
                bodyScaleRank: pair ? 2 : 1);
            unit.SetSpirit(100, 100);
            return unit;
        }

        private sealed class TileQuery : ICombatSpatialQuery
        {
            private readonly Dictionary<HexCoord, SpatialCellRules> cells = new Dictionary<HexCoord, SpatialCellRules>();
            private SpatialQueryBoard board;
            private int metric = 1000;
            private bool effects = true;

            public TileQuery()
            {
                for (int q = -4; q <= 6; q++) for (int r = -4; r <= 6; r++)
                    cells.Add(new HexCoord(q, r), new SpatialCellRules(0, false, false, false, 0));
                Rebuild();
            }
            public void SetCell(HexCoord cell, int height = 0, bool blocksMovement = false, bool blocksSight = false)
            { cells[cell] = new SpatialCellRules(height, blocksMovement, blocksSight, false, 0); Rebuild(); }
            public void SetMetric(int value) { metric = value; Rebuild(); }
            public void SetEffects(bool value) { effects = value; Rebuild(); }
            private void Rebuild()
            {
                var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
                foreach (var cell in cells.Keys) foreach (var next in cell.Neighbors())
                    if (cells.ContainsKey(next)) edges.Add(new SpatialDirectedEdge(cell, next), new SpatialEdgeRules(metric, true, effects));
                board = new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(1000, 16));
            }
            public SpatialContactResult QueryRange(SpatialPose source, SpatialPose target, int min, int max, SpatialAttackPolicy policy = null) =>
                board.QueryContact(source, target, min, max, policy);
            public CombatMovementQueryResult QueryMovement(SpatialPose source, HexCoord destination, int budget, int efficiency,
                IReadOnlyCollection<HexCoord> occupied, int? destinationFacing = null)
            {
                var path = board.QueryMovementPath(source, destination, budget, efficiency, occupied, destinationFacing);
                return new CombatMovementQueryResult(path.Steps.Count > 0, path.Reason, path.Steps, path.UnitsPerRange, path.ActionPointCostUnits);
            }
            public IReadOnlyDictionary<SpatialPose, int> FindReachable(SpatialPose source, int budget, int efficiency,
                IReadOnlyCollection<HexCoord> occupied) => board.FindReachableWithinActionPoints(source, budget, efficiency, occupied);
        }
    }
}
