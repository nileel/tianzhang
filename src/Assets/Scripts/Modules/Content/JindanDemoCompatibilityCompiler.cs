using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TianZhang.Entity
{
    /// <summary>Compiles only the independent, explicitly registered demo contracts.</summary>
    public static class JindanDemoCompatibilityCompiler
    {
        public static string Compile(JindanDemoCatalogData catalog, JindanDemoCompatibilityContract contract)
        {
            if (catalog == null || contract == null) throw new InvalidDataException("JD_DEMO_CONTRACT_MISSING");
            var seat = catalog.seats.SingleOrDefault(s => s.positionId == contract.positionId);
            var carrier = catalog.GetAbility(contract.carrierDefinitionId);
            if (seat == null || carrier == null || carrier.kind != "GUARDIAN" ||
                seat.guardianDefinitionId != carrier.definitionId || seat.positionVersion != contract.positionProfileVersion ||
                seat.roadId != contract.roadId || seat.positionType != contract.positionType ||
                !seat.candidateEffectIds.Contains(contract.effectId, StringComparer.Ordinal))
                throw new InvalidDataException("JD_DEMO_CONTRACT_AUTHORITY");
            var source = catalog.GetAbility(carrier.sourceSpellId);
            if (source == null || source.kind != "SOURCE_SPELL" || carrier.sourceDisposition != "INTERNALIZE" ||
                carrier.upgradePlanId != source.definitionId + "_upgrade_v1")
                throw new InvalidDataException("JD_DEMO_SOURCE_UNRESOLVED");

            // Each approved operation has one declared result structure. No theme/name inference.
            Intersect(contract.objectSchema, carrier.objects, seat.allowedObjects, "OBJECT");
            Intersect(contract.operationSchema, carrier.operations, seat.allowedOperations, "OPERATION");
            Intersect(contract.mediumSchema, carrier.media, seat.allowedMedia, "MEDIUM");
            Intersect(contract.resultCarrierSchema, carrier.results, seat.allowedResults, "RESULT");
            if (string.IsNullOrWhiteSpace(contract.scopeSchema) || string.IsNullOrWhiteSpace(contract.contractVersion) ||
                contract.forbiddenContractIds == null || !contract.forbiddenContractIds.Contains("UNDECLARED") ||
                contract.forbiddenContractIds.Contains(contract.contractId))
                throw new InvalidDataException("JD_DEMO_CONTRACT_BOUNDARY");
            string filter = contract.positionType switch
            {
                JindanStaticPositionType.Source => "MANUAL",
                JindanStaticPositionType.Transformation => "FIXED_ENEMY_ENTRY",
                JindanStaticPositionType.Domain => contract.effectId == "YIN_CONCEAL" ? "OWN_FACTION" : "ALL_FACTIONS",
                _ => throw new InvalidDataException("JD_DEMO_CONTRACT_TYPE"),
            };
            if (contract.filterSchema != filter) throw new InvalidDataException("JD_DEMO_CONTRACT_FILTER");
            var cost = catalog.GetCommand(contract.costSchemaId);
            var capacity = catalog.GetCommand(contract.capacitySchemaId);
            if (cost == null || capacity != cost || cost.abilityDefinitionId != carrier.definitionId ||
                cost.effectId != contract.effectId || cost.kind != "DEPLOY" || cost.actionPointCostUnits <= 0 ||
                cost.manaCost <= 0 || cost.maximumInstances != 1 || cost.capacity <= 0 ||
                cost.upkeepMana < 0 || cost.ctPenalty < 0 || cost.cooldownTicks <= 0)
                throw new InvalidDataException("JD_DEMO_CONTRACT_LEDGER");
            if (contract.resultCarrierSchema.Contains("ABILITY") || contract.resultCarrierSchema.Contains("RESOURCE"))
                throw new InvalidDataException("JD_DEMO_CONTRACT_RECURSION");

            var frozen = new[]
            {
                catalog.contentVersion, contract.contractId, contract.contractVersion, contract.positionId,
                contract.positionProfileVersion, contract.roadId, contract.positionType.ToString(), contract.effectId,
                carrier.definitionId, carrier.definitionVersion, source.definitionId, source.definitionVersion,
                carrier.upgradePlanId, carrier.sourceDisposition, seat.guardianAbilityInstanceId,
                seat.proofProfileId, seat.proofVersion,
                Set(contract.objectSchema), Set(contract.operationSchema), Set(contract.mediumSchema),
                contract.filterSchema, contract.scopeSchema, Set(contract.resultCarrierSchema),
                Set(contract.forbiddenContractIds), Set(carrier.objects), Set(carrier.operations), Set(carrier.media),
                Set(carrier.results), Set(seat.allowedObjects), Set(seat.allowedOperations), Set(seat.allowedMedia),
                Set(seat.allowedResults), contract.costSchemaId, contract.capacitySchemaId,
                cost.actionPointCostUnits.ToString(), cost.manaCost.ToString(), cost.ctPenalty.ToString(),
                cost.cooldownTicks.ToString(), cost.upkeepMana.ToString(), cost.upkeepClock,
                cost.durationCount.ToString(), cost.durationClock, cost.maximumInstances.ToString(),
                cost.capacity.ToString(), cost.capacityUnit, cost.range.ToString(), cost.verticalReachRank.ToString(),
                cost.supportDurability.ToString(), cost.remoteOriginManaCost.ToString(), cost.sourceModeMask.ToString(),
                cost.targetModeMask.ToString(), string.Join("|", cost.metricDistanceModes), cost.failurePolicy,
            };
            if (contract.effectId == "WATER_TIDE")
            {
                var rules = catalog.surfaceRules ?? throw new InvalidDataException("JD_DEMO_SURFACE_RULES_MISSING");
                frozen = frozen.Concat(new[]
                {
                    rules.rulesId, rules.environmentProfileId,
                    rules.wetMovementBurden.ToString(CultureInfo.InvariantCulture), rules.deepMovementBurden.ToString(CultureInfo.InvariantCulture),
                    rules.tideRadius.ToString(CultureInfo.InvariantCulture), rules.waterlineOffset.ToString(CultureInfo.InvariantCulture),
                    rules.forceRank.ToString(CultureInfo.InvariantCulture), rules.distanceBudget.ToString(CultureInfo.InvariantCulture),
                    rules.chargeExtraActionPoints.ToString(CultureInfo.InvariantCulture), rules.chargeExtraMana.ToString(CultureInfo.InvariantCulture),
                    rules.chargeTicks.ToString(CultureInfo.InvariantCulture), rules.eyeExtraActionPoints.ToString(CultureInfo.InvariantCulture),
                    rules.eyeExtraMana.ToString(CultureInfo.InvariantCulture), rules.attackCapacity.ToString(CultureInfo.InvariantCulture),
                    rules.waterMultiplier.ToString("R", CultureInfo.InvariantCulture), rules.chargeMultiplier.ToString("R", CultureInfo.InvariantCulture),
                    rules.erosionDamage.ToString(CultureInfo.InvariantCulture),
                }).ToArray();
            }
            if (contract.effectId == "SPACE_SEAL")
            {
                var seal = catalog.sealRules ?? throw new InvalidDataException("JD_DEMO_SEAL_RULES_MISSING");
                frozen = frozen.Concat(new[] { seal.rulesId, seal.fromQ.ToString(CultureInfo.InvariantCulture),
                    seal.fromR.ToString(CultureInfo.InvariantCulture), seal.toQ.ToString(CultureInfo.InvariantCulture),
                    seal.toR.ToString(CultureInfo.InvariantCulture), seal.supportId, seal.localRadius.ToString(CultureInfo.InvariantCulture),
                    seal.layerDurability.ToString(CultureInfo.InvariantCulture), seal.brokenLayerTicks.ToString(CultureInfo.InvariantCulture) }).ToArray();
            }
            using var sha = SHA256.Create();
            return "demo_compat_sha256_" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
                string.Join("\n", frozen)))).Replace("-", "").ToLowerInvariant();
        }

        public static JindanStaticReferenceCatalog BuildReferences(JindanDemoCatalogData catalog,
            FoundationPurpleMansionStateData[] foundations)
        {
            var profiles = catalog.contracts.Select(c =>
            {
                var seat = catalog.seats.Single(s => s.positionId == c.positionId);
                return new JindanCompatibilityReference
                {
                    compatibilityProfileId = Compile(catalog, c), roadId = c.roadId, positionId = c.positionId,
                    equippedBaseEffectId = c.effectId, primaryCarrierAbilityInstanceId = seat.guardianAbilityInstanceId,
                    auxiliaryCarrierAbilityInstanceIds = Array.Empty<string>(),
                };
            }).ToArray();
            return new JindanStaticReferenceCatalog
            {
                foundationPurpleMansionStates = foundations,
                roads = catalog.seats.GroupBy(s => s.roadId).Select(g => new JindanRoadReference
                { roadId = g.Key, baseEffectCandidateIds = g.SelectMany(s => s.candidateEffectIds).Distinct().ToArray() }).ToArray(),
                positions = catalog.seats.Select(s => new JindanPositionReference
                {
                    positionId = s.positionId, version = VersionCode(s.positionVersion), roadId = s.roadId,
                    positionType = s.positionType, proofProfileId = s.proofProfileId,
                }).ToArray(),
                compatibilityProfiles = profiles,
                danxingDefinitionIds = Array.Empty<string>(),
                danxiangPresentationProfileIds = catalog.presets.Select(p => p.danxiangPresentationProfileId).ToArray(),
                ledgerReferences = catalog.seats.SelectMany(s => new[]
                {
                    s.guardianAbilityInstanceId + "_resource", s.guardianAbilityInstanceId + "_cooldown",
                    s.guardianAbilityInstanceId + "_cost", s.guardianAbilityInstanceId + "_conflict_reserve",
                }).ToArray(),
                conflictCostProfileIds = new[] { "demo_20261024_visibility_conflict_v1" },
            };
        }

        // Existing static schema stores an integer; encode major.minor without losing minor identity.
        public static int VersionCode(string version)
        {
            var parts = version?.Split('.');
            if (parts == null || parts.Length != 2 || !int.TryParse(parts[0], out int major) ||
                !int.TryParse(parts[1], out int minor) || major < 1 || minor < 0 || minor >= 100)
                throw new InvalidDataException("JD_DEMO_VERSION_INVALID");
            return checked(major * 100 + minor);
        }

        private static void Intersect(string[] selected, string[] carrier, string[] position, string field)
        {
            if (selected == null || carrier == null || position == null || selected.Length == 0 ||
                selected.Any(string.IsNullOrWhiteSpace) || selected.Distinct(StringComparer.Ordinal).Count() != selected.Length ||
                selected.Except(carrier, StringComparer.Ordinal).Any() || selected.Except(position, StringComparer.Ordinal).Any())
                throw new InvalidDataException("JD_DEMO_INCOMPATIBLE_" + field);
        }

        private static string Set(IEnumerable<string> values) => string.Join("|", values.OrderBy(x => x, StringComparer.Ordinal));
    }
}
