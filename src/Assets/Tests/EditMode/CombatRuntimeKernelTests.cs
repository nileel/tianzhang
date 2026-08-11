using System;
using System.Collections.Generic;
using System.IO;
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
                "CombatSession.cs", "CombatTurnScheduler.cs", "Turns/CTBEngine.cs"
            };
            string[] forbidden = { "TianZhang.Entity", "MonoBehaviour", "UnityEngine.UI", "Renderer", "Debug.", "SceneManager" };
            foreach (string relativePath in sourceFiles)
            {
                string content = File.ReadAllText(Path.Combine(combatRoot, relativePath));
                foreach (string token in forbidden)
                    Assert.That(content, Does.Not.Contain(token), relativePath + " must remain pure.");
            }
        }

        private static CombatSession CreateSession(IReadOnlyList<CombatantSnapshot> combatants, ICombatSpatialQuery query)
        {
            return new CombatSession(combatants, new[]
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
            int defense)
        {
            return new CombatantSnapshot(id, team, position, speed, health, health, attack, attack, defense, defense)
            {
                Facing = 0,
            };
        }

        private sealed class FixedRangeQuery : ICombatSpatialQuery
        {
            private readonly bool inRange;
            private readonly string reason;

            public FixedRangeQuery(bool inRange, string reason = "")
            {
                this.inRange = inRange;
                this.reason = reason;
            }

            public CombatRangeQueryResult QueryRange(HexCoord source, HexCoord target, int minimumRange, int maximumRange)
            {
                return new CombatRangeQueryResult(inRange, reason);
            }
        }
    }
}
