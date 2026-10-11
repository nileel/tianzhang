using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Spatial;
using TianZhang.Editor;
using TianZhang.Entity;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

using TianZhang.Spatial;

namespace TianZhang.Tests
{
    public class AttackProfileDataTests
    {
        private const string AttackProfileFixtureRelativePath = "Assets/Tests/EditMode/Fixtures/AttackProfiles.fixture.csv";

        private readonly List<AttackProfileData> profiles = new List<AttackProfileData>();

        [TearDown]
        public void DestroyProfiles()
        {
            foreach (var profile in profiles)
                UnityEngine.Object.DestroyImmediate(profile);
            profiles.Clear();
        }

        [Test]
        public void StrictCsvValidationRejectsNonExactRowsWithoutWritingAssets()
        {
            string header = File.ReadAllLines(ProductionAbsolutePath()).First(line => line.StartsWith("attackProfileId,"));
            var valid = new string[header.Split(',').Length];
            void Field(string name, string value) => valid[Array.IndexOf(header.Split(','), name)] = value;
            Field("attackSchemaVersion", "2");
            Field("ruleSetVersion", "AP-GROWTH@0.1"); Field("abilityTierId", "tier_lianqi");
            Field("cooldownClass", "basic_repeat"); Field("effectBudgetMode", "fixed_weapon");
            valid[0] = "basic_test";
            valid[1] = "name_test";
            valid[2] = "basic";
            valid[3] = "main_equipment";
            valid[8] = "physical";
            valid[9] = "element_none";
            valid[10] = "1";
            valid[15] = "none";
            valid[16] = "0";
            valid[17] = "50000";
            valid[18] = "25000";
            valid[19] = "0";
            valid[20] = "0";
            valid[21] = "1";
            valid[22] = "1";
            valid[23] = "single";

            Assert.IsTrue(
                CombatContentImporter.TryValidateAttackProfileCsv(
                    new[] { header, string.Join(",", valid) },
                    new HashSet<string> { "name_test" },
                    out var validReason),
                validReason);
            Assert.IsFalse(
                CombatContentImporter.TryValidateAttackProfileCsv(
                    new[] { header, "basic_test,name_test,basic" },
                    new HashSet<string> { "name_test" },
                    out var invalidReason));
            StringAssert.StartsWith("attack_profile_short_or_extra_row", invalidReason);
        }

        /// <summary>旧单一 cooldownTicks 不得与四个新列并存；38 列旧表头与 42 列并存表头都整批拒绝。</summary>
        [Test]
        public void LegacyCooldownColumnCanNeverCoexistWithTheFourActionCostColumns()
        {
            string header = File.ReadAllLines(ProductionAbsolutePath())
                .First(line => line.StartsWith("attackProfileId,", StringComparison.Ordinal));
            var columns = header.Split(',').ToList();
            Assert.AreEqual(63, columns.Count);
            Assert.AreEqual(17, columns.IndexOf("actionPointCostUnits"));

            string[] legacyColumns = columns.Where(name => name is not (
                "actionPointCostUnits" or "minimumNetCostUnits" or "ctPenalty" or "reuseCooldownTicks"))
                .ToList().ToArray();
            legacyColumns = legacyColumns.Take(17).Concat(new[] { "cooldownTicks" }).Concat(legacyColumns.Skip(17)).ToArray();
            Assert.AreEqual(60, legacyColumns.Length);

            string[] coexistingColumns = columns.Take(21).Concat(new[] { "cooldownTicks" })
                .Concat(columns.Skip(21)).ToArray();
            Assert.AreEqual(64, coexistingColumns.Length);

            foreach (string[] rejected in new[] { legacyColumns, coexistingColumns })
            {
                Assert.IsFalse(
                    CombatContentImporter.TryValidateAttackProfileCsv(
                        new[] { string.Join(",", rejected) }, ProductionLanguageKeys(), out string reason),
                    "coexisting or legacy cooldown column must be rejected");
                StringAssert.StartsWith("attack_profile_header_not_exact", reason);
            }
        }

