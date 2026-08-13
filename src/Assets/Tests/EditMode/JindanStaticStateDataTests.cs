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
        private const string FoundationFixtureFileName = "FoundationPurpleMansionStates.fixture.csv";
        private const string JindanFixtureFileName = "JindanStaticStates.fixture.csv";

        private static readonly string[] Columns =
        {
            "schemaId", "schemaVersion", "characterId", "foundationPurpleMansionStateRef", "mansionInputs",
            "jindanCoreBinding", "danxiang", "stablePositionBindings", "abilityLedgerBindings", "fixtureId",
            "expect", "fixtureOnlyNumericProfile",
        };

        [TestCase("jd.valid.one-mansion-one-seat", 1, 1)]
        [TestCase("jd.valid.three-mansion-three-seats", 3, 3)]
        [TestCase("jd.valid.five-mansion-three-seats", 5, 3)]
        public void ParseJindanStaticStatesAcceptsEverySpecifiedValidFixture(
            string fixtureId,
            int completedMansions,
            int stablePositions)
        {
            var foundation = ParseFoundationFixture(fixtureId);
            var catalog = BuildCatalog(foundation, stablePositions);
            var states = CultivationContentImporter.ParseJindanStaticStates(
                LoadFixtureLines(JindanFixtureFileName, fixtureId),
                JindanFixtureFileName,
                catalog);
            try
            {
                var state = states.Single();
                Assert.AreEqual("jindanStaticState", state.schemaId);
                Assert.AreEqual(5, state.mansionInputs.Length);
                Assert.AreEqual(5, state.mansionInputs.Select(input => input.mansionKind).Distinct().Count());
                Assert.AreEqual(completedMansions, state.mansionInputs.Count(input => input.state == PurpleMansionBuildState.Complete));
                Assert.AreEqual(stablePositions, state.stablePositionBindings.Length);
                Assert.AreEqual(stablePositions, state.stablePositionBindings.Select(binding => binding.positionType).Distinct().Count());
                Assert.AreEqual(completedMansions, state.abilityLedgerBindings.Length);
                Assert.AreEqual(completedMansions, state.abilityLedgerBindings.Select(binding => binding.abilityInstanceId).Distinct().Count());
                Assert.AreEqual(state.jindanCoreBinding.jindanInstanceId, state.danxiang.jindanInstanceId);
                Assert.IsFalse(string.IsNullOrWhiteSpace(state.jindanCoreBinding.jindanCoreBindingId));
                Assert.IsFalse(string.IsNullOrWhiteSpace(state.danxiang.danxiangInstanceId));
            }
            finally
            {
                DestroyAll(states);
                DestroyFoundation(foundation);
            }
        }

        [TestCase("jd.invalid.input-not-formed", "JD_FPM_INPUT_NOT_FORMED", 1)]
        [TestCase("jd.invalid.missing-mansion-input", "JD_MANSION_INPUT_INCOMPLETE", 1)]
        [TestCase("jd.invalid.unknown-static-reference", "JD_UNKNOWN_STATIC_REFERENCE", 1)]
        [TestCase("jd.invalid.effect-outside-road-candidates", "JD_EFFECT_LOADOUT_INVALID", 2)]
        [TestCase("jd.invalid.fourth-stable-position", "JD_STABLE_POSITION_LIMIT", 1)]
        [TestCase("jd.invalid.second-core", "JD_CORE_NOT_UNIQUE", 1)]
        [TestCase("jd.invalid.second-danxiang", "JD_DANXIANG_NOT_UNIQUE", 1)]
        [TestCase("jd.invalid.duplicate-primary-carrier", "JD_PRIMARY_CARRIER_DUPLICATE", 3)]
        [TestCase("jd.invalid.illegal-carrier-reference", "JD_CARRIER_REFERENCE_INVALID", 1)]
        [TestCase("jd.invalid.shared-instance-ledger", "JD_ABILITY_LEDGER_OWNERSHIP_INVALID", 3)]
        [TestCase("jd.invalid.conflict-reference-foreign", "JD_CONFLICT_REFERENCE_INVALID", 1)]
        [TestCase("jd.invalid.legacy-or-display-string", "JD_LEGACY_OR_DISPLAY_FIELD", 1)]
        public void ParseJindanStaticStatesFailsWithItsStableReason(
            string fixtureId,
            string expectedReason,
            int catalogPositionCount)
        {
            var foundation = ParseFoundationFixture(fixtureId);
            try
            {
                string[] lines = LoadFixtureLines(JindanFixtureFileName, fixtureId);
                if (fixtureId == "jd.invalid.legacy-or-display-string")
                    lines[0] = lines[0].Replace("fixtureOnlyNumericProfile", "displayName");

                var exception = Assert.Throws<InvalidDataException>(() => CultivationContentImporter.ParseJindanStaticStates(
                    lines,
                    JindanFixtureFileName,
                    BuildCatalog(foundation, catalogPositionCount)));
                StringAssert.StartsWith(expectedReason + ":", exception.Message);
            }
            finally
            {
                DestroyFoundation(foundation);
            }
        }

        [Test]
        public void ImportJindanStaticStatesRejectsTheWholeTableBeforeCreatingAnAsset()
        {
            const string sourceAssetPath = "Assets/DataConfig/JindanStaticStates.csv";
            const string importedAssetPath = "Assets/Data/JindanStaticStates/JindanStaticState_fixture_character.asset";
            string sourceFilePath = Path.Combine(Application.dataPath, "DataConfig/JindanStaticStates.csv");
            byte[] originalContents = File.ReadAllBytes(sourceFilePath);
            var invalidProductionRow = LoadFixtureValues("jd.valid.one-mansion-one-seat");
            invalidProductionRow["mansionInputs"] = string.Join("|", invalidProductionRow["mansionInputs"].Split('|').Take(4));
            invalidProductionRow["fixtureId"] = "";
            invalidProductionRow["expect"] = "";
            invalidProductionRow["fixtureOnlyNumericProfile"] = "";

            try
            {
                AssetDatabase.DeleteAsset(importedAssetPath);
                File.WriteAllText(sourceFilePath, FixtureHeader + "\n" + BuildRow(invalidProductionRow) + "\n");
                AssetDatabase.ImportAsset(sourceAssetPath, ImportAssetOptions.ForceSynchronousImport);

                var exception = Assert.Throws<InvalidDataException>(() => CultivationContentImporter.ImportJindanStaticStates());
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
            var states = CultivationContentImporter.ParseJindanStaticStates(
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

        private static string FixtureDirectory => Path.Combine(Application.dataPath, "Tests", "EditMode", "Fixtures");

        private static string FixtureHeader => ReadFixtureFile(JindanFixtureFileName)
            .First(line => line.StartsWith("schemaId,", StringComparison.Ordinal));

        private static FoundationPurpleMansionStateData ParseFoundationFixture(string fixtureId)
        {
            return CultivationContentImporter.ParseFoundationPurpleMansionStates(
                LoadFixtureLines(FoundationFixtureFileName, fixtureId),
                FoundationFixtureFileName).Single();
        }

        private static JindanStaticReferenceCatalog BuildCatalog(
            FoundationPurpleMansionStateData foundation,
            int positionCount)
        {
            var definitions = new[]
            {
                new FixturePositionDefinition(
                    "position_source", "road_source", JindanStaticPositionType.Source,
                    "proof_source", "effect_source", "compat_source", "guardian_ming"),
                new FixturePositionDefinition(
                    "position_transformation", "road_transformation", JindanStaticPositionType.Transformation,
                    "proof_transformation", "effect_transformation", "compat_transformation", "guardian_hun"),
                new FixturePositionDefinition(
                    "position_domain", "road_domain", JindanStaticPositionType.Domain,
                    "proof_domain", "effect_domain", "compat_domain", "guardian_shi"),
            }.Take(positionCount).ToArray();

            return new JindanStaticReferenceCatalog
            {
                foundationPurpleMansionStates = new[] { foundation },
                roads = definitions.Select(definition => new JindanRoadReference
                {
                    roadId = definition.RoadId,
                    baseEffectCandidateIds = new[]
                    {
                        definition.EffectId,
                        definition.EffectId + "_two",
                        definition.EffectId + "_three",
                    },
                }).ToArray(),
                positions = definitions.Select(definition => new JindanPositionReference
                {
                    positionId = definition.PositionId,
                    version = 1,
                    roadId = definition.RoadId,
                    positionType = definition.PositionType,
                    proofProfileId = definition.ProofProfileId,
                }).ToArray(),
                compatibilityProfiles = definitions.Select(definition => new JindanCompatibilityReference
                {
                    compatibilityProfileId = definition.CompatibilityProfileId,
                    roadId = definition.RoadId,
                    positionId = definition.PositionId,
                    equippedBaseEffectId = definition.EffectId,
                    primaryCarrierAbilityInstanceId = definition.PrimaryCarrierAbilityInstanceId,
                    auxiliaryCarrierAbilityInstanceIds = Array.Empty<string>(),
                }).ToArray(),
                danxingDefinitionIds = new[] { "fixture_danxing" },
                danxiangPresentationProfileIds = new[] { "profile_danxiang" },
                ledgerReferences = foundation.mansionStates
                    .Where(mansion => mansion.state == PurpleMansionBuildState.Complete)
                    .Select(mansion => $"ledger_{mansion.mansionKind.ToString().ToLowerInvariant()}_resource")
                    .Concat(new[] { "conflict_ming" })
                    .ToArray(),
                conflictCostProfileIds = new[] { "conflict_cost_ming" },
            };
        }

        private static string[] LoadFixtureLines(string fileName, string fixtureId)
        {
            string[] lines = ReadFixtureFile(fileName);
            int headerIndex = Array.FindIndex(lines, line => line.StartsWith("schemaId,", StringComparison.Ordinal));
            if (headerIndex < 0)
                throw new InvalidOperationException($"Fixture '{fileName}' has no header.");

            string header = lines[headerIndex];
            int fixtureIdIndex = Array.IndexOf(header.Split(','), "fixtureId");
            if (fixtureIdIndex < 0)
                throw new InvalidOperationException($"Fixture '{fileName}' has no fixtureId column.");

            string row = lines.Skip(headerIndex + 1).Single(line =>
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    return false;
                string[] values = line.Split(',');
                return values.Length > fixtureIdIndex && values[fixtureIdIndex] == fixtureId;
            });
            return new[] { header, row };
        }

        private static Dictionary<string, string> LoadFixtureValues(string fixtureId)
        {
            string[] lines = LoadFixtureLines(JindanFixtureFileName, fixtureId);
            string[] values = lines[1].Split(',');
            return Columns.Select((column, index) => new { column, value = values[index] })
                .ToDictionary(item => item.column, item => item.value, StringComparer.Ordinal);
        }

        private static string[] ReadFixtureFile(string fileName)
        {
            return File.ReadAllLines(Path.Combine(FixtureDirectory, fileName));
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

        private static void DestroyFoundation(FoundationPurpleMansionStateData foundation)
        {
            UnityEngine.Object.DestroyImmediate(foundation);
        }

        private sealed class FixturePositionDefinition
        {
            public FixturePositionDefinition(
                string positionId,
                string roadId,
                JindanStaticPositionType positionType,
                string proofProfileId,
                string effectId,
                string compatibilityProfileId,
                string primaryCarrierAbilityInstanceId)
            {
                PositionId = positionId;
                RoadId = roadId;
                PositionType = positionType;
                ProofProfileId = proofProfileId;
                EffectId = effectId;
                CompatibilityProfileId = compatibilityProfileId;
                PrimaryCarrierAbilityInstanceId = primaryCarrierAbilityInstanceId;
            }

            public string PositionId { get; }
            public string RoadId { get; }
            public JindanStaticPositionType PositionType { get; }
            public string ProofProfileId { get; }
            public string EffectId { get; }
            public string CompatibilityProfileId { get; }
            public string PrimaryCarrierAbilityInstanceId { get; }
        }
    }
}
