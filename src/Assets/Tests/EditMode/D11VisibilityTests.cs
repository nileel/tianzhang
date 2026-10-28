using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    public sealed class D11VisibilityTests
    {
        private const string Left = "a_yang", Right = "z_yin", LeftSeat = "yang-seat", RightSeat = "yin-seat";
        private readonly CombatCommandService commands = new CombatCommandService();

        [TestCase("permission")] [TestCase("position")] [TestCase("support")]
        [TestCase("paid_cost")] [TestCase("carrying")]
        public void EachPriorityDecidesBeforeLaterAdvantagesAndWithoutPayment(string step)
        {
            var baseline = new CombatConflictPriority(true, 2, 2, 250000, 400, 2);
            CombatConflictPriority other = step switch
            {
                "permission" => new CombatConflictPriority(false, 99, 99, 900000, 900, 99),
                "position" => new CombatConflictPriority(true, 1, 99, 900000, 900, 99),
                "support" => new CombatConflictPriority(true, 2, 1, 900000, 900, 99),
                "paid_cost" => new CombatConflictPriority(true, 2, 2, 249999, 400, 99),
                _ => new CombatConflictPriority(true, 2, 2, 250000, 400, 1),
            };
            var s = Ready();
            var conflict = Contest(s, baseline, other);
            Assert.AreEqual(step, conflict.PriorityStep); Assert.AreEqual(CombatConflictOutcome.Left, conflict.Outcome);
            conflict.Skip(conflict.EventId); conflict.Input(conflict.EventId, 0, true, false, 1);
            Assert.AreEqual(0, conflict.PulseCount); Assert.AreEqual(0, conflict.LeftSpent); Assert.AreEqual(0, conflict.RightSpent);
            Assert.AreEqual(200, Seat(s, LeftSeat).VisibilityLedger.ReserveRemaining);
            var reverse = Contest(s, other, baseline);
            Assert.AreEqual(CombatConflictOutcome.Right, reverse.Outcome); Assert.AreEqual(step, reverse.PriorityStep);
        }

        [Test]
        public void EqualPriorityWaitsAndMixedPaidCurrenciesCannotInventAnExchangeRate()
        {
            var s = Ready(); var equal = Contest(s);
            Assert.AreEqual("equal", equal.PriorityStep); Assert.IsTrue(equal.IsPending); Assert.AreEqual(0, equal.PulseCount);
            Assert.Throws<ArgumentException>(() => CombatConflictPriority.Compare(
                new CombatConflictPriority(true, 1, 1, 250000, 400, 1),
                new CombatConflictPriority(true, 1, 1, 200000, 500, 1), out _));
        }

        [TestCase(200, 200, CombatConflictOutcome.Neutral, 200, 200)]
        [TestCase(150, 100, CombatConflictOutcome.Left, 150, 100)]
        [TestCase(100, 150, CombatConflictOutcome.Right, 100, 150)]
        [TestCase(0, 200, CombatConflictOutcome.Right, 0, 50)]
        [TestCase(200, 0, CombatConflictOutcome.Left, 50, 0)]
        [TestCase(0, 0, CombatConflictOutcome.Neutral, 0, 0)]
        [TestCase(49, 200, CombatConflictOutcome.Right, 0, 50)]
        public void AutomaticPulsesStopAtTheFirstUnaffordableSide(int leftMana, int rightMana,
            CombatConflictOutcome outcome, int leftSpent, int rightSpent)
        {
            var s = Ready(); Actor(s, Left).SetSpirit(5000, leftMana); Actor(s, Right).SetSpirit(5000, rightMana);
            var conflict = Contest(s); conflict.Skip(conflict.EventId);
            Assert.AreEqual(outcome, conflict.Outcome); Assert.AreEqual(leftSpent, conflict.LeftSpent); Assert.AreEqual(rightSpent, conflict.RightSpent);
            Assert.AreEqual(leftMana - leftSpent, Actor(s, Left).CurrentSpirit); Assert.AreEqual(rightMana - rightSpent, Actor(s, Right).CurrentSpirit);
            Assert.AreEqual(200 - leftSpent, Seat(s, LeftSeat).VisibilityLedger.ReserveRemaining);
            Assert.AreEqual(200 - rightSpent, Seat(s, RightSeat).VisibilityLedger.ReserveRemaining);
            Assert.AreEqual(leftSpent, Seat(s, LeftSeat).TotalManaSpent); Assert.AreEqual(rightSpent, Seat(s, RightSeat).TotalManaSpent);
            int count = conflict.PulseCount; conflict.Skip(conflict.EventId); conflict.Input(conflict.EventId, 99999, true, false, 3);
            Assert.AreEqual(count, conflict.PulseCount); Assert.AreEqual(leftMana - leftSpent, Actor(s, Left).CurrentSpirit);
        }

        [TestCase(true)] [TestCase(false)]
        public void EqualExhaustionUsesTheObservedVariablesNeutralBaseline(bool baseVisible)
        {
            var s = Ready(); var conflict = Contest(s, baseVisible: baseVisible); conflict.Skip(conflict.EventId);
            Assert.AreEqual(CombatConflictOutcome.Neutral, conflict.Outcome); Assert.AreEqual(baseVisible, conflict.Visible);
        }

        [Test]
        public void BattleReserveCapsPulsesEvenWithPlentyOfMana()
        {
            var s = Ready(leftReserve: 150, rightReserve: 100); var conflict = Contest(s); conflict.Skip(conflict.EventId);
            Assert.AreEqual(CombatConflictOutcome.Left, conflict.Outcome);
            Assert.AreEqual(150, conflict.LeftSpent); Assert.AreEqual(100, conflict.RightSpent);
            Assert.AreEqual(4850, Actor(s, Left).CurrentSpirit); Assert.AreEqual(4900, Actor(s, Right).CurrentSpirit);
        }

        [Test]
        public void WaitingWithoutSpaceDoesNotPayAndWindowIs250Inclusive750Exclusive()
        {
            var s = Ready(); var conflict = Contest(s);
            conflict.Input(conflict.EventId, 0, false, false, 1);
            conflict.Input(conflict.EventId, 10000, false, false, 2);
            Assert.IsFalse(conflict.Started); Assert.AreEqual(0, conflict.LeftSpent);
            conflict.Input(conflict.EventId, 11000, true, false, 0);
            Assert.AreEqual(50, conflict.LeftSpent); Assert.AreEqual(1, conflict.PulseCount);
            conflict.Input(conflict.EventId, 11249, false, false, 1); Assert.AreEqual(0, conflict.Gauge);
            conflict.Input(conflict.EventId, 11250, false, false, 1); Assert.AreEqual(50, conflict.Gauge);
            conflict.Input(conflict.EventId, 11251, false, false, 1); Assert.AreEqual(50, conflict.Gauge);
            conflict.Input(conflict.EventId, 12000, false, false, 0); Assert.AreEqual(2, conflict.PulseCount);
            conflict.Input(conflict.EventId, 12750, false, false, 2); Assert.AreEqual(50, conflict.Gauge);
            Assert.IsFalse(conflict.WindowOpen); Assert.IsTrue(conflict.IsPending);
        }

        [Test]
        public void WrongAndSimultaneousKeysHaveNoPenaltyAndOnlyOneCorrectEdgeCountsPerWindow()
        {
            var s = Ready(); var conflict = Contest(s); conflict.Input(conflict.EventId, 0, true, false, 0);
            conflict.Input(conflict.EventId, 250, false, false, 2);
            conflict.Input(conflict.EventId, 300, false, false, 3);
            Assert.AreEqual(0, conflict.Gauge); Assert.AreEqual(50, conflict.LeftSpent); Assert.AreEqual(50, conflict.RightSpent);
            conflict.Input(conflict.EventId, 350, false, false, 1); Assert.AreEqual(50, conflict.Gauge);
            conflict.Input(conflict.EventId, 400, false, false, 1); Assert.AreEqual(50, conflict.Gauge);
            conflict.Input(conflict.EventId, 1000, false, false, 0);
            Assert.AreEqual("D", conflict.ExpectedKey); Assert.AreEqual(100, conflict.LeftSpent);
            conflict.Input(conflict.EventId, 1250, false, false, 2);
            Assert.AreEqual(CombatConflictOutcome.Left, conflict.Outcome); Assert.AreEqual("gauge", conflict.EndReason);
            Assert.AreEqual(100, conflict.LeftSpent); Assert.AreEqual(100, conflict.RightSpent);
        }

        [Test]
        public void ReleasingStopsImmediatelyAndDoesNotPayFuturePulses()
        {
            var s = Ready(); var conflict = Contest(s); conflict.Input(conflict.EventId, 0, true, false, 0);
            conflict.Input(conflict.EventId, 100, false, true, 0);
            Assert.AreEqual(CombatConflictOutcome.Right, conflict.Outcome); Assert.AreEqual("released", conflict.EndReason);
            conflict.Input(conflict.EventId, 5000, true, false, 1);
            Assert.AreEqual(50, conflict.LeftSpent); Assert.AreEqual(50, conflict.RightSpent);
        }

        [Test]
        public void SkipAfterManualProgressUsesOnlyRemainingReserves()
        {
            var s = Ready(); Actor(s, Left).SetSpirit(5000, 150); Actor(s, Right).SetSpirit(5000, 100);
            var conflict = Contest(s); conflict.Input(conflict.EventId, 0, true, false, 0);
            conflict.Input(conflict.EventId, 250, false, false, 1); conflict.Skip(conflict.EventId);
            Assert.AreEqual(CombatConflictOutcome.Left, conflict.Outcome); Assert.AreEqual(150, conflict.LeftSpent); Assert.AreEqual(100, conflict.RightSpent);
            Assert.AreEqual(0, Actor(s, Left).CurrentSpirit); Assert.AreEqual(0, Actor(s, Right).CurrentSpirit);
        }

        [Test]
        public void OldEventAndBackwardTimeCannotAdvanceOrPayTheCurrentContest()
        {
            var s = Ready(); var conflict = Contest(s);
            conflict.Input(0, 99999, true, false, 1); conflict.Skip(0);
            Assert.AreEqual(0, conflict.PulseCount);
            conflict.Input(conflict.EventId, 100, true, false, 0);
            conflict.Input(conflict.EventId, 90, false, false, 1);
            Assert.AreEqual(1, conflict.PulseCount); Assert.AreEqual(0, conflict.Gauge); Assert.AreEqual(50, conflict.LeftSpent);
        }

        [Test]
        public void BothDomainsDeployThroughTheSharedLedgerAndReadQueriesCannotCharge()
        {
            var s = Deployed(); Assert.IsTrue(s.Visibility.IsPaused);
            foreach (var seat in s.Jindan.Seats)
            {
                Assert.AreEqual(400, seat.TotalManaSpent); Assert.AreEqual(250000, seat.TotalActionPointSpent);
                Assert.AreEqual(1, seat.SuccessfulDeployments); Assert.AreEqual(600, seat.ActiveVisibility.Support.Durability);
            }
            long id = s.Visibility.Current.EventId;
            for (int i = 0; i < 4; i++) { s.Visibility.IsVisible(Left, Right); s.Visibility.Refresh(); }
            Assert.AreEqual(id, s.Visibility.Current.EventId); Assert.AreEqual(1, s.Visibility.History.Count);
            Assert.AreEqual(400, Seat(s, LeftSeat).TotalManaSpent);
            s.Visibility.Skip(id, false); Assert.IsFalse(s.Visibility.IsPaused); Assert.IsTrue(s.Visibility.IsVisible(Left, Right));
            for (int i = 0; i < 4; i++) { s.Visibility.Refresh(); s.Visibility.Skip(id, false); }
            Assert.AreEqual(1, s.Visibility.History.Count); Assert.AreEqual(600, Seat(s, LeftSeat).TotalManaSpent);
        }

        [Test]
        public void ANewTickAllowsANewObservationWithoutRefillingBattleReserves()
        {
            var s = Deployed(); s.Visibility.Skip(s.Visibility.Current.EventId, false);
            long first = s.Visibility.Current.EventId; int tick = s.TurnScheduler.CurrentTick;
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, Right)); commands.AdvanceUntilAction(s);
            Assert.Greater(s.TurnScheduler.CurrentTick, tick); Assert.IsFalse(s.Visibility.IsPaused);
            Assert.Greater(s.Visibility.Current.EventId, first);
            int mp = Actor(s, Left).CurrentSpirit; s.Visibility.Skip(first, true);
            Assert.IsFalse(s.Visibility.IsPaused); Assert.IsFalse(s.Visibility.AutoSkip);
            s.Visibility.Skip(s.Visibility.Current.EventId, false);
            Assert.AreEqual(CombatConflictOutcome.Neutral, s.Visibility.Current.Outcome); Assert.AreEqual(0, s.Visibility.Current.LeftSpent);
            Assert.AreEqual(0, s.Visibility.Current.PulseCount); Assert.IsFalse(s.Visibility.Current.Started);
            Assert.AreEqual(mp, Actor(s, Left).CurrentSpirit); Assert.AreEqual(0, Seat(s, LeftSeat).VisibilityLedger.ReserveRemaining);
        }

        [Test]
        public void WholeBattleSkipAutomaticallySettlesLaterObservations()
        {
            var s = Deployed(); s.Visibility.Skip(s.Visibility.Current.EventId, true); Assert.IsTrue(s.Visibility.AutoSkip);
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, Right)); commands.AdvanceUntilAction(s);
            Assert.IsFalse(s.Visibility.IsPaused); Assert.AreEqual(2, s.Visibility.History.Count);
            Assert.AreEqual(0, s.Visibility.Current.LeftSpent); Assert.AreEqual(0, s.Visibility.Current.RightSpent);
        }

        [Test]
        public void PendingConflictFreezesAuthorityClocksStatusesCooldownsAndEnvironmentUntilItResolves()
        {
            var s = Deployed(); Actor(s, Left).SetCooldown("witness", 7);
            Actor(s, Left).ApplyStatus(CombatStatusKind.DamageOverTime, 3, 9);
            string before = FrozenClocks(s); long id = s.Visibility.Current.EventId;
            foreach (var kind in new[] { CombatCommandKind.Guard, CombatCommandKind.Wait, CombatCommandKind.EndTurn })
            {
                var result = commands.Execute(s, new CombatCommand(kind, Right));
                Assert.IsFalse(result.Succeeded); Assert.AreEqual("combat_visibility_conflict_pending", result.RejectionReason);
            }
            for (int i = 0; i < 3; i++) Assert.AreEqual(0, commands.AdvanceUntilAction(s).TicksElapsed);
            s.Visibility.Input(id, 0, true, false, 0); s.Visibility.Input(id, 1000, false, false, 0);
            Assert.AreEqual(before, FrozenClocks(s));
            s.Visibility.Skip(id, false); Assert.AreEqual(before, FrozenClocks(s));
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, Right)); commands.AdvanceUntilAction(s);
            Assert.Less(Actor(s, Left).GetCooldown("witness"), 7); Assert.AreNotEqual(before, FrozenClocks(s));
        }

        [TestCase(1, false, true)] [TestCase(3, true, false)] [TestCase(6, false, false)]
        public void NearTargetsWallsAndOutOfVisionDoNotCreateAConflict(int targetQ, bool wall, bool visible)
        {
            var s = Deployed(targetQ: targetQ, wall: wall);
            Assert.IsFalse(s.Visibility.IsPaused); Assert.IsEmpty(s.Visibility.History);
            Assert.AreEqual(visible, s.Visibility.IsVisible(Left, Right));
            Assert.AreEqual(0, Seat(s, LeftSeat).VisibilityLedger.ConflictManaSpent);
        }

        [TestCase(2, 1, 2)] [TestCase(1, 4, 4)]
        public void PaidSpaceScaleRevealsCompressedNearTargetsAndCannotHidePhysicalNeighbours(
            int targetQ, int edgeMetric, int expectedDistanceUnits)
        {
            if (targetQ == 2)
            {
                var baseline = Deployed(targetQ: targetQ);
                Assert.IsTrue(baseline.Visibility.IsPaused, "Two uncompressed hexes are outside natural reveal distance.");
                Assert.IsFalse(baseline.Visibility.NaturallyRevealed(Actor(baseline, Right)));
            }
            var s = Ready(targetQ: targetQ, withScale: true);
            var path = Enumerable.Range(0, targetQ + 1).Select(q => new HexCoord(q, 1)).ToArray();
            Commit(s, new CombatCommand(CombatCommandKind.JindanAbility, Left, profileId: "SPACE_SCALE",
                jindan: new CombatJindanRequest("scale-seat", path, edgeMetric)));
            Assert.AreEqual(250000, Seat(s, "scale-seat").TotalActionPointSpent);
            Assert.AreEqual(400, Seat(s, "scale-seat").TotalManaSpent);
            Assert.AreEqual(expectedDistanceUnits, s.SceneObjects.Board.QueryMetricDistance(
                Actor(s, Left).Position, Actor(s, Right).Position, SpatialQueryKind.Sight).DistanceUnits);
            Commit(s, Deploy(Left)); Commit(s, new CombatCommand(CombatCommandKind.EndTurn, Left));
            Assert.AreEqual(Right, commands.AdvanceUntilAction(s).ActorId); Commit(s, Deploy(Right));
            Assert.IsTrue(s.Visibility.NaturallyRevealed(Actor(s, Right)));
            Assert.IsTrue(s.Visibility.IsVisible(Left, Right)); Assert.IsFalse(s.Visibility.IsPaused); Assert.IsEmpty(s.Visibility.History);
            Assert.AreEqual(0, Seat(s, LeftSeat).VisibilityLedger.ConflictManaSpent);
            Assert.AreEqual(0, Seat(s, RightSeat).VisibilityLedger.ConflictManaSpent);
            Assert.AreEqual(200, Seat(s, LeftSeat).VisibilityLedger.ReserveRemaining);
            Assert.AreEqual(200, Seat(s, RightSeat).VisibilityLedger.ReserveRemaining);
        }

        [TestCase(Left)] [TestCase(Right)]
        public void VisibilityExpiresAtCreationPlusSixAfterExactlyFivePaidMaintenanceTicks(string ownerId)
        {
            var s = Ready();
            if (ownerId == Right)
            { Commit(s, new CombatCommand(CombatCommandKind.EndTurn, Left)); Assert.AreEqual(Right, commands.AdvanceUntilAction(s).ActorId); }
            Commit(s, Deploy(ownerId)); var seat = Seat(s, ownerId == Left ? LeftSeat : RightSeat);
            var instance = seat.ActiveVisibility; int created = instance.CreatedTick;
            Assert.AreEqual(created + 6, instance.ExpiresTick); Assert.AreEqual(400, seat.TotalManaSpent);
            for (int elapsed = 1; elapsed <= 6; elapsed++)
            {
                for (int steps = 0; steps < 2 && s.TurnScheduler.CurrentTick < created + elapsed; steps++)
                {
                    Commit(s, new CombatCommand(CombatCommandKind.EndTurn, s.CurrentActorId));
                    commands.AdvanceUntilAction(s);
                }
                Assert.AreEqual(created + elapsed, s.TurnScheduler.CurrentTick);
                Assert.AreEqual(Math.Min(elapsed, 5), seat.MaintenancePayments);
                Assert.AreEqual(400 + 40 * Math.Min(elapsed, 5), seat.TotalManaSpent);
                if (elapsed < 6)
                {
                    Assert.AreSame(instance, seat.ActiveVisibility);
                    Assert.AreEqual(created + elapsed, instance.LastPaidTick);
                }
            }
            Assert.IsNull(seat.ActiveVisibility); Assert.AreEqual("visibility_expired", seat.LastEndReason);
            Assert.AreEqual(created + 5, instance.LastPaidTick); Assert.AreEqual(4400, Actor(s, ownerId).CurrentSpirit);
            Assert.IsNull(s.SceneObjects.Find(instance.Support.Id)); Assert.IsEmpty(s.SceneObjects.All);
            Assert.IsEmpty(s.Visibility.History); Assert.AreEqual(200, seat.VisibilityLedger.ReserveRemaining);
        }

        [Test]
        public void ClosingAndRedeployingCannotRefillTheGuardianConflictBudget()
        {
            var s = Deployed(); s.Visibility.Skip(s.Visibility.Current.EventId, false);
            var ledger = Seat(s, RightSeat).VisibilityLedger;
            Commit(s, Close(Right)); Assert.IsNull(Seat(s, RightSeat).ActiveVisibility);
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, Right)); commands.AdvanceUntilAction(s);
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, Left)); commands.AdvanceUntilAction(s);
            Commit(s, Deploy(Right));
            Assert.AreSame(ledger, Seat(s, RightSeat).VisibilityLedger); Assert.AreEqual(0, ledger.ReserveRemaining);
            Assert.AreEqual(200, ledger.ConflictManaSpent); Assert.AreEqual(2, Seat(s, RightSeat).SuccessfulDeployments);
        }

        [Test]
        public void BattleClearCancelsPendingInputAndLeavesNoDomainOrLatePayment()
        {
            var s = Deployed(); var conflict = s.Visibility.Current;
            s.Visibility.Input(conflict.EventId, 0, true, false, 0); int mana = Actor(s, Left).CurrentSpirit;
            s.Jindan.Clear(s); Assert.IsFalse(s.Visibility.IsPaused); Assert.AreEqual(CombatConflictOutcome.Cancelled, conflict.Outcome);
            Assert.IsTrue(s.Jindan.Seats.All(seat => seat.ActiveVisibility == null));
            s.Visibility.Input(conflict.EventId, 5000, true, false, 1); s.Visibility.Skip(conflict.EventId, false); s.Visibility.Refresh();
            Assert.AreEqual(mana, Actor(s, Left).CurrentSpirit); Assert.IsEmpty(s.SceneObjects.All);
        }

        [Test]
        public void QuoteFromBeforeConflictCannotCommitDuringOrAfterSettlement()
        {
            var s = Ready(); Commit(s, Deploy(Left)); Commit(s, new CombatCommand(CombatCommandKind.EndTurn, Left));
            commands.AdvanceUntilAction(s);
            var guard = new CombatCommand(CombatCommandKind.Guard, Right); var old = commands.Preview(s, guard);
            Assert.IsTrue(old.IsLegal, old.RejectionReason); Commit(s, Deploy(Right));
            int ap = Actor(s, Right).ActionPointsRemainingUnits;
            Assert.AreEqual("combat_quote_stale", commands.Execute(s, guard, old).RejectionReason);
            s.Visibility.Skip(s.Visibility.Current.EventId, false);
            Assert.AreEqual("combat_quote_stale", commands.Execute(s, guard, old).RejectionReason);
            Assert.AreEqual(ap, Actor(s, Right).ActionPointsRemainingUnits); Assert.IsFalse(Actor(s, Right).IsGuarding);
            Commit(s, guard); Assert.IsTrue(Actor(s, Right).IsGuarding);
        }

        [Test]
        public void BattleEndingDeathCancelsTheContestBeforeAnotherClockAdvance()
        {
            var s = Deployed(); var conflict = s.Visibility.Current; int tick = s.TurnScheduler.CurrentTick;
            Actor(s, Right).ReceiveDamage(10000);
            var next = commands.AdvanceUntilAction(s);
            Assert.IsFalse(next.HasActor); Assert.AreEqual(tick, s.TurnScheduler.CurrentTick);
            Assert.IsFalse(s.Visibility.IsPaused); Assert.AreEqual(CombatConflictOutcome.Cancelled, conflict.Outcome);
            Assert.IsTrue(s.Jindan.Seats.All(seat => seat.ActiveVisibility == null)); Assert.IsEmpty(s.SceneObjects.All);
        }

        private CombatSession Deployed(int targetQ = 3, bool wall = false)
        {
            var s = Ready(targetQ: targetQ, wall: wall); Commit(s, Deploy(Left));
            Commit(s, new CombatCommand(CombatCommandKind.EndTurn, Left)); Assert.AreEqual(Right, commands.AdvanceUntilAction(s).ActorId);
            Commit(s, Deploy(Right)); return s;
        }

        private static CombatConflictSettings Settings(int reserve = 200) => new CombatConflictSettings(
            "d11-test", reserve, 50, 1000, 250, 750, 50, 100, "Space", "A", "D");
        private static CombatConflictPriority EqualPriority => new CombatConflictPriority(true, 1, 1, 250000, 400, 1);
        private static CombatVisibilityConflict Contest(CombatSession s, CombatConflictPriority? left = null,
            CombatConflictPriority? right = null, bool baseVisible = true) => new CombatVisibilityConflict(1, "query", Left, Right,
                Actor(s, Left), Seat(s, LeftSeat), Actor(s, Right), Seat(s, RightSeat), Settings(), left ?? EqualPriority, right ?? EqualPriority, baseVisible);
        private static CombatantSnapshot Actor(CombatSession s, string id) { s.Combatants.TryGet(id, out var actor); return actor; }
        private static CombatJindanSeatState Seat(CombatSession s, string id) => s.Jindan.Find(id);
        private static CombatCommand Deploy(string actor) => new CombatCommand(CombatCommandKind.JindanAbility, actor,
            profileId: actor == Left ? CombatVisibility.Yang : CombatVisibility.Yin,
            jindan: new CombatJindanRequest(actor == Left ? LeftSeat : RightSeat));
        private static CombatCommand Close(string actor) => new CombatCommand(CombatCommandKind.CloseJindan, actor,
            profileId: actor == Left ? CombatVisibility.Yang : CombatVisibility.Yin,
            jindan: new CombatJindanRequest(actor == Left ? LeftSeat : RightSeat));
        private void Commit(CombatSession s, CombatCommand command)
        { var result = commands.Execute(s, command); Assert.IsTrue(result.Succeeded, result.RejectionReason); }

        private static string FrozenClocks(CombatSession s)
        {
            var turns = s.TurnScheduler.GetSnapshot(s.Combatants.All);
            return string.Join("|", turns.CurrentTick, s.TurnSequence, s.CurrentActorId, s.TurnPhase, s.PendingCtPenalty,
                string.Join("/", turns.Units.Select(unit => unit.Id + ":" + unit.ChargeTime + ":" + unit.NextActionThreshold)),
                string.Join("/", s.Combatants.All.Select(actor => actor.Id + ":" + actor.CurrentHealth + ":" + actor.ActionPointsRemainingUnits +
                    ":" + actor.OwnTurnsStarted + ":" + actor.OwnTurnsEnded + ":" + string.Join(",", actor.Cooldowns.Select(pair => pair.Key + ":" + pair.Value)) +
                    ":" + string.Join(",", actor.Statuses.Select(status => status.Kind + ":" + status.RemainingOwnTurnEnds)))),
                string.Join("/", s.Jindan.Seats.Select(seat => seat.Definition.SeatId + ":" + seat.MaintenancePayments + ":" + seat.ActiveVisibility?.LastPaidTick)),
                s.SceneObjects.Revision, s.Surfaces.Revision, s.Surfaces.SettlementSequence,
                string.Join("/", s.Surfaces.FireSources.Select(fire => fire.ElapsedTicks + ":" + fire.RemainingFuel)));
        }

        private CombatSession Ready(int leftReserve = 200, int rightReserve = 200, int targetQ = 3, bool wall = false, bool withScale = false)
        {
            var cells = new Dictionary<HexCoord, SpatialCellRules>();
            for (int q = 0; q <= 6; q++) for (int r = 0; r <= 2; r++)
                cells.Add(new HexCoord(q, r), new SpatialCellRules(0, false, wall && q == 2 && r == 1, false, 0, true, 4));
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            foreach (var cell in cells.Keys) for (int direction = 0; direction < 6; direction++)
                if (cells.ContainsKey(cell.Step(direction))) edges.Add(new SpatialDirectedEdge(cell, cell.Step(direction)), new SpatialEdgeRules(2, true, true, allowsFlight: true));
            var environment = new CombatSceneObjects(new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(2, 16)), Array.Empty<CombatSceneObject>(), 10, 20);
            CombatantSnapshot Make(string id, CombatTeam team, HexCoord cell, string effect)
            {
                var bindings = new Dictionary<string, string> { [effect] = id + "_guardian" };
                if (withScale && id == Left) bindings.Add("SPACE_SCALE", Left + "_scale_guardian");
                var actor = new CombatantSnapshot(id, team, cell, 100, 10000, 10000, 100, 2400, 1, 1, 1000000, 20000,
                    abilityInstanceIds: bindings, realmStage: "金丹");
                actor.SetSpirit(5000, 5000); return actor;
            }
            var left = Make(Left, CombatTeam.Player, new HexCoord(0, 1), CombatVisibility.Yang);
            var right = Make(Right, CombatTeam.Enemy, new HexCoord(targetQ, 1), CombatVisibility.Yin);
            var s = new CombatSession(new[] { left, right }, Array.Empty<CombatAttackProfile>(), new Query(environment),
                new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0),
                    new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) },
                ruleSet: CombatActionPointKernelTests.Rules(), sceneObjects: environment);
            CombatJindanAbilityDefinition Ability(string effect, HexCoord center, int reserve) => new CombatJindanAbilityDefinition(
                effect, true, 250000, 400, 1, 25000, 40, 4, 3, 1, 600, 0, 4, Array.Empty<int>(), "", 6,
                visibility: new CombatVisibilityDefinition(center, effect + "_support", 1, 1, 6, Settings(reserve), 1, 30, 100, 200));
            s.ConfigureJindan(new CombatJindanLoadout(new[] {
                new CombatJindanSeatDefinition(Left, LeftSeat, "yang", "Domain", Left + "_guardian", new[] { CombatVisibility.Yang }, CombatVisibility.Yang),
                new CombatJindanSeatDefinition(Right, RightSeat, "yin", "Domain", Right + "_guardian", new[] { CombatVisibility.Yin }, CombatVisibility.Yin) }
                .Concat(withScale ? new[] { new CombatJindanSeatDefinition(Left, "scale-seat", "scale", "Source",
                    Left + "_scale_guardian", new[] { "SPACE_SCALE" }, "SPACE_SCALE") } : Array.Empty<CombatJindanSeatDefinition>()),
                new[] { Ability(CombatVisibility.Yang, left.Position, leftReserve), Ability(CombatVisibility.Yin, right.Position, rightReserve) }
                .Concat(withScale ? new[] { new CombatJindanAbilityDefinition("SPACE_SCALE", true, 250000, 400, 1,
                    25000, 40, 4, 3, 2, 600, 300, 4, new[] { 1, 4 }, "") } : Array.Empty<CombatJindanAbilityDefinition>()), 1));
            s.ConfigureSurfaces(new CombatSurfaces(cells.Keys.Select(cell => new CombatSurfaceDefinition(cell, CombatSurfaceKind.None)),
                new[] { new CombatFireDefinition("clock-witness", Right, 1, 20, 1, 10, 2400, .01f) }));
            Assert.AreEqual(Left, commands.AdvanceUntilAction(s).ActorId); return s;
        }

        private sealed class Query : ICombatSpatialQuery
        {
            private readonly CombatSceneObjects environment;
            public Query(CombatSceneObjects environment) => this.environment = environment;
            public SpatialContactResult QueryRange(SpatialPose a, SpatialPose b, int min, int max, SpatialAttackPolicy policy = null) => environment.Board.QueryContact(a, b, min, max, policy);
            public CombatMovementQueryResult QueryMovement(SpatialPose a, HexCoord b, int budget, int efficiency, IReadOnlyCollection<HexCoord> occupied, int? facing = null)
            { var p = environment.Board.QueryMovementPath(a, b, budget, efficiency, occupied, facing); return new CombatMovementQueryResult(p.Steps.Count > 0, p.Reason, p.Steps, p.UnitsPerRange, p.ActionPointCostUnits); }
            public IReadOnlyDictionary<SpatialPose, int> FindReachable(SpatialPose a, int budget, int efficiency, IReadOnlyCollection<HexCoord> occupied) => environment.Board.FindReachableWithinActionPoints(a, budget, efficiency, occupied);
        }
    }
}
