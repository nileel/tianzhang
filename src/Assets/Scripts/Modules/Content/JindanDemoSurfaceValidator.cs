using System;
using System.IO;
using System.Linq;

namespace TianZhang.Entity
{
    internal static class JindanDemoSurfaceValidator
    {
        public static void Validate(JindanDemoCatalogData catalog)
        {
            var rules = catalog.surfaceRules;
            Require(rules != null && rules.rulesId == "demo_20261026_water_surface_rules" &&
                rules.environmentProfileId == "env_guanzhong_jindan_demo" &&
                rules.wetMovementBurden == 1 && rules.deepMovementBurden == 2 &&
                rules.tideRadius == 1 && rules.waterlineOffset == 1 && rules.forceRank == 2 && rules.distanceBudget == 1 &&
                rules.chargeExtraActionPoints == 125000 && rules.chargeExtraMana == 200 && rules.chargeTicks == 1 &&
                rules.eyeExtraActionPoints == 125000 && rules.eyeExtraMana == 200 && rules.attackCapacity == 2400 &&
                Math.Abs(rules.waterMultiplier - 13f / 280f) < 0.00000001f && rules.chargeMultiplier == 1.25f &&
                rules.erosionDamage == 150 && !string.IsNullOrWhiteSpace(rules.sourceDocument), "SURFACE_RULES");
            Require(catalog.commands.Count(value => value.effectId == "WATER_TIDE" && value.enabled) == 2,
                "WATER_COMMAND_SET");
            Require(catalog.surfaceFires != null && catalog.surfaceFires.Length == 1 &&
                catalog.surfaceCells != null && catalog.surfaceCells.Length == 3, "SURFACE_DEFINITIONS");
            var fire = catalog.surfaceFires[0];
            Require(fire != null && fire.segmentId == "demo_20261024_segment_2" &&
                fire.sourceRef == "demo_20261026_environment_fire" &&
                catalog.GetSegment(fire.segmentId).actors.Any(value => value.characterId == fire.actorId) &&
                fire.periodTicks == 10 && fire.durationTicks == 40 && fire.capacity == 3 && fire.fuelBudget == 12 &&
                fire.attackCapacity == 2400 && fire.damageMultiplier == 0.01f &&
                !string.IsNullOrWhiteSpace(fire.sourceDocument), "SURFACE_FIRE");
            Require(catalog.surfaceCells.All(value => value != null && value.segmentId == fire.segmentId &&
                value.kind is "None" or "Burning" && value.fuel == 4) &&
                catalog.surfaceCells.Select(value => value.q + ":" + value.r).Distinct().Count() == catalog.surfaceCells.Length &&
                catalog.surfaceCells.Sum(value => value.fuel) == fire.fuelBudget, "SURFACE_FUEL");
            var initial = catalog.surfaceCells.Where(value => value.kind == "Burning").ToArray();
            Require(initial.Length == 1 && initial[0].sourceRef == fire.sourceRef &&
                catalog.surfaceCells.Where(value => value.kind != "Burning").All(value => string.IsNullOrEmpty(value.sourceRef)),
                "SURFACE_SOURCE");
        }

        private static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidDataException("JD_DEMO_" + reason);
        }
    }
}
