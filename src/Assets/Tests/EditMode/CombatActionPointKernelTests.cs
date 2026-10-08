using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Features.Adventure;
using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    public sealed class CombatActionPointKernelTests
    {
        private readonly CombatCommandService service = new CombatCommandService();
        internal static CombatOrdinaryActionDefinition[] Ordinary() => new[]
        {
            new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0),
            new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60),
        };
        private static CombatantSnapshot Actor(string id = "player", CombatTeam team = CombatTeam.Player,
            int ap = 100000, int v = 5000, int speed = 100, IEnumerable<CombatActionEconomyModifier> modifiers = null,
            IReadOnlyDictionary<string, string> abilities = null) => new CombatantSnapshot(id, team,
                new HexCoord(team == CombatTeam.Player ? 0 : 4, 0), speed, 10000, 10000, 20, 20, 10, 10, ap, v,
                basicAttackProfileId: "basic", equippedArtProfileIds: new[] { "art", "other" },
                availableArtProfileIds: new[] { "art", "other", "spare" },
                equippedDivineProfileIds: new[] { "divine" }, availableDivineProfileIds: new[] { "divine" },
                actionEconomyModifiers: modifiers, abilityInstanceIds: abilities);
        private static CombatAttackProfile Attack(string id = "basic", int ap = 40000, int ct = 0, int cd = 0, int mp = 0,
            int minimum = 10000, bool talisman = false) => new CombatAttackProfile(id,
                id == "basic" ? CombatAttackKind.Basic : id == "divine" ? CombatAttackKind.Divine : CombatAttackKind.Art,
                CombatAttackEffect.Physical, 0, 10, ap, minimum, ct, cd, physicalMultiplier: 1, spiritCost: mp, isTalisman: talisman);
        private CombatSession Ready(CombatantSnapshot player = null, CombatantSnapshot enemy = null,
            params CombatAttackProfile[] profiles)
        {
            var session = new CombatSession(new[] { player ?? Actor(), enemy ?? Actor("enemy", CombatTeam.Enemy, speed: 1) },
                profiles.Length == 0 ? new[] { Attack(), Attack("art", 30000), Attack("other", 30000), Attack("spare"), Attack("divine") } : profiles,
                ProductionSpace(), Ordinary());
            Assert.AreEqual("player", service.AdvanceUntilAction(session).ActorId);
            return session;
        }
        private static ICombatSpatialQuery ProductionSpace()
        {
            var cells = new Dictionary<HexCoord, SpatialCellRules>();
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            for (int i = 0; i <= 4; i++) cells[new HexCoord(i, 0)] = new SpatialCellRules(0, false, false, false, 0);
            for (int i = 0; i < 4; i++)
                edges[new SpatialDirectedEdge(new HexCoord(i, 0), new HexCoord(i + 1, 0))] = new SpatialEdgeRules(1000, true, true);
            var board = new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(1000, 16));
            Type type = typeof(CombatEntryAdapter).GetNestedType("SpatialCombatQuery", BindingFlags.NonPublic);
            return (ICombatSpatialQuery)Activator.CreateInstance(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { board }, null);
        }
        private static CombatCommand Hit(string profile = "basic", float roll = 0) => new CombatCommand(
            profile == "basic" ? CombatCommandKind.BasicAttack : profile == "divine" ? CombatCommandKind.Divine : CombatCommandKind.Art,
            "player", "enemy", profile, new CombatResolutionRolls(roll, 100, 100, 100));
        private static CombatCommand Move(int q) => new CombatCommand(CombatCommandKind.Move, "player", destination: new HexCoord(q, 0));
        private CombatActionResult Commit(CombatSession s, CombatCommand c)
        { var r = service.Execute(s, c); Assert.IsTrue(r.Succeeded, r.RejectionReason); return r; }

        [TestCase(false)] [TestCase(true)]
        public void MoveAndAttackInEitherOrderShareOneBudgetAndOneTurn(bool attackFirst)
        {
            var s = Ready(); var p = s.Combatants.All[0]; int tick = s.TurnScheduler.CurrentTick; long turn = s.TurnSequence;
            Commit(s, attackFirst ? Hit() : Move(1)); Commit(s, attackFirst ? Move(1) : Hit());
            Assert.AreEqual(40000, p.ActionPointsRemainingUnits); Assert.AreEqual(tick, s.TurnScheduler.CurrentTick);
            Assert.AreEqual(turn, s.TurnSequence); Assert.AreEqual(0, p.OwnTurnsEnded);
        }
        [Test]
        public void MoveCastMoveAndMultipleAttacksContinueWithoutASecondGrant()
        {
            var s = Ready(); Commit(s, Move(1)); Commit(s, Hit("art")); Commit(s, Move(2));
            Assert.AreEqual(30000, s.Combatants.All[0].ActionPointsRemainingUnits);
            var attacks = Ready(); Commit(attacks, Hit()); Commit(attacks, Hit());
            Assert.AreEqual(20000, attacks.Combatants.All[0].ActionPointsRemainingUnits);
            Assert.AreEqual("player", service.AdvanceUntilAction(attacks).ActorId);
            Assert.AreEqual(1, attacks.Combatants.All[0].OwnTurnsStarted);
        }
        [Test]
        public void ProductionAdapterUsesPerEdgeRoundingAcrossAnInterveningCast()
        {
            var s = Ready(Actor(ap: 120000, v: 3000), null, Attack("art", 10000));
            var whole = service.Preview(s, Move(3)); Assert.AreEqual(99999, whole.ActionPointCostUnits);
            Assert.AreEqual(99999, s.SpatialQuery.FindReachable(s.Combatants.All[0].Position, 120000, 3000,
                new[] { s.Combatants.All[1].Position })[new HexCoord(3, 0)]);
            int first = Commit(s, Move(2)).ActionPointSpentUnits; Commit(s, Hit("art"));
            int second = Commit(s, Move(3)).ActionPointSpentUnits;
            Assert.AreEqual(99999, first + second); Assert.AreEqual(10001, s.Combatants.All[0].ActionPointsRemainingUnits);
            Assert.IsFalse(service.Execute(s, Move(4), whole).Succeeded);
        }
        [TestCase(false)] [TestCase(true)]
        public void PenaltiesSumInEitherOrderAndZeroCannotEraseEarlierCosts(bool reverse)
        {
            var s = Ready(null, null, Attack("art", 30000, 10), Attack("other", 30000, 30), Attack());
            Commit(s, Hit(reverse ? "other" : "art")); Commit(s, Hit(reverse ? "art" : "other"));
            var quote = service.Preview(s, Hit()); Assert.AreEqual(140, quote.Turn.NextActionThreshold);
            Assert.AreEqual(2, quote.Turn.TicksUntilReady); Commit(s, Hit());
            var p = s.Combatants.All[0]; Assert.AreEqual(0, p.ActionPointsRemainingUnits);
            Assert.AreEqual(1, p.OwnTurnsEnded); Assert.AreEqual(CombatTurnPhase.Finished, s.TurnPhase);
            Assert.AreEqual(140, s.TurnScheduler.GetSnapshot(s.Combatants.All).Units.Single(u => u.Id == p.Id).NextActionThreshold);
            Assert.IsFalse(service.Execute(s, new CombatCommand(CombatCommandKind.EndTurn, p.Id)).Succeeded);
            Assert.AreEqual(1, p.OwnTurnsEnded);
        }
        [Test]
        public void SharedAbilityCooldownIsUniqueAndOnlyGlobalTicksReduceIt()
        {
            var p = Actor(abilities: new Dictionary<string,string> { ["art"] = "shared", ["other"] = "shared" });
            var s = Ready(p, null, Attack("art", 30000, 7, 13), Attack("other", 30000));
            Commit(s, Hit("art")); Commit(s, Move(1));
            Assert.AreEqual(13, p.GetCooldown("other")); Assert.IsFalse(service.Execute(s, Hit("other")).Succeeded);
            Assert.AreEqual(1, p.Cooldowns.Count); Commit(s, new CombatCommand(CombatCommandKind.EndTurn, p.Id));
            service.AdvanceUntilAction(s); Assert.AreEqual(11, p.GetCooldown("art"));
        }
        [TestCase(CombatCommandKind.Guard)] [TestCase(CombatCommandKind.SwapSpell)]
        public void OrdinaryActionsChargeTheirExplicitDefinition(CombatCommandKind kind)
        {
            var s = Ready(); var c = new CombatCommand(kind, "player", profileId: kind == CombatCommandKind.SwapSpell ? "spare" : null, slotIndex: 0);
            Commit(s, c); Assert.AreEqual(50000, s.Combatants.All[0].ActionPointsRemainingUnits);
            if (kind == CombatCommandKind.Guard) Assert.IsFalse(service.Execute(s, c).Succeeded);
            else Assert.AreEqual(60, s.Combatants.All[0].GetCooldown("spare"));
        }
        [Test]
        public void GuardExitsOnlyForSuccessfulBenefitAndPersistsThroughEnd()
        {
            var s = Ready(); var p = s.Combatants.All[0]; Commit(s, new CombatCommand(CombatCommandKind.Guard, p.Id));
            Assert.AreEqual(1.5f, p.DefenseMultiplier(true)); Assert.AreEqual(1.5f, p.DefenseMultiplier(false));
            Assert.IsFalse(service.Execute(s, Move(4)).Succeeded); Assert.IsTrue(p.IsGuarding);
            Commit(s, Hit()); Assert.IsFalse(p.IsGuarding);
            var guarded = Ready(); p = guarded.Combatants.All[0]; Commit(guarded, new CombatCommand(CombatCommandKind.Guard, p.Id));
            Commit(guarded, new CombatCommand(CombatCommandKind.EndTurn, p.Id)); Assert.IsTrue(p.IsGuarding);
            service.AdvanceUntilAction(guarded); Assert.IsFalse(p.IsGuarding);
            p.IsGuarding = true; p.ReceiveDamage(p.CurrentHealth); Assert.IsFalse(p.IsGuarding);
        }
        [Test]
        public void WaitRequiresNoCommittedBenefitWhileEndIsAlwaysAvailable()
        {
            var s = Ready(); var p = s.Combatants.All[0]; Commit(s, Move(1));
            Assert.IsFalse(service.Execute(s, new CombatCommand(CombatCommandKind.Wait, p.Id)).Succeeded);
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, p.Id)); Assert.AreEqual(0, p.ActionPointsRemainingUnits);
            var fresh = Ready(); Commit(fresh, new CombatCommand(CombatCommandKind.EndTurn, "player"));
            var waiting = Ready(); Commit(waiting, new CombatCommand(CombatCommandKind.Wait, "player"));
            Assert.AreEqual(50, waiting.TurnScheduler.GetSnapshot(waiting.Combatants.All).Units.Single(u => u.Id == "player").ChargeTime);
        }
        [Test]
        public void StaleDuplicateOrChangedTargetQuotesCannotPartiallyCommit()
        {
            var s = Ready(); var p = s.Combatants.All[0]; var quote = service.Preview(s, Hit());
            Assert.AreEqual(100000, p.ActionPointsRemainingUnits); Commit(s, Hit("art"));
            long version = s.StateVersion; int balance = p.ActionPointsRemainingUnits;
            Assert.IsFalse(service.Execute(s, Hit(), quote).Succeeded); Assert.AreEqual(balance, p.ActionPointsRemainingUnits);
            Assert.AreEqual(version, s.StateVersion);
            quote = service.Preview(s, Hit()); Assert.IsTrue(service.Execute(s, Hit(), quote).Succeeded);
            balance = p.ActionPointsRemainingUnits; Assert.IsFalse(service.Execute(s, Hit(), quote).Succeeded);
            Assert.AreEqual(balance, p.ActionPointsRemainingUnits);
            quote = service.Preview(s, Move(1)); s.Combatants.All[1].SetPosition(new HexCoord(1, 0));
            Assert.IsFalse(service.Execute(s, Move(1), quote).Succeeded); Assert.AreEqual(balance, p.ActionPointsRemainingUnits);
        }
        [Test]
        public void InsufficientMpApAndPreparedEffectOverflowLeaveEveryLedgerUnchanged()
        {
            var s = Ready(null, null, Attack(), Attack("art", 30000, 7, 13, 20)); var p = s.Combatants.All[0];
            p.SetSpirit(30, 10); p.IsGuarding = true;
            Assert.IsFalse(service.Execute(s, Hit("art")).Succeeded); Assert.AreEqual(10, p.CurrentSpirit);
            Assert.IsTrue(p.IsGuarding); Assert.AreEqual(0, p.GetCooldown("art")); Assert.AreEqual(0, s.PendingCtPenalty);
            Commit(s, Hit()); Commit(s, Hit()); long version = s.StateVersion;
            Assert.IsFalse(service.Execute(s, Hit()).Succeeded); Assert.AreEqual(version, s.StateVersion);
            var huge = new CombatAttackProfile("basic", CombatAttackKind.Basic, CombatAttackEffect.Physical, 0, 10,
                40000, 10000, 0, 0, physicalMultiplier: float.MaxValue);
            var overflow = Ready(null, null, huge); Assert.IsFalse(service.Execute(overflow, Hit()).Succeeded);
            Assert.AreEqual(100000, overflow.Combatants.All[0].ActionPointsRemainingUnits);
            Assert.AreEqual(10000, overflow.Combatants.All[1].CurrentHealth);
        }
        [Test]
        public void MissStillPaysAllDeclaredCostsAndDoesNotAdvanceTime()
        {
            var s = Ready(null, null, Attack("art", 30000, 7, 13, 20)); var p = s.Combatants.All[0]; p.SetSpirit(30, 30);
            var r = Commit(s, Hit("art", 101)); Assert.IsFalse(r.Damage[0].IsHit);
            Assert.AreEqual(70000, p.ActionPointsRemainingUnits); Assert.AreEqual(10, p.CurrentSpirit);
            Assert.AreEqual(13, p.GetCooldown("art")); Assert.AreEqual(7, s.PendingCtPenalty);
        }
        [Test]
        public void RecoveryAndControlSkipSettleOnceEvenWithRepeatedAdvance()
        {
            var p = Actor(); p.ReceiveDamage(9950); p.SetSpirit(30, 10); p.HealthRecoveryPerOwnTurn = 7; p.SpiritRecoveryPerOwnTurn = 5;
            var s = Ready(p); Assert.AreEqual(57, p.CurrentHealth); Assert.AreEqual(15, p.CurrentSpirit);
            Commit(s, Move(1)); Commit(s, Hit()); service.AdvanceUntilAction(s);
            Assert.AreEqual(57, p.CurrentHealth); Assert.AreEqual(15, p.CurrentSpirit); Assert.AreEqual(1, p.OwnTurnsStarted);
            var stunned = Actor(); stunned.ReceiveDamage(9950); stunned.HealthRecoveryPerOwnTurn = 7;
            stunned.ApplyStatus(CombatStatusKind.Stun, 1);
            var skip = new CombatSession(new[] { stunned, Actor("z", CombatTeam.Enemy, speed: 100) }, new[] { Attack() }, ProductionSpace(), Ordinary());
            Assert.AreEqual("z", service.AdvanceUntilAction(skip).ActorId); Assert.AreEqual(57, stunned.CurrentHealth);
            Assert.AreEqual(0, stunned.ActionPointsRemainingUnits); Assert.AreEqual(1, stunned.OwnTurnsEnded);
        }
        [Test]
        public void StartDotDeathCannotRecoverOrResurrect()
        {
            var p = Actor(); p.ReceiveDamage(9950); p.HealthRecoveryPerOwnTurn = 100;
            p.ApplyStatus(CombatStatusKind.DamageOverTime, 2, 50);
            var s = new CombatSession(new[] { p, Actor("z", CombatTeam.Enemy, speed: 100) }, new[] { Attack() }, ProductionSpace(), Ordinary());
            Assert.IsFalse(service.AdvanceUntilAction(s).HasActor); Assert.AreEqual(0, p.CurrentHealth);
            Assert.AreEqual(1, p.OwnTurnsStarted); Assert.AreEqual(1, p.OwnTurnsEnded);
        }
        [Test]
        public void StatusDurationCountsSubsequentOwnEndsInsteadOfSubActions()
        {
            var s = Ready(); var p = s.Combatants.All[0]; p.ApplyStatus(CombatStatusKind.SealSpirit, 2);
            Commit(s, Hit()); Commit(s, Move(1)); Assert.AreEqual(2, p.Statuses[0].RemainingOwnTurnEnds);
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, p.Id)); Assert.AreEqual(2, p.Statuses[0].RemainingOwnTurnEnds);
            service.AdvanceUntilAction(s); Commit(s, new CombatCommand(CombatCommandKind.EndTurn, p.Id));
            Assert.AreEqual(1, p.Statuses[0].RemainingOwnTurnEnds);
            service.AdvanceUntilAction(s); Commit(s, new CombatCommand(CombatCommandKind.EndTurn, p.Id)); Assert.IsEmpty(p.Statuses);
        }
        [Test]
        public void ImmobilizeAndSealDoNotBecomeLegalWithMoreAp()
        {
            var s = Ready(); var p = s.Combatants.All[0]; p.ApplyStatus(CombatStatusKind.Immobilize, 2);
            Assert.IsFalse(service.Execute(s, Move(1)).Succeeded); Commit(s, Hit());
            p.ApplyStatus(CombatStatusKind.SealSpirit, 2); Assert.IsFalse(service.Execute(s, Hit("art")).Succeeded);
            Assert.IsFalse(service.Execute(s, Hit("divine")).Succeeded);
            var immobile = Ready(Actor(v: 0)); Assert.IsFalse(service.Execute(immobile, Move(1)).Succeeded);
        }
        [Test]
        public void RefundIsBoundedByNetFloorCountsAndOriginalTurnBudget()
        {
            var modifier = new CombatActionEconomyModifier("fixture-instance", "fixture-definition", CombatCommandKind.BasicAttack,
                "basic", 5000, 800, 10000, 0, 0, 50000, 2, 20000);
            var s = Ready(Actor(modifiers: new[] { modifier })); var p = s.Combatants.All[0];
            Assert.AreEqual(28000, service.Preview(s, Hit()).ActionPointCostUnits);
            var quote = service.Preview(s, Hit()); var first = service.Execute(s, Hit(), quote);
            Assert.AreEqual(18000, first.ActionPointRefundUnits); Assert.IsFalse(service.Execute(s, Hit(), quote).Succeeded);
            Assert.AreEqual(2000, Commit(s, Hit()).ActionPointRefundUnits); Assert.AreEqual(0, Commit(s, Hit()).ActionPointRefundUnits);
            Assert.AreEqual(36000, p.ActionPointsRemainingUnits); Commit(s, Hit());
            Assert.IsFalse(service.Execute(s, Hit()).Succeeded); Assert.AreEqual(8000, p.ActionPointsRemainingUnits);
        }
        [Test]
        public void StableIdsOrderSameTickWithoutChargingAgain()
        {
            var actors = new[] { Actor("d", CombatTeam.Enemy, speed: 60), Actor("b", speed: 60),
                Actor("c", CombatTeam.Enemy, speed: 60), Actor("a", speed: 60) };
            var s = new CombatSession(actors, new[] { Attack() }, ProductionSpace(), Ordinary());
            foreach (string id in new[] { "a", "b", "c", "d" })
            {
                var turn = service.AdvanceUntilAction(s); Assert.AreEqual(id, turn.ActorId); Assert.AreEqual(2, s.TurnScheduler.CurrentTick);
                Commit(s, new CombatCommand(CombatCommandKind.EndTurn, id));
            }
        }
        [Test]
        public void CtOverflowIsRejectedBeforePaymentAndCannotWashOutEarlierPenalty()
        {
            var s = Ready(null, null, Attack("art", 10000, int.MaxValue - 100), Attack("other", 10000, 1));
            Commit(s, Hit("art")); Assert.IsFalse(service.Execute(s, Hit("other")).Succeeded);
            Assert.AreEqual(90000, s.Combatants.All[0].ActionPointsRemainingUnits); Assert.AreEqual(int.MaxValue - 100, s.PendingCtPenalty);
        }
        [Test]
        public void CombatTerminationClearsAllTemporaryBudgetsAndGuard()
        {
            var s = Ready(); var e = s.Combatants.All[1]; e.ReceiveDamage(e.CurrentHealth - 1); e.IsGuarding = true;
            Commit(s, Hit()); Assert.AreEqual(CombatSessionOutcome.Victory, new CombatResultBuilder().Build(s).Outcome);
            Assert.IsTrue(s.Combatants.All.All(a => a.ActionPointsRemainingUnits == 0 && !a.IsGuarding));
        }
        [Test]
        public void TurnEndStacksDoNotMultiplyWithAttacksAndMovementSuppressesShouyi()
        {
            var p = Actor(); p.GongFaId = "抱元守一经"; var s = Ready(p);
            Commit(s, Hit()); Commit(s, Hit()); Assert.AreEqual(0, p.ShouyiStacks);
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, p.Id)); Assert.AreEqual(1, p.ShouyiStacks);
            service.AdvanceUntilAction(s); Commit(s, Move(1)); Commit(s, new CombatCommand(CombatCommandKind.EndTurn, p.Id));
            Assert.AreEqual(1, p.ShouyiStacks);
            p = Actor(); p.GongFaId = "云篆度人经"; s = Ready(p); Commit(s, Hit()); Commit(s, Hit());
            Assert.AreEqual(0, p.FudanStacks); Commit(s, new CombatCommand(CombatCommandKind.EndTurn, p.Id)); Assert.AreEqual(1, p.FudanStacks);
        }
    }
}
