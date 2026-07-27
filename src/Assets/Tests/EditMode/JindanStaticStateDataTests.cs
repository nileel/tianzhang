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
    public class JindanStaticStateDataTests
    {
        private static readonly string[] Columns =
        {
            "schemaId", "schemaVersion", "characterId", "foundationPurpleMansionStateRef", "mansionInputs",
            "jindanCoreBinding", "danxiang", "stablePositionBindings", "abilityLedgerBindings", "fixtureId",
            "expect", "fixtureOnlyNumericProfile",
        };

        private static readonly string Header = string.Join(",", Columns);

        [TestCase(1, 1)]
        [TestCase(3, 3)]
        [TestCase(5, 3)]
        public void ParseJindanStaticStatesAcceptsEverySpecifiedValidFixture(int completedMansions, int stablePositions)
        {
            var values = BuildValues(completedMansions, stablePositions);
            var states = DataConfigImporter.ParseJindanStaticStates(
                new[] { Header, BuildRow(values) },
                "JindanStaticStates.fixture.csv",
                BuildCatalog(completedMansions, stablePositions));
            try
            {
                Assert.AreEqual(1, states.Length);
                Assert.AreEqual("jindanStaticState", states[0].schemaId);
                Assert.AreEqual(5, states[0].mansionInputs.Length);
                Assert.AreEqual(stablePositions, states[0].stablePositionBindings.Length);
                Assert.AreEqual(completedMansions, states[0].abilityLedgerBindings.Length);
            }
            finally
            {
                DestroyAll(states);
            }
        }

        [TestCase("inputNotFormed", "JD_FPM_INPUT_NOT_FORMED")]
        [TestCase("missingMansion", "JD_MANSION_INPUT_INCOMPLETE")]
        [TestCase("unknownStatic", "JD_UNKNOWN_STATIC_REFERENCE")]
        [TestCase("fourthPosition", "JD_STABLE_POSITION_LIMIT")]
        [TestCase("secondCore", "JD_CORE_NOT_UNIQUE")]
        [TestCase("secondDanxiang", "JD_DANXIANG_NOT_UNIQUE")]
        [TestCase("duplicatePrimary", "JD_PRIMARY_CARRIER_DUPLICATE")]
        [TestCase("sharedLedger", "JD_ABILITY_LEDGER_OWNERSHIP_INVALID")]
        [TestCase("foreignConflict", "JD_CONFLICT_REFERENCE_INVALID")]
        [TestCase("legacyDisplay", "JD_LEGACY_OR_DISPLAY_FIELD")]
        public void ParseJindanStaticStatesFailsWithItsStableReason(string fixture, string expectedReason)
        {
            int completedMansions = fixture == "duplicatePrimary" || fixture == "sharedLedger" ? 3 : 1;
            int stablePositions = fixture == "duplicatePrimary" ? 3 : 1;
            var values = BuildValues(completedMansions, stablePositions);
            var catalog = BuildCatalog(completedMansions, stablePositions);
            string header = Header;

            switch (fixture)
            {
                case "inputNotFormed":
                    catalog.foundationPurpleMansionStates[0].jindanLock.status = JindanLockStatus.PreJindan;
                    break;
                case "missingMansion":
                    values["mansionInputs"] = string.Join("|", values["mansionInputs"].Split('|').Take(4));
                    break;
                case "unknownStatic":
                    values["stablePositionBindings"] = values["stablePositionBindings"].Replace("road_source", "road_unknown");
                    break;
                case "fourthPosition":
                    values["stablePositionBindings"] += "|position_four~1~road_four~SOURCE~proof_four~effect_four~compat_four~guardian_ming~none";
                    break;
                case "secondCore":
                    values["jindanCoreBinding"] += "|core_second~jindan_second~danshu_second~formation_second~1";
                    break;
                case "secondDanxiang":
                    values["danxiang"] += "|danxiang_second~jindan_second~key_second~fixture_danxing~profile_danxiang";
                    break;
                case "duplicatePrimary":
                    values["stablePositionBindings"] = values["stablePositionBindings"].Replace("guardian_hun", "guardian_ming");
                    break;
                case "sharedLedger":
                    values["abilityLedgerBindings"] = values["abilityLedgerBindings"].Replace("ledger_hun_resource", "ledger_ming_resource");
                    break;
                case "foreignConflict":
                    values["abilityLedgerBindings"] = values["abilityLedgerBindings"].Replace(
                        "guardian_ming~ledger_ming_resource~none~none~none~none~none",
                        "guardian_ming~ledger_ming_resource~none~none~none~conflict_ming~none");
                    break;
                case "legacyDisplay":
                    header = Header.Replace("fixtureOnlyNumericProfile", "displayName");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fixture));
            }

            var exception = Assert.Throws<InvalidDataException>(() => DataConfigImporter.ParseJindanStaticStates(
                new[] { header, BuildRow(values) },
                "JindanStaticStates.fixture.csv",
                catalog));
            StringAssert.StartsWith(expectedReason + ":", exception.Message);
            DestroyCatalog(catalog);
        }

        [Test]
        public void ImportJindanStaticStatesRejectsTheWholeTableBeforeCreatingAnAsset()
        {
            const string sourceAssetPath = "Assets/DataConfig/JindanStaticStates.csv";
            const string importedAssetPath = "Assets/Data/JindanStaticStates/JindanStaticState_fixture_character.asset";
            string sourceFilePath = Path.Combine(Application.dataPath, "DataConfig/JindanStaticStates.csv");
            byte[] originalContents = File.ReadAllBytes(sourceFilePath);
            var invalidProductionRow = BuildValues(1, 1);
            invalidProductionRow["mansionInputs"] = string.Join("|", invalidProductionRow["mansionInputs"].Split('|').Take(4));
            invalidProductionRow["fixtureId"] = "";
            invalidProductionRow["expect"] = "";
            invalidProductionRow["fixtureOnlyNumericProfile"] = "";

            try
            {
                AssetDatabase.DeleteAsset(importedAssetPath);
                File.WriteAllText(sourceFilePath, Header + "\n" + BuildRow(invalidProductionRow) + "\n");
                AssetDatabase.ImportAsset(sourceAssetPath, ImportAssetOptions.ForceSynchronousImport);

                var exception = Assert.Throws<InvalidDataException>(() => DataConfigImporter.ImportJindanStaticStates());
                StringAssert.StartsWith("JD_UNKNOWN_STATIC_REFERENCE:", exception.Message);
                Assert.IsNull(AssetDatabase.LoadAssetAtPath<JindanStaticStateData>(importedAssetPath));
            }
            finally
            {
                AssetDatabase.DeleteAsset(importedAssetPath);
                File.WriteAllBytes(sourceFilePath, originalContents);
                AssetDatabase.ImportAsset(sourceAssetPath, ImportAssetOptions.ForceSynchronousImport);
            }
        }

        [Test]
        public void ProductionJindanStaticCsvHasNoFixtureRows()
        {
            string sourceFilePath = Path.Combine(Application.dataPath, "DataConfig/JindanStaticStates.csv");
            var states = DataConfigImporter.ParseJindanStaticStates(
                File.ReadAllLines(sourceFilePath),
                sourceFilePath,
                new JindanStaticReferenceCatalog(),
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

        private static Dictionary<string, string> BuildValues(int completedMansions, int stablePositions)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["schemaId"] = "jindanStaticState",
                ["schemaVersion"] = "1",
                ["characterId"] = "fixture_character",
                ["foundationPurpleMansionStateRef"] = "foundation_fixture",
                ["mansionInputs"] = MansionInputs(completedMansions),
                ["jindanCoreBinding"] = "core_fixture~jindan_fixture~danshu_fixture~formation_fixture~1",
                ["danxiang"] = "danxiang_fixture~jindan_fixture~key_danxiang~fixture_danxing~profile_danxiang",
                ["stablePositionBindings"] = StablePositions(stablePositions),
                ["abilityLedgerBindings"] = AbilityLedgers(completedMansions),
                ["fixtureId"] = $"jd.valid.{completedMansions}-mansion-{stablePositions}-seat",
                ["expect"] = "ACCEPT",
                ["fixtureOnlyNumericProfile"] = "fixture_numeric",
            };
        }

        private static string MansionInputs(int completeCount)
        {
            return string.Join("|", MansionKinds().Select((kind, index) => index < completeCount
                ? $"{kind}~COMPLETE~mansion_{kind.ToLowerInvariant()}~body_{kind.ToLowerInvariant()}~guardian_{kind.ToLowerInvariant()}~spell_{kind.ToLowerInvariant()}~upgrade_{kind.ToLowerInvariant()}~RETAIN"
                : $"{kind}~NOT_BUILT"));
        }

        private static string StablePositions(int count)
        {
            var values = new[]
            {
                "position_source~1~road_source~SOURCE~proof_source~effect_source~compat_source~guardian_ming~none",
                "position_transformation~1~road_transformation~TRANSFORMATION~proof_transformation~effect_transformation~compat_transformation~guardian_hun~none",
                "position_domain~1~road_domain~DOMAIN~proof_domain~effect_domain~compat_domain~guardian_shi~none",
            };
            return string.Join("|", values.Take(count));
        }

        private static string AbilityLedgers(int count)
        {
            return string.Join("|", MansionKinds().Take(count).Select(kind =>
                $"guardian_{kind.ToLowerInvariant()}~ledger_{kind.ToLowerInvariant()}_resource~none~none~none~none~none"));
        }

        private static JindanStaticReferenceCatalog BuildCatalog(int completedMansions, int stablePositions)
        {
            var foundation = ScriptableObject.CreateInstance<FoundationPurpleMansionStateData>();
            foundation.characterId = "fixture_character";
            foundation.foundationState = new FoundationStateRecord
            {
                foundationInstanceId = "foundation_fixture",
                phase = FoundationPhase.Phase4,
            };
            foundation.mansionStates = MansionKinds().Select((kind, index) => new PurpleMansionStateRecord
            {
                mansionKind = ParseKind(kind),
                state = index < completedMansions ? PurpleMansionBuildState.Complete : PurpleMansionBuildState.NotBuilt,
                mansionInstanceId = index < completedMansions ? $"mansion_{kind.ToLowerInvariant()}" : null,
                mansionBodyEffectBindingId = index < completedMansions ? $"body_{kind.ToLowerInvariant()}" : null,
                guardianAbilityInstanceId = index < completedMansions ? $"guardian_{kind.ToLowerInvariant()}" : null,
                sourceSpellId = index < completedMansions ? $"spell_{kind.ToLowerInvariant()}" : null,
                upgradePlanId = index < completedMansions ? $"upgrade_{kind.ToLowerInvariant()}" : null,
                sourceSpellDisposition = index < completedMansions ? "RETAIN" : null,
            }).ToArray();
            foundation.jindanLock = new JindanLockRecord
            {
                status = JindanLockStatus.Formed,
                formationSnapshot = new JindanFormationSnapshot(),
            };

            var positionDefinitions = new[]
            {
                new { Id = "position_source", Road = "road_source", Type = JindanStaticPositionType.Source, Proof = "proof_source", Effect = "effect_source", Compatibility = "compat_source", Carrier = "guardian_ming" },
                new { Id = "position_transformation", Road = "road_transformation", Type = JindanStaticPositionType.Transformation, Proof = "proof_transformation", Effect = "effect_transformation", Compatibility = "compat_transformation", Carrier = "guardian_hun" },
                new { Id = "position_domain", Road = "road_domain", Type = JindanStaticPositionType.Domain, Proof = "proof_domain", Effect = "effect_domain", Compatibility = "compat_domain", Carrier = "guardian_shi" },
            }.Take(stablePositions).ToArray();

            return new JindanStaticReferenceCatalog
            {
                foundationPurpleMansionStates = new[] { foundation },
                roads = positionDefinitions.Select(definition => new JindanRoadReference
                {
                    roadId = definition.Road,
                    baseEffectCandidateIds = new[] { definition.Effect, definition.Effect + "_two", definition.Effect + "_three" },
                }).ToArray(),
                positions = positionDefinitions.Select(definition => new JindanPositionReference
                {
                    positionId = definition.Id,
                    version = 1,
                    roadId = definition.Road,
                    positionType = definition.Type,
                    proofProfileId = definition.Proof,
                }).ToArray(),
                compatibilityProfiles = positionDefinitions.Select(definition => new JindanCompatibilityReference
                {
                    compatibilityProfileId = definition.Compatibility,
                    roadId = definition.Road,
                    positionId = definition.Id,
                    equippedBaseEffectId = definition.Effect,
                    primaryCarrierAbilityInstanceId = definition.Carrier,
                    auxiliaryCarrierAbilityInstanceIds = Array.Empty<string>(),
                }).ToArray(),
                danxingDefinitionIds = new[] { "fixture_danxing" },
                danxiangPresentationProfileIds = new[] { "profile_danxiang" },
                ledgerReferences = MansionKinds().Take(completedMansions).Select(kind => $"ledger_{kind.ToLowerInvariant()}_resource").Concat(new[] { "conflict_ming" }).ToArray(),
                conflictCostProfileIds = new[] { "conflict_cost_ming" },
            };
        }

        private static PurpleMansionKind ParseKind(string kind)
        {
            return kind switch
            {
                "MING" => PurpleMansionKind.Ming,
                "HUN" => PurpleMansionKind.Hun,
                "SHI" => PurpleMansionKind.Shi,
                "WU" => PurpleMansionKind.Wu,
                "YUN" => PurpleMansionKind.Yun,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }

        private static IEnumerable<string> MansionKinds()
        {
            return new[] { "MING", "HUN", "SHI", "WU", "YUN" };
        }

        private static string BuildRow(IReadOnlyDictionary<string, string> values)
        {
            return string.Join(",", Columns.Select(column => values[column]));
        }

        private static void DestroyAll(IEnumerable<JindanStaticStateData> states)
        {
            foreach (var state in states)
                UnityEngine.Object.DestroyImmediate(state);
        }

        private static void DestroyCatalog(JindanStaticReferenceCatalog catalog)
        {
            foreach (var foundation in catalog.foundationPurpleMansionStates ?? Array.Empty<FoundationPurpleMansionStateData>())
                UnityEngine.Object.DestroyImmediate(foundation);
        }
    }
}
