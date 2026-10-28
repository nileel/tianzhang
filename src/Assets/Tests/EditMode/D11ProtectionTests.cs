using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    public sealed class D11ProtectionTests
    {
        private const string Defender = "a_defender", Attacker = "z_attacker", DefenseSeat = "defense-seat", AttackSeat = "attack-seat";
        private readonly CombatCommandService commands = new CombatCommandService();

        [Test]
        public void YangProtectsOnlyTheFirstRootPerMemberAndSharesTheConfiguredBudget()
        {
            // Combat admits two members per side. A reduced test budget exercises partial exhaustion
            // without inventing extra production actors or changing the shipped 300-point budget.
            var s = Ready(CombatVisibility.Yang, physical: 1000, allies: 1, windowBudget: 150);
            Assert.AreEqual(900, Hit(s, Defender).FinalDamage);
            Assert.AreEqual(1000, Hit(s, Defender).FinalDamage);
            Assert.AreEqual(950, Hit(s, "b_ally_1").FinalDamage);
            Assert.AreEqual(1000, Hit(s, "b_ally_1").FinalDamage);
            Assert.AreEqual(150, Ledger(s).ProtectionUsed);
        }

        [Test]
        public void YangFirstHitRenewsAtThePublicFourTickBoundary()
        {
            var s = Ready(CombatVisibility.Yang);
            Assert.AreEqual(1, s.TurnScheduler.CurrentTick);
            Assert.AreEqual(90, Hit(s, Defender).FinalDamage);
            Assert.AreEqual(100, Hit(s, Defender).FinalDamage);
            while (s.TurnScheduler.CurrentTick < 4) NextTo(s, Attacker);
            Assert.AreEqual(4, s.TurnScheduler.CurrentTick);
            Assert.IsNotNull(s.Jindan.Find(DefenseSeat).ActiveVisibility);
            Assert.AreEqual(90, Hit(s, Defender).FinalDamage);
            Assert.AreEqual(1, Ledger(s).ProtectionWindow);
            Assert.AreEqual(10, Ledger(s).ProtectionUsed);
        }

        [Test]
        public void APreparedButUncommittedAttackDoesNotConsumeProtection()
        {
            var s = Ready(CombatVisibility.Yang);
            var command = Attack(s, Defender);
            var quote = commands.Preview(s, command);
            Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
            var prepared = commands.Prepare(s, command, quote, out _);
            Assert.IsTrue(prepared.Succeeded, prepared.RejectionReason);
            Assert.AreEqual(90, prepared.Damage.Single().FinalDamage);
            Assert.AreEqual(20000, Actor(s, Defender).CurrentHealth);
            Assert.AreEqual(0, Ledger(s).ProtectionUsed);
            Assert.AreEqual(90, Hit(s, Defender).FinalDamage);
            Assert.AreEqual(10, Ledger(s).ProtectionUsed);
        }

        [Test]
        public void MissDoesNotConsumeTheNextEligibleFirstHit()
        {
            var s = Ready(CombatVisibility.Yang);
            Actor(s, Defender).DodgeRate = 20;
            var missed = Hit(s, Defender, hitRoll: 100);
            Assert.IsFalse(missed.IsHit); Assert.AreEqual(0, missed.FinalDamage);
            Assert.AreEqual(0, Ledger(s).ProtectionUsed);
            Assert.AreEqual(90, Hit(s, Defender).FinalDamage);
            Assert.AreEqual(100, Hit(s, Defender).FinalDamage);
            // Physical/soul root hits currently clamp to at least 1 before the at-most-30% protection.
            // A zero-damage root hit is therefore not reachable through the production attack API.
        }

        [Test]
        public void ClosingAndRedeployingKeepsTheSameGuardianFirstHitReceipt()
        {
            var s = Ready(CombatVisibility.Yang);
            Assert.AreEqual(90, Hit(s, Defender).FinalDamage);
            var ledger = Ledger(s);
            NextTo(s, Defender);
            Assert.AreEqual(2, s.TurnScheduler.CurrentTick);
            Commit(s, DomainCommand(Defender, CombatVisibility.Yang, false));
            Assert.IsNull(s.Jindan.Find(DefenseSeat).ActiveVisibility);
            Commit(s, DomainCommand(Defender, CombatVisibility.Yang, true));
            NextTo(s, Attacker);
            Assert.AreSame(ledger, Ledger(s));
            Assert.AreEqual(2, s.Jindan.Find(DefenseSeat).SuccessfulDeployments);
            Assert.AreEqual(100, Hit(s, Defender).FinalDamage);
            Assert.AreEqual(10, Ledger(s).ProtectionUsed);
        }

        [TestCase(3, 90)]
        [TestCase(1, 100)]
        public void YinProtectsDistantIncomingRootsButNotAdjacentOnes(int distance, int expected)
        {
            var s = Ready(CombatVisibility.Yin, distance: distance);
            ResolvePending(s);
            Assert.AreEqual(expected, Hit(s, Defender).FinalDamage);
            Assert.AreEqual(expected, Hit(s, Defender).FinalDamage);
        }

        [Test]
        public void LosingVisibilityDoesNotRemoveYinsIndependentDistantProtection()
        {
            var s = Ready(CombatVisibility.Yin);
            Assert.IsTrue(s.Visibility.IsPaused);
            var conflict = s.Visibility.Current;
            s.Visibility.Input(conflict.EventId, 0, true, false, 0);
            s.Visibility.Input(conflict.EventId, 250, false, false, 1);
            s.Visibility.Input(conflict.EventId, 1000, false, false, 0);
            s.Visibility.Input(conflict.EventId, 1250, false, false, 2);
            Assert.AreEqual(CombatConflictOutcome.Left, conflict.Outcome);
            Assert.IsTrue(s.Visibility.IsVisible(Attacker, Defender));
            Assert.IsNotNull(s.Jindan.Find(DefenseSeat).ActiveVisibility);
            Assert.AreEqual(90, Hit(s, Defender).FinalDamage);
        }

        [Test]
        public void HiddenTargetRejectsDirectSelectionButLegalBlindCellAttackStillHits()
        {
            var s = Ready(CombatVisibility.Yin);
            var conflict = s.Visibility.Current;
            s.Visibility.Input(conflict.EventId, 0, true, false, 0);
            s.Visibility.Input(conflict.EventId, 1, false, true, 0);
            Assert.AreEqual(CombatConflictOutcome.Right, conflict.Outcome);
            Assert.IsFalse(s.Visibility.IsVisible(Attacker, Defender));
            Assert.AreEqual("combat_target_not_visible", commands.Preview(s, Attack(s, Defender)).RejectionReason);
            var blind = new CombatCommand(CombatCommandKind.BasicAttack, Attacker, profileId: "root-attack",
                rolls: new CombatResolutionRolls(0, 100, 100, 100), targetCell: Actor(s, Defender).Position);
            var quote = commands.Preview(s, blind);
            Assert.IsTrue(quote.IsLegal, quote.RejectionReason);
            Assert.IsTrue(quote.AttackArea.TargetIds.Contains(Defender));
            var result = commands.Execute(s, blind);
            Assert.IsTrue(result.Succeeded, result.RejectionReason);
            Assert.AreEqual(90, result.Damage.Single().FinalDamage);
            Assert.AreEqual(19910, Actor(s, Defender).CurrentHealth);
        }

        [Test]
        public void ExistingOrdinaryReductionLeavesAtMostThirtyPercentForTheCombinedRule()
        {
            // A maximum legal 30% test definition must use only the 10% remaining after Shouyi's 20%.
            // The shipped domain remains configured at 10%; this fixture exercises the common ceiling.
            var s = Ready(CombatVisibility.Yang, reductionPercent: 30, gongFa: "抱元守一经");
            Assert.Greater(Actor(s, Defender).ShouyiStacks, 0);
            Assert.AreEqual(72, Hit(s, Defender).FinalDamage);
            Assert.AreEqual(8, Ledger(s).ProtectionUsed);
        }

        [TestCase(1, 10, 10, 1, 1)]
        [TestCase(400, 600, 900, 2, 100)]
        public void HybridIsOneProtectedRootAndRoundingRemainderUsesPhysicalFirst(
            int physical, int soul, int expectedDamage, int expectedLeijie, int expectedProtection)
        {
            var s = Ready(CombatVisibility.Yang, physical: physical, soul: soul, gongFa: "九霄雷劫录");
            int health = Actor(s, Defender).CurrentHealth;
            Assert.AreEqual(expectedDamage, Hit(s, Defender).FinalDamage);
            Assert.AreEqual(health - expectedDamage, Actor(s, Defender).CurrentHealth);
            Assert.AreEqual(expectedProtection, Ledger(s).ProtectionUsed);
            // With 1+10 damage, the one-point reduction remainder removes physical damage completely.
            // Only the positive soul component then grants a Leijie receipt. Larger roots keep both.
            Assert.AreEqual(expectedLeijie, Actor(s, Defender).LeijieStacks);
        }

        [Test]
        public void HybridWithoutActiveDomainsRetainsBothLeijieDamageReceipts()
        {
            var s = Ready(null, physical: 1, soul: 10, gongFa: "九霄雷劫录");
            Assert.IsFalse(s.Visibility.HasDomain);
            Assert.AreEqual(11, Hit(s, Defender).FinalDamage);
            Assert.AreEqual(2, Actor(s, Defender).LeijieStacks);
            Assert.AreEqual(0, Ledger(s).ProtectionUsed);
        }

        private CombatDamageResult Hit(CombatSession session, string target, float hitRoll = 0)
        {
            var result = commands.Execute(session, Attack(session, target, hitRoll));
            Assert.IsTrue(result.Succeeded, result.RejectionReason);
            return result.Damage.Single();
        }

        private static CombatCommand Attack(CombatSession session, string target, float hitRoll = 0)
        {
            return new CombatCommand(CombatCommandKind.BasicAttack, Attacker, target, "root-attack",
                new CombatResolutionRolls(hitRoll, 100, 100, 100));
        }

        private static CombatantSnapshot Actor(CombatSession session, string id)
        { session.Combatants.TryGet(id, out var actor); return actor; }
        private static CombatVisibilityLedger Ledger(CombatSession session) => session.Jindan.Find(DefenseSeat).VisibilityLedger;
        private static CombatCommand DomainCommand(string actor, string effect, bool deploy) => new CombatCommand(
            deploy ? CombatCommandKind.JindanAbility : CombatCommandKind.CloseJindan, actor, profileId: effect,
            jindan: new CombatJindanRequest(actor == Defender ? DefenseSeat : AttackSeat));
        private void Commit(CombatSession session, CombatCommand command)
        { var result = commands.Execute(session, command); Assert.IsTrue(result.Succeeded, result.RejectionReason); }
        private static void ResolvePending(CombatSession session)
        {
            while (session.Visibility.IsPaused) session.Visibility.Skip(session.Visibility.Current.EventId, true);
        }
        private void NextTo(CombatSession session, string actor)
        {
            ResolvePending(session);
            Commit(session, new CombatCommand(CombatCommandKind.EndTurn, session.CurrentActorId));
            for (int count = 0; count < 12; count++)
            {
                var next = commands.AdvanceUntilAction(session);
                ResolvePending(session);
                if (next.ActorId == actor) return;
                Commit(session, new CombatCommand(CombatCommandKind.EndTurn, next.ActorId));
            }
            Assert.Fail("requested_protection_test_turn_not_reached");
        }

        private CombatSession Ready(string domain, int physical = 100, int soul = 0, int distance = 3,
            int allies = 0, int reductionPercent = 10, string gongFa = null, int windowBudget = 300)
        {
            var cells = new Dictionary<HexCoord, SpatialCellRules>();
            for (int q = 0; q <= 5; q++) for (int r = 0; r <= 4; r++)
                cells.Add(new HexCoord(q, r), new SpatialCellRules(0, false, false, false, 0, true, 4));
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            foreach (var cell in cells.Keys) for (int direction = 0; direction < 6; direction++)
                if (cells.ContainsKey(cell.Step(direction))) edges.Add(new SpatialDirectedEdge(cell, cell.Step(direction)),
                    new SpatialEdgeRules(2, true, true, allowsFlight: true));
            var environment = new CombatSceneObjects(new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(2, 16)),
                Array.Empty<CombatSceneObject>(), 10, 20);
            string defenseEffect = domain ?? CombatVisibility.Yang;
            string attackEffect = domain == CombatVisibility.Yin ? CombatVisibility.Yang : "SPACE_SCALE";
            CombatantSnapshot Make(string id, CombatTeam team, HexCoord cell, string effect = null)
            {
                var bindings = new Dictionary<string, string>();
                if (effect != null) bindings.Add(effect, id + "-guardian");
                var actor = new CombatantSnapshot(id, team, cell, 100, 20000, 20000, physical, soul, 0, 0, 1000000, 20000,
                    realmMultiplier: 6, basicAttackProfileId: "root-attack", abilityInstanceIds: bindings, realmStage: "金丹",
                    facing: team == CombatTeam.Enemy ? cell.DirectionToward(new HexCoord(distance, 1)) : 0);
                actor.SetSpirit(6000, 6000);
                return actor;
            }
            var defender = Make(Defender, CombatTeam.Enemy, new HexCoord(0, 1), defenseEffect);
            defender.GongFaId = gongFa;
            var attacker = Make(Attacker, CombatTeam.Player, new HexCoord(distance, 1), attackEffect);
            var actors = new List<CombatantSnapshot> { defender, attacker };
            var allyCells = new[] { new HexCoord(0, 2) };
            for (int i = 0; i < allies; i++) actors.Add(Make("b_ally_" + (i + 1), CombatTeam.Enemy, allyCells[i]));
            var attack = new CombatAttackProfile("root-attack", CombatAttackKind.Basic,
                soul > 0 ? CombatAttackEffect.Hybrid : CombatAttackEffect.Physical, 1, 6, 1000, 1000, 0, 0,
                physicalMultiplier: 1, soulMultiplier: soul > 0 ? 1 : 0);
            var session = new CombatSession(actors, new[] { attack }, new Query(environment),
                new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0),
                    new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) },
                ruleSet: CombatActionPointKernelTests.Rules(), sceneObjects: environment);
            CombatJindanAbilityDefinition Ability(string effect, HexCoord center) => new CombatJindanAbilityDefinition(
                effect, effect != "SPACE_SCALE", 250000, 400, 1, 25000, 40, 4, 3, 1, 600, 0, 6, Array.Empty<int>(), "unused", 6,
                visibility: effect == "SPACE_SCALE" ? null : new CombatVisibilityDefinition(center, effect + "-support", 1, 1, 6,
                    new CombatConflictSettings("protection-test", 200, 50, 1000, 250, 750, 50, 100, "Space", "A", "D"),
                    4, reductionPercent, 100, windowBudget));
            session.ConfigureJindan(new CombatJindanLoadout(new[] {
                new CombatJindanSeatDefinition(Defender, DefenseSeat, "defense-position", "Domain", Defender + "-guardian", new[] { defenseEffect }, defenseEffect),
                new CombatJindanSeatDefinition(Attacker, AttackSeat, "attack-position", attackEffect == "SPACE_SCALE" ? "Source" : "Domain",
                    Attacker + "-guardian", new[] { attackEffect }, attackEffect) },
                new[] { Ability(defenseEffect, defender.Position), Ability(attackEffect, attacker.Position) }, 1));
            Assert.AreEqual(Defender, commands.AdvanceUntilAction(session).ActorId);
            if (domain != null) Commit(session, DomainCommand(Defender, domain, true));
            NextTo(session, Attacker);
            if (domain == CombatVisibility.Yin) Commit(session, DomainCommand(Attacker, CombatVisibility.Yang, true));
            return session;
        }

        private sealed class Query : ICombatSpatialQuery
        {
            private readonly CombatSceneObjects environment;
            internal Query(CombatSceneObjects environment) => this.environment = environment;
            public SpatialContactResult QueryRange(SpatialPose source, SpatialPose target, int minimum, int maximum, SpatialAttackPolicy policy = null)
                => environment.Board.QueryContact(source, target, minimum, maximum, policy);
            public CombatMovementQueryResult QueryMovement(SpatialPose source, HexCoord target, int budget, int efficiency,
                IReadOnlyCollection<HexCoord> occupied, int? facing = null)
            {
                var result = environment.Board.QueryMovementPath(source, target, budget, efficiency, occupied, facing);
                return new CombatMovementQueryResult(result.Steps.Count > 0, result.Reason, result.Steps, result.UnitsPerRange, result.ActionPointCostUnits);
            }
            public IReadOnlyDictionary<SpatialPose, int> FindReachable(SpatialPose source, int budget, int efficiency,
                IReadOnlyCollection<HexCoord> occupied) => environment.Board.FindReachableWithinActionPoints(source, budget, efficiency, occupied);
        }
    }
}