        [Test]
        public void FixtureRowsProjectExplicitlyWithoutWritingAssets()
        {
            var lines = CurrentFixtureLines();
            var languageKeys = new HashSet<string>(LanguageFixtureKeys(), StringComparer.Ordinal);
            Assert.IsTrue(
                CombatContentImporter.TryBuildAttackProfileProjection(lines, languageKeys, out var projected, out var reason),
                reason);
            try
            {
                Assert.AreEqual(5, projected.Length);
                var byId = projected.ToDictionary(profile => profile.attackProfileId, StringComparer.Ordinal);
                var main = byId["basic_fixture_main"];
                Assert.AreEqual(AttackProfileKind.Basic, main.profileKind);
                Assert.AreEqual(BasicAttackBindingKind.MainEquipment, main.basicBindingKind);
                Assert.AreEqual(AttackEffectType.Physical, main.effectType);
                Assert.AreEqual("element_none", main.damageElementId);
                Assert.AreEqual(1f, main.physicalDamageMultiplier, 0.0001f);
                Assert.AreEqual(AttackResourceKind.None, main.resourceKind);
                Assert.AreEqual(0, main.resourceCost);
                Assert.AreEqual(40000, main.actionPointCostUnits);
                Assert.AreEqual(20000, main.minimumNetCostUnits);
                Assert.AreEqual(0, main.ctPenalty);
                Assert.AreEqual(0, main.reuseCooldownTicks);
                Assert.AreEqual(1, main.minCastRange);
                Assert.AreEqual(1, main.maxCastRange);
                Assert.AreEqual(AttackTargetingMode.Single, main.targetingMode);
                Assert.AreEqual(AttackAreaShapeKind.Unknown, main.areaShapeKind);
                Assert.AreEqual(-1, main.areaFacing);

                var artSingle = byId["art_fixture_single"];
                Assert.AreEqual(AttackProfileKind.Art, artSingle.profileKind);
                Assert.AreEqual(BasicAttackBindingKind.Unknown, artSingle.basicBindingKind);
                Assert.AreEqual("player", artSingle.contentScope);
                Assert.AreEqual("fixture_sect", artSingle.sourceAffiliation);
                Assert.AreEqual("realm_fanren", artSingle.realmRequirementId);
                Assert.AreEqual("element_none", artSingle.elementRequirementId);
                Assert.AreEqual(AttackEffectType.Magic, artSingle.effectType);
                Assert.AreEqual("element_water", artSingle.damageElementId);
                Assert.AreEqual(0f, artSingle.physicalDamageMultiplier, 0.0001f);
                Assert.AreEqual(1.5f, artSingle.soulDamageMultiplier, 0.0001f);
                Assert.AreEqual(AttackResourceKind.Mp, artSingle.resourceKind);
                Assert.AreEqual(8, artSingle.resourceCost);
                Assert.AreEqual(25000, artSingle.actionPointCostUnits);
                Assert.AreEqual(12500, artSingle.minimumNetCostUnits);
                Assert.AreEqual(0, artSingle.ctPenalty);
                Assert.AreEqual(0, artSingle.reuseCooldownTicks);
                Assert.AreEqual(2, artSingle.minCastRange);
                Assert.AreEqual(3, artSingle.maxCastRange);
                Assert.AreEqual(AttackTargetingMode.Single, artSingle.targetingMode);
                Assert.AreEqual(AttackAreaShapeKind.Unknown, artSingle.areaShapeKind);
                Assert.AreEqual(-1, artSingle.areaFacing);

                var circle = byId["art_fixture_circle"];
                Assert.AreEqual(AttackTargetingMode.Area, circle.targetingMode);
                Assert.AreEqual(AttackAreaCenterKind.Caster, circle.areaCenterKind);
                Assert.AreEqual(AttackAreaShapeKind.Circle, circle.areaShapeKind);
                Assert.AreEqual(2, circle.areaRadius);
                Assert.AreEqual(0, circle.areaLength);
                Assert.AreEqual(0, circle.areaFanHalfAngleSteps);
                Assert.AreEqual(-1, circle.areaFacing);
                Assert.AreEqual(1, circle.areaInnerRadius);
                Assert.AreEqual(AttackAreaEffectBlocker.None, circle.areaEffectBlockers);
                Assert.AreEqual(AttackAreaTargetFaction.Enemy, circle.areaAllowedFactions);
                Assert.AreEqual(AttackAreaTargetState.Alive, circle.areaAllowedStates);

                var fan = byId["art_fixture_fan"];
                Assert.AreEqual(AttackAreaCenterKind.TargetCell, fan.areaCenterKind);
                Assert.AreEqual(AttackAreaShapeKind.Fan, fan.areaShapeKind);
                Assert.AreEqual(0, fan.areaRadius);
                Assert.AreEqual(2, fan.areaLength);
                Assert.AreEqual(1, fan.areaFanHalfAngleSteps);
                Assert.AreEqual(1, fan.areaFacing);
                Assert.AreEqual(0, fan.areaInnerRadius);
                // fixture 的 fan 行使用单值阵营（ally）：BattleSim 侧按原 CSV 行逐字
                // Split(',') 读取同一物理行，双值阵营字段（"ally,enemy"）无法在该
                // 读取方式下保持全部列对齐，故按契约允许的子集以 ally 单值承载。
                Assert.AreEqual(AttackAreaTargetFaction.Ally, fan.areaAllowedFactions);

                var line = byId["divine_fixture_line"];
                Assert.AreEqual(AttackProfileKind.Divine, line.profileKind);
                Assert.AreEqual("realm_zhuji", line.realmRequirementId);
                Assert.AreEqual("element_fire", line.elementRequirementId);
                Assert.AreEqual(AttackEffectType.Physical, line.effectType);
                Assert.AreEqual("element_fire", line.damageElementId);
                Assert.AreEqual(0.8f, line.physicalDamageMultiplier, 0.0001f);
                Assert.AreEqual(0f, line.soulDamageMultiplier, 0.0001f);
                Assert.AreEqual(2.5f, line.defensePenetration, 0.0001f);
                Assert.AreEqual(12, line.resourceCost);
                Assert.AreEqual(60000, line.actionPointCostUnits);
                Assert.AreEqual(30000, line.minimumNetCostUnits);
                Assert.AreEqual(0, line.ctPenalty);
                Assert.AreEqual(0, line.reuseCooldownTicks);
                Assert.AreEqual(2, line.minCastRange);
                Assert.AreEqual(4, line.maxCastRange);
                Assert.AreEqual(AttackAreaShapeKind.Line, line.areaShapeKind);
                Assert.AreEqual(3, line.areaLength);
                Assert.AreEqual(0, line.areaFacing);
                Assert.AreEqual(1, line.areaInnerRadius);
                Assert.AreEqual(AttackAreaEffectBlocker.DirectedEdge, line.areaEffectBlockers);
                Assert.IsTrue(line.isDomain);
                Assert.IsFalse(line.isBloodline);
                Assert.AreEqual("special_fixture_divine", line.specialEffectTextKey);
            }
            finally
            {
                foreach (var profile in projected)
                    UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void FixtureRowVariantsShareOnePerColumnMutationFailureWithNoPartialProjection()
        {
            var lines = CurrentFixtureLines();
            var languageKeys = new HashSet<string>(LanguageFixtureKeys(), StringComparer.Ordinal);
            var fixtureLine = lines.First(line => line.StartsWith("art_fixture_single,", System.StringComparison.Ordinal));
            var fixtureColumns = fixtureLine.Split(',');
            var originalBasicLine = lines.First(line => line.StartsWith("basic_fixture_main,", System.StringComparison.Ordinal));
            var basicColumns = originalBasicLine.Split(',');
            var originalLineLine = lines.First(line => line.StartsWith("divine_fixture_line,", System.StringComparison.Ordinal));
            var lineColumns = originalLineLine.Split(',');
            var originalCircleLine = lines.First(line => line.StartsWith("art_fixture_circle,", System.StringComparison.Ordinal));
            var circleColumns = originalCircleLine.Split(',');

            void AssertRejects(string[] mutated, string expectedPrefix)
            {
                var header = lines.First(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'));
                var mutatedLines = lines.Where(line =>
                    !line.StartsWith("art_fixture_single,", System.StringComparison.Ordinal) &&
                    !line.Equals(header, System.StringComparison.Ordinal) &&
                    !line.TrimStart().StartsWith('#')).ToArray();
                var combined = new[] { header }.Concat(mutatedLines).Concat(new[] { string.Join(",", mutated) }).ToArray();
                Assert.IsFalse(
                    CombatContentImporter.TryBuildAttackProfileProjection(combined, languageKeys, out var rejected, out var reason),
                    $"mutated row must fail: {expectedPrefix}");
                StringAssert.StartsWith(expectedPrefix, reason);
                Assert.IsEmpty(rejected);
            }

            // 逐列变异按列名定位：新增四列后旧列号会整体后移，用名字避免再次隐性错位。
            string[] header = lines.First(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#')).Split(',');
            void Set(string[] columns, string field, string value) => columns[Array.IndexOf(header, field)] = value;

            var duplicateId = (string[])fixtureColumns.Clone();
            Set(duplicateId, "attackProfileId", "basic_fixture_main");
            AssertRejects(duplicateId, "attack_profile_id_duplicate");

            var unknownRealm = (string[])fixtureColumns.Clone();
            Set(unknownRealm, "realmRequirementId", "realm_unknown");
            AssertRejects(unknownRealm, "attack_profile_requirement_reference_unknown");

            var unknownElement = (string[])fixtureColumns.Clone();
            Set(unknownElement, "elementRequirementId", "element_unknown");
            AssertRejects(unknownElement, "attack_profile_requirement_reference_unknown");

            var unknownDisplay = (string[])fixtureColumns.Clone();
            Set(unknownDisplay, "displayNameKey", "name_unknown");
            AssertRejects(unknownDisplay, "attack_profile_display_key_unknown");

            var negativeCost = (string[])fixtureColumns.Clone();
            Set(negativeCost, "resourceCost", "-1");
            AssertRejects(negativeCost, "attack_profile_resourceCost_invalid");

            var reversedRange = (string[])fixtureColumns.Clone();
            Set(reversedRange, "minCastRange", "4");
            Set(reversedRange, "maxCastRange", "2");
            AssertRejects(reversedRange, "attack_profile_cast_range_invalid");

            var nonNumericMult = (string[])fixtureColumns.Clone();
            Set(nonNumericMult, "soulDamageMultiplier", "abc");
            AssertRejects(nonNumericMult, "attack_profile_soulDamageMultiplier_must_be_empty");

            var wrongKindFields = (string[])basicColumns.Clone();
            Set(wrongKindFields, "contentScope", "player");
            AssertRejects(wrongKindFields, "attack_profile_contentScope_must_be_empty");

            var badCircleShape = (string[])circleColumns.Clone();
            Set(badCircleShape, "areaFacing", "east");
            AssertRejects(badCircleShape, "attack_profile_areaFacing_must_be_empty");

            var badLineShape = (string[])lineColumns.Clone();
            Set(badLineShape, "areaRadius", "1");
            AssertRejects(badLineShape, "area_shape_contract_invalid");

            var badFanHalfAngle = (string[])circleColumns.Clone();
            Set(badFanHalfAngle, "areaRadius", "0");
            Set(badFanHalfAngle, "areaFanHalfAngleSteps", "2");
            Set(badFanHalfAngle, "areaFacing", "east");
            Set(badFanHalfAngle, "areaShapeKind", "fan");
            AssertRejects(badFanHalfAngle, "area_shape_contract_invalid");

            var unknownFacing = (string[])lineColumns.Clone();
            Set(unknownFacing, "areaFacing", "north");
            AssertRejects(unknownFacing, "attack_profile_area_facing_invalid");

            var unknownShape = (string[])circleColumns.Clone();
            Set(unknownShape, "areaShapeKind", "hex");
            AssertRejects(unknownShape, "attack_profile_area_shape_invalid");

            var singleWithAreaColumn = (string[])fixtureColumns.Clone();
            Set(singleWithAreaColumn, "areaCenterKind", "caster");
            AssertRejects(singleWithAreaColumn, "attack_profile_areaCenterKind_must_be_empty");

            var unknownBlocker = (string[])circleColumns.Clone();
            Set(unknownBlocker, "areaEffectBlockers", "sight_blocked");
            AssertRejects(unknownBlocker, "attack_profile_area_blocker_invalid");
        }

        /// <summary>
        /// 四个统一行动力字段逐列失败关闭：缺列、零／负主动费、净费下限越界、
        /// 分数与 32 位溢出，以及 CT 上界越界。任一变体都不产生部分投影。
        /// </summary>
        [TestCase("actionPointCostUnits", "", "attack_profile_required_actionPointCostUnits_missing")]
        [TestCase("actionPointCostUnits", "0", "attack_profile_actionPointCostUnits_invalid")]
        [TestCase("actionPointCostUnits", "-1", "attack_profile_actionPointCostUnits_invalid")]
        [TestCase("actionPointCostUnits", "2.5", "attack_profile_actionPointCostUnits_invalid")]
        [TestCase("actionPointCostUnits", "2147483648", "attack_profile_actionPointCostUnits_invalid")]
        [TestCase("minimumNetCostUnits", "", "attack_profile_required_minimumNetCostUnits_missing")]
        [TestCase("minimumNetCostUnits", "0", "attack_profile_minimumNetCostUnits_invalid")]
        [TestCase("minimumNetCostUnits", "-1", "attack_profile_minimumNetCostUnits_invalid")]
        [TestCase("minimumNetCostUnits", "25001", "attack_profile_minimumNetCostUnits_exceeds_actionPointCostUnits")]
        [TestCase("ctPenalty", "", "attack_profile_required_ctPenalty_missing")]
        [TestCase("ctPenalty", "-1", "attack_profile_ctPenalty_invalid")]
        [TestCase("ctPenalty", "1.5", "attack_profile_ctPenalty_invalid")]
        [TestCase("ctPenalty", "2147483548", "attack_profile_ctPenalty_invalid")]
        [TestCase("ctPenalty", "2147483648", "attack_profile_ctPenalty_invalid")]
        [TestCase("reuseCooldownTicks", "-1", "attack_profile_reuseCooldownTicks_invalid")]
        [TestCase("reuseCooldownTicks", "3.5", "attack_profile_reuseCooldownTicks_invalid")]
        [TestCase("reuseCooldownTicks", "4294967296", "attack_profile_reuseCooldownTicks_invalid")]
        public void ActionCostCsvRejectsMissingZeroNegativeFractionalOrOverflowingValues(
            string field,
            string value,
            string expectedReason)
        {
            var lines = CurrentFixtureLines();
            var languageKeys = new HashSet<string>(LanguageFixtureKeys(), StringComparer.Ordinal);
            string header = lines.First(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'));
            string[] columns = header.Split(',');
            var row = lines.First(line => line.StartsWith("art_fixture_single,", StringComparison.Ordinal)).Split(',');
            row[Array.IndexOf(columns, field)] = value;
            Assert.IsFalse(
                CombatContentImporter.TryValidateAttackProfileCsv(
                    new[] { header, string.Join(",", row) }, languageKeys, out string reason),
                $"mutated production contract row must fail: {expectedReason}");
            StringAssert.StartsWith(expectedReason, reason);
        }

        [Test]
        public void CtAndReuseColumnsAreReadIndependentlyPerRow()
        {
            var lines = CurrentFixtureLines();
            var languageKeys = new HashSet<string>(LanguageFixtureKeys(), StringComparer.Ordinal);
            string header = lines.First(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'));
            string[] columns = header.Split(',');
            string single = lines.First(line => line.StartsWith("art_fixture_single,", StringComparison.Ordinal));

            string[] Mutate(string field, string value)
            {
                var row = single.Split(',');
                row[Array.IndexOf(columns, field)] = value;
                return row;
            }

            // 技术输入 7 与 13：只要两列被分别读取，投影就必须分别等于 7 与 13。
            var mutated = Mutate("ctPenalty", "7");
            mutated[Array.IndexOf(columns, "reuseCooldownTicks")] = "13";
            var table = new[] { header }.Concat(lines.Where(line =>
                !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#') &&
                !line.Equals(header, StringComparison.Ordinal) &&
                !line.StartsWith("art_fixture_single,", StringComparison.Ordinal)))
                .Concat(new[] { string.Join(",", mutated) }).ToArray();
            Assert.IsTrue(
                CombatContentImporter.TryBuildAttackProfileProjection(table, languageKeys, out var projected, out string reason),
                reason);
            try
            {
                var profile = projected.Single(item => item.attackProfileId == "art_fixture_single");
                Assert.AreEqual(7, profile.ctPenalty);
                Assert.AreEqual(13, profile.reuseCooldownTicks);
                Assert.AreEqual(25000, profile.actionPointCostUnits);
                Assert.AreEqual(12500, profile.minimumNetCostUnits);
            }
            finally
            {
                foreach (var profile in projected)
                    UnityEngine.Object.DestroyImmediate(profile);
            }

            // 普攻必须显式声明零 CT 与零重用刻：任一非零都整表拒绝。
            foreach (string field in new[] { "ctPenalty", "reuseCooldownTicks" })
            {
                var basic = lines.First(line => line.StartsWith("basic_fixture_main,", StringComparison.Ordinal)).Split(',');
                basic[Array.IndexOf(columns, field)] = "1";
                Assert.IsFalse(
                    CombatContentImporter.TryValidateAttackProfileCsv(
                        new[] { header, string.Join(",", basic) }, languageKeys, out string basicReason),
                    $"basic attack must reject non-zero {field}");
                StringAssert.StartsWith("basic_attack_resource_or_cooldown_invalid", basicReason);
            }
        }

        [Test]
        public void ProjectionFailureLeavesNoPartialAssetAndNoFallbackDefaults()
        {
            var lines = CurrentFixtureLines();
            var languageKeys = new HashSet<string>(LanguageFixtureKeys(), StringComparer.Ordinal);
            var duplicateLines = lines.Concat(new[] { lines.First(line => line.StartsWith("art_fixture_single,", System.StringComparison.Ordinal)) }).ToArray();
            Assert.IsFalse(
                CombatContentImporter.TryBuildAttackProfileProjection(duplicateLines, languageKeys, out var rejected, out _));
            Assert.IsEmpty(rejected);

            var singleRow = new[]
            {
                lines.First(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#')),
                lines.First(line => line.StartsWith("art_fixture_single,", System.StringComparison.Ordinal)),
            };
            Assert.IsTrue(
                CombatContentImporter.TryBuildAttackProfileProjection(singleRow, languageKeys, out var single, out var reason),
                reason);
            try
            {
                Assert.AreEqual(1, single.Length);
                Assert.AreEqual("art_fixture_single", single[0].attackProfileId);
            }
            finally
            {
                foreach (var profile in single)
                    UnityEngine.Object.DestroyImmediate(profile);
            }
        }


        public static string[] UnarmedFixtureLines()
        {
            var production = File.ReadAllLines(ProductionAbsolutePath());
            string header = production.Single(line => line.StartsWith("attackProfileId,", StringComparison.Ordinal));
            var fields = header.Split(',');
            var baseline = production.Single(line => line.StartsWith("basic_iron_sword,", StringComparison.Ordinal)).Split(',');
            var rows = new List<string> { header };
            foreach (var entry in new[] {
                ("body_human", "realm_fanren", "tier_lianqi", 20000, 30),
                ("body_human", "realm_lianqi", "tier_lianqi", 25000, 120),
                ("body_human", "realm_zhuji", "tier_zhuji", 75000, 500),
                ("body_human", "realm_jindan", "tier_jindan", 250000, 2400),
                ("body_shijiahou", "realm_lianqi", "tier_lianqi", 25000, 120) })
            {
                var row = (string[])baseline.Clone();
                void Set(string key, string value) => row[Array.IndexOf(fields, key)] = value;
                Set("attackProfileId", "basic_" + entry.Item1 + "_" + entry.Item2.Substring(6));
                Set("displayNameKey", "attack_profile_" + entry.Item1);
                Set("basicBindingKind", "unarmed_body"); Set("effectBudgetMode", "fixed_unarmed");
                Set("abilityTierId", entry.Item3); Set("unarmedFamilyId", entry.Item1); Set("unarmedRealmId", entry.Item2);
                Set("actionPointCostUnits", entry.Item4.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Set("minimumNetCostUnits", entry.Item4.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Set("unarmedPhysicalAttackCapacity", entry.Item5.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Set("physicalDamageMultiplier", "1"); Set("reuseCooldownTicks", "0");
                rows.Add(string.Join(",", row));
            }
            return rows.ToArray();
        }
        public static string[] UnarmedKeys => new[] { "attack_profile_body_human", "attack_profile_body_shijiahou" };
        public static CombatRuleSet UnarmedFixtureRules(string[] lines)
        {
            var sources = new[] { "RealmCombatGrowth", "CombatEffectTierRules", "CombatActionEconomyRules", "AttackProfiles" }
                .ToDictionary(name => "src/Assets/DataConfig/" + name + ".csv",
                    name => File.ReadAllBytes(Path.Combine(Application.dataPath, "DataConfig", name + ".csv")), StringComparer.Ordinal);
            sources["src/Assets/DataConfig/AttackProfiles.csv"] = System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n");
            string[][] Table(string name) => CombatContentImporter.TokenizeRuleTable(File.ReadAllLines("Assets/DataConfig/" + name + ".csv"));
            return CombatRuleSet.Parse(Table("RealmCombatGrowth"), Table("CombatEffectTierRules"), Table("CombatActionEconomyRules"),
                CombatRuleSet.ComputeSourceDigest(sources)).WithUnarmedRows(CombatRuleSet.ParseUnarmedRows(CombatContentImporter.TokenizeRuleTable(lines)));
        }
        public static TianZhang.Content.CombatRuleSetData UnarmedFixtureRuleData(CombatRuleSet rules)
        {
            var data = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<TianZhang.Content.CombatRuleSetData>("Assets/Data/CombatRuleSet.asset"));
            data.sourceDigest = rules.SourceDigest;
            data.unarmedRows = rules.UnarmedRows.Select(row => new TianZhang.Content.CombatRuleSetData.UnarmedRow
            {
                familyId = row.FamilyId, realmId = row.RealmId, profileId = row.ProfileId, abilityTierId = row.AbilityTierId,
                actionPointCostUnits = row.ActionPointCostUnits, minimumNetCostUnits = row.MinimumNetCostUnits,
                physicalAttackCapacity = row.PhysicalAttackCapacity,
            }).ToArray();
            return data;
        }
        [Test]
        public void FiveExplicitBodyRowsShareTheirSourceDigestAndProductionAtNine()
        {
            var lines = UnarmedFixtureLines(); var rules = UnarmedFixtureRules(lines);
            TestContext.WriteLine("unarmed-config fixture sourceDigest=" + rules.SourceDigest);
            Assert.That(CombatContentImporter.TryBuildAttackProfileProjection(lines, UnarmedKeys, out var projected, out string reason, rules), Is.True, reason);
            profiles.AddRange(projected);
            foreach (var profile in projected)
            {
                var mapping = rules.GetUnarmed(profile.unarmedFamilyId, profile.unarmedRealmId);
                Assert.That(profile.attackSchemaVersion, Is.EqualTo(2));
                Assert.That(profile.ruleSetSourceDigest, Is.EqualTo(rules.SourceDigest));
                Assert.That(profile.actionPointCostUnits, Is.EqualTo(mapping.ActionPointCostUnits));
                Assert.That(profile.minimumNetCostUnits, Is.EqualTo(mapping.MinimumNetCostUnits));
                Assert.That(profile.unarmedPhysicalAttackCapacity, Is.EqualTo(mapping.PhysicalAttackCapacity));
                Assert.That(profile.TryValidate(out reason), Is.True, reason);
            }
            Assert.That(projected.Single(row => row.unarmedRealmId == "realm_fanren").unarmedPhysicalAttackCapacity, Is.EqualTo(30));
            Assert.That(CombatContentImporter.TryReadCombatRuleSet(out var production, out reason), Is.True, reason);
            Assert.That(production.UnarmedRows.Count, Is.EqualTo(5));
            Assert.That(CombatContentImporter.TokenizeRuleTable(File.ReadAllLines(ProductionAbsolutePath())).Length, Is.EqualTo(10));
            Assert.Throws<InvalidDataException>(() => rules.GetUnarmed("body_human", "realm_yuanying"));
            Assert.Throws<InvalidDataException>(() => rules.GetUnarmed("body_shijiahou", "realm_zhuji"));
        }
        [TestCase("attackSchemaVersion", "")]
        [TestCase("attackSchemaVersion", "1")]
        [TestCase("unarmedFamilyId", "")]
        [TestCase("unarmedFamilyId", "body_unknown")]
        [TestCase("unarmedRealmId", "realm_yuanying")]
        [TestCase("attackProfileId", "basic_unarmed")]
        [TestCase("attackProfileId", "basic_body_human_lianqi")]
        [TestCase("abilityTierId", "tier_zhuji")]
        [TestCase("basicBindingKind", "main_equipment")]
        [TestCase("effectBudgetMode", "fixed_weapon")]
        [TestCase("displayNameKey", "attack_profile_body_shijiahou")]
        [TestCase("unarmedPhysicalAttackCapacity", "")]
        [TestCase("unarmedPhysicalAttackCapacity", "0")]
        [TestCase("unarmedPhysicalAttackCapacity", "-1")]
        [TestCase("unarmedPhysicalAttackCapacity", "1.5")]
        [TestCase("unarmedPhysicalAttackCapacity", "1e2")]
        [TestCase("unarmedPhysicalAttackCapacity", "2147483648")]
        [TestCase("actionPointCostUnits", "0")]
        [TestCase("actionPointCostUnits", "1.5")]
        [TestCase("minimumNetCostUnits", "20001")]
        [TestCase("physicalDamageMultiplier", "NaN")]
        [TestCase("physicalDamageMultiplier", "Infinity")]
        [TestCase("physicalDamageMultiplier", "0")]
        [TestCase("defensePenetration", "0")]
        [TestCase("controlChancePermille", "0")]
        [TestCase("controlUsesMindContest", "0")]
        [TestCase("isDomain", "0")]
        [TestCase("statusId", "status_test")]
        [TestCase("efficiencyNumerator", "1")]
        [TestCase("spiritRecoveryMultiplier", "0")]
        [TestCase("soulDamageMultiplier", "0")]
        [TestCase("resourceCost", "1")]
        [TestCase("ctPenalty", "1")]
        [TestCase("reuseCooldownTicks", "")]
        [TestCase("reuseCooldownTicks", "1")]
        [TestCase("minCastRange", "0")]
        [TestCase("sourceAffiliation", "body_human")]
        public void BadBodyRowRejectsTheWholeFixture(string field, string value)
        {
            var lines = UnarmedFixtureLines(); var row = lines[1].Split(',');
            row[Array.IndexOf(lines[0].Split(','), field)] = value; lines[1] = string.Join(",", row);
            Assert.That(CombatContentImporter.TryBuildAttackProfileProjection(lines, UnarmedKeys, out var rejected, out _), Is.False);
            Assert.That(rejected, Is.Empty);
            Assert.Throws<InvalidDataException>(() => CombatRuleSet.ParseUnarmedRows(CombatContentImporter.TokenizeRuleTable(lines)));
        }
        [Test]
        public void MissingDuplicateNonIncreasingAndOldHeaderBodyFixturesFailClosed()
        {
            var lines = UnarmedFixtureLines();
            var variants = new List<string[]> { lines.Take(5).ToArray(), lines.Concat(new[] { lines[1] }).ToArray() };
            foreach (string field in new[] { "actionPointCostUnits", "unarmedPhysicalAttackCapacity" })
            {
                var changed = (string[])lines.Clone(); var row = changed[1].Split(',');
                row[Array.IndexOf(lines[0].Split(','), field)] = field == "actionPointCostUnits" ? "25000" : "120";
                changed[1] = string.Join(",", row); variants.Add(changed);
            }
            variants.Add(lines.Select(line => string.Join(",", line.Split(',').Take(59))).ToArray());
            foreach (var variant in variants)
            {
                Assert.That(CombatContentImporter.TryBuildAttackProfileProjection(variant, UnarmedKeys, out var rejected, out _), Is.False);
                Assert.That(rejected, Is.Empty);
            }
            var production = File.ReadAllLines(ProductionAbsolutePath());
            string header = production.Single(line => line.StartsWith("attackProfileId,"));
            var weapon = production.Single(line => line.StartsWith("basic_iron_sword,")).Split(',');
            weapon[Array.IndexOf(header.Split(','), "unarmedPhysicalAttackCapacity")] = "0";
            Assert.That(CombatContentImporter.TryBuildAttackProfileProjection(new[] { header, string.Join(",", weapon) }, ProductionLanguageKeys(), out _, out _), Is.False);
        }

        // The historical unarmed fixture is a rejection input, never part of the current valid set.
        private static string[] UpgradeHistoricalFixture(string[] lines) => lines.Select(line =>
            string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#") ? line :
            line + (line.StartsWith("attackProfileId,", StringComparison.Ordinal)
                ? ",attackSchemaVersion,unarmedFamilyId,unarmedRealmId,unarmedPhysicalAttackCapacity" : ",2,,,")).ToArray();
        private static string[] CurrentFixtureLines() => UpgradeHistoricalFixture(File.ReadAllLines(FixtureAbsolutePath()))
            .Where(line => !line.StartsWith("basic_fixture_unarmed,", StringComparison.Ordinal)).ToArray();

        [Test]
        public void HistoricalUnarmedFixtureCannotEnterTheImporter()
        {
            Assert.IsFalse(CombatContentImporter.TryBuildAttackProfileProjection(
                UpgradeHistoricalFixture(File.ReadAllLines(FixtureAbsolutePath())), LanguageFixtureKeys(), out var rejected, out var reason));
            Assert.IsEmpty(rejected);
            StringAssert.StartsWith("basic_attack_binding_kind_invalid", reason);
        }

        private static string FixtureAbsolutePath()
        {
            return Path.Combine(Application.dataPath, "..", AttackProfileFixtureRelativePath);
        }

        private static string[] LanguageFixtureKeys()
        {
            return new[]
            {
                "name_fixture_basic_main",
                "name_fixture_basic_unarmed",
                "name_fixture_art_single",
                "name_fixture_art_circle",
                "name_fixture_art_fan",
                "name_fixture_divine_line",
                "special_fixture_divine",
            };
        }

        [Test]
        public void ProductionAttackProfileCsvProjectsExactlyTheNineApprovedProfiles()
        {
            var lines = File.ReadAllLines(ProductionAbsolutePath());
            var languageKeys = new HashSet<string>(ProductionLanguageKeys(), StringComparer.Ordinal);
            Assert.IsTrue(
                CombatContentImporter.TryBuildAttackProfileProjection(lines, languageKeys, out var projected, out var reason),
                reason);
            try
            {
                Assert.AreEqual(9, projected.Length);
                CollectionAssert.AreEquivalent(new[] { "spell_guanshiji", "spell_julingshu", "basic_iron_sword", "basic_iron_blade",
                    "basic_body_human_fanren", "basic_body_human_lianqi", "basic_body_human_zhuji", "basic_body_human_jindan",
                    "basic_body_shijiahou_lianqi" },
                    projected.Select(item => item.attackProfileId).ToArray());
                var byId = projected.ToDictionary(item => item.attackProfileId);
                var strike = byId["spell_guanshiji"];
                Assert.AreEqual(AttackEffectType.Physical, strike.effectType);
                Assert.AreEqual(1.2f, strike.physicalDamageMultiplier);
                Assert.AreEqual("element_none", strike.damageElementId);
                Assert.AreEqual(12, strike.resourceCost);
                Assert.AreEqual(50000, strike.actionPointCostUnits);
                Assert.AreEqual(25000, strike.minimumNetCostUnits);
                Assert.AreEqual(0, strike.ctPenalty);
                Assert.AreEqual(0, strike.reuseCooldownTicks);
                Assert.AreEqual(1, strike.minCastRange);
                Assert.AreEqual(1, strike.maxCastRange);
                var recovery = byId["spell_julingshu"];
                Assert.AreEqual(AttackEffectType.RestoreSpirit, recovery.effectType);
                Assert.AreEqual(0, recovery.resourceCost);
                Assert.AreEqual(25000, recovery.actionPointCostUnits);
                Assert.AreEqual(12500, recovery.minimumNetCostUnits);
                Assert.AreEqual(5, recovery.ctPenalty);
                Assert.AreEqual(16, recovery.reuseCooldownTicks);
                Assert.AreEqual(0, recovery.minCastRange);
                Assert.AreEqual(2, recovery.maxCastRange);
                Assert.AreEqual(0.5f, recovery.spiritRecoveryMultiplier);
                Assert.AreEqual(5, recovery.minimumSpiritRecovery);
                Assert.AreEqual(20, recovery.otherTargetResourceCost);
                Assert.AreEqual(0.5f, recovery.otherTargetRecoveryMultiplier);
                foreach (var art in new[] { strike, recovery })
                {
                    Assert.AreEqual(AttackProfileKind.Art, art.profileKind);
                    Assert.AreEqual("player", art.contentScope);
                    Assert.AreEqual("faction_sanxiu", art.sourceAffiliation);
                    Assert.AreEqual("realm_lianqi", art.realmRequirementId);
                    Assert.AreEqual("element_none", art.elementRequirementId);
                    Assert.AreEqual(art.attackProfileId, art.displayNameKey);
                    Assert.AreEqual(AttackResourceKind.Mp, art.resourceKind);
                    Assert.AreEqual(AttackTargetingMode.Single, art.targetingMode);
                }
                var profile = byId["basic_iron_sword"];
                Assert.AreEqual("basic_iron_sword", profile.attackProfileId);
                Assert.AreEqual("attack_profile_basic_iron_sword", profile.displayNameKey);
                Assert.AreEqual(AttackProfileKind.Basic, profile.profileKind);
                Assert.AreEqual(BasicAttackBindingKind.MainEquipment, profile.basicBindingKind);
                Assert.AreEqual(AttackEffectType.Physical, profile.effectType);
                Assert.AreEqual("element_none", profile.damageElementId);
                Assert.AreEqual(1f, profile.physicalDamageMultiplier, 0.0001f);
                Assert.AreEqual(0f, profile.soulDamageMultiplier, 0.0001f);
                Assert.AreEqual(AttackResourceKind.None, profile.resourceKind);
                Assert.AreEqual(0, profile.resourceCost);
                Assert.AreEqual(40000, profile.actionPointCostUnits);
                Assert.AreEqual(40000, profile.minimumNetCostUnits);
                Assert.AreEqual(0, profile.ctPenalty);
                Assert.AreEqual(0, profile.reuseCooldownTicks);
                Assert.AreEqual(1, profile.minCastRange);
                Assert.AreEqual(1, profile.maxCastRange);
                Assert.AreEqual(AttackTargetingMode.Single, profile.targetingMode);
                Assert.AreEqual(AttackAreaShapeKind.Unknown, profile.areaShapeKind);
                Assert.AreEqual(-1, profile.areaFacing);
                Assert.IsTrue(string.IsNullOrEmpty(profile.contentScope));
                Assert.IsTrue(string.IsNullOrEmpty(profile.sourceAffiliation));
                Assert.IsTrue(string.IsNullOrEmpty(profile.realmRequirementId));
                Assert.IsTrue(string.IsNullOrEmpty(profile.elementRequirementId));
                Assert.IsTrue(string.IsNullOrEmpty(profile.specialEffectTextKey));
                Assert.IsFalse(profile.isDomain);
                Assert.IsFalse(profile.isBloodline);
                Assert.AreEqual("铁剑普攻", ProductionLanguageValue(profile.displayNameKey), "display key must resolve to 铁剑");
            }
            finally
            {
                foreach (var profile in projected)
                    UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [TestCase("spiritRecoveryMultiplier", "", "attack_profile_required_spiritRecoveryMultiplier_missing")]
        [TestCase("spiritRecoveryMultiplier", "NaN", "attack_profile_spiritRecoveryMultiplier_invalid")]
        [TestCase("spiritRecoveryMultiplier", "Infinity", "attack_profile_spiritRecoveryMultiplier_invalid")]
        [TestCase("spiritRecoveryMultiplier", "0", "spirit_recovery_profile_contract_invalid")]
        [TestCase("minimumSpiritRecovery", "", "attack_profile_required_minimumSpiritRecovery_missing")]
        [TestCase("minimumSpiritRecovery", "-1", "attack_profile_minimumSpiritRecovery_invalid")]
        [TestCase("otherTargetResourceCost", "", "attack_profile_required_otherTargetResourceCost_missing")]
        [TestCase("otherTargetResourceCost", "-1", "attack_profile_otherTargetResourceCost_invalid")]
        [TestCase("otherTargetRecoveryMultiplier", "", "attack_profile_required_otherTargetRecoveryMultiplier_missing")]
        [TestCase("otherTargetRecoveryMultiplier", "NaN", "attack_profile_otherTargetRecoveryMultiplier_invalid")]
        [TestCase("otherTargetRecoveryMultiplier", "0", "spirit_recovery_profile_contract_invalid")]
        [TestCase("otherTargetRecoveryMultiplier", "2", "spirit_recovery_profile_contract_invalid")]
        [TestCase("healAmount", "5", "attack_profile_healAmount_must_be_empty")]
        public void RecoveryCsvRejectsMissingMalformedOrForeignEffectFields(string field, string value, string expectedReason)
        {
            var lines = File.ReadAllLines(ProductionAbsolutePath());
            string header = lines.First(line => line.StartsWith("attackProfileId,", StringComparison.Ordinal));
            var row = lines.First(line => line.StartsWith("spell_julingshu,", StringComparison.Ordinal)).Split(',');
            row[Array.IndexOf(header.Split(','), field)] = value;
            Assert.IsFalse(CombatContentImporter.TryBuildAttackProfileProjection(new[] { header, string.Join(",", row) },
                ProductionLanguageKeys(), out var projected, out string reason));
            Assert.IsEmpty(projected);
            StringAssert.StartsWith(expectedReason, reason);
        }

        [TestCase("spiritRecoveryMultiplier")]
        [TestCase("minimumSpiritRecovery")]
        [TestCase("otherTargetResourceCost")]
        [TestCase("otherTargetRecoveryMultiplier")]
        public void NonRecoveryCsvRequiresEmptyRecoveryColumnsEvenForZero(string field)
        {
            var lines = File.ReadAllLines(ProductionAbsolutePath());
            string header = lines.First(line => line.StartsWith("attackProfileId,", StringComparison.Ordinal));
            var row = lines.First(line => line.StartsWith("basic_iron_sword,", StringComparison.Ordinal)).Split(',');
            row[Array.IndexOf(header.Split(','), field)] = "0";
            Assert.IsFalse(CombatContentImporter.TryValidateAttackProfileCsv(new[] { header, string.Join(",", row) },
                ProductionLanguageKeys(), out string reason));
            StringAssert.StartsWith("attack_profile_" + field + "_must_be_empty", reason);
        }

        [Test]
        public void ProductionWeaponRowFailsClosedOnIllegalMutations()
        {
            var lines = File.ReadAllLines(ProductionAbsolutePath());
            var languageKeys = new HashSet<string>(ProductionLanguageKeys(), StringComparer.Ordinal);
            string header = lines.First(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'));
            string row = lines.First(line => line.StartsWith("basic_iron_sword,", System.StringComparison.Ordinal));
            string[] columns = header.Split(',');

            string Mutate(string field, string value, string source = null)
            {
                var mutated = (source ?? row).Split(',');
                mutated[Array.IndexOf(columns, field)] = value;
                return string.Join(",", mutated);
            }

            void AssertRejects(string mutated, string expectedPrefix)
            {
                Assert.IsFalse(
                    CombatContentImporter.TryValidateAttackProfileCsv(new[] { header, mutated }, languageKeys, out var reason),
                    $"mutated production row must fail: {expectedPrefix}");
                StringAssert.StartsWith(expectedPrefix, reason);
            }

            AssertRejects(Mutate("displayNameKey", "name_unknown"), "attack_profile_display_key_unknown");
            AssertRejects(Mutate("basicBindingKind", "weapon"), "basic_attack_binding_kind_invalid");
            AssertRejects(Mutate("profileKind", "art"), "attack_profile_basicBindingKind_must_be_empty");
            // 种类改为 heal：清掉仅物理行适用的倍率与元素列后，heal 仍要求治疗量 → 失败关闭。
            string healRow = Mutate("effectType", "heal");
            healRow = Mutate("physicalDamageMultiplier", "", healRow);
            healRow = Mutate("damageElementId", "", healRow);
            AssertRejects(healRow, "attack_profile_required_healAmount_missing");
            AssertRejects(Mutate("damageElementId", "element_unknown"), "attack_profile_damage_element_unknown");
            AssertRejects(Mutate("physicalDamageMultiplier", "abc"), "attack_profile_physicalDamageMultiplier_invalid");
            AssertRejects(Mutate("resourceCost", "5"), "basic_attack_resource_or_cooldown_invalid");
            AssertRejects(Mutate("ctPenalty", "2"), "basic_attack_resource_or_cooldown_invalid");
            AssertRejects(Mutate("reuseCooldownTicks", "2"), "basic_attack_resource_or_cooldown_invalid");
            AssertRejects(Mutate("actionPointCostUnits", "0"), "attack_profile_actionPointCostUnits_invalid");
            AssertRejects(Mutate("minimumNetCostUnits", "50001"),
                "attack_profile_minimumNetCostUnits_exceeds_actionPointCostUnits");
            AssertRejects(Mutate("minCastRange", "3"), "attack_profile_cast_range_invalid");
            AssertRejects(Mutate("areaCenterKind", "caster"), "attack_profile_areaCenterKind_must_be_empty");

            Assert.IsFalse(
                CombatContentImporter.TryValidateAttackProfileCsv(
                    new[] { header, row, row }, languageKeys, out var duplicateReason),
                "duplicated production row must fail");
            StringAssert.StartsWith("attack_profile_id_duplicate", duplicateReason);
        }

        [Test]
        public void ProductionImportWritesCanonicalWeaponAssetAndValidatesBindings()
        {
            // 真实导入：整表、语言键、asset ID 与绑定种类全部合法时生成/更新规范路径 asset。
            CombatContentImporter.ImportAttackProfiles();

            const string assetPath = "Assets/Data/AttackProfiles/AttackProfile_basic_iron_sword.asset";
            var asset = AssetDatabase.LoadAssetAtPath<AttackProfileData>(assetPath);
            Assert.IsNotNull(asset, "production import must generate the canonical basic_iron_sword asset");
            Assert.AreEqual("basic_iron_sword", asset.attackProfileId);
            Assert.AreEqual("attack_profile_basic_iron_sword", asset.displayNameKey);
            Assert.AreEqual(AttackProfileKind.Basic, asset.profileKind);
            Assert.AreEqual(BasicAttackBindingKind.MainEquipment, asset.basicBindingKind);
            Assert.AreEqual(AttackEffectType.Physical, asset.effectType);
            Assert.AreEqual("element_none", asset.damageElementId);
            Assert.AreEqual(1f, asset.physicalDamageMultiplier, 0.0001f);
            Assert.AreEqual(AttackResourceKind.None, asset.resourceKind);
            Assert.AreEqual(0, asset.resourceCost);
            Assert.AreEqual(40000, asset.actionPointCostUnits);
            Assert.AreEqual(40000, asset.minimumNetCostUnits);
            Assert.AreEqual(0, asset.ctPenalty);
            Assert.AreEqual(0, asset.reuseCooldownTicks);
            Assert.AreEqual(1, asset.minCastRange);
            Assert.AreEqual(1, asset.maxCastRange);
            Assert.AreEqual(AttackTargetingMode.Single, asset.targetingMode);
            Assert.IsTrue(asset.TryValidate(out var validationReason), validationReason);
            foreach (string id in new[] { "spell_guanshiji", "spell_julingshu" })
            {
                var art = AssetDatabase.LoadAssetAtPath<AttackProfileData>($"Assets/Data/AttackProfiles/AttackProfile_{id}.asset");
                Assert.IsNotNull(art, id);
                Assert.AreEqual(id, art.attackProfileId);
                Assert.AreEqual(id, art.displayNameKey);
                Assert.IsTrue(art.TryValidate(out string artReason), artReason);
                if (id == "spell_julingshu")
                {
                    Assert.AreEqual(AttackEffectType.RestoreSpirit, art.effectType);
                    Assert.AreEqual(0.5f, art.spiritRecoveryMultiplier);
                    Assert.AreEqual(5, art.minimumSpiritRecovery);
                    Assert.AreEqual(20, art.otherTargetResourceCost);
                    Assert.AreEqual(0.5f, art.otherTargetRecoveryMultiplier);
                    Assert.AreEqual(25000, art.actionPointCostUnits);
                    Assert.AreEqual(12500, art.minimumNetCostUnits);
                    Assert.AreEqual(5, art.ctPenalty);
                    Assert.AreEqual(16, art.reuseCooldownTicks);
                }
                else
                {
                    Assert.AreEqual(AttackEffectType.Physical, art.effectType);
                    Assert.AreEqual(1.2f, art.physicalDamageMultiplier);
                    Assert.AreEqual(0f, art.spiritRecoveryMultiplier);
                    Assert.AreEqual(0, art.minimumSpiritRecovery);
                    Assert.AreEqual(0, art.otherTargetResourceCost);
                    Assert.AreEqual(0f, art.otherTargetRecoveryMultiplier);
                    Assert.AreEqual(50000, art.actionPointCostUnits);
                    Assert.AreEqual(25000, art.minimumNetCostUnits);
                    Assert.AreEqual(0, art.ctPenalty);
                    Assert.AreEqual(0, art.reuseCooldownTicks);
                }
            }
            Assert.IsTrue(
                CombatContentImporter.TryValidateAttackProfileAssetIdProjection(
                    new[] { "spell_guanshiji", "spell_julingshu", "basic_iron_sword", "basic_iron_blade" }, out var idReason),
                idReason);

            // 石甲兽模板 asset 显式引用同一档案：导入绑定校验必须通过（ImportAttackProfiles 已成功）。
            var shijiahou = AssetDatabase.LoadAssetAtPath<CharacterData>(
                "Assets/Data/Characters/Char_Enemy_enemy_shijiahou.asset");
            Assert.IsNotNull(shijiahou);
            Assert.IsTrue(string.IsNullOrEmpty(shijiahou.unarmedBasicAttackProfileId));
            Assert.IsTrue(string.IsNullOrEmpty(shijiahou.mainEquipmentBasicAttackProfileId));
        }

        private static string ProductionAbsolutePath()
        {
            return Path.Combine(Application.dataPath, "DataConfig", "AttackProfiles.csv");
        }

        private static readonly string[] ProductionAttackProfileAssetPaths =
        {
            "Assets/Data/AttackProfiles/AttackProfile_spell_guanshiji.asset",
            "Assets/Data/AttackProfiles/AttackProfile_spell_julingshu.asset",
            "Assets/Data/AttackProfiles/AttackProfile_basic_iron_sword.asset",
            "Assets/Data/AttackProfiles/AttackProfile_basic_iron_blade.asset",
        };

        /// <summary>
        /// 非法整表必须在任何 asset 写入前拒绝：末行净费下限越界后导入报错，
        /// 三个同 ID asset 的字节与 GUID 全部不变。
        /// </summary>
        [Test]
        public void InvalidActionCostTableWritesNoAssetAndKeepsEveryGuid()
        {
            string csvPath = ProductionAbsolutePath();
            byte[] originalCsv = File.ReadAllBytes(csvPath);
            var guidsBefore = ProductionAttackProfileAssetPaths.ToDictionary(
                path => path, AssetDatabase.AssetPathToGUID, StringComparer.Ordinal);
            var bytesBefore = ProductionAttackProfileAssetPaths.ToDictionary(
                path => path, path => File.ReadAllBytes(ToAbsoluteAssetPath(path)), StringComparer.Ordinal);
            try
            {
                var lines = File.ReadAllLines(csvPath).ToList();
                int lastRow = lines.FindLastIndex(line => line.StartsWith("spell_julingshu,", StringComparison.Ordinal));
                Assert.That(lastRow, Is.GreaterThan(0));
                string[] columns = lines
                    .First(line => line.StartsWith("attackProfileId,", StringComparison.Ordinal)).Split(',');
                var row = lines[lastRow].Split(',');
                row[Array.IndexOf(columns, "minimumNetCostUnits")] = "99999";
                lines[lastRow] = string.Join(",", row);
                File.WriteAllText(csvPath, string.Join("\n", lines) + "\n");

                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                    "attack_profile_minimumNetCostUnits_exceeds_actionPointCostUnits"));
                CombatContentImporter.ImportAttackProfiles();
            }
            finally
            {
                File.WriteAllBytes(csvPath, originalCsv);
            }

            foreach (string path in ProductionAttackProfileAssetPaths)
            {
                CollectionAssert.AreEqual(bytesBefore[path], File.ReadAllBytes(ToAbsoluteAssetPath(path)), path);
                Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guidsBefore[path]), path);
            }
        }

        /// <summary>重复导入同 ID asset 无语义变化：字节、GUID 与四个冻结算入逐项稳定。</summary>
        [Test]
        public void RepeatedProductionImportKeepsAssetsGuidsAndTheFourFrozenFieldsStable()
        {
            CombatContentImporter.ImportAttackProfiles();
            var guidsFirst = ProductionAttackProfileAssetPaths.ToDictionary(
                path => path, AssetDatabase.AssetPathToGUID, StringComparer.Ordinal);
            var bytesFirst = ProductionAttackProfileAssetPaths.ToDictionary(
                path => path, path => File.ReadAllBytes(ToAbsoluteAssetPath(path)), StringComparer.Ordinal);

            CombatContentImporter.ImportAttackProfiles();

            foreach (string path in ProductionAttackProfileAssetPaths)
            {
                CollectionAssert.AreEqual(bytesFirst[path], File.ReadAllBytes(ToAbsoluteAssetPath(path)), path);
                Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guidsFirst[path]), path);
            }

            var expected = new Dictionary<string, int[]>(StringComparer.Ordinal)
            {
                ["AttackProfile_basic_iron_sword.asset"] = new[] { 40000, 40000, 0, 0 },
                ["AttackProfile_basic_iron_blade.asset"] = new[] { 50000, 50000, 0, 0 },
                ["AttackProfile_spell_guanshiji.asset"] = new[] { 50000, 25000, 0, 0 },
                ["AttackProfile_spell_julingshu.asset"] = new[] { 25000, 12500, 5, 16 },
            };
            foreach (var pair in expected)
            {
                string assetPath = "Assets/Data/AttackProfiles/" + pair.Key;
                var asset = AssetDatabase.LoadAssetAtPath<AttackProfileData>(assetPath);
                Assert.IsNotNull(asset, assetPath);
                Assert.AreEqual(pair.Value[0], asset.actionPointCostUnits, assetPath);
                Assert.AreEqual(pair.Value[1], asset.minimumNetCostUnits, assetPath);
                Assert.AreEqual(pair.Value[2], asset.ctPenalty, assetPath);
                Assert.AreEqual(pair.Value[3], asset.reuseCooldownTicks, assetPath);
                Assert.IsTrue(asset.TryValidate(out string reason), reason);
            }
        }

        private static string ToAbsoluteAssetPath(string assetPath)
        {
            return Path.Combine(Application.dataPath,
                assetPath.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar));
        }

        private static HashSet<string> ProductionLanguageKeys()
        {
            string path = Path.Combine(Application.dataPath, "DataConfig", "Language.csv");
            return new HashSet<string>(
                File.ReadAllLines(path)
                    .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
                    .Select(line => line.Split(',')[0].Trim()),
                StringComparer.Ordinal);
        }

        private static string ProductionLanguageValue(string key)
        {
            string path = Path.Combine(Application.dataPath, "DataConfig", "Language.csv");
            return File.ReadAllLines(path)
                .Where(line => line.StartsWith(key + ",", System.StringComparison.Ordinal))
                .Select(line => line.Substring(line.IndexOf(',') + 1).Trim())
                .FirstOrDefault();
        }

        [Test]
        public void AssetIdConflictOnCanonicalPathFailsClosedWithoutMutation()
        {
            // 非规范路径上存在同 ID asset：同一 attackProfileId 映射到多个 asset 路径必须失败。
            // 规范目录 Assets/Data/AttackProfiles/ 尚无生产 asset，测试在既有 Fixtures 旁创建并清理临时 asset。
            const string strayAssetPath = "Assets/Tests/EditMode/AttackProfile_stray_fixture.asset";
            AssetDatabase.DeleteAsset(strayAssetPath);
            AssetDatabase.Refresh();

            var lines = CurrentFixtureLines();
            var languageKeys = new HashSet<string>(LanguageFixtureKeys(), StringComparer.Ordinal);
            Assert.IsTrue(
                CombatContentImporter.TryBuildAttackProfileProjection(lines, languageKeys, out var projected, out var reason),
                reason);
            foreach (var profile in projected)
                UnityEngine.Object.DestroyImmediate(profile);
            Assert.IsTrue(
                CombatContentImporter.TryValidateAttackProfileAssetIdProjection(
                    new[] { "basic_fixture_main" }, out var cleanReason),
                cleanReason);

            var stray = ScriptableObject.CreateInstance<AttackProfileData>();
            stray.attackProfileId = "basic_fixture_main";
            stray.displayNameKey = "name_fixture_basic_main";
            stray.profileKind = AttackProfileKind.Basic;
            stray.basicBindingKind = BasicAttackBindingKind.MainEquipment;
            stray.effectType = AttackEffectType.Physical;
            stray.damageElementId = "element_none";
            stray.physicalDamageMultiplier = 1f;
            stray.resourceKind = AttackResourceKind.None;
            stray.actionPointCostUnits = 40000;
            stray.minimumNetCostUnits = 20000;
            stray.ctPenalty = 0;
            stray.reuseCooldownTicks = 0;
            stray.minCastRange = 1;
            stray.maxCastRange = 1;
            stray.targetingMode = AttackTargetingMode.Single;
            stray.areaFacing = -1;
            try
            {
                AssetDatabase.CreateAsset(stray, strayAssetPath);
                Assert.IsFalse(
                    CombatContentImporter.TryValidateAttackProfileAssetIdProjection(
                        new[] { "basic_fixture_main" }, out var conflictReason),
                    "same attackProfileId on a non-canonical path must fail the projection check");
                StringAssert.AreEqualIgnoringCase("attack_profile_asset_id_duplicate", conflictReason);
            }
            finally
            {
                AssetDatabase.DeleteAsset(strayAssetPath);
                AssetDatabase.Refresh();
            }
        }

    }
}
