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
    public class NpcCultivationActionWeightProfileDataTests
    {
        private const string CsvRelativePath = "Assets/DataConfig/NpcCultivationActionWeightProfiles.csv";
        private const string AssetPath = "Assets/Data/NpcCultivationActionWeightProfiles/NpcCultivationActionWeightProfile_npc-cultivation-production-v1.asset";

        [Test]
        public void ProductionProfileParsesToOneValidatedRuntimeProjection()
        {
            var profiles = ContentImportCoordinator.ParseNpcCultivationActionWeightProfiles(
                File.ReadAllLines(Path.Combine(Application.dataPath, "DataConfig/NpcCultivationActionWeightProfiles.csv")),
                CsvRelativePath);
            try
            {
                Assert.That(profiles, Has.Length.EqualTo(1));
                var profile = profiles[0];
                Assert.That(profile.schemaId, Is.EqualTo(NpcCultivationActionWeightProfileRuntime.SchemaId));
                Assert.That(profile.authorityKind, Is.EqualTo("CSV_SOURCE_SET"));
                Assert.That(profile.sourceContentHash, Has.Length.EqualTo(64));
                Assert.That(profile.actionWeightRows.Select(row => row.actionStableId), Is.EquivalentTo(new[]
                {
                    "FOUNDATION_TRIAL", "FOUNDATION_NURTURE", "MANSION_EMBRYO_NURTURE", "MANSION_OPENING_TRIAL", "JINDAN_PROOF",
                }));
                Assert.That(NpcCultivationActionWeightProfileRuntime.TryCreate(profile, out _, out string failureReason), Is.True, failureReason);
            }
            finally
            {
                foreach (var profile in profiles)
                    UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void FullInputUsesConfiguredCapsAndAllSixModifierSources()
        {
            var runtime = LoadRuntime();
            var result = runtime.Evaluate(new NpcCultivationActionDecisionContext(
                new[] { "FOUNDATION_TRIAL", "FOUNDATION_NURTURE", "MANSION_EMBRYO_NURTURE", "MANSION_OPENING_TRIAL", "JINDAN_PROOF" },
                new[]
                {
                    "PERS_AMBITIOUS", "PERS_DISCIPLINED", "PERS_PRUDENT", "SECT_CULTIVATION", "SECT_MANSION_CRAFT",
                    "GOAL_FOUNDATION_COMPLETE", "GOAL_OPEN_MANSION", "GOAL_JINDAN", "LIFE_URGENT", "RESERVE_RICH",
                    "ENV_SPIRIT_RICH", "ENV_JINDAN_AUSPICIOUS", "KNOWN_JINDAN_OPPORTUNITY",
                },
                new Dictionary<string, float> { ["subjective_success"] = 70 }));

            var jindan = result.candidates.Single(candidate => candidate.actionStableId == "JINDAN_PROOF");
            Assert.That(result.selectedActionStableId, Is.EqualTo("JINDAN_PROOF"));
            Assert.That(jindan.score, Is.EqualTo(130).Within(0.0001f));
            Assert.That(jindan.matchedModifierIds, Has.Length.EqualTo(5));
        }

        [Test]
        public void IllegalActionsAndRiskGateAreRejectedBeforeRanking()
        {
            var runtime = LoadRuntime();
            var resourceTight = runtime.Evaluate(new NpcCultivationActionDecisionContext(
                new[] { "FOUNDATION_NURTURE" },
                new[] { "SECT_CULTIVATION", "GOAL_FOUNDATION_COMPLETE", "RESERVE_TIGHT" },
                new Dictionary<string, float>()));
            Assert.That(resourceTight.selectedActionStableId, Is.EqualTo("FOUNDATION_NURTURE"));
            Assert.That(resourceTight.candidates.Single(candidate => candidate.actionStableId == "MANSION_OPENING_TRIAL").rejectionReason,
                Is.EqualTo(NpcCultivationActionWeightProfileRuntime.IllegalAction));

            var urgent = runtime.Evaluate(new NpcCultivationActionDecisionContext(
                new[] { "JINDAN_PROOF" },
                new[] { "LIFE_URGENT", "KNOWN_JINDAN_OPPORTUNITY" },
                new Dictionary<string, float> { ["subjective_success"] = 45 }));
            Assert.That(urgent.selectedActionStableId, Is.EqualTo("JINDAN_PROOF"));

            var rejected = runtime.Evaluate(new NpcCultivationActionDecisionContext(
                new[] { "JINDAN_PROOF" },
                new[] { "KNOWN_JINDAN_OPPORTUNITY" },
                new Dictionary<string, float> { ["subjective_success"] = 55 }));
            Assert.That(rejected.selectedActionStableId, Is.Null);
            Assert.That(rejected.candidates.Single(candidate => candidate.actionStableId == "JINDAN_PROOF").rejectionReason,
                Is.EqualTo(NpcCultivationActionWeightProfileRuntime.RiskGateRejected));
        }

        [Test]
        public void ImportProducesAnAssetWithTheSameSourceHash()
        {
            ContentImportCoordinator.ImportNpcCultivationActionWeightProfiles();
            AssetDatabase.SaveAssets();
            var asset = AssetDatabase.LoadAssetAtPath<NpcCultivationActionWeightProfileData>(AssetPath);
            Assert.That(asset, Is.Not.Null);

            var parsed = ContentImportCoordinator.ParseNpcCultivationActionWeightProfiles(
                File.ReadAllLines(Path.Combine(Application.dataPath, "DataConfig/NpcCultivationActionWeightProfiles.csv")),
                CsvRelativePath);
            try
            {
                Assert.That(asset.profileId, Is.EqualTo(parsed[0].profileId));
                Assert.That(asset.sourceContentHash, Is.EqualTo(parsed[0].sourceContentHash));
                Assert.That(asset.authorityKind, Is.EqualTo("CSV_SOURCE_SET"));
            }
            finally
            {
                foreach (var profile in parsed)
                    UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void MismatchedManifestHashFailsBeforeAnyAssetCanBeCreated()
        {
            var lines = File.ReadAllLines(Path.Combine(Application.dataPath, "DataConfig/NpcCultivationActionWeightProfiles.csv"));
            lines[1] = lines[1].Replace("8f9e1e7c6154d6b1bcbae301cf8e0fd219fd258565d43a1a55ec84cd8d9ec1b9", new string('0', 64));
            var exception = Assert.Throws<InvalidDataException>(() =>
                ContentImportCoordinator.ParseNpcCultivationActionWeightProfiles(lines, "NpcCultivationActionWeightProfiles.invalid.csv"));
            StringAssert.StartsWith("NPC_WEIGHT_DOUBLE_AUTHORITY:", exception.Message);
        }

        private static NpcCultivationActionWeightProfileRuntime LoadRuntime()
        {
            var profiles = ContentImportCoordinator.ParseNpcCultivationActionWeightProfiles(
                File.ReadAllLines(Path.Combine(Application.dataPath, "DataConfig/NpcCultivationActionWeightProfiles.csv")),
                CsvRelativePath);
            Assert.That(profiles, Has.Length.EqualTo(1));
            Assert.That(NpcCultivationActionWeightProfileRuntime.TryCreate(profiles[0], out var runtime, out string failureReason), Is.True, failureReason);
            return runtime;
        }
    }
}
