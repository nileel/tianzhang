using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Entity;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.EditMode
{
    public sealed class JindanDemoContentTests
    {
        private readonly List<Object> owned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        [Test]
        public void ProductionSourcesRegisterOnlyTheApprovedRosterAndExecutableScale()
        {
            var catalog = Read();
            Assert.That(catalog.presets.Length, Is.EqualTo(5));
            Assert.That(catalog.seats.Length, Is.EqualTo(7));
            Assert.That(catalog.abilities.Count(a => a.kind == "SOURCE_SPELL"), Is.EqualTo(7));
            Assert.That(catalog.abilities.Count(a => a.kind == "GUARDIAN"), Is.EqualTo(7));
            Assert.That(catalog.abilities.Count(a => a.kind == "FLIGHT"), Is.EqualTo(1));
            Assert.That(catalog.commands.Where(c => c.enabled).Select(c => c.commandId), Is.EquivalentTo(new[]
            { "demo_20261024_scale_deploy", "demo_20261024_scale_terminate", "demo_20261024_flight_takeoff",
                "demo_20261024_flight_ascend", "demo_20261024_flight_descend", "demo_20261024_flight_land", "demo_20261024_flight_recover",
                "demo_20261024_earth_manifest_deploy","demo_20261024_earth_manifest_terminate",
                "demo_20261024_earth_gravity_deploy","demo_20261024_earth_gravity_terminate" }));
            Assert.That(catalog.commands.Where(c => !c.enabled).All(c => !string.IsNullOrEmpty(c.unavailableReasonKey)), Is.True);
            Assert.That(JindanDemoCatalogValidator.Validate(catalog, false, out string reason), Is.True, reason);
        }

        [Test]
        public void ProductionIdentityRootsResolveSourceInstancesAndCompiledHashes()
        {
            var catalog = Read();
            var foundations = Foundations();
            foreach (var state in foundations) JindanDemoCatalogValidator.ValidateFoundation(catalog, state);
            var references = JindanDemoCompatibilityCompiler.BuildReferences(catalog, foundations);
            var states = CultivationContentImporter.ParseJindanStaticStates(
                JindanDemoCsv.MaterializeCompatibility(Lines("JindanStaticStates"), catalog), "production", references, false);
            owned.AddRange(states);
            Assert.That(states.Length, Is.EqualTo(5));
            Assert.That(states.SelectMany(s => s.stablePositionBindings).Select(b => b.positionId).Distinct().Count(), Is.EqualTo(7));
            foreach (var state in states)
            {
                Assert.That(state.mansionInputs.Length, Is.EqualTo(5));
                Assert.That(state.abilityLedgerBindings.Select(l => l.abilityInstanceId).Distinct().Count(), Is.EqualTo(state.abilityLedgerBindings.Length));
                Assert.That(state.abilityLedgerBindings.All(l => string.IsNullOrEmpty(l.chargeLedgerRef)), Is.True);
                Assert.That(state.stablePositionBindings.All(b => b.compatibilityProfileId.StartsWith("demo_compat_sha256_")), Is.True);
            }
        }

        [Test]
        public void FullProductionPreviewResolvesNumericProfilesAndIdentityRoots()
        {
            var preview = JindanDemoContentImporter.ReadPreview();
            try
            {
                Assert.That(preview.Validate(out string reason), Is.True, reason);
                Assert.That(preview.presets.Select(p => p.foundationState.foundationState.currentMansionCarryingCapacity),
                    Is.EqualTo(new[] { 1, 1, 1, 2, 2 }));
                Assert.That(preview.presets.All(p => p.foundationState != null && p.jindanState != null &&
                    p.sourceCharacterDefinition != null && p.sourceGongFa != null), Is.True);
            }
            finally { JindanDemoContentImporter.DestroyPreview(preview); }
        }

        [Test]
        public void ProductionNumericProfilesRequireExplicitRegisteredAuthority()
        {
            var lines = Lines("FoundationPurpleMansionStates");
            var missing = Assert.Throws<InvalidDataException>(() =>
                CultivationContentImporter.ParseFoundationPurpleMansionStates(lines, "production", false));
            Assert.That(missing.Message, Does.StartWith("FPM_EXTERNAL_REFERENCE_UNRESOLVED:"));
            foreach (string id in new[] { "foundation_mansion_complete_v2", "demo_20261024_capacity_1" })
            {
                var invalid = lines.Select(line => line.Replace(id, "unregistered_profile")).ToArray();
                var error = Assert.Throws<InvalidDataException>(() => JindanDemoContentImporter.ParseFoundationStates(invalid));
                Assert.That(error.Message, Is.EqualTo("JD_DEMO_FOUNDATION_SOURCE"));
            }
            var unknown = lines.Select(line => line.Replace("demo_20261024_earth", "unregistered_character")).ToArray();
            Assert.That(Assert.Throws<InvalidDataException>(() => JindanDemoContentImporter.ParseFoundationStates(unknown)).Message,
                Is.EqualTo("JD_DEMO_UNREGISTERED_FOUNDATION"));
        }

        [Test]
        public void CompilerRejectsOperationOutsideBothSourceAndPosition()
        {
            var catalog = Read();
            var contract = catalog.contracts.Single(c => c.effectId == "SPACE_SCALE");
            contract.operationSchema = new[] { "TELEPORT" };
            Assert.That(Assert.Throws<InvalidDataException>(() => JindanDemoCompatibilityCompiler.Compile(catalog, contract)).Message,
                Is.EqualTo("JD_DEMO_INCOMPATIBLE_OPERATION"));
        }

        [Test]
        public void CompilerRejectsUnregisteredSourceRatherThanTrustingAContractId()
        {
            var catalog = Read();
            catalog.GetAbility("demo_20261024_guardian_fangwei_dingchi").sourceSpellId = "missing_source";
            var contract = catalog.contracts.Single(c => c.effectId == "SPACE_SCALE");
            Assert.That(Assert.Throws<InvalidDataException>(() => JindanDemoCompatibilityCompiler.Compile(catalog, contract)).Message,
                Is.EqualTo("JD_DEMO_SOURCE_UNRESOLVED"));
        }

        [Test]
        public void FrozenHashIsOrderIndependentButChangesWithActualPrice()
        {
            var catalog = Read();
            var contract = catalog.contracts.Single(c => c.effectId == "SPACE_SCALE");
            string before = JindanDemoCompatibilityCompiler.Compile(catalog, contract);
            Array.Reverse(contract.mediumSchema);
            Assert.That(JindanDemoCompatibilityCompiler.Compile(catalog, contract), Is.EqualTo(before));
            catalog.GetCommand(contract.costSchemaId).manaCost++;
            Assert.That(JindanDemoCompatibilityCompiler.Compile(catalog, contract), Is.Not.EqualTo(before));
            Assert.That(JindanDemoCatalogValidator.Validate(catalog, false, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo("JD_DEMO_STALE_COMPILATION"));
        }

        [Test]
        public void PositionVersionAndConcreteCarrierAreRequired()
        {
            var catalog = Read();
            var contract = catalog.contracts.Single(c => c.effectId == "SPACE_SCALE");
            contract.positionProfileVersion = "1.2";
            Assert.Throws<InvalidDataException>(() => JindanDemoCompatibilityCompiler.Compile(catalog, contract));
            Assert.That(JindanDemoCompatibilityCompiler.VersionCode("1.1"), Is.EqualTo(101));
            Assert.That(JindanDemoCompatibilityCompiler.VersionCode("1.3"), Is.EqualTo(103));
        }

        [Test]
        public void EnablingAnUnimplementedAbilityFailsClosed()
        {
            var catalog = Read();
            var flight = catalog.commands.First(c => c.effectId == "WATER_TIDE");
            flight.enabled = true; flight.unavailableReasonKey = string.Empty;
            Assert.That(JindanDemoCatalogValidator.Validate(catalog, false, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo("JD_DEMO_UNIMPLEMENTED_COMMAND_ENABLED"));
        }

        [Test]
        public void TwoEarthCandidatesShareOneGuardianAndOneSeat()
        {
            var catalog = Read();
            var earth = catalog.GetSeat("demo_20261024_earth_seat");
            Assert.That(earth.candidateEffectIds, Is.EquivalentTo(new[] { "EARTH_MANIFEST", "EARTH_GRAVITY" }));
            var compiled = JindanDemoCompatibilityCompiler.BuildReferences(catalog, Foundations()).compatibilityProfiles
                .Where(c => c.positionId == earth.positionId).ToArray();
            Assert.That(compiled.Length, Is.EqualTo(2));
            Assert.That(compiled.All(c => c.primaryCarrierAbilityInstanceId == earth.guardianAbilityInstanceId), Is.True);
        }

        [Test]
        public void MansionBindingUsesInstanceIdentityAndRejectsWrongModule()
        {
            var lines = Lines("FoundationPurpleMansionStates");
            var states = Foundations();
            var earth = states.Single(s => s.characterId == "demo_20261024_earth");
            Assert.That(earth.mansionStates.Single(m => m.state == PurpleMansionBuildState.Complete).mansionBodyEffectBindingId,
                Is.EqualTo("demo_20261024_earth_ming_body"));
            var invalid = lines.Select(l => l.Replace("SELF~MANSION_BODY_MING_YUAN_HUIHU~", "SELF~MANSION_BODY_HUN_LINGTAI_DINGPO~")).ToArray();
            Assert.Throws<InvalidDataException>(() => JindanDemoContentImporter.ParseFoundationStates(invalid));
        }

        [Test]
        public void SharedMpInputsAndSwitchGateAreExplicitWithoutGrowthInference()
        {
            var catalog = Read();
            Assert.That(catalog.presets.All(p => p.manaMaximum == 6000 && p.actionPointMaximumUnits == 1000000 &&
                p.movementEfficiencyMilliUnits == 4800 && p.normalVisionRange >= 4), Is.True);
            Assert.That(catalog.loadoutTuning.switchActionPointCostMode, Is.EqualTo("SNAPSHOT_MAX_AP"));
            Assert.That(catalog.loadoutTuning.activationGate, Is.EqualTo("NEXT_OWN_TURN_START"));
            Assert.That(catalog.GetCommand("demo_20261024_scale_deploy").metricDistanceModes, Is.EqualTo(new[] { 1, 4 }));
            var third = catalog.GetSegment("demo_20261024_segment_3");
            Assert.That(third.actors.Select(a => a.characterId), Is.EqualTo(new[]
            { "demo_20261024_space_yang", "demo_20261024_seal_yin" }));
        }

        private JindanDemoCatalogData Read()
        {
            var catalog = JindanDemoContentImporter.ReadDefinitions(); owned.Add(catalog); return catalog;
        }
        private FoundationPurpleMansionStateData[] Foundations()
        {
            var values = JindanDemoContentImporter.ParseFoundationStates(Lines("FoundationPurpleMansionStates"));
            owned.AddRange(values); return values;
        }
        private static string[] Lines(string table) => File.ReadAllLines("Assets/DataConfig/" + table + ".csv");
    }
}
