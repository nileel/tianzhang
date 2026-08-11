using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Core;
using TianZhang.Entity;
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
            var legacyEngine = new CTBEngine();
            legacyEngine.RegisterUnit(50);
            legacyEngine.RegisterUnit(25);

            CombatTurnAdvance advance = service.AdvanceUntilAction(session);
            Assert.That(advance.ActorId, Is.EqualTo("player"));
            Assert.That(advance.TicksElapsed, Is.EqualTo(2));
            Assert.That(legacyEngine.AdvanceUntilAction().ticksElapsed, Is.EqualTo(advance.TicksElapsed));

            CombatActionResult result = service.Execute(session, new CombatCommand(
                CombatCommandKind.BasicAttack,
                "player",
                "enemy",
                "basic",
                new CombatResolutionRolls(0f, 100f, 100f, 100f)));

            Assert.That(result.Succeeded, Is.True, result.RejectionReason);
            Assert.That(result.Damage.Count, Is.EqualTo(1));
            var legacyAttacker = new Character
            {
                PhysAtk = 40,
                RealmMultiplier = 1f,
                Position = new HexCoord(0, 0),
            };
            var legacyDefender = new Character
            {
                PhysDef = 20,
                RealmMultiplier = 1f,
                Position = new HexCoord(1, 0),
                Facing = 0,
            };
            DamageCalculator.DamageResult legacyDamage = DamageCalculator.CalcPhysical(
                legacyAttacker.PhysAtk, 1f, legacyAttacker, legacyDefender);
            Assert.That(result.Damage[0].FinalDamage, Is.EqualTo(legacyDamage.FinalDamage));
            Assert.That(result.Damage[0].FinalDamage, Is.EqualTo(35));
            Assert.That(enemy.CurrentHealth, Is.EqualTo(100 - legacyDamage.FinalDamage));
            Assert.That(session.TurnScheduler.IsReady("player"), Is.False);
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
        public void MoveUsesTheCanonicalSpatialPathAndConsumesOneReadyAction()
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
            Assert.That(result.MovementCost, Is.EqualTo(2));
            Assert.That(player.Position, Is.EqualTo(destination));
            Assert.That(session.TurnScheduler.IsReady(player.Id), Is.False);
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
            Assert.That(player.CombatSwapsUsed, Is.EqualTo(1));
            Assert.That(player.GetCooldown("new_art"), Is.EqualTo(60));
            Assert.That(session.TurnScheduler.IsReady(player.Id), Is.False);
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
        public void PlayerAndAiReceiveTheSameSevenLegalCommandKinds()
        {
            HexCoord playerOrigin = new HexCoord(0, 0);
            HexCoord enemyOrigin = new HexCoord(1, 0);
            CombatantSnapshot player = CreateCombatant(
                "player", CombatTeam.Player, playerOrigin, 100, 100, 20, 10, movePoints: 1,
                equippedArtProfileIds: new[] { "art" }, availableArtProfileIds: new[] { "art", "backup_art" });
            CombatantSnapshot enemy = CreateCombatant(
                "enemy", CombatTeam.Enemy, enemyOrigin, 100, 100, 20, 10, movePoints: 1,
                equippedArtProfileIds: new[] { "art" }, availableArtProfileIds: new[] { "art", "backup_art" });
            var query = new FixedRangeQuery(true, movementBySource: new Dictionary<HexCoord, IReadOnlyDictionary<HexCoord, CombatMovementQueryResult>>
            {
                [playerOrigin] = new Dictionary<HexCoord, CombatMovementQueryResult>
                {
                    [new HexCoord(-1, 0)] = new CombatMovementQueryResult(true, string.Empty, new[] { new HexCoord(-1, 0) }, 1),
                },
                [enemyOrigin] = new Dictionary<HexCoord, CombatMovementQueryResult>
                {
                    [new HexCoord(2, 0)] = new CombatMovementQueryResult(true, string.Empty, new[] { new HexCoord(2, 0) }, 1),
                },
            });
            CombatSession session = CreateSession(new[] { player, enemy }, query, CreateProfiles("art", "backup_art"));
            var commandService = new CombatCommandService();
            var legalActions = new CombatLegalActionService(commandService);

            Assert.That(commandService.AdvanceUntilAction(session).HasActor, Is.True);
            ISet<CombatCommandKind> playerKinds = new HashSet<CombatCommandKind>(
                legalActions.GetLegalActions(session, player.Id).Select(command => command.Kind));
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
            };

            CollectionAssert.AreEquivalent(expected, playerKinds);
            CollectionAssert.AreEquivalent(expected, enemyKinds);
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

        private static CombatSession CreateSession(
            IReadOnlyList<CombatantSnapshot> combatants,
            ICombatSpatialQuery query,
            IReadOnlyList<CombatAttackProfile> profiles = null)
        {
            return new CombatSession(combatants, profiles ?? new[]
            {
                new CombatAttackProfile("basic", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, physicalMultiplier: 1f),
            }, query);
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
            int combatSwapsUsed = 0)
        {
            return new CombatantSnapshot(
                id,
                team,
                position,
                speed,
                health,
                health,
                attack,
                attack,
                defense,
                defense,
                movePoints: movePoints,
                equippedArtProfileIds: equippedArtProfileIds,
                availableArtProfileIds: availableArtProfileIds,
                combatSwapsUsed: combatSwapsUsed)
            {
                Facing = 0,
            };
        }

        private static IReadOnlyList<CombatAttackProfile> CreateProfiles(params string[] artProfileIds)
        {
            var profiles = new List<CombatAttackProfile>
            {
                new CombatAttackProfile("basic", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, physicalMultiplier: 1f),
                new CombatAttackProfile("divine", CombatAttackKind.Divine, CombatAttackEffect.Soul, 1, 1, soulMultiplier: 1f),
            };
            foreach (string id in artProfileIds)
                profiles.Add(new CombatAttackProfile(id, CombatAttackKind.Art, CombatAttackEffect.Soul, 1, 1, soulMultiplier: 1f));
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
                    [destination] = new CombatMovementQueryResult(true, string.Empty, path, movementCost),
                },
            };
        }

        private sealed class FixedRangeQuery : ICombatSpatialQuery
        {
            private readonly bool inRange;
            private readonly string reason;
            private readonly IReadOnlyDictionary<HexCoord, IReadOnlyDictionary<HexCoord, CombatMovementQueryResult>> movementBySource;

            public FixedRangeQuery(
                bool inRange,
                string reason = "",
                IReadOnlyDictionary<HexCoord, IReadOnlyDictionary<HexCoord, CombatMovementQueryResult>> movementBySource = null)
            {
                this.inRange = inRange;
                this.reason = reason;
                this.movementBySource = movementBySource ??
                    new Dictionary<HexCoord, IReadOnlyDictionary<HexCoord, CombatMovementQueryResult>>();
            }

            public CombatRangeQueryResult QueryRange(HexCoord source, HexCoord target, int minimumRange, int maximumRange)
            {
                return new CombatRangeQueryResult(inRange, reason);
            }

            public CombatMovementQueryResult QueryMovement(
                HexCoord source,
                HexCoord destination,
                int movementPoints,
                IReadOnlyCollection<HexCoord> occupied)
            {
                return movementBySource.TryGetValue(source, out IReadOnlyDictionary<HexCoord, CombatMovementQueryResult> moves) &&
                       moves.TryGetValue(destination, out CombatMovementQueryResult movement)
                    ? movement
                    : new CombatMovementQueryResult(false, "declared_move_path_blocker", null, 0);
            }

            public IReadOnlyDictionary<HexCoord, int> FindReachable(
                HexCoord source,
                int movementPoints,
                IReadOnlyCollection<HexCoord> occupied)
            {
                var reachable = new Dictionary<HexCoord, int>();
                if (!movementBySource.TryGetValue(source, out IReadOnlyDictionary<HexCoord, CombatMovementQueryResult> moves))
                    return reachable;

                foreach (KeyValuePair<HexCoord, CombatMovementQueryResult> entry in moves)
                {
                    if (!occupied.Contains(entry.Key) && entry.Value.IsReachable)
                        reachable.Add(entry.Key, entry.Value.MovementCost);
                }
                return reachable;
            }
        }
    }
}
