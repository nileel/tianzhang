using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Character;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Editor;
using TianZhang.Entity;
using TianZhang.Features.Adventure;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.Spatial;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.EditMode
{
    public sealed class CombatRealmStatusTests
    {
        private const string Up = "fixture_percent_attack_up", Down = "fixture_percent_defense_down", Stun = "fixture_probability_stun";
        private readonly List<Object> temporary = new List<Object>();
        private readonly CombatCommandService service = new CombatCommandService();
        private CombatRuleSetData Rules => AssetDatabase.LoadAssetAtPath<CombatRuleSetData>("Assets/Data/CombatRuleSet.asset");
        private static readonly string[] Stages = { "凡人", "练气", "筑基", "金丹" };
        [TearDown] public void Cleanup() { foreach (Object value in temporary) Object.DestroyImmediate(value); temporary.Clear(); }

        private AttackProfileData Project(string id, params (string field, string value)[] changes)
        {
            string[] lines = File.ReadAllLines(Path.Combine(Application.dataPath, "Tests/EditMode/Fixtures/RealmStatuses.fixture.csv"));
            string[] fields = lines[0].Split(','), row = lines.Single(line => line.StartsWith(id + ",", StringComparison.Ordinal)).Split(',');
            foreach (var change in changes) row[Array.IndexOf(fields, change.field)] = change.value;
            Assert.That(CombatContentImporter.TryBuildAttackProfileProjection(new[] { lines[0], string.Join(",", row) },
                new[] { id }, out var profiles, out string reason), Is.True, reason);
            temporary.AddRange(profiles); return profiles.Single();
        }
        private static CombatantSnapshot Unit(string id, CombatTeam team, int stage = 1, int basis = 100, int? mind = 10) =>
            new CombatantSnapshot(id, team, new HexCoord(id == "player" ? 0 : 1, 0), id == "player" ? 100 : 1,
                100000, 99900, basis, basis, basis, basis, 1000000, 4000, realmMultiplier: 1,
                equippedArtProfileIds: new[] { Up, Down, Stun }, availableArtProfileIds: new[] { Up, Down, Stun },
                realmStage: Stages[stage], visibleRootElement: "土", visibleRootGrade: "下品", rawMind: mind);
        private CombatSession Ready(CombatantSnapshot actor, CombatantSnapshot target, params AttackProfileData[] profiles)
        {
            var environment = AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>("Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset");
            var adapter = new CombatEntryAdapter(CombatActionPointKernelTests.Ordinary(), Rules);
            var other = target.Team != actor.Team ? target : Unit("opponent", actor.Team == CombatTeam.Player ? CombatTeam.Enemy : CombatTeam.Player);
            Assert.That(adapter.TryCreateSession(new AdventureSpawnSet(actor, other, null, "", "", Array.Empty<string>()),
                profiles, environment, out var session, out string reason), Is.True, reason);
            if (target.Team == actor.Team && target != actor)
                session = new CombatSession(new[] { actor, target, other, Unit("opponent2", other.Team) },
                    session.AttackProfiles, session.SpatialQuery, CombatActionPointKernelTests.Ordinary(), ruleSet: session.RuleSet);
            Assert.That(service.AdvanceUntilAction(session).ActorId, Is.EqualTo(actor.Id)); return session;
        }
        private static CombatCommand Command(CombatantSnapshot actor, CombatantSnapshot target, string id,
            float? control = 0, float hit = 0) => new CombatCommand(CombatCommandKind.Art, actor.Id, target.Id, id,
                new CombatResolutionRolls(hit, 100, 100, 100, control));
        private void Commit(CombatSession session, CombatCommand command)
        { var result = service.Execute(session, command); Assert.That(result.Succeeded, Is.True, result.RejectionReason); }
        private static int Attribute(CombatantSnapshot unit, string attribute) => attribute switch
        {
            "physical_attack" => unit.PhysicalAttack, "soul_attack" => unit.SoulAttack,
            "physical_defense" => unit.PhysicalDefense, _ => unit.SoulDefense,
        };
        private static object Call(CombatantSnapshot unit, string method) => typeof(CombatantSnapshot)
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(unit, null);
        private static IEnumerable<TestCaseData> AttributeCases()
        {
            foreach (string attribute in new[] { "physical_attack", "soul_attack", "physical_defense", "soul_defense" })
            foreach (bool up in new[] { true, false }) foreach (bool mirror in new[] { true, false })
            for (int delta = -2; delta <= 2; delta++) yield return new TestCaseData(attribute, up, mirror, delta);
        }
        [TestCaseSource(nameof(AttributeCases))]
        public void StrictFixtureAdapterCandidatesCommandAndPublishedAttributesFollowFiveRealmBands(string attribute, bool up, bool mirror, int delta)
        {
            var profile = Project(up ? Up : Down, ("statusAttributeId", attribute));
            var actor = Unit("player", mirror ? CombatTeam.Enemy : CombatTeam.Player, delta > 0 ? 1 + delta : 1);
            var target = Unit("target", up ? actor.Team : mirror ? CombatTeam.Player : CombatTeam.Enemy, delta < 0 ? 1 - delta : 1);
            var session = Ready(actor, target, profile);
            Assert.That(new CombatLegalActionService().GetLegalActions(session, actor.Id).Any(c => c.TargetId == target.Id && c.ProfileId == profile.attackProfileId), Is.True);
            Commit(session, Command(actor, target, profile.attackProfileId));
            int amount = delta switch { -2 => 3, -1 => 5, 0 => 10, 1 => 15, _ => 20 };
            Assert.That(Attribute(target, attribute), Is.EqualTo(100 + (up ? amount : -amount)));
            var status = target.Statuses.Single();
            Assert.That(status.Polarity, Is.EqualTo(up ? CombatStatusPolarity.BUFF : CombatStatusPolarity.DEBUFF));
            Assert.That(status.Tags, Is.EqualTo(CombatStatusTags.ATTRIBUTE | (attribute.EndsWith("attack") ? CombatStatusTags.OFFENSE : CombatStatusTags.DEFENSE)));
            Assert.That(status.SourceRef, Is.EqualTo(actor.GetAbilityInstanceId(profile.attackProfileId)));
            Assert.That(status.SourceKind, Is.EqualTo(CombatStatusSourceKind.OTHER_ABILITY));
            Assert.That(status.RemovalPolicy, Is.EqualTo("NORMAL"));
        }
        [TestCase("tier_lianqi", 1472)] [TestCase("tier_jindan", 1606)]
        public void SelfBuffUsesUnbuffedBasisAndFixedTierInsteadOfCompounding(string tier, int expected)
        {
            var profile = Project(Up, ("abilityTierId", tier), ("reuseCooldownTicks", "0"));
            var actor = Unit("player", CombatTeam.Player, 3, 1460);
            var session = Ready(actor, Unit("enemy", CombatTeam.Enemy), profile);
            for (int i = 0; i < 3; i++) Commit(session, Command(actor, actor, Up));
            Assert.That(actor.PhysicalAttack, Is.EqualTo(expected)); Assert.That(actor.Statuses.Count, Is.EqualTo(1));
            Assert.That(actor.Statuses[0].SourceKind, Is.EqualTo(CombatStatusSourceKind.SELF_ABILITY));
            Call(actor, "EndOwnTurn"); Assert.That(actor.Statuses[0].RemainingOwnTurnEnds, Is.EqualTo(2));
            Call(actor, "BeginOwnTurn"); Call(actor, "EndOwnTurn"); Assert.That(actor.Statuses[0].RemainingOwnTurnEnds, Is.EqualTo(1));
            Call(actor, "BeginOwnTurn"); Call(actor, "EndOwnTurn"); Assert.That(actor.PhysicalAttack, Is.EqualTo(1460));
        }
        [Test]
        public void AggregateRoundsOnceAndAppliesTheTotalThirtyPercentPeak()
        {
            var a = Project(Up, ("statusRelativePermille", "4"), ("reuseCooldownTicks", "0"));
            var b = Project(Down, ("effectType", "buff"), ("statusAttributeId", "physical_attack"), ("statusRelativePermille", "4"));
            var actor = Unit("player", CombatTeam.Player); var target = Unit("target", CombatTeam.Player);
            // Explicitly shared instance is required for the already permitted same-source aggregate.
            actor = new CombatantSnapshot("player", CombatTeam.Player, new HexCoord(0, 0), 100, 100000, 100000,
                100, 100, 100, 100, 1000000, 4000, equippedArtProfileIds: new[] { Up, Down }, availableArtProfileIds: new[] { Up, Down },
                realmStage: "练气", visibleRootElement: "土", visibleRootGrade: "下品",
                abilityInstanceIds: new Dictionary<string, string> { [Up] = "same-source", [Down] = "same-source" });
            var session = Ready(actor, target, a, b);
            Commit(session, Command(actor, target, Up)); Assert.That(target.PhysicalAttack, Is.EqualTo(100));
            Commit(session, Command(actor, target, Down)); Assert.That(target.PhysicalAttack, Is.EqualTo(101));
            var high = Project(Up, ("statusRelativePermille", "1000"));
            actor = Unit("player", CombatTeam.Player, 3); target = Unit("target", CombatTeam.Player);
            Commit(Ready(actor, target, high), Command(actor, target, Up)); Assert.That(target.PhysicalAttack, Is.EqualTo(130));
            a = Project(Up, ("statusRelativePermille", "1000"));
            b = Project(Down, ("effectType", "buff"), ("statusAttributeId", "physical_attack"), ("statusRelativePermille", "1000"));
            actor = new CombatantSnapshot("player", CombatTeam.Player, new HexCoord(0, 0), 100, 100000, 100000,
                100, 100, 100, 100, 1000000, 4000, equippedArtProfileIds: new[] { Up, Down }, availableArtProfileIds: new[] { Up, Down },
                realmStage: "练气", visibleRootElement: "土", visibleRootGrade: "下品",
                abilityInstanceIds: new Dictionary<string, string> { [Up] = "same-source", [Down] = "same-source" });
            target = Unit("target", CombatTeam.Player, 3, 1460); session = Ready(actor, target, a, b);
            Commit(session, Command(actor, target, Up)); actor.AdvanceCooldowns(4);
            Commit(session, Command(actor, target, Down)); Assert.That(target.PhysicalAttack, Is.EqualTo(1496));
        }
        [TestCase("statusAttributeId", "hit_rate")] [TestCase("statusStackLimit", "99")]
        [TestCase("statusRefreshPolicy", "stack")] [TestCase("statusRelativePermille", "0")]
        [TestCase("statusRelativePermille", "-100")] [TestCase("statusDurationOwnTurnEnds", "0")]
        [TestCase("buffMultiplier", "1.1")]
        public void StrictStatusCsvRejectsUnsupportedOrConflictingPayloadsBeforeProjection(string field, string value)
        {
            string[] lines = File.ReadAllLines(Path.Combine(Application.dataPath, "Tests/EditMode/Fixtures/RealmStatuses.fixture.csv"));
            string[] row = lines[1].Split(','); row[Array.IndexOf(lines[0].Split(','), field)] = value;
            Assert.That(CombatContentImporter.TryBuildAttackProfileProjection(new[] { lines[0], string.Join(",", row) },
                new[] { Up }, out var profiles, out _), Is.False); Assert.That(profiles, Is.Empty);
        }
        [Test]
        public void OnlyStrongerSameSourceReplacesWithItsNewDurationAndCopiesDoNotLeak()
        {
            var profile = Project(Up, ("reuseCooldownTicks", "0"));
            var actor = Unit("player", CombatTeam.Player); var session = Ready(actor, Unit("enemy", CombatTeam.Enemy), profile);
            Commit(session, Command(actor, actor, Up)); Call(actor, "EndOwnTurn"); Call(actor, "BeginOwnTurn"); Call(actor, "EndOwnTurn");
            Assert.That(actor.Statuses[0].RemainingOwnTurnEnds, Is.EqualTo(1));
            var copy = (CombatantSnapshot)Call(actor, "CopyForResolution"); Call(copy, "BeginOwnTurn"); Call(copy, "EndOwnTurn");
            Assert.That(copy.Statuses, Is.Empty); Assert.That(actor.Statuses[0].RemainingOwnTurnEnds, Is.EqualTo(1));
            // New adapter/session retains the target's existing record, but changes only the declared source strength.
            foreach (int relative in new[] { 50, 100, 200 })
            {
                var changed = Project(Up, ("statusRelativePermille", relative.ToString()), ("reuseCooldownTicks", "0"));
                session = Ready(actor, Unit("enemy", CombatTeam.Enemy), changed);
                Commit(session, Command(actor, actor, Up));
                Assert.That(actor.Statuses[0].RemainingOwnTurnEnds, Is.EqualTo(relative == 200 ? 2 : 1));
                Assert.That(actor.PhysicalAttack, Is.EqualTo(relative == 200 ? 120 : 110));
            }
        }
        [TestCase("physical_attack")] [TestCase("soul_attack")]
        [TestCase("physical_defense")] [TestCase("soul_defense")]
        public void PublishedAttributesAreConsumedByTheNextRealDamageCommand(string attribute)
        {
            bool attack = attribute.EndsWith("attack");
            var status = Project(attack ? Up : Down, ("statusAttributeId", attribute));
            var damage = Project(Stun, ("effectType", attribute.StartsWith("soul") ? "magic" : "physical"),
                ("controlChancePermille", "0"), ("reuseCooldownTicks", "0"));
            var actor = Unit("player", CombatTeam.Player); var target = Unit("enemy", CombatTeam.Enemy);
            var session = Ready(actor, target, status, damage);
            int before = service.Execute(session, Command(actor, target, Stun)).Damage.Single().FinalDamage;
            Commit(session, Command(actor, attack ? actor : target, status.attackProfileId));
            int after = service.Execute(session, Command(actor, target, Stun)).Damage.Single().FinalDamage;
            Assert.That(after, Is.GreaterThan(before));
        }
        [Test]
        public void PreparedRejectedAndOverflowCommandsPublishNeitherStatusNorPayment()
        {
            var profile = Project(Up); var actor = Unit("player", CombatTeam.Player); var session = Ready(actor, Unit("enemy", CombatTeam.Enemy), profile);
            var command = Command(actor, actor, Up); var quote = service.Preview(session, command);
            Assert.That(service.Prepare(session, command, quote, out var apply).Succeeded, Is.True);
            Assert.That(actor.Statuses, Is.Empty); Assert.That(actor.ActionPointsRemainingUnits, Is.EqualTo(1000000));
            Assert.That(service.Execute(session, Command(actor, session.Combatants.All[1], Up)).Succeeded, Is.False);
            Assert.That(actor.Statuses, Is.Empty); apply(); Assert.That(actor.PhysicalAttack, Is.EqualTo(110));
            Assert.That(service.Execute(session, command, quote).RejectionReason, Is.EqualTo("combat_quote_stale"));
            var over = Project(Up, ("abilityTierId", "tier_jindan")); actor = Unit("player", CombatTeam.Player, 3, int.MaxValue);
            session = Ready(actor, Unit("enemy", CombatTeam.Enemy), over);
            Assert.That(service.Execute(session, Command(actor, actor, Up)).RejectionReason, Is.EqualTo("combat_effect_overflow"));
            Assert.That(actor.Statuses, Is.Empty); Assert.That(actor.ActionPointsRemainingUnits, Is.EqualTo(1000000)); Assert.That(actor.Cooldowns, Is.Empty);
        }
        [TestCase(-2, 2.49f, true)] [TestCase(-2, 2.5f, false)] [TestCase(-1, 4.99f, true)]
        [TestCase(0, 9.99f, true)] [TestCase(1, 14.99f, true)] [TestCase(2, 19.99f, true)]
        public void FifthSampleControlsUnroundedProbabilityAcrossRealmBands(int delta, float roll, bool controlled)
        {
            var profile = Project(Stun); var actor = Unit("player", CombatTeam.Player, delta > 0 ? 1 + delta : 1);
            var target = Unit("enemy", CombatTeam.Enemy, delta < 0 ? 1 - delta : 1);
            Commit(Ready(actor, target, profile), Command(actor, target, Stun, roll));
            Assert.That(target.HasStatus(CombatStatusKind.Stun), Is.EqualTo(controlled));
        }
        [TestCase(null)] [TestCase(-1f)] [TestCase(101f)] [TestCase(float.NaN)]
        public void MissingOrInvalidControlSampleRejectsWithoutPartialEffects(float? roll)
        {
            var actor = Unit("player", CombatTeam.Player); var target = Unit("enemy", CombatTeam.Enemy);
            var session = Ready(actor, target, Project(Stun));
            Assert.That(service.Execute(session, Command(actor, target, Stun, roll)).RejectionReason, Is.EqualTo("combat_control_roll_invalid"));
            Assert.That(target.CurrentHealth, Is.EqualTo(99900)); Assert.That(target.Statuses, Is.Empty); Assert.That(actor.Cooldowns, Is.Empty);
        }
        [TestCase(false)] [TestCase(true)]
        public void MindContestUsesAuthoritativeInputsAndHasNoOldFloorAfterRealmCorrection(bool missing)
        {
            var actor = Unit("player", CombatTeam.Player, mind: missing ? null : 0); var target = Unit("enemy", CombatTeam.Enemy, 3, mind: 100);
            var session = Ready(actor, target, Project(Stun, ("controlUsesMindContest", "1")));
            var result = service.Execute(session, Command(actor, target, Stun, 1.25f));
            if (missing) { Assert.That(result.RejectionReason, Is.EqualTo("combat_raw_mind_missing")); Assert.That(actor.Cooldowns, Is.Empty); }
            else { Assert.That(result.Succeeded, Is.True); Assert.That(target.HasStatus(CombatStatusKind.Stun), Is.False); }
            Assert.That(actor.RawMindInputsPresent, Is.EqualTo(!missing));
        }
        [TestCase(89.99f, true)] [TestCase(90f, false)]
        public void ControlIsCappedAtConfiguredNinetyPercent(float roll, bool controlled)
        {
            var actor = Unit("player", CombatTeam.Player, 3, mind: 100); var target = Unit("enemy", CombatTeam.Enemy, mind: 0);
            Commit(Ready(actor, target, Project(Stun, ("controlUsesMindContest", "1"))), Command(actor, target, Stun, roll));
            Assert.That(target.HasStatus(CombatStatusKind.Stun), Is.EqualTo(controlled));
        }
        [Test]
        public void MissAndNonpositiveDamageNeverWriteControlAndExistingStunNeverRefreshes()
        {
            var actor = Unit("player", CombatTeam.Player); var target = Unit("enemy", CombatTeam.Enemy); target.DodgeRate = 95;
            var profile = Project(Stun, ("reuseCooldownTicks", "0")); var session = Ready(actor, target, profile);
            Commit(session, Command(actor, target, Stun, 0, 100)); Assert.That(target.Statuses, Is.Empty);
            target.DodgeRate = 0; target.ApplyStatus(CombatStatusKind.Stun, 1, sourceRef: "prior");
            var previous = target.Statuses[0]; Commit(session, Command(actor, target, Stun));
            Assert.That(target.Statuses[0].SourceRef, Is.EqualTo("prior")); Assert.That(target.Statuses[0].RemainingOwnTurnEnds, Is.EqualTo(1));
            var zero = Project(Stun, ("effectType", "hybrid"));
            zero.physicalDamageMultiplier = zero.soulDamageMultiplier = 0;
            // Kernel damage has a minimum one on a hit; the reachable no-positive case is both lines missing.
            actor = Unit("player", CombatTeam.Player); target = Unit("enemy", CombatTeam.Enemy); target.DodgeRate = 95;
            var result = service.Execute(Ready(actor, target, zero), Command(actor, target, Stun, 0, 100));
            Assert.That(result.Damage.Single().FinalDamage, Is.Zero); Assert.That(target.Statuses, Is.Empty);
        }
        [Test]
        public void StunSkipsAWholeOwnTurnStillRecoversThenAllowsTheFollowingTurn()
        {
            var actor = Unit("player", CombatTeam.Player); var target = Unit("enemy", CombatTeam.Enemy);
            target.SetSpirit(100, 50); target.HealthRecoveryPerOwnTurn = 5; target.SpiritRecoveryPerOwnTurn = 7;
            Commit(Ready(actor, target, Project(Stun)), Command(actor, target, Stun)); int health = target.CurrentHealth;
            Assert.That(Call(target, "BeginOwnTurn"), Is.EqualTo(false)); Assert.That(target.ActionPointsRemainingUnits, Is.Zero);
            Assert.That(target.CurrentHealth, Is.EqualTo(health + 5)); Assert.That(target.CurrentSpirit, Is.EqualTo(57));
            Call(target, "EndOwnTurn"); Assert.That(target.Statuses, Is.Empty);
            Assert.That(Call(target, "BeginOwnTurn"), Is.EqualTo(true)); Assert.That(target.ActionPointsRemainingUnits, Is.EqualTo(1000000));
        }
        [Test]
        public void SchedulerSkipsTheControlledTurnAndReturnsTheNextNormalTargetTurn()
        {
            var actor = Unit("player", CombatTeam.Player); var target = Unit("enemy", CombatTeam.Enemy);
            target.SetSpirit(100, 50); target.HealthRecoveryPerOwnTurn = 5; target.SpiritRecoveryPerOwnTurn = 7;
            var session = Ready(actor, target, Project(Stun)); Commit(session, Command(actor, target, Stun));
            int health = target.CurrentHealth;
            for (int index = 0; index < 250 && target.OwnTurnsStarted == 0; index++)
            {
                Commit(session, new CombatCommand(CombatCommandKind.EndTurn, session.CurrentActorId)); service.AdvanceUntilAction(session);
            }
            Assert.That(target.OwnTurnsStarted, Is.EqualTo(1)); Assert.That(target.OwnTurnsEnded, Is.EqualTo(1));
            Assert.That(target.ActionPointsRemainingUnits, Is.Zero); Assert.That(target.CurrentHealth, Is.EqualTo(health + 5));
            Assert.That(target.CurrentSpirit, Is.EqualTo(57)); Assert.That(target.Statuses, Is.Empty);
            for (int index = 0; index < 250 && session.CurrentActorId != target.Id; index++)
            {
                Commit(session, new CombatCommand(CombatCommandKind.EndTurn, session.CurrentActorId)); service.AdvanceUntilAction(session);
            }
            Assert.That(session.CurrentActorId, Is.EqualTo(target.Id)); Assert.That(target.ActionPointsRemainingUnits, Is.EqualTo(1000000));
        }
        [Test]
        public void FiniteImmunityBlocksOnlyStunAndNeverRefundsDamageOrCostsOrExtendsTheDeadline()
        {
            var actor = Unit("player", CombatTeam.Player); var target = Unit("enemy", CombatTeam.Enemy);
            var session = Ready(actor, target, Project(Stun)); target.ApplyStatus(CombatStatusKind.Xuantong, 1, sourceRef: "explicit-immunity");
            target.ApplyStatus(CombatStatusKind.Xuantong, 5, sourceRef: "other");
            Commit(session, Command(actor, target, Stun)); Assert.That(target.CurrentHealth, Is.LessThan(99900));
            Assert.That(actor.ActionPointsRemainingUnits, Is.EqualTo(980000)); Assert.That(actor.GetCooldown(Stun), Is.EqualTo(12));
            Assert.That(target.Statuses.Single().Kind, Is.EqualTo(CombatStatusKind.Xuantong));
            Assert.That(target.Statuses[0].RemainingOwnTurnEnds, Is.EqualTo(1));
            Assert.That(target.Statuses[0].Polarity, Is.EqualTo(CombatStatusPolarity.BUFF));
            Call(target, "BeginOwnTurn"); Call(target, "EndOwnTurn"); Assert.That(target.Statuses, Is.Empty);
            target.GongFaId = "南华玄感录"; actor.AdvanceCooldowns(12);
            Commit(session, Command(actor, target, Stun)); Assert.That(target.HasStatus(CombatStatusKind.Stun), Is.True);
        }
        [Test]
        public void DeathAndBattleEndRemoveAllTemporaryRecords()
        {
            var actor = Unit("player", CombatTeam.Player); var target = Unit("enemy", CombatTeam.Enemy);
            var session = Ready(actor, target, Project(Up), Project(Stun)); Commit(session, Command(actor, actor, Up));
            target.ApplyStatus(CombatStatusKind.Xuantong, 2, sourceRef: "explicit"); target.ReceiveDamage(100000);
            Assert.That(target.Statuses, Is.Empty); service.AdvanceUntilAction(session); Assert.That(actor.Statuses, Is.Empty);
        }
        [Test]
        public void ProductionSpawnerProjectsBothRawMindInputsWithoutUsingDerivedAttacks()
        {
            var definition = Object.Instantiate(AssetDatabase.LoadAssetAtPath<CharacterData>("Assets/Data/Characters/Char_Player.asset"));
            temporary.Add(definition); definition.mind = 17;
            var player = CharacterRuntimeProfile.FromDefinition("player", definition, Rules).Capture();
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>("Assets/Data/ContentCatalog/ContentCatalog.asset");
            var go = new GameObject("RawMindProof"); temporary.Add(go);
            Assert.That(go.AddComponent<AdventureUnitSpawner>().TrySpawn(player, catalog, new AdventureNodeData { q = 0, r = 0 },
                new AdventureNodeData { q = 1, r = 0, contentId = "enemy_shijiahou" }, out var spawned, out string reason), Is.True, reason);
            Assert.That(spawned.Player.RawMind, Is.EqualTo(17)); Assert.That(spawned.Player.RawMindInputsPresent, Is.True);
            Assert.That(spawned.Enemy.RawMind, Is.EqualTo(spawned.EnemyData.combatTemplate.mind)); Assert.That(spawned.Enemy.RawMindInputsPresent, Is.True);
        }
        private sealed class Samples : ICombatResolutionRandomSource
        {
            public int Count; public float Control;
            public float NextPercent() { Count++; return Count % 5 == 0 ? Control : Count % 5 == 1 ? 0 : 100; }
        }
        private sealed class SilentUnits : ICombatUnitPresentationPort
        {
            public void Prepare(IReadOnlyList<CombatUnitPresentationDescriptor> units) { }
            public void Spawn(CombatUnitPresentationDescriptor unit) { }
            public void Present(CombatUnitPresentationEventProjection value) { }
            public void Remove(string id) { }
            public void Clear() { }
        }
        [TestCase(0f, true)] [TestCase(100f, false)]
        public void RealCoordinatorSamplesFifthValueOnlyForLegalDamageAndHonorsImmunity(float control, bool controlled)
        {
            var actor = Unit("player", CombatTeam.Player); var target = Unit("enemy", CombatTeam.Enemy);
            var session = Ready(actor, target, Project(Stun, ("reuseCooldownTicks", "0")));
            var go = new GameObject("CoordinatorRollProof"); temporary.Add(go); var coordinator = go.AddComponent<EncounterCoordinator>();
            typeof(EncounterCoordinator).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(coordinator, session);
            typeof(EncounterCoordinator).GetField("unitPresentation", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(coordinator, new SilentUnits());
            var samples = new Samples { Control = control }; coordinator.SetCombatResolutionRandomSource(samples);
            var command = Command(actor, target, Stun); service.Preview(session, command); Assert.That(samples.Count, Is.Zero);
            var method = typeof(EncounterCoordinator).GetMethod("ExecuteCommand", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(((CombatActionResult)method.Invoke(coordinator, new object[] { Command(actor, actor, Stun), null })).Succeeded, Is.False);
            Assert.That(samples.Count, Is.Zero);
            Assert.That(((CombatActionResult)method.Invoke(coordinator, new object[] { command, null })).Succeeded, Is.True);
            Assert.That(samples.Count, Is.EqualTo(5)); Assert.That(target.HasStatus(CombatStatusKind.Stun), Is.EqualTo(controlled));
            target.ApplyStatus(CombatStatusKind.Xuantong, 1, sourceRef: "explicit");
            if (controlled) { Call(target, "BeginOwnTurn"); Call(target, "EndOwnTurn"); target.ApplyStatus(CombatStatusKind.Xuantong, 1, sourceRef: "explicit2"); }
            int before = target.CurrentHealth;
            Assert.That(((CombatActionResult)method.Invoke(coordinator, new object[] { command, null })).Succeeded, Is.True);
            Assert.That(samples.Count, Is.EqualTo(10)); Assert.That(target.CurrentHealth, Is.LessThan(before));
            Assert.That(target.HasStatus(CombatStatusKind.Stun), Is.False);
        }
        [TestCase("化神",995)] [TestCase("元婴",991)] [TestCase("金丹",982)] [TestCase("筑基",973)] [TestCase("练气",964)]
        public void WaterMarksKeepSourceTierAggregatePeakAndOverwriteOneReduction(string targetRealm,int defense)
        {
            var actor=CombatActionPointKernelTests.PrototypeUnit("player",CombatTeam.Player);
            var target=CombatActionPointKernelTests.PrototypeUnit("enemy",CombatTeam.Enemy,targetRealm,hp:100000);
            var water=CombatActionPointKernelTests.Prototype("WaterDivine");
            var session=new CombatSession(new[] { actor,target },new[] { water,CombatActionPointKernelTests.Reply() },
                new CombatActionPointKernelTests.DirectRange(),CombatActionPointKernelTests.Ordinary(),ruleSet:CombatActionPointKernelTests.Rules());
            Assert.That(service.AdvanceUntilAction(session).ActorId,Is.EqualTo("player"));
            for(int i=0;i<4;i++)
            {
                actor.AdvanceCooldowns(12);
                Commit(session,CombatActionPointKernelTests.PrototypeCommand("water"));
                var mark=target.Statuses.Single(s=>s.Kind==CombatStatusKind.WaterMark);
                Assert.That(mark.StackCount,Is.EqualTo(Math.Min(i+1,3))); Assert.That(mark.AbilityTierId,Is.EqualTo("tier_lianqi"));
                Assert.That(mark.SourceRealmOrder,Is.EqualTo(3)); Assert.That(mark.SourceRef,Is.EqualTo("player:water"));
                Assert.That(actor.Statuses.Count(s=>s.Kind==CombatStatusKind.WaterReduction),Is.EqualTo(1));
                Assert.That(actor.Statuses.Single().DeltaNumerator,Is.EqualTo(300000));
            }
            Assert.That(target.PhysicalDefense,Is.EqualTo(defense));
            var markCopy=(CombatantSnapshot)Call(target,"CopyForResolution");
            Assert.That(markCopy.Statuses.Single(),Is.Not.SameAs(target.Statuses.Single()));
            service.Execute(session,new CombatCommand(CombatCommandKind.EndTurn,actor.Id));
            Assert.That(service.AdvanceUntilAction(session).ActorId,Is.EqualTo("enemy"));
            var hit=new CombatCommand(CombatCommandKind.BasicAttack,"enemy","player","reply",new CombatResolutionRolls(0,100,100,100));
            var miss=new CombatCommand(CombatCommandKind.BasicAttack,"enemy","player","reply",new CombatResolutionRolls(100,100,100,100));
            actor.DodgeRate=100; Commit(session,miss); Assert.That(actor.HasStatus(CombatStatusKind.WaterReduction),Is.True);
            actor.DodgeRate=0; Commit(session,hit); Assert.That(actor.HasStatus(CombatStatusKind.WaterReduction),Is.False);
            int first=session.Combatants.All.Single(a=>a.Id=="player").CurrentHealth;
            Commit(session,hit); Assert.That(actor.CurrentHealth,Is.LessThan(first));
            Assert.That(target.HasStatus(CombatStatusKind.WaterMark),Is.True);
            target.ReceiveDamage(target.CurrentHealth); Assert.That(target.Statuses,Is.Empty);
        }
        [Test]
        public void WaterMissingFifthSampleOrRawMindRejectsTheEntireSpecialRoot()
        {
            var actor=CombatActionPointKernelTests.PrototypeUnit("player",CombatTeam.Player);
            var target=CombatActionPointKernelTests.PrototypeUnit("enemy",CombatTeam.Enemy,hp:100000);
            var session=new CombatSession(new[] { actor,target },new[] { CombatActionPointKernelTests.Prototype("WaterDivine") },
                new CombatActionPointKernelTests.DirectRange(),CombatActionPointKernelTests.Ordinary(),ruleSet:CombatActionPointKernelTests.Rules());
            service.AdvanceUntilAction(session);
            Assert.That(service.Execute(session,CombatActionPointKernelTests.PrototypeCommand("water",control:null)).RejectionReason,Is.EqualTo("combat_control_roll_invalid"));
            Assert.That(actor.Statuses,Is.Empty); Assert.That(target.Statuses,Is.Empty);
            Assert.That(actor.CurrentHealth,Is.EqualTo(10000)); Assert.That(target.CurrentHealth,Is.EqualTo(100000));
            Assert.That(actor.ActionPointsRemainingUnits,Is.EqualTo(1000000)); Assert.That(actor.GetCooldown("water"),Is.Zero);
            target=CombatActionPointKernelTests.PrototypeUnit("enemy",CombatTeam.Enemy,hp:100000,mind:null);
            session=new CombatSession(new[] { actor,target },session.AttackProfiles,new CombatActionPointKernelTests.DirectRange(),
                CombatActionPointKernelTests.Ordinary(),ruleSet:CombatActionPointKernelTests.Rules()); service.AdvanceUntilAction(session);
            Assert.That(service.Preview(session,CombatActionPointKernelTests.PrototypeCommand("water")).Validation.RejectionReason,Is.EqualTo("combat_raw_mind_missing"));
        }
    }
}
