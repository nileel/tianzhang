using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Editor;
using TianZhang.Features.Adventure;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.Spatial;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.EditMode
{
    public sealed class CombatAbilityCapacityTests
    {
        private readonly List<Object> temporary = new List<Object>();
        private readonly CombatCommandService service = new CombatCommandService();
        private static readonly CombatResolutionRolls Rolls = new CombatResolutionRolls(0, 100, 100, 100);
        private const string Guanshiji = "spell_guanshiji", Julingshu = "spell_julingshu";

        [TearDown]
        public void TearDown()
        {
            foreach (Object value in temporary) Object.DestroyImmediate(value);
            temporary.Clear();
        }

        [TestCase(Guanshiji, 79)]
        [TestCase("basic_iron_sword", 65)]
        [TestCase("basic_iron_blade", 82)]
        public void ProductionAttacksUseTheirFixedTierForHighRealmActors(string id, int expected)
        {
            AttackProfileData profile = ProductionProfile(id);
            var actor = Actor(2160, 2160, realm: 6, basic: profile.profileKind == AttackProfileKind.Basic ? id : "basic_iron_sword");
            var target = Target(100, realm: 6);
            CombatSession session = Ready(actor, target, profile);
            Assert.That(session.TryGetProfile(id, out var projected), Is.True);
            Assert.That(projected.RuleMetadata.Tier.PhysicalAttackCapacity, Is.EqualTo(120));
            Assert.That(projected.PhysicalMultiplier, Is.EqualTo(profile.physicalDamageMultiplier));
            Assert.That(Attack(session, profile).Damage.Single().FinalDamage, Is.EqualTo(expected));
            Assert.That(target.CurrentHealth, Is.EqualTo(100000 - expected));
        }

        [TestCase("tier_lianqi", 105, 65)]
        [TestCase("tier_zhuji", 480, 477)]
        [TestCase("tier_jindan", 2160, 2477)]
        public void SameTierAttacksKeepApprovedGrowthBelowTheConfiguredCapacity(string tier, int attack, int expected)
        {
            var profile = ProjectGuanshiji(("abilityTierId", tier),
                ("actionPointCostUnits", tier == "tier_lianqi" ? "50000" : tier == "tier_zhuji" ? "150000" : "500000"),
                ("resourceCost", tier == "tier_lianqi" ? "12" : tier == "tier_zhuji" ? "96" : "960"));
            Assert.That(profile.physicalDamageMultiplier, Is.EqualTo(1.2f).Within(0.00001f), "fixed migration is not multiplied again by higher AP");
            Assert.That(Attack(Ready(Actor(attack, attack), Target(100), profile), profile).Damage.Single().FinalDamage,
                Is.EqualTo(expected));
        }

        [TestCase("physical", 15)]
        [TestCase("magic", 13)]
        [TestCase("hybrid", 28)]
        public void BothAttackChannelsCapTheSourceAndKeepRealTargetDefense(string effect, int expected)
        {
            var profile = ProjectGuanshiji(("effectType", effect));
            var actor = Actor(2160, 119);
            actor.GongFaId = "南华玄感录";
            Assert.That(actor.MindStrengthBonus, Is.EqualTo(3));
            Assert.That(Attack(Ready(actor, Target(1000), profile), profile).Damage.Single().FinalDamage,
                Is.EqualTo(expected), "A is capped in both numerator positions; D=1000 must remain real");
        }

        [Test]
        public void SoulCapacityIsAppliedAfterMindBonusWithoutIntegerOverflow()
        {
            var profile = ProjectGuanshiji(("effectType", "magic"));
            var actor = Actor(0, int.MaxValue);
            actor.GongFaId = "南华玄感录";
            Assert.That(Attack(Ready(actor, Target(100), profile), profile).Damage.Single().FinalDamage, Is.EqualTo(67));
        }

        [TestCase(false, true, 12, 0)]
        [TestCase(false, false, 6, 20)]
        [TestCase(true, true, 25, 0)]
        [TestCase(true, false, 12, 20)]
        public void ProductionJulingshuCapsRawSpiritBeforeEnvironmentAndTargetFactors(bool node, bool self, int recovery, int cost)
        {
            var profile = ProductionProfile(Julingshu);
            var actor = Actor(0, 0, rawSpirit: 100);
            actor.SetSpirit(100, 50);
            var target = Target(0);
            target.SetSpirit(100, 50);
            CombatSession session = Ready(actor, target, profile, node: node);
            CombatActionResult result = Attack(session, profile, self);
            Assert.That(result.SpiritSpent, Is.EqualTo(cost));
            Assert.That(result.SpiritRecoveries.Single().RequestedRecovery, Is.EqualTo(recovery));
            Assert.That(result.SpiritRecoveries.Single().ActualRecovery, Is.EqualTo(recovery));
            Assert.That((self ? actor : target).CurrentSpirit, Is.EqualTo(50 + recovery));
            if (!self) Assert.That(actor.CurrentSpirit, Is.EqualTo(30));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CappedRecoveryStillPaysAndTruncatesOnlyAtTargetRemainingSpirit(bool self)
        {
            var profile = ProductionProfile(Julingshu);
            var actor = Actor(0, 0, rawSpirit: 100);
            var target = Target(0);
            actor.SetSpirit(100, self ? 97 : 50);
            target.SetSpirit(100, 97);
            CombatSession session = Ready(actor, target, profile, node: true);
            CombatActionResult result = Attack(session, profile, self);
            Assert.That(result.SpiritRecoveries.Single().RequestedRecovery, Is.EqualTo(self ? 25 : 12));
            Assert.That(result.SpiritRecoveries.Single().ActualRecovery, Is.EqualTo(3));
            Assert.That(result.SpiritSpent, Is.EqualTo(self ? 0 : 20));
            Assert.That(result.ActionPointSpentUnits, Is.EqualTo(25000));
            Assert.That((self ? actor : target).CurrentSpirit, Is.EqualTo(100));
        }

        [Test]
        public void RepeatedPaymentDoesNotIncreaseDeclaredBudgetOrDamage()
        {
            var profile = ProductionProfile(Guanshiji);
            var session = Ready(Actor(2160, 2160), Target(100), profile);
            var actualCosts = new List<int>();
            for (int index = 0; index < 4; index++)
            {
                CombatActionResult result = Attack(session, profile);
                actualCosts.Add(result.ActionPointSpentUnits);
                Assert.That(result.Damage.Single().FinalDamage, Is.EqualTo(79));
            }
            // Later repetition surcharges are owned by AP2-REPEAT; this test does not freeze that sequence.
            Assert.That(actualCosts[0], Is.EqualTo(50000));
            Assert.That(session.TryGetProfile(Guanshiji, out var projected), Is.True);
            Assert.That(projected.ActionPointCostUnits, Is.EqualTo(50000));
            Assert.That(projected.PhysicalMultiplier, Is.EqualTo(1.2f).Within(0.00001f));
        }

        [Test]
        public void InMemoryTierTableVariantChangesBudgetAndCapacityThroughTheSameAdapterReference()
        {
            var rules = Copy(AssetDatabase.LoadAssetAtPath<CombatRuleSetData>("Assets/Data/CombatRuleSet.asset"));
            var tier = rules.tiers.Single(value => value.abilityTierId == "tier_lianqi");
            tier.actionPointReferenceUnits = 50000;
            tier.spiritBudgetReference = 40;
            tier.physicalAttackCapacity = 60;
            tier.rawSpiritRecoveryCapacity = 10;
            var sources = new[] { "RealmCombatGrowth", "CombatEffectTierRules", "CombatActionEconomyRules", "AttackProfiles" }
                .ToDictionary(name => "src/Assets/DataConfig/" + name + ".csv",
                    name => File.ReadAllBytes(Path.Combine(Application.dataPath, "DataConfig", name + ".csv")));
            sources["src/Assets/DataConfig/CombatEffectTierRules.csv"] = Encoding.UTF8.GetBytes(
                string.Join("\n", new[] { string.Join(",", CombatRuleSet.TierColumns) }
                    .Concat(rules.ExportTiers().Select(row => string.Join(",", row)))) + "\n");
            rules.sourceDigest = CombatRuleSet.ComputeSourceDigest(sources);
            CombatRuleSet parsed = CombatRuleSet.Parse(new[] { CombatRuleSet.RealmColumns }.Concat(rules.ExportRealms()).ToArray(),
                new[] { CombatRuleSet.TierColumns }.Concat(rules.ExportTiers()).ToArray(),
                new[] { CombatRuleSet.EconomyColumns }.Concat(rules.ExportEconomy()).ToArray(), rules.sourceDigest);
            var attack = Copy(ProductionProfile(Guanshiji));
            attack.ruleSetSourceDigest = rules.sourceDigest;
            attack.physicalDamageMultiplier = parsed.ResolveDamageMultiplier(attack.abilityTierId, attack.actionPointCostUnits,
                attack.resourceCost, false, new CombatRuleRatio(attack.efficiencyNumerator, attack.efficiencyDenominator),
                new CombatRuleRatio(attack.damageMigrationNumerator, attack.damageMigrationDenominator),
                new CombatRuleRatio(attack.damageWeightNumerator, attack.damageWeightDenominator), new CombatRuleRatio(0, 1), false);
            Assert.That(attack.physicalDamageMultiplier, Is.EqualTo(0.75f).Within(0.00001f));
            var session = Ready(Actor(2160, 2160, basic: ""), Target(100), attack, rules: rules);
            Assert.That(session.TryGetProfile(Guanshiji, out var projected), Is.True);
            Assert.That(projected.RuleMetadata.SourceDigest, Is.EqualTo(rules.sourceDigest));
            Assert.That(projected.RuleMetadata.Tier.ActionPointReferenceUnits, Is.EqualTo(50000));
            Assert.That(projected.RuleMetadata.Tier.SpiritBudgetReference, Is.EqualTo(40));
            Assert.That(projected.RuleMetadata.Tier.PhysicalAttackCapacity, Is.EqualTo(60));
            Assert.That(Attack(session, attack).Damage.Single().FinalDamage, Is.EqualTo(17));
            var restore = Copy(ProductionProfile(Julingshu));
            restore.ruleSetSourceDigest = rules.sourceDigest;
            var actor = Actor(0, 0, rawSpirit: 100, basic: "");
            actor.SetSpirit(100, 50);
            Assert.That(Attack(Ready(actor, Target(0), restore, rules: rules), restore, true)
                .SpiritRecoveries.Single().RequestedRecovery, Is.EqualTo(5));
        }

        [Test]
        public void FixedHealAmountIsAnExplicitOutputInsteadOfASourceAttribute()
        {
            var profile = Copy(ProductionProfile(Guanshiji));
            profile.effectType = AttackEffectType.Heal;
            profile.effectBudgetMode = "fixed_output";
            profile.physicalDamageMultiplier = 0;
            profile.efficiencyNumerator = profile.efficiencyDenominator = 0;
            profile.damageMigrationNumerator = profile.damageMigrationDenominator = 0;
            profile.damageWeightNumerator = profile.damageWeightDenominator = 0;
            profile.healAmount = 300;
            var target = Target(0);
            target.ReceiveDamage(1000);
            Attack(Ready(Actor(2160, 2160), target, profile), profile);
            Assert.That(target.CurrentHealth, Is.EqualTo(99300));
        }


        [TestCase("basic_body_human_fanren", 30, 7)]
        [TestCase("basic_body_human_lianqi", 120, 65)]
        [TestCase("basic_body_human_zhuji", 500, 417)]
        [TestCase("basic_body_human_jindan", 2400, 2304)]
        [TestCase("basic_body_shijiahou_lianqi", 120, 65)]
        public void ExplicitBodyFixtureFlowsThroughAdapterCommandsAndItsOwnCapacity(string id, int capacity, int damage)
        {
            var lines = AttackProfileDataTests.UnarmedFixtureLines(); var rules = AttackProfileDataTests.UnarmedFixtureRules(lines);
            var data = AttackProfileDataTests.UnarmedFixtureRuleData(rules); temporary.Add(data);
            Assert.That(CombatContentImporter.TryBuildAttackProfileProjection(lines, AttackProfileDataTests.UnarmedKeys,
                out var profiles, out string reason, rules), Is.True, reason); temporary.AddRange(profiles);
            var profile = profiles.Single(row => row.attackProfileId == id);
            var session = Ready(Actor(10000, 0, basic: id, actionPoints: 2000000), Target(100), profile, rules: data, additionalProfiles: profiles);
            Assert.That(session.TryGetProfile(id, out var projected), Is.True);
            Assert.That(projected.RuleMetadata.SourceDigest, Is.EqualTo(rules.SourceDigest));
            Assert.That(projected.RuleMetadata.UnarmedPhysicalAttackCapacity, Is.EqualTo(capacity));
            Assert.That(projected.RuleMetadata.UnarmedFamilyId, Is.EqualTo(profile.unarmedFamilyId));
            Assert.That(projected.RuleMetadata.UnarmedRealmId, Is.EqualTo(profile.unarmedRealmId));
            foreach (int multiplier in new[] { 1000, 1000, 1250, 1500 })
            {
                var command = new CombatCommand(CombatCommandKind.BasicAttack, "actor", "target", id, Rolls);
                var preview = service.Preview(session, command);
                Assert.That(preview.IsLegal, Is.True, preview.Validation.RejectionReason);
                var result = service.Execute(session, command, preview);
                Assert.That(result.ActionPointSpentUnits, Is.EqualTo(preview.ActionPointCostUnits));
                Assert.That(result.Succeeded, Is.True, result.RejectionReason);
                Assert.That(result.ActionPointSpentUnits, Is.EqualTo(CombatRuleSet.CeilRatio(profile.actionPointCostUnits, multiplier, 1000)));
                Assert.That(result.Damage.Single().FinalDamage, Is.EqualTo(damage));
            }
            Assert.That(data.TryValidateIdentity(out reason), Is.True, reason);
        }

        [Test]
        public void BodyParametersStayDataDrivenAndFractionalRepeatFeesCeilOnce()
        {
            var lines = AttackProfileDataTests.UnarmedFixtureLines(); var row = lines[1].Split(','); var fields = lines[0].Split(',');
            row[Array.IndexOf(fields, "actionPointCostUnits")] = row[Array.IndexOf(fields, "minimumNetCostUnits")] = "20001";
            row[Array.IndexOf(fields, "unarmedPhysicalAttackCapacity")] = "60";
            row[Array.IndexOf(fields, "physicalDamageMultiplier")] = "1.1"; lines[1] = string.Join(",", row);
            var rules = AttackProfileDataTests.UnarmedFixtureRules(lines);
            var data = AttackProfileDataTests.UnarmedFixtureRuleData(rules); temporary.Add(data);
            Assert.That(CombatContentImporter.TryBuildAttackProfileProjection(lines, AttackProfileDataTests.UnarmedKeys,
                out var profiles, out string reason, rules), Is.True, reason); temporary.AddRange(profiles);
            var profile = profiles.Single(value => value.unarmedRealmId == "realm_fanren");
            var session = Ready(Actor(10000, 0, basic: profile.attackProfileId), Target(100), profile, rules: data, additionalProfiles: profiles);
            foreach (int cost in new[] { 20001, 20001, 25002, 30002 })
            {
                var command = new CombatCommand(CombatCommandKind.BasicAttack, "actor", "target", profile.attackProfileId, Rolls);
                var preview = service.Preview(session, command);
                Assert.That(preview.ActionPointCostUnits, Is.EqualTo(cost));
                var result = service.Execute(session, command, preview);
                Assert.That(result.Succeeded, Is.True, result.RejectionReason);
                Assert.That(result.ActionPointSpentUnits, Is.EqualTo(cost));
                Assert.That(result.Damage.Single().FinalDamage, Is.EqualTo(25));
            }
        }

        [Test]
        public void BodyFixtureKeepsActualDefenseAndRejectsMissingProfilesMappingsAndDigest()
        {
            var lines = AttackProfileDataTests.UnarmedFixtureLines(); var rules = AttackProfileDataTests.UnarmedFixtureRules(lines);
            var data = AttackProfileDataTests.UnarmedFixtureRuleData(rules); temporary.Add(data);
            Assert.That(CombatContentImporter.TryBuildAttackProfileProjection(lines, AttackProfileDataTests.UnarmedKeys,
                out var profiles, out string reason, rules), Is.True, reason); temporary.AddRange(profiles);
            var profile = profiles.Single(row => row.unarmedRealmId == "realm_fanren");
            var session = Ready(Actor(10000, 0, basic: profile.attackProfileId), Target(1000), profile, rules: data, additionalProfiles: profiles);
            Assert.That(Attack(session, profile).Damage.Single().FinalDamage, Is.EqualTo(1));
            var actor = Actor(10000, 0, basic: profile.attackProfileId); var target = Target(100);
            var spawned = new AdventureSpawnSet(actor, target, null, actor.BasicAttackProfileId, target.BasicAttackProfileId, Array.Empty<string>());
            var environment = AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>("Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset");
            var adapter = new CombatEntryAdapter(CombatActionPointKernelTests.Ordinary(), data);
            Assert.That(adapter.TryCreateSession(spawned, profiles.Take(4).ToArray(), environment, out _, out reason), Is.False);
            Assert.That(reason, Is.EqualTo("attack_profile_unarmed_mapping_incomplete"));
            profile.ruleSetSourceDigest = new string('b', 64);
            Assert.That(adapter.TryCreateSession(spawned, profiles, environment, out _, out reason), Is.False);
            Assert.That(reason, Is.EqualTo("attack_profile_rule_reference_invalid"));
            profile.ruleSetSourceDigest = rules.SourceDigest;
            profile.unarmedPhysicalAttackCapacity = 31;
            Assert.That(adapter.TryCreateSession(spawned, profiles, environment, out _, out reason), Is.False);
            Assert.That(reason, Is.EqualTo("attack_profile_unarmed_mapping_invalid"));
            profile.unarmedPhysicalAttackCapacity = 30;
            var productionAdapter = new CombatEntryAdapter(CombatActionPointKernelTests.Ordinary(), AssetDatabase.LoadAssetAtPath<CombatRuleSetData>("Assets/Data/CombatRuleSet.asset"));
            profile.ruleSetSourceDigest = AssetDatabase.LoadAssetAtPath<CombatRuleSetData>("Assets/Data/CombatRuleSet.asset").sourceDigest;
            Assert.That(productionAdapter.TryCreateSession(spawned, profiles, environment, out _, out reason), Is.False);
            Assert.That(reason, Is.EqualTo("attack_profile_unarmed_mapping_invalid"));
        }

        private T Copy<T>(T value) where T : Object
        {
            T copy = Object.Instantiate(value);
            temporary.Add(copy);
            return copy;
        }

        private static AttackProfileData ProductionProfile(string id) =>
            AssetDatabase.LoadAssetAtPath<AttackProfileData>("Assets/Data/AttackProfiles/AttackProfile_" + id + ".asset");

        private AttackProfileData ProjectGuanshiji(params (string field, string value)[] changes)
        {
            string[] lines = File.ReadAllLines(Path.Combine(Application.dataPath, "DataConfig", "AttackProfiles.csv"));
            string header = lines.First(line => line.StartsWith("attackProfileId,", StringComparison.Ordinal));
            string[] fields = header.Split(',');
            string[] row = lines.Single(line => line.StartsWith(Guanshiji + ",", StringComparison.Ordinal)).Split(',');
            foreach (var change in changes) row[Array.IndexOf(fields, change.field)] = change.value;
            Assert.That(CombatContentImporter.TryBuildAttackProfileProjection(new[] { header, string.Join(",", row) },
                new[] { Guanshiji }, out var profiles, out string reason), Is.True, reason);
            temporary.AddRange(profiles);
            return profiles.Single();
        }

        private static CombatantSnapshot Actor(int physical, int soul, int rawSpirit = 25, float realm = 1, string basic = "basic_iron_sword", int actionPoints = 1000000)
        {
            var actor = new CombatantSnapshot("actor", CombatTeam.Player, new HexCoord(0, 0), 100, 100000, 100000,
                physical, soul, 100, 100, actionPoints, 4000, realmMultiplier: realm,
                basicAttackProfileId: basic, equippedArtProfileIds: new[] { Guanshiji, Julingshu },
                availableArtProfileIds: new[] { Guanshiji, Julingshu }, rawSpirit: rawSpirit,
                realmStage: "金丹", visibleRootElement: "土", visibleRootGrade: "下品");
            actor.SetSpirit(100000, 100000);
            return actor;
        }

        private static CombatantSnapshot Target(int defense, float realm = 1) => new CombatantSnapshot(
            "target", CombatTeam.Enemy, new HexCoord(1, 0), 1, 100000, 100000, 0, 0, defense, defense,
            100000, 4000, realmMultiplier: realm) { Facing = 3 };

        private CombatSession Ready(CombatantSnapshot actor, CombatantSnapshot target, AttackProfileData profile,
            bool node = false, CombatRuleSetData rules = null, AttackProfileData[] additionalProfiles = null)
        {
            var environment = Copy(AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>(
                "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset"));
            environment.spiritRecoveryEnvironment = node ? SpiritRecoveryEnvironment.SpiritVeinNode : SpiritRecoveryEnvironment.Ordinary;
            var adapter = new CombatEntryAdapter(CombatActionPointKernelTests.Ordinary(), rules ??
                AssetDatabase.LoadAssetAtPath<CombatRuleSetData>("Assets/Data/CombatRuleSet.asset"));
            var profiles = additionalProfiles == null ? new List<AttackProfileData> { profile } : new List<AttackProfileData>(additionalProfiles);
            foreach (string basicId in new[] { actor.BasicAttackProfileId,target.BasicAttackProfileId }.Where(id => !string.IsNullOrEmpty(id)).Distinct())
                if (profiles.All(p => p.attackProfileId != basicId)) profiles.Add(ProductionProfile(basicId));
            Assert.That(adapter.TryCreateSession(new AdventureSpawnSet(actor, target, null, actor.BasicAttackProfileId,
                target.BasicAttackProfileId, Array.Empty<string>()), profiles.ToArray(), environment, out var session, out string reason), Is.True, reason);
            Assert.That(service.AdvanceUntilAction(session).ActorId, Is.EqualTo("actor"));
            return session;
        }

        private CombatActionResult Attack(CombatSession session, AttackProfileData profile, bool self = false)
        {
            var result = service.Execute(session, new CombatCommand(profile.profileKind == AttackProfileKind.Basic ?
                CombatCommandKind.BasicAttack : CombatCommandKind.Art, "actor", self ? "actor" : "target", profile.attackProfileId, Rolls));
            Assert.That(result.Succeeded, Is.True, result.RejectionReason);
            return result;
        }
    }
}
