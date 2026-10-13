using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    public sealed class CombatRuntimeKernelTests
    {
        [Test]
        public void OneVsOneUsesStableIdsCtbAndTheEstablishedPhysicalDamageLine()
        {
            CombatantSnapshot player = CreateCombatant("player", CombatTeam.Player, new HexCoord(0, 0), 50, 100, 40, 20);
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 25, 100, 20, 20);
            CombatSession session = CreateSession(new[] { player, enemy }, new FixedRangeQuery(true));
            var service = new CombatCommandService();

            CombatTurnAdvance advance = service.AdvanceUntilAction(session);
            Assert.That(advance.ActorId, Is.EqualTo("player"));
            Assert.That(advance.TicksElapsed, Is.EqualTo(2));

            CombatActionResult result = service.Execute(session, new CombatCommand(
                CombatCommandKind.BasicAttack,
                "player",
                "enemy",
                "basic",
                new CombatResolutionRolls(0f, 100f, 100f, 100f)));

            Assert.That(result.Succeeded, Is.True, result.RejectionReason);
            Assert.That(result.Damage.Count, Is.EqualTo(1));
            Assert.That(result.Damage[0].FinalDamage, Is.EqualTo(35));
            Assert.That(enemy.CurrentHealth, Is.EqualTo(65));
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(99999));
            Assert.That(session.TurnScheduler.IsReady("player"), Is.True);
        }

        [Test]
        public void RangeAndTeamRejectionsLeaveTheTurnReadyForAValidRetry()
        {
            CombatantSnapshot player = CreateCombatant("player", CombatTeam.Player, new HexCoord(0, 0), 100, 100, 20, 10);
            CombatantSnapshot ally = CreateCombatant("ally", CombatTeam.Player, new HexCoord(1, 0), 1, 100, 20, 10);
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(2, 0), 1, 100, 20, 10);
            CombatSession session = CreateSession(new[] { player, ally, enemy, CreateCombatant("enemy_two", CombatTeam.Enemy, new HexCoord(3, 0), 1, 100, 20, 10) }, new FixedRangeQuery(false, "declared_effect_blocker"));
            var service = new CombatCommandService();

            Assert.That(service.AdvanceUntilAction(session).ActorId, Is.EqualTo("player"));
            CombatActionResult allyResult = service.Execute(session, new CombatCommand(CombatCommandKind.BasicAttack, "player", "ally", "basic"));
            Assert.That(allyResult.Succeeded, Is.False);
            Assert.That(allyResult.RejectionReason, Is.EqualTo("combat_session_target_invalid"));
            Assert.That(session.TurnScheduler.IsReady("player"), Is.True);

            CombatActionResult rangeResult = service.Execute(session, new CombatCommand(CombatCommandKind.BasicAttack, "player", "enemy", "basic"));
            Assert.That(rangeResult.Succeeded, Is.False);
            Assert.That(rangeResult.RejectionReason, Is.EqualTo("declared_effect_blocker"));
            Assert.That(session.TurnScheduler.IsReady("player"), Is.True);
        }

        [Test]
        public void MoveUsesTheCanonicalSpatialPathAndConsumesActionPoints()
        {
            HexCoord origin = new HexCoord(0, 0);
            HexCoord destination = new HexCoord(0, 1);
            CombatantSnapshot player = CreateCombatant(
                "player", CombatTeam.Player, origin, 100, 100, 20, 10, movePoints: 2);
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(2, 0), 1, 100, 20, 10);
            var query = new FixedRangeQuery(true, movementBySource: CreateMovementMap(
                origin,
                destination,
                new[] { new HexCoord(1, 0), destination },
                2));
            CombatSession session = CreateSession(new[] { player, enemy }, query);
            var service = new CombatCommandService();

            Assert.That(service.AdvanceUntilAction(session).ActorId, Is.EqualTo(player.Id));
            CombatActionResult result = service.Execute(
                session,
                new CombatCommand(CombatCommandKind.Move, player.Id, destination: destination));

            Assert.That(result.Succeeded, Is.True, result.RejectionReason);
            Assert.That(result.MovementPath, Is.EqualTo(new[] { new HexCoord(1, 0), destination }));
            Assert.That(result.MovementCost, Is.EqualTo(20000));
            Assert.That(player.Position, Is.EqualTo(destination));
            Assert.That(player.ActionPointMaximumUnits, Is.EqualTo(100000));
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(80000));
            Assert.That(player.Facing, Is.EqualTo(new HexCoord(1, 0).DirectionTo(destination)));
            Assert.That(session.TurnScheduler.IsReady(player.Id), Is.True);
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.BasicAttack,
                player.Id, enemy.Id, "basic")).Succeeded, Is.True);
        }

        [Test]
        public void MoveRejectionsLeavePositionAndCtbStateUnchanged()
        {
            HexCoord origin = new HexCoord(0, 0);
            CombatantSnapshot player = CreateCombatant(
                "player", CombatTeam.Player, origin, 100, 100, 20, 10, movePoints: 1);
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1, 100, 20, 10);
            CombatSession session = CreateSession(new[] { player, enemy }, new FixedRangeQuery(true));
            var service = new CombatCommandService();

            Assert.That(service.AdvanceUntilAction(session).ActorId, Is.EqualTo(player.Id));
            CombatActionResult occupied = service.Execute(
                session,
                new CombatCommand(CombatCommandKind.Move, player.Id, destination: enemy.Position));
            Assert.That(occupied.Succeeded, Is.False);
            Assert.That(occupied.RejectionReason, Is.EqualTo("combat_move_destination_occupied"));
            Assert.That(player.Position, Is.EqualTo(origin));
            Assert.That(session.TurnScheduler.IsReady(player.Id), Is.True);

            CombatActionResult unreachable = service.Execute(
                session,
                new CombatCommand(CombatCommandKind.Move, player.Id, destination: new HexCoord(0, 1)));
            Assert.That(unreachable.Succeeded, Is.False);
            Assert.That(unreachable.RejectionReason, Is.EqualTo("declared_move_path_blocker"));
            Assert.That(player.Position, Is.EqualTo(origin));
            Assert.That(session.TurnScheduler.IsReady(player.Id), Is.True);
        }

        [Test]
        public void SpellSwapUsesTheLegacyLimitAndFixedCooldownWithoutCtbPenalty()
        {
            CombatantSnapshot player = CreateCombatant(
                "player",
                CombatTeam.Player,
                new HexCoord(0, 0),
                100,
                100,
                20,
                10,
                equippedArtProfileIds: new[] { "old_art" },
                availableArtProfileIds: new[] { "old_art", "new_art", "backup_art" });
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1, 100, 20, 10);
            CombatSession session = CreateSession(
                new[] { player, enemy },
                new FixedRangeQuery(true),
                CreateProfiles("old_art", "new_art", "backup_art"));
            var service = new CombatCommandService();

            Assert.That(service.AdvanceUntilAction(session).ActorId, Is.EqualTo(player.Id));
            CombatActionResult swapped = service.Execute(
                session,
                new CombatCommand(CombatCommandKind.SwapSpell, player.Id, profileId: "new_art", slotIndex: 0));

            Assert.That(swapped.Succeeded, Is.True, swapped.RejectionReason);
            Assert.That(player.EquippedArtProfileIds[0], Is.EqualTo("new_art"));
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(50000));
            Assert.That(player.CombatSwapsUsed, Is.EqualTo(1));
            Assert.That(player.GetCooldown("new_art"), Is.EqualTo(60));
            Assert.That(session.TurnScheduler.IsReady(player.Id), Is.True);
        }

        [Test]
        public void SpellSwapRejectionsLeaveLoadoutCooldownAndCtbStateUnchanged()
        {
            CombatantSnapshot player = CreateCombatant(
                "player",
                CombatTeam.Player,
                new HexCoord(0, 0),
                100,
                100,
                20,
                10,
                equippedArtProfileIds: new[] { "old_art" },
                availableArtProfileIds: new[] { "old_art", "missing_art" });
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1, 100, 20, 10);
            CombatSession session = CreateSession(new[] { player, enemy }, new FixedRangeQuery(true), CreateProfiles("old_art"));
            var service = new CombatCommandService();

            Assert.That(service.AdvanceUntilAction(session).ActorId, Is.EqualTo(player.Id));
            CombatActionResult same = service.Execute(
                session,
                new CombatCommand(CombatCommandKind.SwapSpell, player.Id, profileId: "old_art", slotIndex: 0));
            Assert.That(same.RejectionReason, Is.EqualTo("combat_swap_same_profile"));
            CombatActionResult missing = service.Execute(
                session,
                new CombatCommand(CombatCommandKind.SwapSpell, player.Id, profileId: "missing_art", slotIndex: 0));
            Assert.That(missing.RejectionReason, Is.EqualTo("combat_swap_profile_unresolved"));
            CombatActionResult invalidSlot = service.Execute(
                session,
                new CombatCommand(CombatCommandKind.SwapSpell, player.Id, profileId: "old_art", slotIndex: 1));
            Assert.That(invalidSlot.RejectionReason, Is.EqualTo("combat_swap_slot_invalid"));
            Assert.That(player.EquippedArtProfileIds[0], Is.EqualTo("old_art"));
            Assert.That(player.CombatSwapsUsed, Is.EqualTo(0));
            Assert.That(player.GetCooldown("old_art"), Is.EqualTo(0));
            Assert.That(session.TurnScheduler.IsReady(player.Id), Is.True);
        }

        [Test]
        public void SpellSwapRejectsEmptyCandidatesDuplicateEquipmentAndAnExhaustedLimit()
        {
            CombatantSnapshot noCandidate = CreateCombatant(
                "player", CombatTeam.Player, new HexCoord(0, 0), 100, 100, 20, 10,
                equippedArtProfileIds: new[] { "old_art" });
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1, 100, 20, 10);
            var service = new CombatCommandService();
            CombatSession noCandidateSession = CreateSession(new[] { noCandidate, enemy }, new FixedRangeQuery(true), CreateProfiles("old_art", "new_art"));
            Assert.That(service.AdvanceUntilAction(noCandidateSession).ActorId, Is.EqualTo(noCandidate.Id));
            Assert.That(
                service.Execute(noCandidateSession, new CombatCommand(CombatCommandKind.SwapSpell, noCandidate.Id, profileId: "new_art", slotIndex: 0)).RejectionReason,
                Is.EqualTo("combat_swap_candidate_unavailable"));

            CombatantSnapshot duplicate = CreateCombatant(
                "duplicate", CombatTeam.Player, new HexCoord(0, 0), 100, 100, 20, 10,
                equippedArtProfileIds: new[] { "old_art", "other_art" },
                availableArtProfileIds: new[] { "other_art" });
            CombatantSnapshot duplicateEnemy = CreateCombatant("duplicate_enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1, 100, 20, 10);
            CombatSession duplicateSession = CreateSession(
                new[] { duplicate, duplicateEnemy }, new FixedRangeQuery(true), CreateProfiles("old_art", "other_art"));
            Assert.That(service.AdvanceUntilAction(duplicateSession).ActorId, Is.EqualTo(duplicate.Id));
            Assert.That(
                service.Execute(duplicateSession, new CombatCommand(CombatCommandKind.SwapSpell, duplicate.Id, profileId: "other_art", slotIndex: 0)).RejectionReason,
                Is.EqualTo("combat_swap_profile_already_equipped"));

            CombatantSnapshot exhausted = CreateCombatant(
                "exhausted", CombatTeam.Player, new HexCoord(0, 0), 100, 100, 20, 10,
                equippedArtProfileIds: new[] { "old_art" },
                availableArtProfileIds: new[] { "new_art" },
                combatSwapsUsed: 2);
            CombatantSnapshot exhaustedEnemy = CreateCombatant("exhausted_enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1, 100, 20, 10);
            CombatSession exhaustedSession = CreateSession(
                new[] { exhausted, exhaustedEnemy }, new FixedRangeQuery(true), CreateProfiles("old_art", "new_art"));
            Assert.That(service.AdvanceUntilAction(exhaustedSession).ActorId, Is.EqualTo(exhausted.Id));
            Assert.That(
                service.Execute(exhaustedSession, new CombatCommand(CombatCommandKind.SwapSpell, exhausted.Id, profileId: "new_art", slotIndex: 0)).RejectionReason,
                Is.EqualTo("combat_swap_limit_reached"));
            Assert.That(exhausted.EquippedArtProfileIds[0], Is.EqualTo("old_art"));
            Assert.That(exhausted.GetCooldown("new_art"), Is.EqualTo(0));
            Assert.That(exhaustedSession.TurnScheduler.IsReady(exhausted.Id), Is.True);
        }

        [Test]
        public void PlayerAndAiReceiveTheSameEightLegalCommandKinds()
        {
            HexCoord playerOrigin = new HexCoord(0, 0);
            HexCoord enemyOrigin = new HexCoord(1, 0);
            CombatantSnapshot player = CreateCombatant(
                "player", CombatTeam.Player, playerOrigin, 101, 100, 20, 10, movePoints: 1,
                equippedArtProfileIds: new[] { "art" }, availableArtProfileIds: new[] { "art", "backup_art" },
                equippedDivineProfileIds: new[] { "divine" }, availableDivineProfileIds: new[] { "divine" });
            CombatantSnapshot enemy = CreateCombatant(
                "enemy", CombatTeam.Enemy, enemyOrigin, 100, 100, 20, 10, movePoints: 1,
                equippedArtProfileIds: new[] { "art" }, availableArtProfileIds: new[] { "art", "backup_art" },
                equippedDivineProfileIds: new[] { "divine" }, availableDivineProfileIds: new[] { "divine" });
            var query = new FixedRangeQuery(true, movementBySource: new Dictionary<HexCoord, IReadOnlyDictionary<HexCoord, CombatMovementQueryResult>>
            {
                [playerOrigin] = new Dictionary<HexCoord, CombatMovementQueryResult>
                {
                    [new HexCoord(-1, 0)] = new CombatMovementQueryResult(true,
                string.Empty,
                new[] { new HexCoord(-1, 0) },
                null,
                1000,
                (1) * 10000),
                },
                [enemyOrigin] = new Dictionary<HexCoord, CombatMovementQueryResult>
                {
                    [new HexCoord(2, 0)] = new CombatMovementQueryResult(true,
                string.Empty,
                new[] { new HexCoord(2, 0) },
                null,
                1000,
                (1) * 10000),
                },
            });
            CombatSession session = CreateSession(new[] { player, enemy }, query, CreateProfiles("art", "backup_art"));
            var commandService = new CombatCommandService();
            var legalActions = new CombatLegalActionService(commandService);

            Assert.That(commandService.AdvanceUntilAction(session).ActorId, Is.EqualTo(player.Id));
            ISet<CombatCommandKind> playerKinds = new HashSet<CombatCommandKind>(
                legalActions.GetLegalActions(session, player.Id).Select(command => command.Kind));
            Assert.That(session.TurnScheduler.IsReady(enemy.Id), Is.True);
            Assert.That(legalActions.GetLegalActions(session, enemy.Id), Is.Empty,
                "A queued ready actor does not own the current turn.");
            Assert.That(commandService.Execute(session, new CombatCommand(CombatCommandKind.Wait, player.Id)).Succeeded, Is.True);
            Assert.That(commandService.AdvanceUntilAction(session).ActorId, Is.EqualTo(enemy.Id));
            ISet<CombatCommandKind> enemyKinds = new HashSet<CombatCommandKind>(
                legalActions.GetLegalActions(session, enemy.Id).Select(command => command.Kind));
            CombatCommandKind[] expected =
            {
                CombatCommandKind.BasicAttack,
                CombatCommandKind.Art,
                CombatCommandKind.Divine,
                CombatCommandKind.Guard,
                CombatCommandKind.Wait,
                CombatCommandKind.Move,
                CombatCommandKind.SwapSpell,
                CombatCommandKind.EndTurn,
            };

            CollectionAssert.AreEquivalent(expected, playerKinds);
            CollectionAssert.AreEquivalent(expected, enemyKinds);
        }

        [Test]
        public void MovementQueryCarriesRawEdgesAndActionPointCostWithoutChangingKernelCosts()
        {
            HexCoord origin = new HexCoord(0, 0), first = new HexCoord(1, 0);
            CombatantSnapshot player = CreateCombatant("player", CombatTeam.Player, origin, 100, 100, 20, 10, movePoints: 3);
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(3, 0), 1, 100, 20, 10);
            var movement = new CombatMovementQueryResult(true,
                SpatialQueryReasons.Ok,
                new[] { first },
                new[] { new SpatialMovementEdgeCost(origin, first, metricDistanceUnits: 800, movementBurdenUnits: 200) },
                unitsPerRange: 1000,
                actionPointCostUnits: 33333);
            var query = new FixedRangeQuery(true, movementBySource: new Dictionary<HexCoord, IReadOnlyDictionary<HexCoord, CombatMovementQueryResult>>
            {
                [origin] = new Dictionary<HexCoord, CombatMovementQueryResult>
                {
                    [first] = movement,
                },
            });
            CombatSession session = CreateSession(new[] { player, enemy }, query);
            var service = new CombatCommandService();
            service.AdvanceUntilAction(session);

            CombatActionResult result = service.Execute(
                session,
                new CombatCommand(CombatCommandKind.Move, player.Id, destination: first));

            Assert.That(result.Succeeded, Is.True, result.RejectionReason);
            Assert.That(result.MovementCost, Is.EqualTo(33333));
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(66667));
            Assert.That(player.Position, Is.EqualTo(first));
            Assert.That(session.PendingCtPenalty, Is.Zero);
            Assert.That(movement.MovementEdges.Count, Is.EqualTo(1));
            Assert.That(movement.MovementEdges[0].DistanceUnits, Is.EqualTo(1000));
            Assert.That(movement.UnitsPerRange, Is.EqualTo(1000));
            Assert.That(movement.ActionPointCostUnits, Is.EqualTo(33333));
        }

        [Test]
        public void ContinuousMovesConsumeCanonicalCostsWithoutChargingCtOrResettingAnUnfinishedTurn()
        {
            HexCoord origin = new HexCoord(0, 0), first = new HexCoord(1, 0), second = new HexCoord(1, 1);
            CombatantSnapshot player = CreateCombatant("player", CombatTeam.Player, origin, 100, 100, 20, 10, movePoints: 3);
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(3, 0), 1, 100, 20, 10);
            var query = new FixedRangeQuery(true, movementBySource: new Dictionary<HexCoord, IReadOnlyDictionary<HexCoord, CombatMovementQueryResult>>
            {
                [origin] = new Dictionary<HexCoord, CombatMovementQueryResult>
                {
                    [first] = new CombatMovementQueryResult(true,
                "",
                new[] { first },
                null,
                1000,
                (7) * 10000),
                },
                [first] = new Dictionary<HexCoord, CombatMovementQueryResult>
                {
                    [second] = new CombatMovementQueryResult(true,
                "",
                new[] { second },
                null,
                1000,
                (3) * 10000),
                },
            });
            CombatSession session = CreateSession(new[] { player, enemy }, query);
            var service = new CombatCommandService();
            service.AdvanceUntilAction(session);
            int tick = session.TurnScheduler.CurrentTick;
            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.Move, player.Id, destination: first)).MovementCost, Is.EqualTo(70000));
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(30000), "Cost comes from Spatial, rather than the number of path entries.");
            CombatTurnAdvance repeatedAdvance = service.AdvanceUntilAction(session);
            Assert.That(repeatedAdvance.ActorId, Is.EqualTo(player.Id));
            Assert.That(repeatedAdvance.TicksElapsed, Is.Zero);
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(30000));
            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.Move, player.Id, destination: second)).Succeeded, Is.True);
            Assert.That(player.ActionPointsRemainingUnits, Is.Zero);
            Assert.That(player.Facing, Is.EqualTo(first.DirectionTo(second)));
            Assert.That(session.TurnScheduler.CurrentTick, Is.EqualTo(tick));
            Assert.That(session.PendingCtPenalty, Is.Zero);
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Finished));
            AssertRejectedWithoutTurnMutation(service, session, player,
                new CombatCommand(CombatCommandKind.Move, player.Id, destination: origin));
            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.BasicAttack, player.Id, enemy.Id, "basic")).Succeeded, Is.False,
                "Exhausting the shared action-point budget ends the turn.");
        }

        [Test]
        public void ActionThenMovementRetainsItsCooldownAndEndTurnSettlesExactlyOnce()
        {
            HexCoord origin = new HexCoord(0, 0), destination = new HexCoord(0, 1);
            CombatantSnapshot player = CreateCombatant("player", CombatTeam.Player, origin, 100, 100, 20, 10, movePoints: 2);
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1, 1000, 20, 10);
            CombatSession session = CreateSession(new[] { player, enemy },
                new FixedRangeQuery(true, movementBySource: CreateMovementMap(origin, destination, new[] { destination }, 1)),
                new[] { new CombatAttackProfile("basic", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1,
                    40000, 40000, 150, 10, physicalMultiplier: 1f) });
            var service = new CombatCommandService();
            service.AdvanceUntilAction(session);
            Assert.That(service.Validate(session, new CombatCommand(CombatCommandKind.EndTurn, player.Id)).Succeeded, Is.True);
            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.BasicAttack, player.Id, enemy.Id, "basic")).Succeeded, Is.True);
            int healthAfterAttack = enemy.CurrentHealth;
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            Assert.That(session.PendingCtPenalty, Is.EqualTo(150));
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(60000));
            AssertRejectedWithoutTurnMutation(service, session, player, new CombatCommand(CombatCommandKind.BasicAttack, player.Id, enemy.Id, "basic"));
            AssertRejectedWithoutTurnMutation(service, session, player, new CombatCommand(CombatCommandKind.Wait, player.Id));
            Assert.That(enemy.CurrentHealth, Is.EqualTo(healthAfterAttack));
            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.Move, player.Id, destination: destination)).Succeeded, Is.True);
            Assert.That(session.PendingCtPenalty, Is.EqualTo(150));
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(50000));
            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.EndTurn, player.Id)).Succeeded, Is.True);
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Finished));
            Assert.That(session.TurnScheduler.IsReady(player.Id), Is.False);
            AssertRejectedWithoutTurnMutation(service, session, player, new CombatCommand(CombatCommandKind.EndTurn, player.Id));
            CombatTurnAdvance next = service.AdvanceUntilAction(session);
            Assert.That(next.ActorId, Is.EqualTo(player.Id));
            Assert.That(next.TicksElapsed, Is.EqualTo(3), "The saved 150 CT penalty must survive a repeated EndTurn rejection.");
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(player.ActionPointMaximumUnits));
            Assert.That(player.GetCooldown("basic"), Is.EqualTo(7));
        }

        [Test]
        public void WaitBeforeAnyBenefitPreservesHalfCtAndMovementPreventsWaiting()
        {
            HexCoord origin = new HexCoord(0, 0), destination = new HexCoord(0, 1);
            CombatantSnapshot player = CreateCombatant("player", CombatTeam.Player, origin, 50, 100, 20, 10, movePoints: 2);
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1, 1000, 20, 10);
            CombatSession session = CreateSession(new[] { player, enemy },
                new FixedRangeQuery(true, movementBySource: CreateMovementMap(origin, destination, new[] { destination }, 1)));
            var service = new CombatCommandService();
            Assert.That(service.AdvanceUntilAction(session).TicksElapsed, Is.EqualTo(2));
            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.Wait, player.Id)).Succeeded, Is.True);
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Finished));
            AssertRejectedWithoutTurnMutation(service, session, player, new CombatCommand(CombatCommandKind.Wait, player.Id));
            Assert.That(service.AdvanceUntilAction(session).TicksElapsed, Is.EqualTo(1));
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(100000));
            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.Move, player.Id, destination: destination)).Succeeded, Is.True);
            AssertRejectedWithoutTurnMutation(service, session, player, new CombatCommand(CombatCommandKind.Wait, player.Id));
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(90000));
            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.Guard, player.Id)).Succeeded, Is.True);
            AssertRejectedWithoutTurnMutation(service, session, player, new CombatCommand(CombatCommandKind.Wait, player.Id));
        }

        [Test]
        public void RejectedMovementAndDeadOrNonCurrentActorsCannotSpendAnyTurnResources()
        {
            HexCoord origin = new HexCoord(0, 0);
            CombatantSnapshot player = CreateCombatant("player", CombatTeam.Player, origin, 101, 100, 20, 10, movePoints: 2);
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 100, 100, 20, 10, movePoints: 2);
            CombatantSnapshot ally = CreateCombatant("ally", CombatTeam.Player, new HexCoord(0, 2), 1, 100, 20, 10);
            CombatantSnapshot otherEnemy = CreateCombatant("other_enemy", CombatTeam.Enemy, new HexCoord(2, 2), 1, 100, 20, 10);
            var query = new FixedRangeQuery(true, movementBySource: new Dictionary<HexCoord, IReadOnlyDictionary<HexCoord, CombatMovementQueryResult>>
            {
                [origin] = new Dictionary<HexCoord, CombatMovementQueryResult>
                {
                    [new HexCoord(0, 1)] = new CombatMovementQueryResult(true,
                "",
                new[] { new HexCoord(0, 1) },
                null,
                1000,
                (11) * 10000),
                    [new HexCoord(2, 1)] = new CombatMovementQueryResult(false,
                "declared_obstacle",
                null,
                null,
                1000,
                (0) * 10000),
                    [new HexCoord(-1, 0)] = new CombatMovementQueryResult(false,
                "declared_board_boundary",
                null,
                null,
                1000,
                (0) * 10000),
                    [new HexCoord(1, -1)] = new CombatMovementQueryResult(false,
                "declared_blocked_edge",
                null,
                null,
                1000,
                (0) * 10000),
                },
                [enemy.Position] = new Dictionary<HexCoord, CombatMovementQueryResult>
                {
                    [new HexCoord(2, 0)] = new CombatMovementQueryResult(true,
                "",
                new[] { new HexCoord(2, 0) },
                null,
                1000,
                (1) * 10000),
                },
            });
            CombatSession session = CreateSession(new[] { player, ally, enemy, otherEnemy }, query);
            var service = new CombatCommandService();
            Assert.That(service.AdvanceUntilAction(session).ActorId, Is.EqualTo(player.Id));
            foreach (HexCoord destination in new[] { origin, enemy.Position, new HexCoord(0, 1), new HexCoord(2, 1), new HexCoord(-1, 0), new HexCoord(1, -1) })
                AssertRejectedWithoutTurnMutation(service, session, player, new CombatCommand(CombatCommandKind.Move, player.Id, destination: destination));
            AssertRejectedWithoutTurnMutation(service, session, player, new CombatCommand(CombatCommandKind.Move, player.Id));
            AssertRejectedWithoutTurnMutation(service, session, enemy, new CombatCommand(CombatCommandKind.Move, enemy.Id, destination: new HexCoord(2, 0)));
            player.ReceiveDamage(player.MaximumHealth);
            foreach (CombatCommandKind kind in new[] { CombatCommandKind.Move, CombatCommandKind.Guard, CombatCommandKind.Wait, CombatCommandKind.EndTurn })
                AssertRejectedWithoutTurnMutation(service, session, player, new CombatCommand(kind, player.Id, destination: new HexCoord(0, 1)));
            Assert.That(service.AdvanceUntilAction(session).ActorId, Is.EqualTo(enemy.Id));
            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.Move, enemy.Id, destination: new HexCoord(2, 0))).Succeeded, Is.True);
            Assert.That(enemy.ActionPointsRemainingUnits, Is.EqualTo(90000));
            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.Guard, enemy.Id)).Succeeded, Is.True);
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.EndTurn, enemy.Id)).Succeeded, Is.True);
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Finished));
        }

        private static void AssertRejectedWithoutTurnMutation(CombatCommandService service, CombatSession session,
            CombatantSnapshot actor, CombatCommand command)
        {
            HexCoord position = actor.Position;
            int facing = actor.Facing, movement = actor.ActionPointsRemainingUnits, tick = session.TurnScheduler.CurrentTick;
            int penalty = session.PendingCtPenalty;
            string current = session.CurrentActorId;
            CombatTurnPhase phase = session.TurnPhase;
            bool ready = session.TurnScheduler.IsReady(actor.Id);
            CombatActionResult result = service.Execute(session, command);
            Assert.That(result.Succeeded, Is.False, command.Kind + " unexpectedly succeeded.");
            Assert.That(actor.Position, Is.EqualTo(position));
            Assert.That(actor.Facing, Is.EqualTo(facing));
            Assert.That(actor.ActionPointsRemainingUnits, Is.EqualTo(movement));
            Assert.That(session.TurnScheduler.CurrentTick, Is.EqualTo(tick));
            Assert.That(session.TurnScheduler.IsReady(actor.Id), Is.EqualTo(ready));
            Assert.That(session.CurrentActorId, Is.EqualTo(current));
            Assert.That(session.TurnPhase, Is.EqualTo(phase));
            Assert.That(session.PendingCtPenalty, Is.EqualTo(penalty));
        }

        [Test]
        public void DirectExecuteRejectsUnauthorizedProfilesWithoutSideEffects()
        {
            CombatantSnapshot player = CreateCombatant(
                "player", CombatTeam.Player, new HexCoord(0, 0), 100, 100, 20, 10,
                basicAttackProfileId: "basic",
                equippedArtProfileIds: new[] { "both_art", "equipped_only_art" },
                availableArtProfileIds: new[] { "both_art", "known_only_art" },
                equippedDivineProfileIds: new[] { "both_divine", "equipped_only_divine" },
                availableDivineProfileIds: new[] { "both_divine", "known_only_divine" });
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1, 100, 20, 10);
            CombatSession session = CreateSession(
                new[] { player, enemy },
                new FixedRangeQuery(true),
                new[]
                {
                    new CombatAttackProfile("basic", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, 1, 1, 0, 0, physicalMultiplier: 1f),
                    new CombatAttackProfile("foreign", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, 1, 1, 0, 0, physicalMultiplier: 1f),
                    new CombatAttackProfile("both_art", CombatAttackKind.Art, CombatAttackEffect.Soul, 1, 1, 1, 1, 0, 0, soulMultiplier: 1f),
                    new CombatAttackProfile("known_only_art", CombatAttackKind.Art, CombatAttackEffect.Soul, 1, 1, 1, 1, 0, 0, soulMultiplier: 1f),
                    new CombatAttackProfile("equipped_only_art", CombatAttackKind.Art, CombatAttackEffect.Soul, 1, 1, 1, 1, 0, 0, soulMultiplier: 1f),
                    new CombatAttackProfile("both_divine", CombatAttackKind.Divine, CombatAttackEffect.Soul, 1, 1, 1, 1, 0, 0, soulMultiplier: 1f),
                    new CombatAttackProfile("known_only_divine", CombatAttackKind.Divine, CombatAttackEffect.Soul, 1, 1, 1, 1, 0, 0, soulMultiplier: 1f),
                    new CombatAttackProfile("equipped_only_divine", CombatAttackKind.Divine, CombatAttackEffect.Soul, 1, 1, 1, 1, 0, 0, soulMultiplier: 1f),
                });
            var service = new CombatCommandService();

            Assert.That(service.AdvanceUntilAction(session).ActorId, Is.EqualTo("player"));
            int health = enemy.CurrentHealth;
            int spirit = player.CurrentSpirit;
            int swapsUsed = player.CombatSwapsUsed;
            string[] equippedArt = player.EquippedArtProfileIds.ToArray();

            (CombatCommandKind, string, string)[] rejected =
            {
                (CombatCommandKind.BasicAttack, "foreign", "attack_profile_not_authorized"),
                (CombatCommandKind.Art, "known_only_art", "attack_profile_not_equipped"),
                (CombatCommandKind.Art, "equipped_only_art", "attack_profile_not_learned"),
                (CombatCommandKind.Divine, "known_only_divine", "attack_profile_not_equipped"),
                (CombatCommandKind.Divine, "equipped_only_divine", "attack_profile_not_learned"),
            };
            foreach ((CombatCommandKind kind, string profileId, string reason) in rejected)
            {
                CombatActionResult result = service.Execute(session, new CombatCommand(
                    kind, "player", "enemy", profileId, new CombatResolutionRolls(0f, 0f, 0f, 0f)));
                Assert.That(result.Succeeded, Is.False, profileId);
                Assert.That(result.RejectionReason, Is.EqualTo(reason), profileId);
            }

            Assert.That(enemy.CurrentHealth, Is.EqualTo(health));
            Assert.That(player.CurrentSpirit, Is.EqualTo(spirit));
            Assert.That(player.CombatSwapsUsed, Is.EqualTo(swapsUsed));
            Assert.That(player.GetCooldown("foreign"), Is.EqualTo(0));
            Assert.That(player.GetCooldown("known_only_art"), Is.EqualTo(0));
            CollectionAssert.AreEqual(equippedArt, player.EquippedArtProfileIds);
            Assert.That(session.TurnScheduler.IsReady("player"), Is.True);
        }

        [Test]
        public void AiEnumerationUsesTheSameAuthorizationAsDirectExecution()
        {
            CombatantSnapshot player = CreateCombatant(
                "player", CombatTeam.Player, new HexCoord(0, 0), 100, 100, 20, 10,
                basicAttackProfileId: "basic",
                equippedArtProfileIds: new[] { "both_art", "equipped_only_art" },
                availableArtProfileIds: new[] { "both_art", "known_only_art" },
                equippedDivineProfileIds: new[] { "both_divine" },
                availableDivineProfileIds: new[] { "both_divine", "known_only_divine" });
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1, 100, 20, 10);
            CombatSession session = CreateSession(
                new[] { player, enemy },
                new FixedRangeQuery(true),
                new[]
                {
                    new CombatAttackProfile("basic", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, 1, 1, 0, 0, physicalMultiplier: 1f),
                    new CombatAttackProfile("foreign", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, 1, 1, 0, 0, physicalMultiplier: 1f),
                    new CombatAttackProfile("both_art", CombatAttackKind.Art, CombatAttackEffect.Soul, 1, 1, 1, 1, 0, 0, soulMultiplier: 1f),
                    new CombatAttackProfile("known_only_art", CombatAttackKind.Art, CombatAttackEffect.Soul, 1, 1, 1, 1, 0, 0, soulMultiplier: 1f),
                    new CombatAttackProfile("equipped_only_art", CombatAttackKind.Art, CombatAttackEffect.Soul, 1, 1, 1, 1, 0, 0, soulMultiplier: 1f),
                    new CombatAttackProfile("both_divine", CombatAttackKind.Divine, CombatAttackEffect.Soul, 1, 1, 1, 1, 0, 0, soulMultiplier: 1f),
                    new CombatAttackProfile("known_only_divine", CombatAttackKind.Divine, CombatAttackEffect.Soul, 1, 1, 1, 1, 0, 0, soulMultiplier: 1f),
                });
            var service = new CombatCommandService();
            var legalActions = new CombatLegalActionService(service);

            Assert.That(service.AdvanceUntilAction(session).HasActor, Is.True);
            IReadOnlyList<CombatCommand> actions = legalActions.GetLegalActions(session, "player");

            Assert.That(actions.Any(command =>
                command.Kind == CombatCommandKind.BasicAttack && command.ProfileId == "foreign"), Is.False);
            Assert.That(actions.Any(command =>
                command.Kind == CombatCommandKind.Art && command.ProfileId == "known_only_art"), Is.False);
            Assert.That(actions.Any(command =>
                command.Kind == CombatCommandKind.Art && command.ProfileId == "equipped_only_art"), Is.False);
            Assert.That(actions.Any(command =>
                command.Kind == CombatCommandKind.Divine && command.ProfileId == "known_only_divine"), Is.False);
            Assert.That(actions.Any(command =>
                command.Kind == CombatCommandKind.BasicAttack && command.ProfileId == "basic"), Is.True);
            Assert.That(actions.Any(command =>
                command.Kind == CombatCommandKind.Art && command.ProfileId == "both_art"), Is.True);
            Assert.That(actions.Any(command =>
                command.Kind == CombatCommandKind.Divine && command.ProfileId == "both_divine"), Is.True);
        }

        [Test]
        public void SwapEquippedArtUpdatesAuthorizationToTheNewSlot()
        {
            CombatantSnapshot player = CreateCombatant(
                "player", CombatTeam.Player, new HexCoord(0, 0), 100, 100, 20, 10,
                equippedArtProfileIds: new[] { "old_art" },
                availableArtProfileIds: new[] { "old_art", "new_art" });
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1, 100, 20, 10);
            CombatSession session = CreateSession(
                new[] { player, enemy }, new FixedRangeQuery(true), CreateProfiles("old_art", "new_art"));
            var service = new CombatCommandService();

            Assert.That(service.AdvanceUntilAction(session).ActorId, Is.EqualTo("player"));
            Assert.That(
                service.Validate(session, new CombatCommand(CombatCommandKind.Art, "player", "enemy", "new_art")).RejectionReason,
                Is.EqualTo("attack_profile_not_equipped"));

            player.SwapEquippedArt(0, "new_art");

            Assert.That(service.Validate(session, new CombatCommand(CombatCommandKind.Art, "player", "enemy", "new_art")).Succeeded, Is.True);
            Assert.That(
                service.Validate(session, new CombatCommand(CombatCommandKind.Art, "player", "enemy", "old_art")).RejectionReason,
                Is.EqualTo("attack_profile_not_equipped"));
        }

        [Test]
        public void ProfileIdListsKeepDistinctKnownIdsInOrder()
        {
            CombatantSnapshot combatant = CreateCombatant(
                "ordered", CombatTeam.Player, new HexCoord(0, 0), 10, 100, 20, 10,
                equippedArtProfileIds: new[] { "art_a", "art_b" },
                availableArtProfileIds: new[] { "art_a", "art_b", "art_c" },
                equippedDivineProfileIds: new[] { "divine_a", "divine_b" },
                availableDivineProfileIds: new[] { "divine_a", "divine_b", "divine_c" });

            CollectionAssert.AreEqual(new[] { "art_a", "art_b" }, combatant.EquippedArtProfileIds);
            CollectionAssert.AreEqual(new[] { "art_a", "art_b", "art_c" }, combatant.AvailableArtProfileIds);
            CollectionAssert.AreEqual(new[] { "divine_a", "divine_b" }, combatant.EquippedDivineProfileIds);
            CollectionAssert.AreEqual(new[] { "divine_a", "divine_b", "divine_c" }, combatant.AvailableDivineProfileIds);
        }

        [Test]
        public void ProfileIdListsRemoveBlankAndDuplicateEntriesForArtAndDivineSets()
        {
            CombatantSnapshot combatant = CreateCombatant(
                "sanitized", CombatTeam.Player, new HexCoord(0, 0), 10, 100, 20, 10,
                equippedArtProfileIds: new[] { "art_a", "", "art_a", "  ", "art_b", null },
                availableArtProfileIds: new[] { "art_a", "art_b", "art_b", "art_c", "" },
                equippedDivineProfileIds: new[] { "divine_a", "divine_a", "", "divine_b", null },
                availableDivineProfileIds: new[] { "divine_a", "  ", "divine_b", "divine_a" });

            CollectionAssert.AreEqual(new[] { "art_a", "art_b" }, combatant.EquippedArtProfileIds);
            CollectionAssert.AreEqual(new[] { "art_a", "art_b", "art_c" }, combatant.AvailableArtProfileIds);
            CollectionAssert.AreEqual(new[] { "divine_a", "divine_b" }, combatant.EquippedDivineProfileIds);
            CollectionAssert.AreEqual(new[] { "divine_a", "divine_b" }, combatant.AvailableDivineProfileIds);
        }

        [TestCase(1f, 3, 5, 3, 0.15f, 3, 1f)]
        [TestCase(1.5f, 3, 5, 3, 0.15f, 3, 1.10f)]
        [TestCase(3f, 4, 3, 3, 0.15f, 5, 1.15f)]
        [TestCase(6f, 5, 5, 5, 0.18f, 8, 1.20f)]
        [TestCase(12f, 5, 5, 5, 0.22f, 12, 1.25f)]
        [TestCase(24f, 5, 5, 5, 0.30f, 12, 1.30f)]
        public void ProjectedRealmRulesMatchEstablishedStackAndDefenseValues(
            float realmMultiplier,
            int maximumShouyi,
            int maximumFudan,
            int maximumLeijie,
            float leijieBonus,
            int mindStrengthBonus,
            float hanhongPhysicalDefenseMultiplier)
        {
            CombatantSnapshot combatant = CreateCombatant(
                "rules", CombatTeam.Player, new HexCoord(0, 0), 10, 100, 20, 10,
                realmMultiplier: realmMultiplier);

            Assert.That(combatant.MaximumShouyiStacks, Is.EqualTo(maximumShouyi));
            Assert.That(combatant.MaximumFudanStacks, Is.EqualTo(maximumFudan));
            Assert.That(combatant.MaximumLeijieStacks, Is.EqualTo(maximumLeijie));
            Assert.That(combatant.LeijieDamageBonusPerStack, Is.EqualTo(leijieBonus).Within(0.0001f));

            combatant.GongFaId = "南华玄感录";
            Assert.That(combatant.MindStrengthBonus, Is.EqualTo(mindStrengthBonus));
            combatant.GongFaId = "含弘光大典";
            Assert.That(combatant.DefenseMultiplier(physical: true),
                Is.EqualTo(hanhongPhysicalDefenseMultiplier).Within(0.0001f));
            Assert.That(combatant.DefenseMultiplier(physical: false), Is.EqualTo(1f));
        }

        [Test]
        public void XuanganMindStrengthChangesOnlyTheSoulDamageLine()
        {
            CombatantSnapshot normal = CreateCombatant(
                "normal", CombatTeam.Player, new HexCoord(0, 0), 10, 100, 300, 50, realmMultiplier: 6f);
            CombatantSnapshot xuangan = CreateCombatant(
                "xuangan", CombatTeam.Player, new HexCoord(0, 0), 10, 100, 300, 50, realmMultiplier: 6f);
            xuangan.GongFaId = "南华玄感录";
            var soul = new CombatAttackProfile(
                "soul", CombatAttackKind.Basic, CombatAttackEffect.Soul, 1, 1, 1, 1, 0, 0, soulMultiplier: 1f);
            var physical = new CombatAttackProfile(
                "physical", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, 1, 1, 0, 0, physicalMultiplier: 1f);

            int normalSoul = ResolveAttack(normal, CreateNeutralTarget("normal_soul"), soul).FinalDamage;
            int xuanganSoul = ResolveAttack(xuangan, CreateNeutralTarget("xuangan_soul"), soul).FinalDamage;
            int normalPhysical = ResolveAttack(normal, CreateNeutralTarget("normal_physical"), physical).FinalDamage;
            int xuanganPhysical = ResolveAttack(xuangan, CreateNeutralTarget("xuangan_physical"), physical).FinalDamage;

            Assert.That(normalSoul, Is.EqualTo(225));
            Assert.That(xuanganSoul, Is.EqualTo(233));
            Assert.That(normalPhysical, Is.EqualTo(225));
            Assert.That(xuanganPhysical, Is.EqualTo(normalPhysical));
        }

        [Test]
        public void HanhongAndZaiwuRecomputeBothDefenseLinesFromCurrentHealth()
        {
            var physical = new CombatAttackProfile(
                "physical", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, 1, 1, 0, 0, physicalMultiplier: 1f);
            var soul = new CombatAttackProfile(
                "soul", CombatAttackKind.Basic, CombatAttackEffect.Soul, 1, 1, 1, 1, 0, 0, soulMultiplier: 1f);
            CombatantSnapshot physicalActor = CreateCombatant(
                "physical_actor", CombatTeam.Player, new HexCoord(0, 0), 10, 100, 200, 50, realmMultiplier: 6f);
            CombatantSnapshot soulActor = CreateCombatant(
                "soul_actor", CombatTeam.Player, new HexCoord(0, 0), 10, 100, 200, 50, realmMultiplier: 6f);
            CombatantSnapshot fullPhysicalTarget = CreateHanhongTarget("full_physical", 100);
            CombatantSnapshot lowPhysicalTarget = CreateHanhongTarget("low_physical", 50);
            CombatantSnapshot fullSoulTarget = CreateHanhongTarget("full_soul", 100);
            CombatantSnapshot lowSoulTarget = CreateHanhongTarget("low_soul", 50);

            Assert.That(fullPhysicalTarget.DefenseMultiplier(physical: true), Is.EqualTo(1.20f).Within(0.0001f));
            Assert.That(lowPhysicalTarget.DefenseMultiplier(physical: true), Is.EqualTo(1.32f).Within(0.0001f));
            Assert.That(fullSoulTarget.DefenseMultiplier(physical: false), Is.EqualTo(1f));
            Assert.That(lowSoulTarget.DefenseMultiplier(physical: false), Is.EqualTo(1.10f).Within(0.0001f));
            Assert.That(ResolveAttack(physicalActor, fullPhysicalTarget, physical).FinalDamage, Is.EqualTo(125));
            Assert.That(ResolveAttack(physicalActor, lowPhysicalTarget, physical).FinalDamage, Is.EqualTo(120));
            Assert.That(ResolveAttack(soulActor, fullSoulTarget, soul).FinalDamage, Is.EqualTo(133));
            Assert.That(ResolveAttack(soulActor, lowSoulTarget, soul).FinalDamage, Is.EqualTo(129));
        }

        [Test]
        public void LeijieChargesToTheProjectedCapUsesTheRealmRateAndThenConsumes()
        {
            var physical = new CombatAttackProfile(
                "physical", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, 1, 1, 0, 0, physicalMultiplier: 1f);
            CombatantSnapshot baseActor = CreateCombatant(
                "base", CombatTeam.Player, new HexCoord(0, 0), 10, 100, 200, 50, realmMultiplier: 6f);
            baseActor.GongFaId = "九霄雷劫录";
            CombatantSnapshot chargedActor = CreateCombatant(
                "charged", CombatTeam.Player, new HexCoord(0, 0), 10, 100, 200, 50, realmMultiplier: 6f);
            chargedActor.GongFaId = "九霄雷劫录";
            for (int i = 0; i < 6; i++)
                chargedActor.ReceiveDamage(1);

            Assert.That(chargedActor.LeijieStacks, Is.EqualTo(5));
            Assert.That(chargedActor.LeijieDamageBonusPerStack, Is.EqualTo(0.18f).Within(0.0001f));
            Assert.That(ResolveAttack(baseActor, CreateNeutralTarget("base_target"), physical).FinalDamage, Is.EqualTo(133));
            Assert.That(ResolveAttack(chargedActor, CreateNeutralTarget("charged_target"), physical).FinalDamage, Is.EqualTo(253));
            Assert.That(chargedActor.LeijieStacks, Is.Zero);
        }

        [Test]
        public void FullShouyiAndLeijieChecksUseTheProjectedFiveStackCap()
        {
            var soul = new CombatAttackProfile(
                "soul", CombatAttackKind.Basic, CombatAttackEffect.Soul, 1, 1, 1, 1, 0, 0, soulMultiplier: 1f);
            CombatantSnapshot actor = CreateCombatant(
                "actor", CombatTeam.Player, new HexCoord(0, 0), 10, 100, 200, 50, realmMultiplier: 6f);
            CombatantSnapshot partialShouyi = CreateNeutralTarget("partial_shouyi", realmMultiplier: 6f);
            partialShouyi.GongFaId = "抱元守一经";
            partialShouyi.ShouyiStacks = 2;
            CombatantSnapshot fullShouyi = CreateNeutralTarget("full_shouyi", realmMultiplier: 6f);
            fullShouyi.GongFaId = "抱元守一经";
            fullShouyi.ShouyiStacks = 5;
            CombatantSnapshot partialLeijie = CreateNeutralTarget("partial_leijie", realmMultiplier: 6f);
            partialLeijie.GongFaId = "九霄雷劫录";
            partialLeijie.LeijieStacks = 2;
            CombatantSnapshot fullLeijie = CreateNeutralTarget("full_leijie", realmMultiplier: 6f);
            fullLeijie.GongFaId = "九霄雷劫录";
            fullLeijie.LeijieStacks = 5;

            Assert.That(ResolveAttack(actor, partialShouyi, soul).FinalDamage, Is.EqualTo(107));
            Assert.That(ResolveAttack(actor, fullShouyi, soul).FinalDamage, Is.EqualTo(91));
            Assert.That(ResolveAttack(actor, partialLeijie, soul).FinalDamage, Is.EqualTo(133));
            Assert.That(ResolveAttack(actor, fullLeijie, soul).FinalDamage, Is.EqualTo(143));
        }

        [Test]
        public void FullFudanUsesItsProjectedCapForPenetrationAndPostActionState()
        {
            var soul = new CombatAttackProfile(
                "soul", CombatAttackKind.Art, CombatAttackEffect.Soul, 1, 1, 1, 1, 0, 0, soulMultiplier: 1f, isTalisman: true);
            CombatantSnapshot partial = CreateCombatant(
                "partial", CombatTeam.Player, new HexCoord(0, 0), 10, 100, 200, 50, realmMultiplier: 6f, equippedArtProfileIds: new[] { "soul" }, availableArtProfileIds: new[] { "soul" });
            partial.GongFaId = "云篆度人经";
            partial.FudanStacks = 4;
            CombatantSnapshot full = CreateCombatant(
                "full", CombatTeam.Player, new HexCoord(0, 0), 10, 100, 200, 50, realmMultiplier: 6f, equippedArtProfileIds: new[] { "soul" }, availableArtProfileIds: new[] { "soul" });
            full.GongFaId = "云篆度人经";
            full.FudanStacks = 5;

            Assert.That(ResolveAttack(partial, CreateNeutralTarget("partial_target", defense: 200), soul).FinalDamage,
                Is.EqualTo(160));
            Assert.That(ResolveAttack(full, CreateNeutralTarget("full_target", defense: 200), soul).FinalDamage,
                Is.EqualTo(206));
            Assert.That(partial.FudanStacks, Is.EqualTo(1));
            Assert.That(full.FudanStacks, Is.EqualTo(1));
        }

        [Test]
        public void ActionStateAdvancesToProjectedShouyiAndFudanCaps()
        {
            var physical = new CombatAttackProfile(
                "physical", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, 1, 1, 0, 0, physicalMultiplier: 1f);
            CombatantSnapshot shouyi = CreateCombatant(
                "shouyi", CombatTeam.Player, new HexCoord(0, 0), 10, 100, 20, 10, realmMultiplier: 3f);
            shouyi.GongFaId = "抱元守一经";
            CombatantSnapshot fudan = CreateCombatant(
                "fudan", CombatTeam.Player, new HexCoord(0, 0), 10, 100, 20, 10, realmMultiplier: 3f);
            fudan.GongFaId = "云篆度人经";

            for (int i = 0; i < 6; i++)
            {
                ResolveAttack(shouyi, CreateNeutralTarget("shouyi_target_" + i), physical);
                ResolveAttack(fudan, CreateNeutralTarget("fudan_target_" + i), physical);
            }

            Assert.That(shouyi.ShouyiStacks, Is.EqualTo(4));
            Assert.That(fudan.FudanStacks, Is.EqualTo(3));
        }

        [Test]
        public void TwoVsTwoAndResultProjectionRemainSessionLocal()
        {
            CombatantSnapshot playerOne = CreateCombatant("player_one", CombatTeam.Player, new HexCoord(0, 0), 10, 100, 20, 10);
            CombatantSnapshot playerTwo = CreateCombatant("player_two", CombatTeam.Player, new HexCoord(0, 1), 9, 100, 20, 10);
            CombatantSnapshot enemyOne = CreateCombatant("enemy_one", CombatTeam.Enemy, new HexCoord(1, 0), 8, 20, 20, 10);
            CombatantSnapshot enemyTwo = CreateCombatant("enemy_two", CombatTeam.Enemy, new HexCoord(1, 1), 7, 20, 20, 10);
            CombatSession session = CreateSession(new[] { playerOne, playerTwo, enemyOne, enemyTwo }, new FixedRangeQuery(true));

            Assert.That(session.Combatants.All, Has.Count.EqualTo(4));
            enemyOne.ReceiveDamage(20);
            enemyTwo.ReceiveDamage(20);

            CombatSessionResult result = new CombatResultBuilder().Build(session);
            Assert.That(result.Outcome, Is.EqualTo(CombatSessionOutcome.Victory));
            CollectionAssert.AreEquivalent(new[] { "enemy_one", "enemy_two" }, result.DefeatedCombatantIds);
        }

        [Test]
        public void SessionRejectsNonMatchingSideCardinality()
        {
            CombatantSnapshot player = CreateCombatant("player", CombatTeam.Player, new HexCoord(0, 0), 10, 100, 20, 10);
            CombatantSnapshot enemyOne = CreateCombatant("enemy_one", CombatTeam.Enemy, new HexCoord(1, 0), 10, 100, 20, 10);
            CombatantSnapshot enemyTwo = CreateCombatant("enemy_two", CombatTeam.Enemy, new HexCoord(2, 0), 10, 100, 20, 10);

            Assert.Throws<ArgumentException>(() => CreateSession(new[] { player, enemyOne, enemyTwo }, new FixedRangeQuery(true)));
        }

        [Test]
        public void KernelSourcesDoNotReferenceProductionOwnersOrUnityPresentation()
        {
            string combatRoot = Path.Combine(UnityEngine.Application.dataPath, "Scripts", "Combat");
            string[] sourceFiles =
            {
                "CombatActionResolver.cs", "CombatActionResult.cs", "CombatAttackProfile.cs", "CombatantRegistry.cs",
                "CombatantSnapshot.cs", "CombatCommand.cs", "CombatCommandService.cs", "CombatResultBuilder.cs",
                "CombatSession.cs", "CombatTurnScheduler.cs", "CombatLegalActionService.cs", "Turns/CTBEngine.cs"
            };
            string[] forbidden = { "TianZhang.Entity", "MonoBehaviour", "UnityEngine.UI", "Renderer", "Debug.", "SceneManager" };
            foreach (string relativePath in sourceFiles)
            {
                string content = File.ReadAllText(Path.Combine(combatRoot, relativePath));
                foreach (string token in forbidden)
                    Assert.That(content, Does.Not.Contain(token), relativePath + " must remain pure.");
            }
        }

        [Test]
        public void CombatParityCasesMatchBattleSimForTheEightNeutralPhysicalCases()
        {
            string jsonPath = Path.GetFullPath(Path.Combine(
                UnityEngine.Application.dataPath, "..", "..", "simulations", "BattleSim", "combat-parity-cases.json"));
            CombatParityDocument document = UnityEngine.JsonUtility.FromJson<CombatParityDocument>(File.ReadAllText(jsonPath));
            Assert.That(document.version, Is.EqualTo("combat-parity-v1"));
            Assert.That(document.defaults, Is.Not.Null);
            Assert.That(document.cases, Is.Not.Empty);

            foreach (CombatParityCase parityCase in document.cases)
            {
                CombatParityDefaults defaults = document.defaults;
                float attack = MergeParity(parityCase.attack, defaults.attack);
                float defense = MergeParity(parityCase.defense, defaults.defense);
                float multiplier = MergeParity(parityCase.multiplier, defaults.multiplier);
                float realmMultiplier = MergeParity(parityCase.realmMultiplier, defaults.realmMultiplier);
                float hitRateBonus = MergeParity(parityCase.hitRateBonus, defaults.hitRateBonus);
                float dodgeRate = MergeParity(parityCase.dodgeRate, defaults.dodgeRate);
                float critRate = MergeParity(parityCase.critRate, defaults.critRate);
                float critDamage = MergeParity(parityCase.critDamage, defaults.critDamage);
                float blockRate = MergeParity(parityCase.blockRate, defaults.blockRate);
                float blockReduction = MergeParity(parityCase.blockReduction, defaults.blockReduction);
                float hitPercent = MergeParity(parityCase.hitPercent, defaults.hitPercent);
                float criticalPercent = MergeParity(parityCase.criticalPercent, defaults.criticalPercent);
                float blockPercent = MergeParity(parityCase.blockPercent, defaults.blockPercent);

                CombatDamageResult damage = ResolveParityAttack(
                    attack, defense, multiplier, realmMultiplier,
                    hitRateBonus, dodgeRate, critRate, critDamage, blockRate, blockReduction,
                    hitPercent, criticalPercent, blockPercent);

                Assert.That(damage.IsHit, Is.EqualTo(parityCase.expected.hit), parityCase.caseId + " hit");
                Assert.That(damage.IsCritical, Is.EqualTo(parityCase.expected.critical), parityCase.caseId + " critical");
                Assert.That(damage.IsBlocked, Is.EqualTo(parityCase.expected.blocked), parityCase.caseId + " blocked");
                Assert.That(damage.FinalDamage, Is.EqualTo(parityCase.expected.damage), parityCase.caseId + " damage");
                Assert.That(
                    CombatActionResolver.EffectiveHitRate(hitRateBonus, dodgeRate),
                    Is.EqualTo(parityCase.expected.hitRate).Within(1e-4f),
                    parityCase.caseId + " hit rate");
                Assert.That(
                    CombatActionResolver.CriticalMultiplier(critDamage, 0f),
                    Is.EqualTo(parityCase.expected.critMultiplier).Within(1e-4f),
                    parityCase.caseId + " crit multiplier");
                Assert.That(
                    CombatActionResolver.BlockReductionMultiplier(blockReduction),
                    Is.EqualTo(parityCase.expected.blockMultiplier).Within(1e-4f),
                    parityCase.caseId + " block multiplier");
            }
        }

        [TestCase("ordinary", 1f, true, 5)]
        [TestCase("spirit_vein_node", 2f, true, 11)]
        [TestCase("blessed_land", 2f, true, 11)]
        [TestCase("ordinary", 1f, false, 2)]
        [TestCase("spirit_vein_node", 2f, false, 5)]
        [TestCase("blessed_land", 2f, false, 5)]
        public void RestoreSpiritUsesRawSpiritAndFloorsOnlyAfterEnvironmentAndTargetFactors(
            string environment, float factor, bool self, int expectedRecovery)
        {
            CombatantSnapshot player = CreateCombatant("player", CombatTeam.Player, new HexCoord(0, 0),
                100, 100, 999, 10, movePoints: 1, rawSpirit: 11,
                equippedArtProfileIds: new[] { "restore" }, availableArtProfileIds: new[] { "restore" });
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1, 100, 20, 10);
            player.SetSpirit(100, 40);
            enemy.SetSpirit(100, 10);
            var session = new CombatSession(new[] { player, enemy },
                new[] { CreateSpiritRecoveryProfile() },
                new FixedRangeQuery(true, enforceDeclaredRange: true),
                new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0), new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) },
                factor, CombatActionPointKernelTests.Rules());
            var service = new CombatCommandService();
            service.AdvanceUntilAction(session);
            int tick = session.TurnScheduler.CurrentTick;
            CombatantSnapshot target = self ? player : enemy;
            int targetBefore = target.CurrentSpirit;

            CombatActionResult result = service.Execute(session, new CombatCommand(
                CombatCommandKind.Art, player.Id, target.Id, "restore",
                new CombatResolutionRolls(100f, 0f, 0f, 0f)));

            Assert.That(result.Succeeded, Is.True, environment + ": " + result.RejectionReason);
            Assert.That(result.Damage, Is.Empty);
            Assert.That(result.SpiritSpent, Is.EqualTo(self ? 0 : 20));
            Assert.That(result.SpiritRecoveries, Has.Count.EqualTo(1));
            Assert.That(result.SpiritRecoveries[0].TargetId, Is.EqualTo(target.Id));
            Assert.That(result.SpiritRecoveries[0].RequestedRecovery, Is.EqualTo(expectedRecovery));
            Assert.That(result.SpiritRecoveries[0].ActualRecovery, Is.EqualTo(expectedRecovery));
            Assert.That(target.CurrentSpirit, Is.EqualTo(targetBefore + expectedRecovery));
            Assert.That(player.CurrentSpirit, Is.EqualTo(self ? 40 + expectedRecovery : 20));
            Assert.That(player.CurrentHealth, Is.EqualTo(100));
            Assert.That(enemy.CurrentHealth, Is.EqualTo(100));
            Assert.That(session.TurnScheduler.CurrentTick, Is.EqualTo(tick));
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            Assert.That(session.PendingCtPenalty, Is.EqualTo(5));
            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.EndTurn, player.Id)).Succeeded, Is.True);
            Assert.That(session.TurnScheduler.IsReady(player.Id), Is.False);
            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.EndTurn, player.Id)).Succeeded, Is.False);
        }

        [TestCase(true, 0, 10, 5, 15)]
        [TestCase(true, 0, 0, 5, 5)]
        [TestCase(true, 0, 100, 0, 100)]
        [TestCase(false, 20, 98, 2, 100)]
        [TestCase(false, 20, 100, 0, 100)]
        [TestCase(true, 2, 100, 2, 100)]
        public void RestoreSpiritPaysBeforeRecoveryCapsAtMaximumAndFullTargetsStillConsumeAction(
            bool self, int selfCost, int targetSpirit, int actualRecovery, int finalSpirit)
        {
            CombatantSnapshot player = CreateCombatant("player", CombatTeam.Player, new HexCoord(0, 0),
                100, 100, 500, 10, rawSpirit: 0,
                equippedArtProfileIds: new[] { "restore" }, availableArtProfileIds: new[] { "restore" });
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1, 100, 20, 10);
            player.SetSpirit(100, self ? targetSpirit : 20);
            enemy.SetSpirit(100, targetSpirit);
            CombatSession session = CreateSession(new[] { player, enemy }, new FixedRangeQuery(true),
                new[] { CreateSpiritRecoveryProfile(selfCost) });
            var service = new CombatCommandService();
            service.AdvanceUntilAction(session);
            CombatantSnapshot target = self ? player : enemy;

            CombatActionResult result = service.Execute(session,
                new CombatCommand(CombatCommandKind.Art, player.Id, target.Id, "restore"));

            Assert.That(result.Succeeded, Is.True, result.RejectionReason);
            Assert.That(result.SpiritSpent, Is.EqualTo(self ? selfCost : 20));
            Assert.That(result.SpiritRecoveries.Single().ActualRecovery, Is.EqualTo(actualRecovery));
            Assert.That(target.CurrentSpirit, Is.EqualTo(finalSpirit));
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            Assert.That(session.TurnScheduler.IsReady(player.Id), Is.True);
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(75000));
            if (!self) Assert.That(player.CurrentSpirit, Is.Zero);
        }

        [Test]
        public void SpiritRecoveryLegalTargetsIncludeSelfAlliesAndEnemiesWithoutFactionFiltering()
        {
            CombatantSnapshot player = CreateCombatant("player", CombatTeam.Player, new HexCoord(0, 0),
                100, 100, 20, 10, equippedArtProfileIds: new[] { "restore" }, availableArtProfileIds: new[] { "restore" });
            player.SetSpirit(100, 20);
            CombatantSnapshot ally = CreateCombatant("ally", CombatTeam.Player, new HexCoord(0, 1), 1, 100, 20, 10);
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1, 100, 20, 10);
            CombatantSnapshot secondEnemy = CreateCombatant("second", CombatTeam.Enemy, new HexCoord(1, 1), 1, 100, 20, 10);
            CombatSession session = CreateSession(new[] { player, ally, enemy, secondEnemy },
                new FixedRangeQuery(true, enforceDeclaredRange: true),
                new[] { CreateSpiritRecoveryProfile() });
            var service = new CombatCommandService();
            service.AdvanceUntilAction(session);

            CollectionAssert.AreEquivalent(new[] { player.Id, ally.Id, enemy.Id, secondEnemy.Id },
                new CombatLegalActionService(service).GetLegalActions(session, player.Id)
                    .Where(command => command.ProfileId == "restore").Select(command => command.TargetId));
            CombatActionResult result = service.Execute(session,
                new CombatCommand(CombatCommandKind.Art, player.Id, ally.Id, "restore"));
            Assert.That(result.Succeeded, Is.True, result.RejectionReason);
            Assert.That(result.SpiritSpent, Is.EqualTo(20));
            Assert.That(result.SpiritRecoveries.Single().TargetId, Is.EqualTo(ally.Id));
        }

        [TestCase("unknown", "attack_profile_not_learned")]
        [TestCase("unequipped", "attack_profile_not_equipped")]
        [TestCase("realm", "attack_profile_requirements_not_met")]
        [TestCase("root", "attack_profile_requirements_not_met")]
        [TestCase("grade", "attack_profile_requirements_not_met")]
        [TestCase("cost", "spirit_insufficient")]
        [TestCase("dead", "combat_session_target_invalid")]
        [TestCase("actor_dead", "combat_session_actor_invalid")]
        [TestCase("far", "target_out_of_range")]
        [TestCase("same_cell", "target_out_of_range")]
        [TestCase("missing", "combat_session_target_invalid")]
        [TestCase("cooldown", "attack_profile_cooldown_active")]
        public void SpiritRecoveryRejectsBeforeAnyResourceTurnOrCombatStateMutation(string invalid, string reason)
        {
            CombatantSnapshot player = CreateCombatant("player", CombatTeam.Player, new HexCoord(0, 0),
                100, 100, 20, 10, movePoints: 1, rawSpirit: 11,
                realmStage: invalid == "realm" ? null : "realm_lianqi",
                visibleRootElement: invalid == "root" ? null : "水",
                visibleRootGrade: invalid == "grade" ? null : "中品",
                equippedArtProfileIds: invalid == "unequipped" ? null : new[] { "restore" },
                availableArtProfileIds: invalid == "unknown" ? null : new[] { "restore" });
            player.SetSpirit(100, invalid == "cost" ? 19 : 20);
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy,
                new HexCoord(invalid == "far" ? 3 : invalid == "same_cell" ? 0 : 1, 0), 1, 100, 20, 10);
            CombatantSnapshot ally = CreateCombatant("ally", CombatTeam.Player, new HexCoord(0, 1), 1, 100, 20, 10);
            CombatantSnapshot secondEnemy = CreateCombatant("second", CombatTeam.Enemy, new HexCoord(2, 0), 1, 100, 20, 10);
            enemy.SetSpirit(100, 10);
            if (invalid == "dead") enemy.ReceiveDamage(100);
            CombatSession session = CreateSession(new[] { player, ally, enemy, secondEnemy },
                new FixedRangeQuery(true, enforceDeclaredRange: true),
                new[] { CreateSpiritRecoveryProfile(qualification: (realm, element, grade) =>
                    realm == "realm_lianqi" && element == "水" && grade == "中品") });
            var service = new CombatCommandService();
            service.AdvanceUntilAction(session);
            if (invalid == "actor_dead") player.ReceiveDamage(100);
            if (invalid == "cooldown") player.SetCooldown("restore", 3);
            player.ShouyiStacks = 2;
            player.FudanStacks = 2;
            player.LeijieStacks = 2;
            int spirit = player.CurrentSpirit, health = enemy.CurrentHealth, cooldown = player.GetCooldown("restore");
            var command = new CombatCommand(CombatCommandKind.Art, player.Id,
                invalid == "missing" ? "missing" : enemy.Id, "restore");

            Assert.That(service.Validate(session, command).RejectionReason, Is.EqualTo(reason));
            AssertRejectedWithoutTurnMutation(service, session, player, command);
            Assert.That(player.CurrentSpirit, Is.EqualTo(spirit));
            Assert.That(enemy.CurrentSpirit, Is.EqualTo(10));
            Assert.That(enemy.CurrentHealth, Is.EqualTo(health));
            Assert.That(player.CurrentHealth, Is.EqualTo(invalid == "actor_dead" ? 0 : 100));
            Assert.That(player.GetCooldown("restore"), Is.EqualTo(cooldown));
            Assert.That(player.ShouyiStacks, Is.EqualTo(2));
            Assert.That(player.FudanStacks, Is.EqualTo(2));
            Assert.That(player.LeijieStacks, Is.EqualTo(2));
            Assert.That(new CombatLegalActionService(service).GetLegalActions(session, player.Id)
                .Any(action => action.ProfileId == "restore" && action.TargetId == command.TargetId), Is.False);
        }

        [TestCase(0f)]
        [TestCase(1.5f)]
        [TestCase(3f)]
        public void SpiritRecoveryEnvironmentRejectsUndeclaredMultipliers(float factor)
        {
            CombatantSnapshot player = CreateCombatant("player", CombatTeam.Player, new HexCoord(0, 0), 100, 100, 20, 10);
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1, 100, 20, 10);
            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatSession(new[] { player, enemy },
                new[] { CreateSpiritRecoveryProfile() },
                new FixedRangeQuery(true),
                new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0), new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) },
                factor));
        }

        [Test]
        public void GuanshijiRetainsPhysicalDamageRollsAndReportsItsRealCostAndCooldown()
        {
            CombatantSnapshot player = CreateCombatant("player", CombatTeam.Player, new HexCoord(0, 0),
                100, 100, 40, 20, movePoints: 1,
                equippedArtProfileIds: new[] { "spell_guanshiji" }, availableArtProfileIds: new[] { "spell_guanshiji" });
            player.SetSpirit(100, 12);
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1, 100, 20, 20);
            enemy.Facing = enemy.Position.DirectionTo(player.Position);
            var profile = new CombatAttackProfile("spell_guanshiji", CombatAttackKind.Art, CombatAttackEffect.Physical,
                1, 1, 50000, 25000, 10, 10, physicalMultiplier: 1.2f, spiritCost: 12);
            CombatSession session = CreateSession(new[] { player, enemy }, new FixedRangeQuery(true), new[] { profile });
            var service = new CombatCommandService();
            service.AdvanceUntilAction(session);

            CombatActionResult result = service.Execute(session, new CombatCommand(CombatCommandKind.Art,
                player.Id, enemy.Id, profile.Id, new CombatResolutionRolls(0f, 100f, 100f, 100f)));

            Assert.That(result.Succeeded, Is.True, result.RejectionReason);
            Assert.That(result.Damage.Single().FinalDamage, Is.EqualTo(32));
            Assert.That(enemy.CurrentHealth, Is.EqualTo(68));
            Assert.That(result.SpiritRecoveries, Is.Empty);
            Assert.That(result.SpiritSpent, Is.EqualTo(12));
            Assert.That(player.CurrentSpirit, Is.Zero);
            Assert.That(session.PendingCtPenalty, Is.EqualTo(10));
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
        }

        /// <summary>
        /// 公共合同要求 CT 轴与重用账本各自只读自己的字段。用不相等的 7 与 13 作为技术输入，
        /// 证明轴只用 ctPenalty、账本只用 reuseCooldownTicks；生产 10/10 与 5/5 无法证明隔离。
        /// </summary>
        [Test]
        public void CtAxisAndReuseLedgerReadTheirOwnFrozenFieldsOnly()
        {
            const int ctPenalty = 7;
            const int reuseCooldownTicks = 13;
            CombatantSnapshot player = CreateCombatant("player", CombatTeam.Player, new HexCoord(0, 0),
                100, 100, 40, 20, movePoints: 1,
                equippedArtProfileIds: new[] { "spell_guanshiji" }, availableArtProfileIds: new[] { "spell_guanshiji" });
            player.SetSpirit(100, 12);
            CombatantSnapshot enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1, 1000, 20, 20);
            enemy.Facing = enemy.Position.DirectionTo(player.Position);
            var profile = new CombatAttackProfile("spell_guanshiji", CombatAttackKind.Art, CombatAttackEffect.Physical,
                1, 1, 50000, 25000, ctPenalty, reuseCooldownTicks, physicalMultiplier: 1.2f, spiritCost: 12);
            CombatSession session = CreateSession(new[] { player, enemy }, new FixedRangeQuery(true), new[] { profile });
            var service = new CombatCommandService();
            service.AdvanceUntilAction(session);

            Assert.That(profile.ActionPointCostUnits, Is.EqualTo(50000));
            Assert.That(profile.MinimumNetCostUnits, Is.EqualTo(25000));
            Assert.That(profile.CtPenalty, Is.EqualTo(ctPenalty));
            Assert.That(profile.ReuseCooldownTicks, Is.EqualTo(reuseCooldownTicks));

            var command = new CombatCommand(CombatCommandKind.Art, player.Id, enemy.Id, profile.Id);
            Assert.That(service.Preview(session, command).CtPenalty, Is.EqualTo(ctPenalty),
                "preview axis must read ctPenalty only");
            Assert.That(player.GetCooldown(profile.Id), Is.Zero,
                "preview must not start the reuse ledger");

            Assert.That(service.Execute(session,
                new CombatCommand(CombatCommandKind.Art, player.Id, enemy.Id, profile.Id,
                    new CombatResolutionRolls(0f, 100f, 100f, 100f))).Succeeded, Is.True);
            Assert.That(session.PendingCtPenalty, Is.EqualTo(ctPenalty),
                "settled CT accumulation must read ctPenalty only");
            Assert.That(player.GetCooldown(profile.Id), Is.EqualTo(reuseCooldownTicks),
                "reuse ledger must read reuseCooldownTicks only");
        }

        private static CombatAttackProfile CreateSpiritRecoveryProfile(int selfCost = 0,
            Func<string, string, string, bool> qualification = null)
        {
            return new CombatAttackProfile("restore", CombatAttackKind.Art, CombatAttackEffect.RestoreSpirit,
                0, 2, 25000, 12500, 5, 5, spiritCost: selfCost,
                spiritRecoveryMultiplier: 0.5f, minimumSpiritRecovery: 5, otherTargetSpiritCost: 20,
                otherTargetSpiritRecoveryMultiplier: 0.5f, qualification: qualification);
        }

        private static float MergeParity(float caseValue, float defaultValue) => caseValue != 0f ? caseValue : defaultValue;

        private static CombatDamageResult ResolveParityAttack(
            float attack,
            float defense,
            float multiplier,
            float realmMultiplier,
            float hitRateBonus,
            float dodgeRate,
            float critRate,
            float critDamage,
            float blockRate,
            float blockReduction,
            float hitPercent,
            float criticalPercent,
            float blockPercent)
        {
            CombatantSnapshot actor = CreateCombatant(
                "parity_actor", CombatTeam.Player, new HexCoord(0, 0), 10, 1000, (int)attack, 0,
                realmMultiplier: realmMultiplier);
            actor.HitRateBonus = hitRateBonus;
            actor.CriticalRate = critRate;
            actor.CriticalDamage = critDamage;

            CombatantSnapshot target = CreateCombatant(
                "parity_target", CombatTeam.Enemy, new HexCoord(1, 0), 10, 1000, 0, (int)defense,
                realmMultiplier: realmMultiplier);
            target.DodgeRate = dodgeRate;
            target.BlockRate = blockRate;
            target.BlockReduction = blockReduction;
            target.Facing = target.Position.DirectionTo(actor.Position);

            var profile = new CombatAttackProfile(
                "basic", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, 1, 1, 0, 0,
                physicalMultiplier: multiplier);
            CombatSession session = CreateSession(
                new[] { actor, target }, new FixedRangeQuery(true), new[] { profile });
            CombatTurnAdvance advance = new CombatCommandService().AdvanceUntilAction(session);
            Assert.That(advance.ActorId, Is.EqualTo(actor.Id));

            CombatActionResult result = new CombatActionResolver().Resolve(
                session,
                new CombatCommand(
                    CombatCommandKind.BasicAttack, actor.Id, target.Id, profile.Id,
                    new CombatResolutionRolls(hitPercent, criticalPercent, blockPercent, 0f)));
            Assert.That(result.Succeeded, Is.True, result.RejectionReason);
            Assert.That(result.Damage.Count, Is.EqualTo(1));
            return result.Damage[0];
        }

        [Serializable]
        private sealed class CombatParityDocument
        {
            public string version;
            public CombatParityDefaults defaults;
            public List<CombatParityCase> cases;
        }

        [Serializable]
        private sealed class CombatParityDefaults
        {
            public float attack;
            public float defense;
            public float multiplier;
            public float realmMultiplier;
            public float defensePenetration;
            public float resistance;
            public float hitRateBonus;
            public float dodgeRate;
            public float critRate;
            public float critDamage;
            public float blockRate;
            public float blockReduction;
            public float soulShieldRate;
            public float soulShieldReduction;
            public float hitPercent;
            public float criticalPercent;
            public float blockPercent;
            public float soulShieldPercent;
        }

        [Serializable]
        private sealed class CombatParityCase
        {
            public string caseId;
            public float attack;
            public float defense;
            public float multiplier;
            public float realmMultiplier;
            public float defensePenetration;
            public float resistance;
            public float hitRateBonus;
            public float dodgeRate;
            public float critRate;
            public float critDamage;
            public float blockRate;
            public float blockReduction;
            public float soulShieldRate;
            public float soulShieldReduction;
            public float hitPercent;
            public float criticalPercent;
            public float blockPercent;
            public float soulShieldPercent;
            public CombatParityExpectation expected;
        }

        [Serializable]
        private sealed class CombatParityExpectation
        {
            public bool hit;
            public bool critical;
            public bool blocked;
            public int damage;
            public float hitRate;
            public float critMultiplier;
            public float blockMultiplier;
        }

        [TestCase(100, 50, 30, false)]
        [TestCase(100, 100, 70, false)]
        [TestCase(220, 50, 0, true)]
        [TestCase(13, 8, 10, false)]
        public void PreviewMatchesIndependentRealSettlementWithoutMutation(int speed, int enemySpeed, int penalty, bool wait)
        {
            var service = new CombatCommandService();
            CombatSession MakeSession()
            {
                var player = CreateCombatant("player", CombatTeam.Player, new HexCoord(0, 0), speed, 1000, 20, 10,
                    movePoints: 2, equippedArtProfileIds: new[] { "preview_art" }, availableArtProfileIds: new[] { "preview_art" });
                player.MaximumSpirit = 100;
                player.RestoreSpirit(100);
                return CreateSession(new[] { player, CreateCombatant("enemy", CombatTeam.Enemy,
                    new HexCoord(1, 0), enemySpeed, 1000, 20, 10) }, new FixedRangeQuery(true), new[]
                {
                    new CombatAttackProfile("preview_art", CombatAttackKind.Art, CombatAttackEffect.Soul,
                        1, 1, 1, 1, penalty, 0, soulMultiplier: 1, spiritCost: 12),
                });
            }
            CombatSession live = MakeSession(), actual = MakeSession();
            foreach (CombatSession current in new[] { live, actual })
            {
                if (service.AdvanceUntilAction(current).ActorId == "enemy")
                    Assert.IsTrue(service.Execute(current, new CombatCommand(CombatCommandKind.EndTurn, "enemy")).Succeeded);
                Assert.AreEqual("player", service.AdvanceUntilAction(current).ActorId);
            }
            var command = new CombatCommand(wait ? CombatCommandKind.Wait : CombatCommandKind.Art,
                "player", "enemy", "preview_art");
            string before = PreviewState(live);
            CombatCommandPreview preview = null;
            for (int index = 0; index < 5; index++) preview = service.Preview(live, command);
            Assert.IsTrue(preview.Validation.Succeeded);
            Assert.AreEqual(before, PreviewState(live));
            Assert.IsTrue(service.Execute(actual, command).Succeeded);
            actual.Combatants.TryGet("player", out CombatantSnapshot realPlayer);
            Assert.AreEqual(100 - preview.SpiritCost, realPlayer.CurrentSpirit);
            if (!wait)
            {
                Assert.IsTrue(preview.RemainsInTurn);
                Assert.AreEqual(CombatTurnPhase.Active, actual.TurnPhase);
                var end = new CombatCommand(CombatCommandKind.EndTurn, "player");
                Assert.AreEqual(penalty, actual.PendingCtPenalty);
                Assert.AreEqual(0, service.Preview(actual, end).CtPenalty);
                Assert.IsTrue(service.Execute(actual, end).Succeeded);
            }
            CombatTurnSnapshot settled = actual.TurnScheduler.GetSnapshot(actual.Combatants.All);
            Assert.AreEqual(preview.Turn.Snapshot.CurrentTick, settled.CurrentTick);
            Assert.That(settled.ReadyQueueIds, Is.EqualTo(preview.Turn.Snapshot.ReadyQueueIds));
            Assert.That(settled.ChargingUnitIds, Is.EqualTo(preview.Turn.Snapshot.ChargingUnitIds));
            foreach (CombatTurnUnitSnapshot unit in settled.Units)
            {
                var predicted = preview.Turn.Snapshot.Units.Single(value => value.Id == unit.Id);
                Assert.AreEqual(predicted.ChargeTime, unit.ChargeTime);
                Assert.AreEqual(predicted.NextActionThreshold, unit.NextActionThreshold);
                Assert.AreEqual(predicted.IsReady, unit.IsReady);
            }
            int originTick = actual.TurnScheduler.CurrentTick;
            // Real scheduler and commands, no copy of the prediction formula in this test.
            for (int step = 0; step < 100; step++)
            {
                CombatTurnAdvance next = service.AdvanceUntilAction(actual);
                if (next.ActorId == "player")
                {
                    Assert.AreEqual(preview.Turn.TicksUntilReady, actual.TurnScheduler.CurrentTick - originTick);
                    return;
                }
                Assert.IsTrue(service.Execute(actual, new CombatCommand(CombatCommandKind.Wait, next.ActorId)).Succeeded);
            }
            Assert.Fail("Actor did not return to the real scheduler.");
        }

        [TestCase(CombatAttackArea.ForwardPair, 0)]
        [TestCase(CombatAttackArea.ForwardPair, 1)]
        [TestCase(CombatAttackArea.ForwardPair, 2)]
        [TestCase(CombatAttackArea.ForwardPair, 3)]
        [TestCase(CombatAttackArea.ForwardPair, 4)]
        [TestCase(CombatAttackArea.ForwardPair, 5)]
        [TestCase(CombatAttackArea.AdjacentPair, 0)]
        [TestCase(CombatAttackArea.AdjacentPair, 1)]
        [TestCase(CombatAttackArea.AdjacentPair, 2)]
        [TestCase(CombatAttackArea.AdjacentPair, 3)]
        [TestCase(CombatAttackArea.AdjacentPair, 4)]
        [TestCase(CombatAttackArea.AdjacentPair, 5)]
        public void AttackAreaQueryReturnsAllEnemiesInStableOrderWithoutSessionMutation(CombatAttackArea area, int direction)
        {
            var origin = new HexCoord(3, 4);
            var geometry = origin.GetTwoCellArea(area == CombatAttackArea.ForwardPair
                ? SpatialTwoCellShape.ForwardPair : SpatialTwoCellShape.AdjacentPair, direction);
            var profile = CreateAreaProfile(area);
            var actor = CreateCombatant("player", CombatTeam.Player, origin, 100, 100, 20, 10, basicAttackProfileId: profile.Id);
            var ally = CreateCombatant("ally", CombatTeam.Player, geometry.First, 1, 100, 20, 10);
            var z = CreateCombatant("a_enemy", CombatTeam.Enemy, geometry.First, 1, 100, 20, 10);
            var a = CreateCombatant("Z_enemy", CombatTeam.Enemy, geometry.Second, 1, 100, 20, 10);
            var query = new FixedRangeQuery(true, enforceDeclaredRange: true);
            var session = CreateSession(new[] { actor, ally, z, a }, query, new[] { profile });
            new CombatCommandService().AdvanceUntilAction(session);
            actor.Facing = (direction + 3) % 6;
            actor.SetCooldown(profile.Id, 7);
            actor.ApplyStatus(CombatStatusKind.Immobilize, 2);
            string before = AreaQueryState(session);
            var result = session.QueryAttackArea(actor.Id, profile.Id, direction);
            Assert.AreEqual(actor.Id, result.ActorId);
            Assert.AreSame(profile, result.Profile);
            Assert.AreEqual(origin, result.Origin);
            Assert.AreEqual(direction, result.Direction);
            Assert.AreEqual(2, result.Cells.Count);
            CollectionAssert.AreEqual(new[] { "Z_enemy", "a_enemy" }, result.TargetIds);
            Assert.AreEqual(before, AreaQueryState(session));
            // A raw read-only fact list may repeat an identity; the session registry still forbids duplicates.
            var repeated = CombatSession.QueryAttackArea(actor, profile, direction, new[] { z, a, actor, ally, z, a }, query);
            CollectionAssert.AreEqual(result.TargetIds, repeated.TargetIds);
            Assert.AreEqual(before, AreaQueryState(session));
        }

        [TestCase(CombatAttackArea.ForwardPair, 0)]
        [TestCase(CombatAttackArea.ForwardPair, 1)]
        [TestCase(CombatAttackArea.ForwardPair, 2)]
        [TestCase(CombatAttackArea.AdjacentPair, 0)]
        [TestCase(CombatAttackArea.AdjacentPair, 1)]
        [TestCase(CombatAttackArea.AdjacentPair, 2)]
        public void AttackAreaQueryFiltersDeadFriendsAndOutsideCells(CombatAttackArea area, int count)
        {
            var actor = CreateCombatant("player", CombatTeam.Player, new HexCoord(0, 0), 10, 100, 20, 10);
            var geometry = actor.Position.GetTwoCellArea(area == CombatAttackArea.ForwardPair
                ? SpatialTwoCellShape.ForwardPair : SpatialTwoCellShape.AdjacentPair, 0);
            var friend = CreateCombatant("friend", CombatTeam.Player, geometry.First, 1, 100, 20, 10);
            var dead = CreateCombatant("dead", CombatTeam.Enemy, geometry.Second, 1, 100, 20, 10);
            dead.ReceiveDamage(100);
            var outside = CreateCombatant("outside", CombatTeam.Enemy, new HexCoord(-1, 0), 1, 100, 20, 10);
            var facts = new List<CombatantSnapshot> { actor, friend, dead, outside };
            for (int index = 0; index < count; index++)
                facts.Add(CreateCombatant("enemy_" + index, CombatTeam.Enemy, geometry.First, 1, 100, 20, 10));
            var result = CombatSession.QueryAttackArea(actor, CreateAreaProfile(area), 0, facts,
                new FixedRangeQuery(true, enforceDeclaredRange: true));
            Assert.AreEqual(2, result.Cells.Count);
            CollectionAssert.AreEqual(Enumerable.Range(0, count).Select(i => "enemy_" + i), result.TargetIds);
        }

        [TestCase(CombatAttackArea.ForwardPair)]
        [TestCase(CombatAttackArea.AdjacentPair)]
        public void AttackAreaQueryUsesExistingSpatialBoundaryForEachCell(CombatAttackArea area)
        {
            var actor = CreateCombatant("player", CombatTeam.Player, new HexCoord(0, 0), 10, 100, 20, 10);
            var shape = area == CombatAttackArea.ForwardPair ? SpatialTwoCellShape.ForwardPair : SpatialTwoCellShape.AdjacentPair;
            var geometry = actor.Position.GetTwoCellArea(shape, 0);
            var first = CreateCombatant("first", CombatTeam.Enemy, geometry.First, 1, 100, 20, 10);
            var second = CreateCombatant("second", CombatTeam.Enemy, geometry.Second, 1, 100, 20, 10);
            var cells = new Dictionary<HexCoord, SpatialCellRules>
            {
                [actor.Position] = new SpatialCellRules(0, false, false, false, 0),
                [geometry.First] = new SpatialCellRules(0, false, false, false, 0),
                [geometry.Second] = new SpatialCellRules(0, false, false, false, 0),
            };
            var board = new SpatialQueryBoard(cells, new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>
            {
                [new SpatialDirectedEdge(actor.Position, geometry.First)] = new SpatialEdgeRules(1, true, true),
                [new SpatialDirectedEdge(area == CombatAttackArea.ForwardPair ? geometry.First : actor.Position, geometry.Second)] =
                    new SpatialEdgeRules(1, true, false),
            }, new SpatialQueryLimits(1, 16));
            var result = CombatSession.QueryAttackArea(actor, CreateAreaProfile(area), 0, new[] { actor, first, second },
                new AreaBoardQuery(board));
            var spatial = board.QueryTwoCellArea(actor.Position, shape, 0);
            CollectionAssert.AreEqual(new[] { "first" }, result.TargetIds);
            for (int index = 0; index < 2; index++)
            {
                Assert.AreEqual(spatial[index].Coord, result.Cells[index].Coord);
                Assert.AreEqual(spatial[index].IsLegal, result.Cells[index].IsLegal);
                Assert.AreEqual(spatial[index].Reason, result.Cells[index].Reason);
            }
        }

        [Test]
        public void AreaDeclarationDoesNotAuthorizeLegacySingleTargetSettlement()
        {
            var profile = CreateAreaProfile(CombatAttackArea.ForwardPair);
            var player = CreateCombatant("player", CombatTeam.Player, new HexCoord(0, 0), 100, 100, 20, 10,
                basicAttackProfileId: profile.Id);
            var enemy = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1, 100, 20, 10);
            var session = CreateSession(new[] { player, enemy }, new FixedRangeQuery(true), new[] { profile });
            var service = new CombatCommandService();
            service.AdvanceUntilAction(session);
            string before = AreaQueryState(session);
            var rejected = service.Execute(session, new CombatCommand(CombatCommandKind.BasicAttack, player.Id, enemy.Id, profile.Id));
            Assert.AreEqual("combat_attack_area_requires_resolution", rejected.RejectionReason);
            Assert.AreEqual(before, AreaQueryState(session));
        }

        [Test]
        public void AreaDeclarationRejectsUnsupportedProfilesAndSingleTargetKeepsOldQuery()
        {
            Assert.Throws<ArgumentException>(() => new CombatAttackProfile("bad", CombatAttackKind.Basic,
                CombatAttackEffect.Physical, 1, 1, 1, 1, 0, 0, area: CombatAttackArea.ForwardPair));
            Assert.Throws<ArgumentException>(() => new CombatAttackProfile("bad", CombatAttackKind.Art,
                CombatAttackEffect.Physical, 1, 1, 1, 1, 0, 0, area: CombatAttackArea.AdjacentPair));
            Assert.Throws<ArgumentException>(() => new CombatAttackProfile("bad", CombatAttackKind.Basic,
                CombatAttackEffect.Physical, 1, 1, 1, 1, 0, 0, area: (CombatAttackArea)99));
            var actor = CreateCombatant("player", CombatTeam.Player, new HexCoord(0, 0), 100, 100, 20, 10);
            var target = CreateCombatant("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1, 100, 20, 10);
            var query = new FixedRangeQuery(true, enforceDeclaredRange: true);
            var session = CreateSession(new[] { actor, target }, query);
            var command = new CombatCommand(CombatCommandKind.BasicAttack, actor.Id, target.Id, "basic");
            new CombatCommandService().AdvanceUntilAction(session);
            var before = session.ValidateCommand(command);
            Assert.Throws<ArgumentException>(() => session.QueryAttackArea(actor.Id, "basic", 0));
            var after = session.ValidateCommand(command);
            Assert.IsTrue(before.Succeeded);
            Assert.AreEqual(before.Succeeded, after.Succeeded);
            Assert.AreEqual(before.RejectionReason, after.RejectionReason);
        }

        private static CombatAttackProfile CreateAreaProfile(CombatAttackArea area) =>
            new CombatAttackProfile("area_fixture", CombatAttackKind.Basic, CombatAttackEffect.Physical,
                1, area == CombatAttackArea.ForwardPair ? 2 : 1, 40000, 40000, 0, 0,
                physicalMultiplier: 1f, area: area);

        private static string AreaQueryState(CombatSession session) => PreviewState(session) + ":" +
            session.StateVersion + ":" + session.TurnSequence + ":" + session.HasCommittedBenefit + ":" +
            string.Join(";", session.Combatants.All.Select(actor => actor.Id + ":" + actor.Facing + ":" +
                actor.IsGuarding + ":" + actor.OwnTurnsStarted + ":" + actor.OwnTurnsEnded + ":" +
                typeof(CombatantSnapshot).GetProperty("CommittedBasicAttackUses",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(actor) + ":" +
                string.Join(",", actor.Cooldowns.Select(p => p.Key + "=" + p.Value)) + ":" +
                string.Join(",", actor.Statuses.Select(s => s.StatusId + "=" + s.RemainingOwnTurnEnds))));

        private sealed class AreaBoardQuery : ICombatSpatialQuery
        {
            private readonly SpatialQueryBoard board;
            public AreaBoardQuery(SpatialQueryBoard board) => this.board = board;
            public CombatRangeQueryResult QueryRange(HexCoord source, HexCoord target, int minimumRange, int maximumRange)
            {
                var result = board.QueryRangeEntry(source, target, minimumRange, maximumRange, SpatialQueryKind.Attack, true);
                return new CombatRangeQueryResult(result.IsInRange, result.Reason);
            }
            public CombatMovementQueryResult QueryMovement(HexCoord source, HexCoord destination, int budget,
                int efficiency, IReadOnlyCollection<HexCoord> occupied) => throw new NotSupportedException();
            public IReadOnlyDictionary<HexCoord, int> FindReachable(HexCoord source, int budget,
                int efficiency, IReadOnlyCollection<HexCoord> occupied) => throw new NotSupportedException();
        }

        private static string PreviewState(CombatSession session)
        {
            CombatTurnSnapshot s = session.TurnScheduler.GetSnapshot(session.Combatants.All);
            return session.CurrentActorId + ":" + session.TurnPhase + ":" + session.PendingCtPenalty + ":" + s.CurrentTick +
                string.Join(";", s.Units.Select(u => u.Id + ":" + u.ChargeTime + ":" + u.NextActionThreshold + ":" + u.IsReady)) +
                string.Join(",", s.ReadyQueueIds) + string.Join(",", s.ChargingUnitIds) +
                string.Join(";", session.Combatants.All.Select(u => u.Id + ":" + u.CurrentHealth + ":" + u.CurrentSpirit +
                    ":" + u.ActionPointsRemainingUnits + ":" + u.Position + ":" + u.GetCooldown("preview_art")));
        }

        private static CombatSession CreateSession(
            IReadOnlyList<CombatantSnapshot> combatants,
            ICombatSpatialQuery query,
            IReadOnlyList<CombatAttackProfile> profiles = null)
        {
            return new CombatSession(combatants,
                profiles ?? new[]
            {
                new CombatAttackProfile("basic", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, 1, 1, 0, 0, physicalMultiplier: 1f),
            },
                query,
                new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0), new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) },
                ruleSet: CombatActionPointKernelTests.Rules());
        }

        private static CombatantSnapshot CreateCombatant(
            string id,
            CombatTeam team,
            HexCoord position,
            int speed,
            int health,
            int attack,
            int defense,
            int movePoints = 0,
            IEnumerable<string> equippedArtProfileIds = null,
            IEnumerable<string> availableArtProfileIds = null,
            int combatSwapsUsed = 0,
            float realmMultiplier = 1f,
            string basicAttackProfileId = "basic",
            IEnumerable<string> equippedDivineProfileIds = null,
            IEnumerable<string> availableDivineProfileIds = null,
            int rawSpirit = 0,
            string realmStage = null,
            string visibleRootElement = null,
            string visibleRootGrade = null)
        {
            return new CombatantSnapshot(id,
                team,
                position,
                speed,
                health,
                health,
                attack,
                attack,
                defense,
                defense,
                100000,
                movePoints * 1000,
                realmMultiplier: realmMultiplier,
                equippedArtProfileIds: equippedArtProfileIds,
                availableArtProfileIds: availableArtProfileIds,
                combatSwapsUsed: combatSwapsUsed,
                basicAttackProfileId: basicAttackProfileId,
                equippedDivineProfileIds: equippedDivineProfileIds,
                availableDivineProfileIds: availableDivineProfileIds,
                rawSpirit: rawSpirit,
                realmStage: realmStage,
                visibleRootElement: visibleRootElement,
                visibleRootGrade: visibleRootGrade)
            {
                Facing = 0,
            };
        }

        private static CombatantSnapshot CreateNeutralTarget(
            string id,
            int health = 100,
            int defense = 100,
            float realmMultiplier = 6f)
        {
            return CreateCombatant(
                id, CombatTeam.Enemy, new HexCoord(1, 0), 1, health, 20, defense,
                realmMultiplier: realmMultiplier);
        }

        private static CombatantSnapshot CreateHanhongTarget(string id, int currentHealth)
        {
            CombatantSnapshot target = CreateNeutralTarget(id, realmMultiplier: 6f);
            target.GongFaId = "含弘光大典";
            if (currentHealth < target.MaximumHealth)
                target.ReceiveDamage(target.MaximumHealth - currentHealth);
            return target;
        }

        private static CombatDamageResult ResolveAttack(
            CombatantSnapshot actor,
            CombatantSnapshot target,
            CombatAttackProfile profile)
        {
            target.Facing = target.Position.DirectionTo(actor.Position);
            if (profile.Kind == CombatAttackKind.Basic)
                actor.BasicAttackProfileId = profile.Id;
            CombatSession session = CreateSession(
                new[] { actor, target }, new FixedRangeQuery(true), new[] { profile });
            CombatTurnAdvance advance = new CombatCommandService().AdvanceUntilAction(session);
            Assert.That(advance.ActorId, Is.EqualTo(actor.Id));
            CombatCommandKind commandKind = profile.Kind switch
            {
                CombatAttackKind.Basic => CombatCommandKind.BasicAttack,
                CombatAttackKind.Art => CombatCommandKind.Art,
                CombatAttackKind.Divine => CombatCommandKind.Divine,
                _ => throw new ArgumentOutOfRangeException(nameof(profile)),
            };
            CombatActionResult result = new CombatActionResolver().Resolve(
                session,
                new CombatCommand(
                    commandKind,
                    actor.Id,
                    target.Id,
                    profile.Id,
                    new CombatResolutionRolls(0f, 100f, 100f, 100f)));
            Assert.That(result.Succeeded, Is.True, result.RejectionReason);
            Assert.That(result.Damage.Count, Is.EqualTo(1));
            if (session.TurnPhase == CombatTurnPhase.Active)
                Assert.That(new CombatCommandService().Execute(session,
                    new CombatCommand(CombatCommandKind.EndTurn, actor.Id)).Succeeded, Is.True);
            return result.Damage[0];
        }

        private static IReadOnlyList<CombatAttackProfile> CreateProfiles(params string[] artProfileIds)
        {
            var profiles = new List<CombatAttackProfile>
            {
                new CombatAttackProfile("basic", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, 1, 1, 0, 0, physicalMultiplier: 1f),
                new CombatAttackProfile("divine", CombatAttackKind.Divine, CombatAttackEffect.Soul, 1, 1, 1, 1, 0, 0, soulMultiplier: 1f),
            };
            foreach (string id in artProfileIds)
                profiles.Add(new CombatAttackProfile(id, CombatAttackKind.Art, CombatAttackEffect.Soul, 1, 1, 1, 1, 0, 0, soulMultiplier: 1f));
            return profiles;
        }

        private static IReadOnlyDictionary<HexCoord, IReadOnlyDictionary<HexCoord, CombatMovementQueryResult>> CreateMovementMap(
            HexCoord source,
            HexCoord destination,
            IReadOnlyList<HexCoord> path,
            int movementCost)
        {
            return new Dictionary<HexCoord, IReadOnlyDictionary<HexCoord, CombatMovementQueryResult>>
            {
                [source] = new Dictionary<HexCoord, CombatMovementQueryResult>
                {
                    [destination] = new CombatMovementQueryResult(true,
                string.Empty,
                path,
                null,
                1000,
                (movementCost) * 10000),
                },
            };
        }

        private sealed class FixedRangeQuery : ICombatSpatialQuery
        {
            private readonly bool inRange;
            private readonly string reason;
            private readonly bool enforceDeclaredRange;
            private readonly IReadOnlyDictionary<HexCoord, IReadOnlyDictionary<HexCoord, CombatMovementQueryResult>> movementBySource;

            public FixedRangeQuery(
                bool inRange,
                string reason = "",
                IReadOnlyDictionary<HexCoord, IReadOnlyDictionary<HexCoord, CombatMovementQueryResult>> movementBySource = null,
                bool enforceDeclaredRange = false)
            {
                this.inRange = inRange;
                this.reason = reason;
                this.enforceDeclaredRange = enforceDeclaredRange;
                this.movementBySource = movementBySource ??
                    new Dictionary<HexCoord, IReadOnlyDictionary<HexCoord, CombatMovementQueryResult>>();
            }

            public CombatRangeQueryResult QueryRange(HexCoord source, HexCoord target, int minimumRange, int maximumRange)
            {
                if (enforceDeclaredRange && (source.Distance(target) < minimumRange || source.Distance(target) > maximumRange))
                    return new CombatRangeQueryResult(false, "target_out_of_range");
                return new CombatRangeQueryResult(inRange, reason);
            }

            public CombatMovementQueryResult QueryMovement(
                HexCoord source,
                HexCoord destination,
                int movementPoints,
                int movementEfficiencyMilliUnits,
                IReadOnlyCollection<HexCoord> occupied)
            {
                if (!movementBySource.TryGetValue(source, out IReadOnlyDictionary<HexCoord, CombatMovementQueryResult> moves) ||
                    !moves.TryGetValue(destination, out CombatMovementQueryResult movement))
                    return new CombatMovementQueryResult(false,
                "declared_move_path_blocker",
                null,
                null,
                1000,
                (0) * 10000);
                if (occupied.Contains(destination) || movement.ActionPointCostUnits > movementPoints)
                    return new CombatMovementQueryResult(false,
                "declared_move_budget_or_occupancy",
                null,
                null,
                1000,
                (0) * 10000);
                if (movement.MovementEdges.Count > 0) return movement;
                var edges = new List<SpatialMovementEdgeCost>();
                HexCoord previous = source;
                foreach (HexCoord step in movement.Path)
                {
                    int metric = checked((int)Math.Max(1L, (long)movement.ActionPointCostUnits * movementEfficiencyMilliUnits / 100000 / movement.Path.Count));
                    edges.Add(new SpatialMovementEdgeCost(previous, step, metric, 0));
                    previous = step;
                }
                return new CombatMovementQueryResult(true, movement.Reason, movement.Path, edges, 1000, movement.ActionPointCostUnits);
            }

            public IReadOnlyDictionary<HexCoord, int> FindReachable(
                HexCoord source,
                int movementPoints,
                int movementEfficiencyMilliUnits,
                IReadOnlyCollection<HexCoord> occupied)
            {
                var reachable = new Dictionary<HexCoord, int>();
                if (!movementBySource.TryGetValue(source, out IReadOnlyDictionary<HexCoord, CombatMovementQueryResult> moves))
                    return reachable;

                foreach (KeyValuePair<HexCoord, CombatMovementQueryResult> entry in moves)
                {
                    if (!occupied.Contains(entry.Key) && entry.Value.IsReachable && entry.Value.ActionPointCostUnits <= movementPoints)
                        reachable.Add(entry.Key, entry.Value.ActionPointCostUnits);
                }
                return reachable;
            }
        }
    }
}
