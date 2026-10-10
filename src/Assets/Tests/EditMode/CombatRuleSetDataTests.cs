using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Editor;
using TianZhang.Features.Adventure;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace TianZhang.Tests
{
    public sealed class CombatRuleSetDataTests
    {
        private const string RuleAsset = "Assets/Data/CombatRuleSet.asset";
        private static string[] SourcePaths => new[] { "RealmCombatGrowth", "CombatEffectTierRules", "CombatActionEconomyRules", "AttackProfiles" }
            .Select(name => "Assets/DataConfig/" + name + ".csv").ToArray();
        private static string[] AssetPaths => new[] { RuleAsset, "Assets/Data/ContentCatalog/ContentCatalog.asset" }
            .Concat(new[] { "spell_guanshiji", "spell_julingshu", "basic_iron_sword", "basic_iron_blade" }
                .Select(id => "Assets/Data/AttackProfiles/AttackProfile_" + id + ".asset")).ToArray();
        private static string[][] Table(int index) => CombatContentImporter.TokenizeRuleTable(File.ReadAllLines(SourcePaths[index]));
        private static CombatRuleSet Parse(string[][] realm = null, string[][] tier = null, string[][] economy = null) =>
            CombatRuleSet.Parse(realm ?? Table(0), tier ?? Table(1), economy ?? Table(2), new string('a', 64));
        private static string[][] Mutate(string[][] table, int row, string field, string value)
        {
            var result = table.Select(values => (string[])values.Clone()).ToArray();
            result[row][Array.IndexOf(result[0], field)] = value; return result;
        }
        [Test]
        public void AllFrozenReferencesAndFiftyOneExplicitTestTriplesRoundTrip()
        {
            CombatContentImporter.ImportAttackProfiles();
            var asset = AssetDatabase.LoadAssetAtPath<CombatRuleSetData>(RuleAsset);
            Assert.That(asset.TryValidateIdentity(out string reason), Is.True, reason);
            var projected = CombatRuleSet.Parse(new[] { CombatRuleSet.RealmColumns }.Concat(asset.ExportRealms()).ToArray(),
                new[] { CombatRuleSet.TierColumns }.Concat(asset.ExportTiers()).ToArray(),
                new[] { CombatRuleSet.EconomyColumns }.Concat(asset.ExportEconomy()).ToArray(), asset.sourceDigest);
            Assert.That(projected.Realms.Count, Is.EqualTo(6)); Assert.That(projected.Tiers.Count, Is.EqualTo(3));
            Assert.That(projected.Economy.Count, Is.EqualTo(52));
            foreach (string id in CombatRuleSet.DefinitionIds)
                Assert.That(projected.Economy.Count(row => row.DefinitionRef == id), Is.EqualTo(3), id);
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>("Assets/Data/ContentCatalog/ContentCatalog.asset");
            Assert.That(catalog.TryGetCombatRuleSet(out var linked, out reason), Is.True, reason);
            Assert.That(linked, Is.SameAs(asset));
            foreach (string path in AssetPaths.Skip(2))
            {
                var profile = AssetDatabase.LoadAssetAtPath<AttackProfileData>(path);
                Assert.That(profile.ruleSetVersion, Is.EqualTo(projected.RuleSetVersion));
                Assert.That(profile.ruleSetSourceDigest, Is.EqualTo(projected.SourceDigest));
            }
        }
        [TestCase("realm", "actionPointMaximumUnits", "0")]
        [TestCase("realm", "realmId", "realm_unknown")]
        [TestCase("realm", "movementNodeWeightNumerators", "1|1")]
        [TestCase("realm", "baseMovementEfficiencyMilliUnits", "2.5")]
        [TestCase("tier", "abilityTierId", "tier_unknown")]
        [TestCase("tier", "physicalAttackCapacity", "2147483648")]
        [TestCase("economy", "repeatThirdPermille", "999")]
        [TestCase("economy", "strongControlLimitPermille", "1001")]
        [TestCase("economy", "waterMindDifferenceDivisor", "0")]
        public void BadVersionKeyScalarAndGlobalRuleRejectWholeProjection(string kind, string field, string value)
        {
            Assert.Throws<InvalidDataException>(() => Parse(
                realm: kind == "realm" ? Mutate(Table(0), 2, field, value) : null,
                tier: kind == "tier" ? Mutate(Table(1), 1, field, value) : null,
                economy: kind == "economy" ? Mutate(Table(2), 1, field, value) : null));
        }
        [TestCase("abilityTierId", "tier_unknown")]
        [TestCase("cooldownClass", "unknown")]
        [TestCase("reuseCooldownOverrideTicks", "-1")]
        [TestCase("spiritFactorDenominator", "0")]
        [TestCase("minimumNetCostOverrideUnits", "999999")]
        [TestCase("schemaVersion", "2")]
        public void BadTestActionRejectsWholeProjection(string field, string value) =>
            Assert.Throws<InvalidDataException>(() => Parse(economy: Mutate(Table(2), 2, field, value)));
        [Test]
        public void MissingDuplicateAndUnknownColumnsNeverProject()
        {
            var table = Table(2);
            Assert.Throws<InvalidDataException>(() => Parse(economy: table.Take(table.Length - 1).ToArray()));
            table[table.Length - 1] = (string[])table[2].Clone();
            Assert.Throws<InvalidDataException>(() => Parse(economy: table));
            var unknown = Table(0); unknown[0][2] = "unknown";
            Assert.Throws<InvalidDataException>(() => Parse(realm: unknown));
            Assert.Throws<InvalidDataException>(() => Parse().ResolveCooldown("tier_lianqi", "typo", 0));
            Assert.Throws<InvalidDataException>(() => Parse().ResolveCooldown("tier_missing", "basic_repeat", 0));
        }
        [Test]
        public void ReducedRationalCalculationChecksOverflowAndCeilsOnlyOnce()
        {
            var ratio = new CombatRuleRatio(int.MaxValue, int.MaxValue) * new CombatRuleRatio(int.MaxValue, int.MaxValue);
            Assert.That(ratio.Numerator, Is.EqualTo(1)); Assert.That(ratio.Denominator, Is.EqualTo(1));
            Assert.That(CombatRuleSet.CeilRatio(1, 1, 3), Is.EqualTo(1));
            Assert.Throws<OverflowException>(() => { var unused = new CombatRuleRatio(long.MaxValue, 1) * new CombatRuleRatio(2, 1); });
            var rules = Parse(economy: Mutate(Mutate(Table(2), 2, "actionPointFactorNumerator", "4"), 2, "actionPointFactorDenominator", "2"));
            Assert.That(rules.Economy[1].ActionPointFactorNumerator, Is.EqualTo(2));
            Assert.That(rules.Economy[1].ActionPointFactorDenominator, Is.EqualTo(1));
        }
        [Test]
        public void MissingOrMalformedAssetRejectsFormalEntryWithoutGlobalFallback()
        {
            var adapter = new CombatEntryAdapter(Array.Empty<CombatOrdinaryActionDefinition>(), null);
            Assert.That(adapter.TryCreateSession(null, null, null, out var session, out string reason), Is.False);
            Assert.That(session, Is.Null); Assert.That(reason, Is.EqualTo("combat_rule_set_missing"));
            var copy = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<CombatRuleSetData>(RuleAsset));
            try
            {
                copy.realms[0].movementNodeWeightNumerators = null;
                adapter = new CombatEntryAdapter(Array.Empty<CombatOrdinaryActionDefinition>(), copy);
                Assert.That(adapter.TryCreateSession(null, null, null, out session, out reason), Is.False);
                Assert.That(reason, Is.EqualTo("combat_rule_set_invalid"));
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }
        [Test]
        public void BadSerializedAttackRatioHasAnExplicitValidationRejection()
        {
            var copy = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<AttackProfileData>("Assets/Data/AttackProfiles/AttackProfile_spell_guanshiji.asset"));
            try
            {
                copy.efficiencyDenominator = 0;
                Assert.That(copy.TryValidate(out string reason), Is.False);
                Assert.That(reason, Is.EqualTo("attack_profile_budget_ratio_invalid"));
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }
        [TestCase(0, "baseMovementEfficiencyMilliUnits", "0")]
        [TestCase(1, "abilityTierId", "tier_missing")]
        [TestCase(2, "repeatFirstPermille", "0")]
        [TestCase(3, "controlChancePermille", "1001")]
        [TestCase(3, "cooldownClass", "unknown")]
        public void EveryBadSourceRejectsBeforeAllAssetWritesAndKeepsGuids(int source, string field, string value)
        {
            var bytes = AssetPaths.ToDictionary(path => path, File.ReadAllBytes);
            var guids = AssetPaths.ToDictionary(path => path, AssetDatabase.AssetPathToGUID);
            byte[] original = File.ReadAllBytes(SourcePaths[source]);
            try
            {
                string[] lines = File.ReadAllLines(SourcePaths[source]);
                int header = CsvTableReader.FindHeaderIndex(lines);
                int rowIndex = source == 3 ? Array.FindIndex(lines, line => line.StartsWith("spell_guanshiji,")) : header + 1;
                string[] row = CsvTableReader.ParseRow(lines[rowIndex]);
                row[Array.IndexOf(CsvTableReader.ParseRow(lines[header]), field)] = value;
                lines[rowIndex] = string.Join(",", row); File.WriteAllText(SourcePaths[source], string.Join("\n", lines) + "\n");
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("(combat_rule_|attack_profile_)"));
                CombatContentImporter.ImportAttackProfiles();
            }
            finally { File.WriteAllBytes(SourcePaths[source], original); }
            foreach (string path in AssetPaths)
            {
                CollectionAssert.AreEqual(bytes[path], File.ReadAllBytes(path), path);
                Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guids[path]), path);
            }
        }
        [Test]
        public void OneParameterChangeUpdatesReferencedOutputsAndAllDigestsWithoutChangingGuids()
        {
            string path = SourcePaths[1]; byte[] original = File.ReadAllBytes(path);
            string originalDigest = AssetDatabase.LoadAssetAtPath<CombatRuleSetData>(RuleAsset).sourceDigest;
            var guids = AssetPaths.ToDictionary(value => value, AssetDatabase.AssetPathToGUID);
            try
            {
                var lines = File.ReadAllLines(path); int header = CsvTableReader.FindHeaderIndex(lines);
                var columns = CsvTableReader.ParseRow(lines[header]); var row = CsvTableReader.ParseRow(lines[header + 1]);
                row[Array.IndexOf(columns, "actionPointReferenceUnits")] = "50000";
                row[Array.IndexOf(columns, "referenceTurnIntervalTicks")] = "5";
                lines[header + 1] = string.Join(",", row); File.WriteAllText(path, string.Join("\n", lines) + "\n");
                CombatContentImporter.ImportAttackProfiles();
                var asset = AssetDatabase.LoadAssetAtPath<CombatRuleSetData>(RuleAsset);
                Assert.That(asset.sourceDigest, Is.Not.EqualTo(originalDigest));
                var strike = AssetDatabase.LoadAssetAtPath<AttackProfileData>("Assets/Data/AttackProfiles/AttackProfile_spell_guanshiji.asset");
                Assert.That(strike.physicalDamageMultiplier, Is.EqualTo(.75f).Within(.00001f));
                var recovery = AssetDatabase.LoadAssetAtPath<AttackProfileData>("Assets/Data/AttackProfiles/AttackProfile_spell_julingshu.asset");
                Assert.That(recovery.reuseCooldownTicks, Is.EqualTo(20));
                foreach (string attackPath in AssetPaths.Skip(2))
                    Assert.That(AssetDatabase.LoadAssetAtPath<AttackProfileData>(attackPath).ruleSetSourceDigest, Is.EqualTo(asset.sourceDigest));
                foreach (string assetPath in AssetPaths) Assert.That(AssetDatabase.AssetPathToGUID(assetPath), Is.EqualTo(guids[assetPath]));
                var firstBytes = AssetPaths.ToDictionary(value => value, File.ReadAllBytes);
                CombatContentImporter.ImportAttackProfiles();
                foreach (string assetPath in AssetPaths) CollectionAssert.AreEqual(firstBytes[assetPath], File.ReadAllBytes(assetPath));
            }
            finally { File.WriteAllBytes(path, original); CombatContentImporter.ImportAttackProfiles(); }
        }
    }
}
