using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Entity;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class FoundationPurpleMansionStateDataTests
    {
        private static readonly string[] Columns =
        {
            "schemaId", "schemaVersion", "characterId", "foundationInstanceId", "foundationDefinitionId",
            "sourceGongFaId", "stageId", "stageCode", "continuousProgress", "phaseBoundarySetId",
            "naturalMansionCapacity", "expansionGrants", "expandedMansionCapacity", "carryingCapacityProfileId",
            "currentMansionCarryingCapacity", "completionMansionCapacity", "mansionStates", "effectBindings",
            "guardianAbilities", "enhancementNodes", "cultivationActionState", "closedRetreatPlan", "jindanLock",
            "fixtureId", "expect", "fixtureOnlyNumericProfile",
        };

        private static readonly string Header = string.Join(",", Columns);

        [TestCase("foundation")]
        [TestCase("complete")]
        [TestCase("completeThenExpand")]
        [TestCase("pausedEmbryo")]
        public void ImporterAndRuntimeAcceptFrozenThreeStageFixtures(string fixture)
        {
            FoundationPurpleMansionStateData[] states = CultivationContentImporter.ParseFoundationPurpleMansionStates(
                new[] { Header, BuildRow(BuildValues(fixture)) },
                "FoundationPurpleMansionStates.fixture.csv");
            try
            {
                FoundationPurpleMansionStateData state = states[0];
                Assert.That(state.schemaVersion, Is.EqualTo(2));
                Assert.That(FoundationPurpleMansionRuntimeState.TryCreate(
                    state, out FoundationPurpleMansionRuntimeState runtimeState, out string reason), Is.True, reason);

                if (fixture == "foundation")
                {
                    Assert.That(runtimeState.Stage, Is.EqualTo(FoundationStage.Foundation));
                    Assert.That(runtimeState.CommittableMansionCapacity, Is.EqualTo(0));
                }
                else if (fixture == "pausedEmbryo")
                {
                    Assert.That(runtimeState.Stage, Is.EqualTo(FoundationStage.Mansion));
                    Assert.That(runtimeState.CommittableMansionCapacity, Is.EqualTo(2));
                    Assert.That(runtimeState.GetMansionBuildState(PurpleMansionKind.Hun), Is.EqualTo(PurpleMansionBuildState.Embryo));
                }
                else
                {
                    Assert.That(runtimeState.Stage, Is.EqualTo(FoundationStage.Complete));
                    Assert.That(runtimeState.CompletionMansionCapacity, Is.EqualTo(1));
                    Assert.That(runtimeState.SelfMansionCapacity, Is.EqualTo(fixture == "completeThenExpand" ? 2 : 1));
                    FoundationPurpleMansionSaveData save = runtimeState.CaptureSaveData();
                    Assert.That(FoundationPurpleMansionRuntimeState.TryRestore(
                        save, out FoundationPurpleMansionRuntimeState restored, out reason), Is.True, reason);
                    Assert.That(restored.Stage, Is.EqualTo(FoundationStage.Complete));
                    Assert.That(restored.CompletionMansionCapacity, Is.EqualTo(1));
                }
            }
            finally
            {
                foreach (FoundationPurpleMansionStateData state in states)
                    UnityEngine.Object.DestroyImmediate(state);
            }
        }

        [TestCase("legacyStage", FoundationPurpleMansionRuntimeState.LegacyStageSchemaIncompatible)]
        [TestCase("overflow", FoundationPurpleMansionRuntimeState.CapacityOverflow)]
        [TestCase("missingBinding", "FPM_COMPLETE_MISSING_BINDING")]
        public void ImporterRejectsIncompatibleOrIncompleteState(string fixture, string reason)
        {
            InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
                CultivationContentImporter.ParseFoundationPurpleMansionStates(
                    new[] { Header, BuildRow(BuildValues(fixture)) },
                    "FoundationPurpleMansionStates.fixture.csv"));
            StringAssert.StartsWith(reason + ":", exception.Message);
        }

        [Test]
        public void ProductionFoundationPurpleMansionCsvHasNoRows()
        {
            string path = Path.Combine(Application.dataPath, "DataConfig/FoundationPurpleMansionStates.csv");
            string[] dataRows = File.ReadAllLines(path);
            Assert.That(Array.FindAll(dataRows, line => line.StartsWith("schemaId,")).Length, Is.EqualTo(1));
            Assert.That(Array.FindAll(dataRows, line =>
                !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#") && !line.StartsWith("schemaId,")).Length,
                Is.EqualTo(0));
        }

        private static Dictionary<string, string> BuildValues(string fixture)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["schemaId"] = "foundationPurpleMansionState",
                ["schemaVersion"] = "2",
                ["characterId"] = "fixture_character",
                ["foundationInstanceId"] = "foundation_fixture",
                ["foundationDefinitionId"] = "fixture_foundation_definition",
                ["sourceGongFaId"] = "fixture_gongfa",
                ["stageId"] = "FOUNDATION",
                ["stageCode"] = "10",
                ["continuousProgress"] = "0",
                ["phaseBoundarySetId"] = "fixture_phase_boundaries",
                ["naturalMansionCapacity"] = "0",
                ["expansionGrants"] = "none",
                ["expandedMansionCapacity"] = "0",
                ["carryingCapacityProfileId"] = "fixture_carrying",
                ["currentMansionCarryingCapacity"] = "0",
                ["completionMansionCapacity"] = "0",
                ["mansionStates"] = NotBuiltMansions(),
                ["effectBindings"] = "none",
                ["guardianAbilities"] = "none",
                ["enhancementNodes"] = "none",
                ["cultivationActionState"] = "none",
                ["closedRetreatPlan"] = "none",
                ["jindanLock"] = "PRE_JINDAN",
                ["fixtureId"] = "fpm.valid.foundation-empty",
                ["expect"] = "ACCEPT",
                ["fixtureOnlyNumericProfile"] = "fixture_phase_boundaries~fixture_carrying~0",
            };

            if (fixture == "foundation") return values;

            ApplyCompleteMing(values);
            if (fixture == "complete") return values;
            if (fixture == "completeThenExpand")
            {
                values["expansionGrants"] = "grant_one~fixture_item_one~capacity_effect_one";
                values["expandedMansionCapacity"] = "1";
                values["effectBindings"] += "|capacity_effect_one~EXPANSION_GRANT~grant_one~1~grant_applied~none~mansion_capacity~MANSION_CAPACITY_PLUS_ONE~profileRef:fixture_numeric";
                return values;
            }
            if (fixture == "pausedEmbryo")
            {
                values["stageId"] = "MANSION";
                values["stageCode"] = "20";
                values["naturalMansionCapacity"] = "2";
                values["currentMansionCarryingCapacity"] = "2";
                values["completionMansionCapacity"] = "0";
                values["mansionStates"] =
                    "MING~COMPLETE~mansion_ming~MANSION_BODY_MING_YUAN_HUIHU~guardian_ming~fixture_spell_ming~fixture_upgrade_ming~RETAIN|" +
                    "HUN~EMBRYO~embryo_hun~fixture_spell_hun~fixture_upgrade_hun~20~progress_hun~action_hun|" +
                    "SHI~NOT_BUILT|WU~NOT_BUILT|YUN~NOT_BUILT";
                values["cultivationActionState"] = "action_hun~MANSION_EMBRYO_NURTURE~PAUSED~embryo_hun~fixture_cycle~fixture_boundary~none~progress_hun~fixture_numeric";
                values["closedRetreatPlan"] = "action_hun~embryo_hun~MANUAL_PAUSE";
                values["fixtureId"] = "fpm.valid.paused-embryo";
                values["fixtureOnlyNumericProfile"] = "fixture_phase_boundaries~fixture_carrying~2";
                return values;
            }
            if (fixture == "legacyStage")
            {
                values["schemaVersion"] = "1";
                values["stageId"] = "PHASE_4";
                values["stageCode"] = "2";
                values["expect"] = "REJECT";
                return values;
            }
            if (fixture == "overflow")
            {
                values["currentMansionCarryingCapacity"] = "0";
                values["expect"] = "REJECT";
                return values;
            }
            if (fixture == "missingBinding")
            {
                values["guardianAbilities"] = "none";
                values["expect"] = "REJECT";
                return values;
            }
            throw new ArgumentOutOfRangeException(nameof(fixture));
        }

        private static void ApplyCompleteMing(Dictionary<string, string> values)
        {
            values["stageId"] = "COMPLETE";
            values["stageCode"] = "30";
            values["continuousProgress"] = "100";
            values["naturalMansionCapacity"] = "1";
            values["currentMansionCarryingCapacity"] = "1";
            values["completionMansionCapacity"] = "1";
            values["mansionStates"] =
                "MING~COMPLETE~mansion_ming~MANSION_BODY_MING_YUAN_HUIHU~guardian_ming~fixture_spell_ming~fixture_upgrade_ming~RETAIN|" +
                "HUN~NOT_BUILT|SHI~NOT_BUILT|WU~NOT_BUILT|YUN~NOT_BUILT";
            values["effectBindings"] = "MANSION_BODY_MING_YUAN_HUIHU~MANSION_BODY~mansion_ming~1~trigger_ming~none~target_ming~atomic_ming~profileRef:fixture_numeric";
            values["guardianAbilities"] = "guardian_ming~fixture_ability_ming~mansion_ming~fixture_spell_ming~fixture_upgrade_ming~RETAIN~PASSIVE~none";
            values["fixtureId"] = "fpm.valid.one-complete-mansion";
            values["fixtureOnlyNumericProfile"] = "fixture_phase_boundaries~fixture_carrying~1";
        }

        private static string NotBuiltMansions()
        {
            return "MING~NOT_BUILT|HUN~NOT_BUILT|SHI~NOT_BUILT|WU~NOT_BUILT|YUN~NOT_BUILT";
        }

        private static string BuildRow(IReadOnlyDictionary<string, string> values)
        {
            var row = new string[Columns.Length];
            for (int index = 0; index < Columns.Length; index++) row[index] = values[Columns[index]];
            return string.Join(",", row);
        }
    }
}
