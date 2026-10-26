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
    public sealed class D8WaterContentTests
    {
        private readonly List<Object> owned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        [Test]
        public void TideBranchesUseOneSourceAndExplicitTotalPrices()
        {
            var catalog = Read();
            var deploy = catalog.GetCommand("demo_20261024_water_tide_deploy");
            var terminate = catalog.GetCommand("demo_20261024_water_tide_terminate");
            var rules = catalog.surfaceRules;
            Assert.That(catalog.commands.Count(value => value.effectId == "WATER_TIDE"), Is.EqualTo(2));
            Assert.That(deploy.enabled && terminate.enabled, Is.True);
            Assert.That(deploy.abilityDefinitionId, Is.EqualTo(terminate.abilityDefinitionId));
            Assert.That(deploy.actionPointCostUnits, Is.EqualTo(250000));
            Assert.That(deploy.manaCost, Is.EqualTo(400));
            Assert.That(deploy.actionPointCostUnits + rules.chargeExtraActionPoints, Is.EqualTo(375000));
            Assert.That(deploy.manaCost + rules.chargeExtraMana, Is.EqualTo(600));
            Assert.That(deploy.actionPointCostUnits + rules.chargeExtraActionPoints + rules.eyeExtraActionPoints, Is.EqualTo(500000));
            Assert.That(deploy.manaCost + rules.chargeExtraMana + rules.eyeExtraMana, Is.EqualTo(800));
            Assert.That(rules.chargeTicks, Is.EqualTo(1));
            Assert.That(rules.waterMultiplier, Is.EqualTo(13f / 280f).Within(0.00000001f));
            Assert.That(rules.waterMultiplier * rules.chargeMultiplier, Is.EqualTo(13f / 224f).Within(0.00000001f));
            Assert.That(deploy.capacity, Is.EqualTo(7));
            Assert.That(deploy.failurePolicy, Is.EqualTo("KEEP_COMMITTED_SURFACES"));
            Assert.That(catalog.commands.Where(value => value.effectId == "FIRE_MANIFEST").All(value => !value.enabled), Is.True);
        }

        [Test]
        public void TideSupplementalPriceChangesOnlyTheWaterCompatibilityHash()
        {
            var catalog = Read();
            var before = catalog.contracts.ToDictionary(value => value.effectId,
                value => JindanDemoCompatibilityCompiler.Compile(catalog, value));
            catalog.surfaceRules.eyeExtraMana++;
            foreach (var contract in catalog.contracts)
            {
                string after = JindanDemoCompatibilityCompiler.Compile(catalog, contract);
                Assert.That(after == before[contract.effectId], Is.EqualTo(contract.effectId != "WATER_TIDE"));
            }
            Assert.That(JindanDemoCatalogValidator.Validate(catalog, false, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo("JD_DEMO_STALE_COMPILATION"));
        }

        [Test]
        public void EnvironmentFireHasFiniteFuelAndAnActualBreakableMiddleCarrier()
        {
            var catalog = Read();
            var profiles = WorldContentImporter.ParseEnvironmentProfiles(Lines("EnvironmentProfiles"), "EnvironmentProfiles");
            var environment = profiles.Single(value => value.profileId == catalog.surfaceRules.environmentProfileId);
            var fire = catalog.surfaceFires.Single();
            Assert.That(fire.periodTicks, Is.EqualTo(10));
            Assert.That(fire.durationTicks, Is.EqualTo(40));
            Assert.That(fire.capacity, Is.EqualTo(3));
            Assert.That(fire.fuelBudget, Is.EqualTo(catalog.surfaceCells.Sum(value => value.fuel)));
            Assert.That(fire.fuelBudget, Is.EqualTo(12));
            Assert.That(catalog.surfaceCells.Count(value => value.kind == "Burning"), Is.EqualTo(1));
            foreach (var cell in catalog.surfaceCells)
                Assert.That(environment.battlefieldCells.Any(value => value.q == cell.q && value.r == cell.r &&
                    !value.blocksGroundMove && value.heightLevel == 1 && value.surfaceKind == "grass_earth"), Is.True);
            var plant = catalog.surfaceCells.Single(value => !string.IsNullOrEmpty(value.fuelObjectId));
            Assert.That(environment.sceneObjects.instances.Any(value => value.instanceId == plant.fuelObjectId &&
                value.cell == plant.q + ":" + plant.r && value.contentType == "low_plant"), Is.True);
            var initial = catalog.surfaceCells.Single(value => value.kind == "Burning");
            var far = catalog.surfaceCells.Single(value => value.kind == "None" && string.IsNullOrEmpty(value.fuelObjectId));
            Assert.That(Distance(initial, plant), Is.EqualTo(1));
            Assert.That(Distance(plant, far), Is.EqualTo(1));
            Assert.That(Distance(initial, far), Is.EqualTo(2), "The plant must not be bypassed by direct spread.");
            Assert.That(environment.battlefieldCells.Any(value => value.q == 5 && value.r == 5 && value.surfaceKind == "shallow_water"), Is.True);
        }

        [Test]
        public void RehearsalActorsStandOnLegalCellsNearTheExistingWater()
        {
            var catalog = Read();
            var profiles = WorldContentImporter.ParseEnvironmentProfiles(Lines("EnvironmentProfiles"), "EnvironmentProfiles");
            var environment = profiles.Single(value => value.profileId == catalog.surfaceRules.environmentProfileId);
            var second = catalog.GetSegment("demo_20261024_segment_2");
            foreach (var actor in second.actors)
                Assert.That(environment.battlefieldCells.Any(value => value.q == actor.q && value.r == actor.r &&
                    !value.blocksGroundMove && value.heightLevel == 1), Is.True);
            Assert.That(second.actors.Single(value => value.characterId == "demo_20261024_water").q, Is.EqualTo(4));
            Assert.That(second.actors.Single(value => value.characterId == "demo_20261024_water").r, Is.EqualTo(5));
            Assert.That(catalog.GetSegment("demo_20261024_segment_1").actors[0].r, Is.EqualTo(0));
        }

        [Test]
        public void MissingPreparationAndUnboundedFireAreRejectedBeforeImport()
        {
            var catalog = Read();
            catalog.surfaceRules.chargeTicks = 0;
            var water = catalog.contracts.Single(value => value.effectId == "WATER_TIDE");
            water.compatibilityProfileId = JindanDemoCompatibilityCompiler.Compile(catalog, water);
            Assert.That(JindanDemoCatalogValidator.Validate(catalog, false, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo("JD_DEMO_SURFACE_RULES"));
            catalog.surfaceRules.chargeTicks = 1;
            water.compatibilityProfileId = JindanDemoCompatibilityCompiler.Compile(catalog, water);
            catalog.surfaceFires[0].durationTicks = 0;
            Assert.That(JindanDemoCatalogValidator.Validate(catalog, false, out reason), Is.False);
            Assert.That(reason, Is.EqualTo("JD_DEMO_SURFACE_FIRE"));
        }

        private static int Distance(JindanDemoSurfaceCell first, JindanDemoSurfaceCell second)
        {
            int q = first.q - second.q, r = first.r - second.r;
            return (Math.Abs(q) + Math.Abs(r) + Math.Abs(q + r)) / 2;
        }

        private JindanDemoCatalogData Read()
        {
            var catalog = JindanDemoContentImporter.ReadDefinitions(); owned.Add(catalog); return catalog;
        }
        private static string[] Lines(string table) => File.ReadAllLines("Assets/DataConfig/" + table + ".csv");
    }
}
