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
                "soul", CombatAttackKind.Basic, CombatAttackEffect.Soul, 1, 1, soulMultiplier: 1f);
            var physical = new CombatAttackProfile(
                "physical", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, physicalMultiplier: 1f);

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
                "physical", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, physicalMultiplier: 1f);
            var soul = new CombatAttackProfile(
                "soul", CombatAttackKind.Basic, CombatAttackEffect.Soul, 1, 1, soulMultiplier: 1f);
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
                "physical", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, physicalMultiplier: 1f);
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
                "soul", CombatAttackKind.Basic, CombatAttackEffect.Soul, 1, 1, soulMultiplier: 1f);
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
                "soul", CombatAttackKind.Basic, CombatAttackEffect.Soul, 1, 1, soulMultiplier: 1f);
            CombatantSnapshot partial = CreateCombatant(
                "partial", CombatTeam.Player, new HexCoord(0, 0), 10, 100, 200, 50, realmMultiplier: 6f);
            partial.GongFaId = "云篆度人经";
            partial.FudanStacks = 4;
            CombatantSnapshot full = CreateCombatant(
                "full", CombatTeam.Player, new HexCoord(0, 0), 10, 100, 200, 50, realmMultiplier: 6f);
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
                "physical", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, physicalMultiplier: 1f);
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
            int combatSwapsUsed = 0,
            float realmMultiplier = 1f)
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
                realmMultiplier: realmMultiplier,
                movePoints: movePoints,
                equippedArtProfileIds: equippedArtProfileIds,
                availableArtProfileIds: availableArtProfileIds,
                combatSwapsUsed: combatSwapsUsed)
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
                id, CombatTeam.Enemy, new HexCoord(1, 0), 10, health, 20, defense,
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
            return result.Damage[0];
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
