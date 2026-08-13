using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Entity;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Tests
{
    public class FoundationPurpleMansionStateDataTests
    {
        private static readonly string[] Columns =
        {
            "schemaId", "schemaVersion", "characterId", "foundationInstanceId", "foundationDefinitionId",
            "sourceGongFaId", "phase", "continuousProgress", "phaseBoundarySetId", "naturalMansionCapacity",
            "releasedNaturalCapacity", "expansionGrants", "expandedMansionCapacity", "totalMansionCapacity",
            "mansionStates", "effectBindings", "guardianAbilities", "enhancementNodes", "cultivationActionState",
            "closedRetreatPlan", "jindanLock", "fixtureId", "expect", "fixtureOnlyNumericProfile",
        };

        private static readonly string Header = string.Join(",", Columns);

        [TestCase("phase1")]
        [TestCase("complete")]
        [TestCase("capacityUpperBound")]
        [TestCase("pausedEmbryo")]
        public void ParseFoundationPurpleMansionStatesAcceptsEverySpecifiedValidFixture(string fixture)
        {
            var states = CultivationContentImporter.ParseFoundationPurpleMansionStates(
                new[] { Header, BuildFixture(fixture) },
                "FoundationPurpleMansionStates.fixture.csv");

            try
            {
                Assert.AreEqual(1, states.Length);
                var state = states[0];
                Assert.AreEqual("foundationPurpleMansionState", state.schemaId);
                Assert.AreEqual(5, state.mansionStates.Length);
                Assert.IsTrue(
                    FoundationPurpleMansionRuntimeState.TryCreate(
                        state,
                        out FoundationPurpleMansionRuntimeState runtimeState,
                        out string failureReason),
                    failureReason);
                Assert.AreEqual(state.foundationState.totalMansionCapacity, runtimeState.TotalMansionCapacity);

                if (fixture == "phase1")
                {
                    Assert.AreEqual(0, state.foundationState.totalMansionCapacity);
                    Assert.IsTrue(state.mansionStates.All(mansion => mansion.state == PurpleMansionBuildState.NotBuilt));
                }
                else if (fixture == "complete")
                {
                    Assert.AreEqual(1, state.foundationState.totalMansionCapacity);
                    Assert.AreEqual(1, state.mansionStates.Count(mansion => mansion.state == PurpleMansionBuildState.Complete));
                    Assert.AreEqual(1, state.guardianAbilities.Length);
                }
                else if (fixture == "capacityUpperBound")
                {
                    Assert.AreEqual(5, state.foundationState.totalMansionCapacity);
                    Assert.AreEqual(5, state.mansionStates.Count(mansion => mansion.state == PurpleMansionBuildState.Complete));
                    Assert.AreEqual(2, state.foundationState.expansionGrants.Length);
                }
                else
                {
                    Assert.AreEqual(CultivationActionStatus.Paused, state.cultivationActionState.status);
                    Assert.AreEqual("embryo_hun", state.closedRetreatPlan.targetRef);
                    Assert.AreEqual(1, state.mansionStates.Count(mansion => mansion.state == PurpleMansionBuildState.Embryo));
                }
            }
            finally
            {
                DestroyAll(states);
            }
        }

        [TestCase("formedOneMansion", 1)]
        [TestCase("formedThreeMansions", 3)]
        public void ParseFoundationPurpleMansionStatesAcceptsFormedSnapshotsWithNotBuiltMansions(
            string fixture,
            int expectedCompleteMansions)
        {
            var states = CultivationContentImporter.ParseFoundationPurpleMansionStates(
                new[] { Header, BuildFixture(fixture) },
                "FoundationPurpleMansionStates.fixture.csv");

            try
            {
                var state = states.Single();
                Assert.AreEqual(expectedCompleteMansions, state.mansionStates.Count(mansion => mansion.state == PurpleMansionBuildState.Complete));
                Assert.IsTrue(state.mansionStates.Where(mansion => mansion.state == PurpleMansionBuildState.NotBuilt).All(mansion =>
                    mansion.mansionBodyEffectBindingId == null && mansion.guardianAbilityInstanceId == null));
                Assert.IsTrue(state.jindanLock.formationSnapshot.mansionStates
                    .Where(mansion => mansion.state == PurpleMansionBuildState.NotBuilt)
                    .All(mansion => mansion.mansionBodyEffectBindingId == null && mansion.guardianAbilityInstanceId == null));
            }
            finally
            {
                DestroyAll(states);
            }
        }

        [TestCase("capacity", "FPM_CAPACITY_OVERFLOW")]
        [TestCase("duplicate", "FPM_DUPLICATE_MANSION_KIND")]
        [TestCase("missingBinding", "FPM_COMPLETE_MISSING_BINDING")]
        [TestCase("multipleGuardian", "FPM_COMPLETE_MISSING_BINDING")]
        [TestCase("recursiveEffect", "FPM_RECURSIVE_EFFECT_BINDING")]
        [TestCase("jindanMutation", "FPM_JINDAN_LOCK_MUTATION")]
        [TestCase("legacyMixed", "FPM_LEGACY_SCHEMA_MIXED")]
        [TestCase("unknownPhase", "FPM_UNKNOWN_PHASE")]
        public void ParseFoundationPurpleMansionStatesFailsWithItsStableReason(string fixture, string expectedReason)
        {
            string header = fixture == "legacyMixed"
                ? Header.Replace("fixtureOnlyNumericProfile", "developedMansions")
                : Header;

            var exception = Assert.Throws<InvalidDataException>(() => CultivationContentImporter.ParseFoundationPurpleMansionStates(
                new[] { header, BuildFixture(fixture) },
                "FoundationPurpleMansionStates.fixture.csv"));

            StringAssert.StartsWith(expectedReason + ":", exception.Message);
        }

        [Test]
        public void ImportFoundationPurpleMansionStatesRejectsTheWholeTableBeforeCreatingAnAsset()
        {
            const string sourceAssetPath = "Assets/DataConfig/FoundationPurpleMansionStates.csv";
            const string importedAssetPath =
                "Assets/Data/FoundationPurpleMansionStates/FoundationPurpleMansionState_fixture_character.asset";
            string sourceFilePath = Path.Combine(Application.dataPath, "DataConfig/FoundationPurpleMansionStates.csv");
            byte[] originalContents = File.ReadAllBytes(sourceFilePath);
            var invalidProductionRow = BuildValues("complete");
            invalidProductionRow["totalMansionCapacity"] = "0";
            invalidProductionRow["fixtureId"] = "";
            invalidProductionRow["expect"] = "";
            invalidProductionRow["fixtureOnlyNumericProfile"] = "";

            try
            {
                AssetDatabase.DeleteAsset(importedAssetPath);
                File.WriteAllText(sourceFilePath, Header + "\n" + BuildRow(invalidProductionRow) + "\n");
                AssetDatabase.ImportAsset(sourceAssetPath, ImportAssetOptions.ForceSynchronousImport);

                var exception = Assert.Throws<InvalidDataException>(() => CultivationContentImporter.ImportFoundationPurpleMansionStates());
                StringAssert.StartsWith("FPM_CAPACITY_OVERFLOW:", exception.Message);
                Assert.IsNull(AssetDatabase.LoadAssetAtPath<FoundationPurpleMansionStateData>(importedAssetPath));
            }
            finally
            {
                AssetDatabase.DeleteAsset(importedAssetPath);
                File.WriteAllBytes(sourceFilePath, originalContents);
                AssetDatabase.ImportAsset(sourceAssetPath, ImportAssetOptions.ForceSynchronousImport);
            }
        }

        [Test]
        public void ProductionFoundationPurpleMansionCsvHasNoFixtureRows()
        {
            string sourceFilePath = Path.Combine(Application.dataPath, "DataConfig/FoundationPurpleMansionStates.csv");
            var states = CultivationContentImporter.ParseFoundationPurpleMansionStates(
                File.ReadAllLines(sourceFilePath),
                sourceFilePath,
                allowFixtures: false);
            try
            {
                Assert.AreEqual(0, states.Length);
            }
            finally
            {
                DestroyAll(states);
            }
        }

        private static string BuildFixture(string fixture)
        {
            var values = BuildValues(fixture);
            return BuildRow(values);
        }

        private static Dictionary<string, string> BuildValues(string fixture)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["schemaId"] = "foundationPurpleMansionState",
                ["schemaVersion"] = "1",
                ["characterId"] = "fixture_character",
                ["foundationInstanceId"] = "foundation_fixture",
                ["foundationDefinitionId"] = "fixture_foundation_definition",
                ["sourceGongFaId"] = "fixture_gongfa",
                ["phase"] = "PHASE_1",
                ["continuousProgress"] = "50",
                ["phaseBoundarySetId"] = "fixture_phase_boundaries",
                ["naturalMansionCapacity"] = "0",
                ["releasedNaturalCapacity"] = "0",
                ["expansionGrants"] = "none",
                ["expandedMansionCapacity"] = "0",
                ["totalMansionCapacity"] = "0",
                ["mansionStates"] = NotBuiltMansions(),
                ["effectBindings"] = "none",
                ["guardianAbilities"] = "none",
                ["enhancementNodes"] = "none",
                ["cultivationActionState"] = "none",
                ["closedRetreatPlan"] = "none",
                ["jindanLock"] = "PRE_JINDAN",
                ["fixtureId"] = "fpm.valid.phase1-empty",
                ["expect"] = "ACCEPT",
                ["fixtureOnlyNumericProfile"] = "fixture_phase_boundaries~100~200~300",
            };

            switch (fixture)
            {
                case "phase1":
                    return values;
                case "complete":
                    ApplyOneCompleteMansion(values);
                    values["fixtureId"] = "fpm.valid.one-complete-mansion";
                    return values;
                case "formedOneMansion":
                    ApplyOneCompleteMansion(values);
                    values["jindanLock"] = FormedLock(
                        1,
                        "MING:COMPLETE:MANSION_BODY_MING_YUAN_HUIHU:guardian_ming",
                        "HUN:NOT_BUILT",
                        "SHI:NOT_BUILT",
                        "WU:NOT_BUILT",
                        "YUN:NOT_BUILT");
                    values["fixtureId"] = "fpm.valid.formed-one-mansion";
                    return values;
                case "formedThreeMansions":
                    ApplyThreeCompleteMansions(values);
                    values["jindanLock"] = FormedLock(
                        3,
                        "MING:COMPLETE:MANSION_BODY_MING_YUAN_HUIHU:guardian_ming",
                        "HUN:COMPLETE:MANSION_BODY_HUN_LINGTAI_DINGPO:guardian_hun",
                        "SHI:COMPLETE:MANSION_BODY_SHI_SHENGUAN_RUWEI:guardian_shi",
                        "WU:NOT_BUILT",
                        "YUN:NOT_BUILT");
                    values["fixtureId"] = "fpm.valid.formed-three-mansion";
                    return values;
                case "capacityUpperBound":
                    ApplyCapacityUpperBound(values);
                    values["fixtureId"] = "fpm.valid.capacity-upper-bound";
                    return values;
                case "pausedEmbryo":
                    ApplyPausedEmbryo(values);
                    values["fixtureId"] = "fpm.valid.paused-embryo";
                    return values;
                case "capacity":
                    ApplyOneCompleteMansion(values);
                    values["totalMansionCapacity"] = "0";
                    values["fixtureId"] = "fpm.invalid.capacity-overflow";
                    values["expect"] = "REJECT";
                    return values;
                case "duplicate":
                    values["mansionStates"] =
                        "MING~NOT_BUILT|MING~NOT_BUILT|SHI~NOT_BUILT|WU~NOT_BUILT|YUN~NOT_BUILT";
                    values["fixtureId"] = "fpm.invalid.duplicate-mansion-kind";
                    values["expect"] = "REJECT";
                    return values;
                case "missingBinding":
                    ApplyOneCompleteMansion(values);
                    values["guardianAbilities"] = "none";
                    values["fixtureId"] = "fpm.invalid.complete-missing-binding";
                    values["expect"] = "REJECT";
                    return values;
                case "multipleGuardian":
                    ApplyOneCompleteMansion(values);
                    values["guardianAbilities"] +=
                        "|guardian_ming_second~fixture_ability_ming_second~mansion_ming~fixture_spell_ming~fixture_upgrade_ming~RETAIN~PASSIVE~none";
                    values["fixtureId"] = "fpm.invalid.complete-missing-binding";
                    values["expect"] = "REJECT";
                    return values;
                case "recursiveEffect":
                    ApplyOneCompleteMansion(values);
                    values["effectBindings"] =
                        "MANSION_BODY_MING_YUAN_HUIHU~MANSION_BODY~mansion_ming~1~after_damage~none~holder_health~body_recovery~children=forbidden";
                    values["fixtureId"] = "fpm.invalid.recursive-effect";
                    values["expect"] = "REJECT";
                    return values;
                case "jindanMutation":
                    ApplyOneCompleteMansion(values);
                    values["jindanLock"] =
                        "FORMED~foundation_fixture~PHASE_4~1~none~MING:NOT_BUILT+HUN:NOT_BUILT+SHI:NOT_BUILT+WU:NOT_BUILT+YUN:NOT_BUILT";
                    values["fixtureId"] = "fpm.invalid.jindan-add-mansion";
                    values["expect"] = "REJECT";
                    return values;
                case "legacyMixed":
                    values["fixtureId"] = "fpm.invalid.legacy-and-new-mixed";
                    values["expect"] = "REJECT";
                    return values;
                case "unknownPhase":
                    values["phase"] = "PHASE_5";
                    values["fixtureId"] = "fpm.invalid.unknown-phase";
                    values["expect"] = "REJECT";
                    return values;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fixture), fixture, "Unknown fixture.");
            }
        }

        private static void ApplyOneCompleteMansion(Dictionary<string, string> values)
        {
            values["phase"] = "PHASE_4";
            values["continuousProgress"] = "400";
            values["naturalMansionCapacity"] = "1";
            values["releasedNaturalCapacity"] = "1";
            values["totalMansionCapacity"] = "1";
            values["mansionStates"] =
                "MING~COMPLETE~mansion_ming~MANSION_BODY_MING_YUAN_HUIHU~guardian_ming~fixture_spell_ming~fixture_upgrade_ming~RETAIN|" +
                "HUN~NOT_BUILT|SHI~NOT_BUILT|WU~NOT_BUILT|YUN~NOT_BUILT";
            values["effectBindings"] = BodyEffect("MING", "mansion_ming");
            values["guardianAbilities"] =
                "guardian_ming~fixture_ability_ming~mansion_ming~fixture_spell_ming~fixture_upgrade_ming~RETAIN~PASSIVE~none";
        }

        private static void ApplyCapacityUpperBound(Dictionary<string, string> values)
        {
            values["phase"] = "PHASE_4";
            values["continuousProgress"] = "400";
            values["naturalMansionCapacity"] = "3";
            values["releasedNaturalCapacity"] = "3";
            values["expansionGrants"] = "grant_one~fixture_item_one~capacity_effect_one|grant_two~fixture_item_two~capacity_effect_two";
            values["expandedMansionCapacity"] = "2";
            values["totalMansionCapacity"] = "5";
            values["mansionStates"] = CompleteMansions();
            values["effectBindings"] = string.Join("|", new[]
            {
                BodyEffect("MING", "mansion_ming"),
                BodyEffect("HUN", "mansion_hun"),
                BodyEffect("SHI", "mansion_shi"),
                BodyEffect("WU", "mansion_wu"),
                BodyEffect("YUN", "mansion_yun"),
                "capacity_effect_one~EXPANSION_GRANT~grant_one~1~grant_applied~none~mansion_capacity~MANSION_CAPACITY_PLUS_ONE~profileRef:fixture_numeric",
                "capacity_effect_two~EXPANSION_GRANT~grant_two~1~grant_applied~none~mansion_capacity~MANSION_CAPACITY_PLUS_ONE~profileRef:fixture_numeric",
            });
            values["guardianAbilities"] = string.Join("|", new[]
            {
                Guardian("ming", "MING"), Guardian("hun", "HUN"), Guardian("shi", "SHI"),
                Guardian("wu", "WU"), Guardian("yun", "YUN"),
            });
        }

        private static void ApplyThreeCompleteMansions(Dictionary<string, string> values)
        {
            values["phase"] = "PHASE_4";
            values["continuousProgress"] = "400";
            values["naturalMansionCapacity"] = "3";
            values["releasedNaturalCapacity"] = "3";
            values["totalMansionCapacity"] = "3";
            values["mansionStates"] =
                "MING~COMPLETE~mansion_ming~MANSION_BODY_MING_YUAN_HUIHU~guardian_ming~fixture_spell_ming~fixture_upgrade_ming~RETAIN|" +
                "HUN~COMPLETE~mansion_hun~MANSION_BODY_HUN_LINGTAI_DINGPO~guardian_hun~fixture_spell_hun~fixture_upgrade_hun~RETAIN|" +
                "SHI~COMPLETE~mansion_shi~MANSION_BODY_SHI_SHENGUAN_RUWEI~guardian_shi~fixture_spell_shi~fixture_upgrade_shi~RETAIN|" +
                "WU~NOT_BUILT|YUN~NOT_BUILT";
            values["effectBindings"] = string.Join("|", new[]
            {
                BodyEffect("MING", "mansion_ming"),
                BodyEffect("HUN", "mansion_hun"),
                BodyEffect("SHI", "mansion_shi"),
            });
            values["guardianAbilities"] = string.Join("|", new[]
            {
                Guardian("ming", "MING"),
                Guardian("hun", "HUN"),
                Guardian("shi", "SHI"),
            });
        }

        private static void ApplyPausedEmbryo(Dictionary<string, string> values)
        {
            ApplyOneCompleteMansion(values);
            values["naturalMansionCapacity"] = "2";
            values["releasedNaturalCapacity"] = "2";
            values["totalMansionCapacity"] = "2";
            values["mansionStates"] =
                "MING~COMPLETE~mansion_ming~MANSION_BODY_MING_YUAN_HUIHU~guardian_ming~fixture_spell_ming~fixture_upgrade_ming~RETAIN|" +
                "HUN~EMBRYO~embryo_hun~fixture_spell_hun~fixture_upgrade_hun~20~progress_hun~action_hun|" +
                "SHI~NOT_BUILT|WU~NOT_BUILT|YUN~NOT_BUILT";
            values["cultivationActionState"] =
                "action_hun~MANSION_EMBRYO_NURTURE~PAUSED~embryo_hun~fixture_cycle~fixture_boundary~none~progress_hun~fixture_numeric";
            values["closedRetreatPlan"] = "action_hun~embryo_hun~MANUAL_PAUSE";
        }

        private static string NotBuiltMansions()
        {
            return "MING~NOT_BUILT|HUN~NOT_BUILT|SHI~NOT_BUILT|WU~NOT_BUILT|YUN~NOT_BUILT";
        }

        private static string FormedLock(int naturalMansionCapacity, params string[] mansionSnapshots)
        {
            return $"FORMED~foundation_fixture~PHASE_4~{naturalMansionCapacity}~none~{string.Join("+", mansionSnapshots)}";
        }

        private static string CompleteMansions()
        {
            return string.Join("|", new[]
            {
                "MING~COMPLETE~mansion_ming~MANSION_BODY_MING_YUAN_HUIHU~guardian_ming~fixture_spell_ming~fixture_upgrade_ming~RETAIN",
                "HUN~COMPLETE~mansion_hun~MANSION_BODY_HUN_LINGTAI_DINGPO~guardian_hun~fixture_spell_hun~fixture_upgrade_hun~RETAIN",
                "SHI~COMPLETE~mansion_shi~MANSION_BODY_SHI_SHENGUAN_RUWEI~guardian_shi~fixture_spell_shi~fixture_upgrade_shi~RETAIN",
                "WU~COMPLETE~mansion_wu~MANSION_BODY_WU_WUJI_SHANCHENG~guardian_wu~fixture_spell_wu~fixture_upgrade_wu~RETAIN",
                "YUN~COMPLETE~mansion_yun~MANSION_BODY_YUN_JIYUAN_SHIZHAO~guardian_yun~fixture_spell_yun~fixture_upgrade_yun~RETAIN",
            });
        }

        private static string BodyEffect(string kind, string mansionId)
        {
            string bindingId = kind switch
            {
                "MING" => "MANSION_BODY_MING_YUAN_HUIHU",
                "HUN" => "MANSION_BODY_HUN_LINGTAI_DINGPO",
                "SHI" => "MANSION_BODY_SHI_SHENGUAN_RUWEI",
                "WU" => "MANSION_BODY_WU_WUJI_SHANCHENG",
                "YUN" => "MANSION_BODY_YUN_JIYUAN_SHIZHAO",
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            return $"{bindingId}~MANSION_BODY~{mansionId}~1~trigger_{kind.ToLowerInvariant()}~none~target_{kind.ToLowerInvariant()}~atomic_{kind.ToLowerInvariant()}~profileRef:fixture_numeric";
        }

        private static string Guardian(string lowerKind, string upperKind)
        {
            return $"guardian_{lowerKind}~fixture_ability_{lowerKind}~mansion_{lowerKind}~fixture_spell_{lowerKind}~fixture_upgrade_{lowerKind}~RETAIN~PASSIVE~none";
        }

        private static string BuildRow(IReadOnlyDictionary<string, string> values)
        {
            return string.Join(",", Columns.Select(column => values[column]));
        }

        private static void DestroyAll(IEnumerable<FoundationPurpleMansionStateData> states)
        {
            foreach (var state in states)
                UnityEngine.Object.DestroyImmediate(state);
        }
    }
}
