using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    /// <summary>Melee approach decisions over the authoritative legal-action list.</summary>
    public sealed class EnemyAITests
    {
        private readonly CombatCommandService commands = new CombatCommandService();

        [Test]
        public void MeleeAdvancesTowardTheTargetInsteadOfSmallestQ()
        {
            // The old ordering moved to the smallest Q; here that walks away from the player.
            CombatSession session = Session(new HexCoord(2, 2), new HexCoord(4, 2));
            CombatCommand chosen = Choose(session, "enemy");
            Assert.AreEqual(CombatCommandKind.Move, chosen.Kind);
            Assert.AreEqual(new HexCoord(3, 2), chosen.Destination.Value);
        }

        [Test]
        public void MeleeClosesTheWholeGapWhileActionPointsRemain()
        {
            CombatSession session = Session(new HexCoord(0, 0), new HexCoord(4, 0));
            CombatCommand chosen = Choose(session, "enemy");
            Assert.AreEqual(CombatCommandKind.Move, chosen.Kind);
            Assert.That(PathDistance(session, chosen.Destination.Value, new HexCoord(4, 0)), Is.EqualTo(1),
                "The enemy should finish the turn next to the player, not one step away from its start.");
            Assert.AreEqual(new HexCoord(3, 0), chosen.Destination.Value);
        }

        [Test]
        public void MeleeDetoursAroundABlockedCellInsteadOfStalling()
        {
            var blocked = new[] { new HexCoord(3, 2) };
            CombatSession session = Session(new HexCoord(2, 2), new HexCoord(4, 2), blocked);
            CombatCommand chosen = Choose(session, "enemy");
            Assert.AreEqual(CombatCommandKind.Move, chosen.Kind);
            HexCoord destination = chosen.Destination.Value;
            Assert.AreNotEqual(new HexCoord(3, 2), destination);
            Assert.That(PathDistance(session, destination, new HexCoord(4, 2)), Is.EqualTo(1),
                "The chosen destination must be adjacent to the player along a legal path.");
            Assert.That(PathDistance(session, new HexCoord(2, 2), new HexCoord(4, 2)), Is.EqualTo(3),
                "The blocked cell must force a detour.");
        }

        [Test]
        public void MeleeAttacksWhenAlreadyAdjacent()
        {
            CombatSession session = Session(new HexCoord(2, 2), new HexCoord(3, 2));
            CombatCommand chosen = Choose(session, "enemy");
            Assert.AreEqual(CombatCommandKind.BasicAttack, chosen.Kind);
            Assert.AreEqual("player", chosen.TargetId);
        }

        [Test]
        public void MeleeGuardsWhenNoLegalMoveExists()
        {
            CombatSession session = Session(new HexCoord(0, 0), new HexCoord(4, 4),
                new[] { new HexCoord(1, 0), new HexCoord(0, 1) });
            IReadOnlyList<CombatCommand> legal = new CombatLegalActionService().GetLegalActions(session, "enemy");
            Assert.That(legal.Any(action => action.Kind == CombatCommandKind.Move), Is.False,
                "This fixture covers the no-move fallback.");
            CombatCommand chosen = new LegalActionAI().ChooseAction(session, legal);
            Assert.AreEqual(CombatCommandKind.Guard, chosen.Kind);
        }

        [Test]
        public void MeleeDoesNotRouteThroughAnAlliedBlocker()
        {
            // One-row corridor: the ally at (2,0) blocks the only route, so the player is
            // unreachable. Ignoring tokens (the old measurement) would claim a 4-hop path and
            // walk the enemy into the ally; the guarded answer is the correct one.
            var units = new[]
            {
                Unit("enemy", CombatTeam.Enemy, new HexCoord(0, 0), speed: 100),
                Unit("enemy_2", CombatTeam.Enemy, new HexCoord(2, 0), speed: 1),
                Unit("player", CombatTeam.Player, new HexCoord(4, 0), speed: 1),
            };
            CombatSession session = Session(Corridor(), units);
            IReadOnlyList<CombatCommand> legal = new CombatLegalActionService().GetLegalActions(session, "enemy");
            Assert.That(legal.Any(action => action.Kind == CombatCommandKind.Move), Is.True,
                "The enemy can still step toward the ally; only the player is unreachable.");
            CombatCommand chosen = new LegalActionAI().ChooseAction(session, legal);
            Assert.AreEqual(CombatCommandKind.Guard, chosen.Kind);
        }

        [Test]
        public void EnemyTurnProducesOnlyAdmittedCommands()
        {
            CombatSession session = Session(new HexCoord(2, 2), new HexCoord(4, 2));
            IReadOnlyList<CombatCommand> legal = new CombatLegalActionService().GetLegalActions(session, "enemy");
            CombatCommand chosen = new LegalActionAI().ChooseAction(session, legal);
            Assert.IsTrue(legal.Contains(chosen), "The AI may only pick a command the legal-action service admitted.");
        }

        [Test]
        public void RangedEnemyFiresFromItsCastRange()
        {
            var units = new[]
            {
                Unit("player", CombatTeam.Player, new HexCoord(3, 1), speed: 1),
                Unit("enemy", CombatTeam.Enemy, new HexCoord(1, 1), speed: 100, arts: new[] { "art_ranged" }),
            };
            CombatSession session = Session(Board(null), units, new[] { BasicAttack(), RangedArt() });
            CombatCommand chosen = Choose(session, "enemy", new RangedActionAI());
            Assert.AreEqual(CombatCommandKind.Art, chosen.Kind);
            Assert.AreEqual("player", chosen.TargetId);
        }

        [Test]
        public void RangedEnemyBacksOffWhenTheTargetClosesInsideItsRange()
        {
            var units = new[]
            {
                Unit("player", CombatTeam.Player, new HexCoord(2, 1), speed: 1),
                Unit("enemy", CombatTeam.Enemy, new HexCoord(1, 1), speed: 100, arts: new[] { "art_ranged" }),
            };
            CombatSession session = Session(Board(null), units, new[] { BasicAttack(), RangedArt() });
            CombatCommand chosen = Choose(session, "enemy", new RangedActionAI());
            Assert.AreEqual(CombatCommandKind.Move, chosen.Kind, "An adjacent target is inside the dead zone.");
            Assert.That(PathDistance(session, chosen.Destination.Value, new HexCoord(2, 1)), Is.GreaterThan(1),
                "The ranged enemy must step out of melee range instead of trading blows.");
        }

        [Test]
        public void RangedEnemyApproachesOnlyIntoItsFiringRange()
        {
            var units = new[]
            {
                Unit("player", CombatTeam.Player, new HexCoord(4, 0), speed: 1),
                Unit("enemy", CombatTeam.Enemy, new HexCoord(0, 0), speed: 100, arts: new[] { "art_ranged" }),
            };
            CombatSession session = Session(Board(null), units, new[] { BasicAttack(), RangedArt() });
            CombatCommand chosen = Choose(session, "enemy", new RangedActionAI());
            Assert.AreEqual(CombatCommandKind.Move, chosen.Kind);
            int distance = PathDistance(session, chosen.Destination.Value, new HexCoord(4, 0));
            Assert.That(distance, Is.InRange(2, 3), "It must stop at its cast range, not walk into melee.");
        }

        [Test]
        public void RangedEnemyWithoutADeadZoneFiresEvenAdjacent()
        {
            // The production 裂石 uses 1-3 range: a faster player can always close, so the beast
            // must still fire instead of retreating forever.
            var units = new[]
            {
                Unit("player", CombatTeam.Player, new HexCoord(2, 1), speed: 1),
                Unit("enemy", CombatTeam.Enemy, new HexCoord(1, 1), speed: 100, arts: new[] { "art_ranged" }),
            };
            CombatSession session = Session(Board(null), units, new[] { BasicAttack(), CloseRangedArt() });
            CombatCommand chosen = Choose(session, "enemy", new RangedActionAI());
            Assert.AreEqual(CombatCommandKind.Art, chosen.Kind);
        }

        private CombatCommand Choose(CombatSession session, string actorId, ICombatActionPolicy policy = null)
        {
            IReadOnlyList<CombatCommand> legal = new CombatLegalActionService().GetLegalActions(session, actorId);
            Assert.IsNotEmpty(legal, "The AI must receive at least one legal command.");
            return (policy ?? new LegalActionAI()).ChooseAction(session, legal);
        }

        private static int PathDistance(CombatSession session, HexCoord from, HexCoord to)
        {
            CombatMovementQueryResult path = session.SpatialQuery.QueryMovement(
                from, to, 100000000, 5000, Array.Empty<HexCoord>());
            return path.IsReachable ? path.Path.Count : int.MaxValue;
        }

        private CombatSession Session(HexCoord enemyPosition, HexCoord playerPosition, HexCoord[] blocked = null) =>
            Session(Board(blocked), new[]
            {
                Unit("player", CombatTeam.Player, playerPosition, speed: 1),
                Unit("enemy", CombatTeam.Enemy, enemyPosition, speed: 100),
            });

        private CombatSession Session(ICombatSpatialQuery board, CombatantSnapshot[] units,
            CombatAttackProfile[] profiles = null)
        {
            var session = new CombatSession(units, profiles ?? new[] { BasicAttack() },
                board, CombatActionPointKernelTests.Ordinary(), ruleSet: CombatActionPointKernelTests.Rules());
            Assert.AreEqual("enemy", commands.AdvanceUntilAction(session).ActorId,
                "The fixture must start on the enemy turn.");
            return session;
        }

        private static ICombatSpatialQuery Corridor()
        {
            var cells = new Dictionary<HexCoord, SpatialCellRules>();
            for (int q = 0; q <= 4; q++)
                cells[new HexCoord(q, 0)] = new SpatialCellRules(0, false, false, false, 0);
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            for (int q = 0; q < 4; q++)
            {
                edges[new SpatialDirectedEdge(new HexCoord(q, 0), new HexCoord(q + 1, 0))] = new SpatialEdgeRules(1000, true, true);
                edges[new SpatialDirectedEdge(new HexCoord(q + 1, 0), new HexCoord(q, 0))] = new SpatialEdgeRules(1000, true, true);
            }
            return new BoardQuery(new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(1000, 16)));
        }

        private static CombatantSnapshot Unit(string id, CombatTeam team, HexCoord position, int speed,
            string[] arts = null) =>
            new CombatantSnapshot(id, team, position, speed, 10000, 10000, 20, 20, 10, 10,
                actionPointMaximumUnits: 100000, movementEfficiencyMilliUnits: 5000,
                basicAttackProfileId: "basic",
                equippedArtProfileIds: arts, availableArtProfileIds: arts);

        private static CombatAttackProfile BasicAttack() =>
            new CombatAttackProfile("basic", CombatAttackKind.Basic, CombatAttackEffect.Physical,
                minimumRange: 1, maximumRange: 1, actionPointCostUnits: 40000, minimumNetCostUnits: 25000,
                ctPenalty: 0, reuseCooldownTicks: 0, physicalMultiplier: 1);

        private static CombatAttackProfile RangedArt() =>
            new CombatAttackProfile("art_ranged", CombatAttackKind.Art, CombatAttackEffect.Physical,
                minimumRange: 2, maximumRange: 3, actionPointCostUnits: 50000, minimumNetCostUnits: 25000,
                ctPenalty: 0, reuseCooldownTicks: 0, physicalMultiplier: 1);

        private static CombatAttackProfile CloseRangedArt() =>
            new CombatAttackProfile("art_ranged", CombatAttackKind.Art, CombatAttackEffect.Physical,
                minimumRange: 1, maximumRange: 3, actionPointCostUnits: 50000, minimumNetCostUnits: 25000,
                ctPenalty: 0, reuseCooldownTicks: 0, physicalMultiplier: 1);

        private static ICombatSpatialQuery Board(HexCoord[] blocked)
        {
            var blockedCells = new HashSet<HexCoord>(blocked ?? Array.Empty<HexCoord>());
            var cells = new Dictionary<HexCoord, SpatialCellRules>();
            for (int q = 0; q <= 4; q++)
            for (int r = 0; r <= 4; r++)
            {
                var cell = new HexCoord(q, r);
                cells[cell] = new SpatialCellRules(0, blockedCells.Contains(cell), false, false, 0);
            }
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            foreach (HexCoord cell in cells.Keys)
            foreach (HexCoord direction in HexCoord.Directions)
            {
                HexCoord next = cell + direction;
                if (!cells.ContainsKey(next)) continue;
                edges[new SpatialDirectedEdge(cell, next)] = new SpatialEdgeRules(1000, true, true);
            }
            return new BoardQuery(new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(1000, 16)));
        }

        private sealed class BoardQuery : ICombatSpatialQuery
        {
            private readonly SpatialQueryBoard board;
            public BoardQuery(SpatialQueryBoard value) => board = value;

            public CombatRangeQueryResult QueryRange(HexCoord source, HexCoord target, int minimumRange, int maximumRange)
            {
                SpatialRangeEntry result = board.QueryRangeEntry(
                    source, target, minimumRange, maximumRange, SpatialQueryKind.Attack, true);
                return new CombatRangeQueryResult(result.IsInRange, result.Reason);
            }

            public CombatMovementQueryResult QueryMovement(
                HexCoord source, HexCoord destination, int actionPointBudgetUnits,
                int movementEfficiencyMilliUnits, IReadOnlyCollection<HexCoord> occupied)
            {
                SpatialMovementPathResult result = board.QueryMovementPath(source, destination,
                    actionPointBudgetUnits, movementEfficiencyMilliUnits, occupied);
                return new CombatMovementQueryResult(result.Path.Count > 0, result.Reason, result.Path,
                    result.Edges, result.UnitsPerRange, result.ActionPointCostUnits);
            }

            public IReadOnlyDictionary<HexCoord, int> FindReachable(
                HexCoord source, int actionPointBudgetUnits, int movementEfficiencyMilliUnits,
                IReadOnlyCollection<HexCoord> occupied) =>
                board.FindReachableWithinActionPoints(source, actionPointBudgetUnits, movementEfficiencyMilliUnits, occupied);
        }
    }
}
