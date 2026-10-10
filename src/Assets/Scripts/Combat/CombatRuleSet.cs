using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TianZhang.Combat
{
    /// <summary>Immutable, non-Unity schema 1 projection. CSV tokenization belongs to the caller.</summary>
    public sealed class CombatRuleSet
    {
        public const string Version = "AP-GROWTH@0.1";
        public const int SchemaVersion = 1;
        public const int AttackSchemaVersion = 2;
        public static readonly string[] AttackColumns = "attackProfileId,displayNameKey,profileKind,basicBindingKind,contentScope,sourceAffiliation,realmRequirementId,elementRequirementId,effectType,damageElementId,physicalDamageMultiplier,soulDamageMultiplier,healAmount,buffMultiplier,defensePenetration,resourceKind,resourceCost,actionPointCostUnits,minimumNetCostUnits,ctPenalty,reuseCooldownTicks,minCastRange,maxCastRange,targetingMode,areaCenterKind,areaShapeKind,areaRadius,areaLength,areaFanHalfAngleSteps,areaFacing,areaInnerRadius,areaEffectBlockers,areaAllowedFactions,areaAllowedStates,isDomain,isBloodline,specialEffectTextKey,spiritRecoveryMultiplier,minimumSpiritRecovery,otherTargetResourceCost,otherTargetRecoveryMultiplier,ruleSetVersion,abilityTierId,cooldownClass,effectBudgetMode,efficiencyNumerator,efficiencyDenominator,damageMigrationNumerator,damageMigrationDenominator,damageWeightNumerator,damageWeightDenominator,statusId,statusAttributeId,statusRelativePermille,statusDurationOwnTurnEnds,statusStackLimit,statusRefreshPolicy,controlChancePermille,controlUsesMindContest,attackSchemaVersion,unarmedFamilyId,unarmedRealmId,unarmedPhysicalAttackCapacity".Split(',');
        public static readonly string[] RealmColumns = "schemaVersion,ruleSetVersion,realmId,realmOrder,usageScope,actionPointMaximumUnits,baseMovementEfficiencyMilliUnits,legacyBaseMovementEfficiencyMilliUnits,movementNodeWeightNumerators,movementNodeWeightDenominator".Split(',');
        public static readonly string[] TierColumns = "schemaVersion,ruleSetVersion,abilityTierId,realmId,actionPointReferenceUnits,spiritBudgetReference,physicalAttackCapacity,soulAttackCapacity,physicalDefenseCapacity,soulDefenseCapacity,recoverySoulAttackCapacity,rawSpiritRecoveryCapacity,reactionCapacity,referenceTurnIntervalTicks".Split(',');
        public static readonly string[] EconomyColumns = "schemaVersion,ruleSetVersion,recordKind,definitionRef,abilityTierId,repeatFirstPermille,repeatSecondPermille,repeatThirdPermille,repeatCappedPermille,realmBelowTwoPermille,realmBelowOnePermille,realmEqualPermille,realmAboveOnePermille,realmAboveTwoPermille,relativeStatusLimitPermille,probabilityPointLimitPermille,strongControlLimitPermille,burstCooldownMultiplier,controlCooldownMultiplier,recoveryCooldownMultiplier,movementBudgetNumerator,movementBudgetDenominator,waterBaseChancePermille,waterMinimumChancePermille,waterMindDifferenceDivisor,waterDefensePerStackPermille,waterDefenseMaxStacks,waterReductionMigrationNumerator,waterReductionMigrationDenominator,actionPointFactorNumerator,actionPointFactorDenominator,spiritFactorNumerator,spiritFactorDenominator,actionPointCostOverrideUnits,minimumNetCostOverrideUnits,spiritCostOverride,ctPenalty,cooldownClass,reuseCooldownOverrideTicks,damageMigrationNumerator,damageMigrationDenominator,damageWeightNumerator,damageWeightDenominator".Split(',');
        public static readonly string[] DefinitionIds = "PhysicalArt,MagicArt,WaterArt,TaiyiArt,TaiyiFuxiuArt,TaixuArt,YuqingArt,YuqingLeijieArt,PhysicalDivine,MagicDivine,WaterDivine,TaiyiDivine,TaiyiFuxiuDivine,TaixuDivine,YuqingDivine,YuqingLeijieDivine,KuxingAction".Split(',');
        public IReadOnlyList<RealmGrowth> Realms { get; }
        public IReadOnlyList<EffectTier> Tiers { get; }
        public IReadOnlyList<EconomyDefinition> Economy { get; }
        public IReadOnlyList<UnarmedRow> UnarmedRows { get; }
        public EconomyDefinition Global => Economy.Single(row => row.RecordKind == "global");
        public string RuleSetVersion => Version;
        public string SourceDigest { get; }

        private CombatRuleSet(RealmGrowth[] realms, EffectTier[] tiers, EconomyDefinition[] economy, string digest,
            UnarmedRow[] unarmedRows = null)
        {
            Realms = Array.AsReadOnly(realms); Tiers = Array.AsReadOnly(tiers);
            Economy = Array.AsReadOnly(economy); SourceDigest = digest;
            UnarmedRows = Array.AsReadOnly(ValidateUnarmedRows(unarmedRows ?? Array.Empty<UnarmedRow>()));
        }
        public CombatRuleSet WithUnarmedRows(IEnumerable<UnarmedRow> rows) =>
            new CombatRuleSet(Realms.ToArray(), Tiers.ToArray(), Economy.ToArray(), SourceDigest,
                rows?.ToArray() ?? throw new InvalidDataException("unarmed_rows_missing"));
        public UnarmedRow GetUnarmed(string familyId, string realmId) =>
            UnarmedRows.SingleOrDefault(row => row.FamilyId == familyId && row.RealmId == realmId)
            ?? throw new InvalidDataException("unarmed_mapping_missing");

        // Same raw schema gate for Unity import, BattleSim and the data-chain checker.
        // Parameters belong only to AttackProfiles; this is a read-only derived mapping.
        public static UnarmedRow[] ParseUnarmedRows(IReadOnlyList<string[]> table)
        {
            if (table == null || table.Count < 2 || !table[0].SequenceEqual(AttackColumns, StringComparer.Ordinal))
                throw new InvalidDataException("attack_profile_header_not_exact");
            var result = new List<UnarmedRow>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in table.Skip(1))
            {
                if (row == null || row.Length != AttackColumns.Length)
                    throw new InvalidDataException("attack_profile_short_or_extra_row");
                string V(string field) => row[Array.IndexOf(AttackColumns, field)];
                if (V("attackSchemaVersion") != "2") throw new InvalidDataException("attack_schema_version_unknown");
                if (!ids.Add(V("attackProfileId"))) throw new InvalidDataException("attack_profile_id_duplicate");
                if (V("attackProfileId") == "basic_unarmed" || V("effectBudgetMode") == "legacy_unarmed")
                    throw new InvalidDataException("legacy_unarmed_rejected");
                if (V("effectBudgetMode") != "fixed_unarmed")
                {
                    if (V("basicBindingKind") == "unarmed_body" || V("unarmedFamilyId") != "" ||
                        V("unarmedRealmId") != "" || V("unarmedPhysicalAttackCapacity") != "")
                        throw new InvalidDataException("non_unarmed_fields_present");
                    continue;
                }
                var mapping = new UnarmedRow(V("unarmedFamilyId"), V("unarmedRealmId"), V("attackProfileId"),
                    V("abilityTierId"), Integer(V("actionPointCostUnits"), true), Integer(V("minimumNetCostUnits"), true),
                    Integer(V("unarmedPhysicalAttackCapacity"), true));
                if (V("ruleSetVersion") != Version || V("profileKind") != "basic" || V("basicBindingKind") != "unarmed_body" ||
                    V("displayNameKey") != "attack_profile_" + mapping.FamilyId ||
                    V("effectType") != "physical" || V("damageElementId") != "element_none" || V("resourceKind") != "none" ||
                    Integer(V("resourceCost")) != 0 || Integer(V("ctPenalty")) != 0 || Integer(V("reuseCooldownTicks")) != 0 ||
                    Integer(V("minCastRange")) != 1 || Integer(V("maxCastRange")) != 1 ||
                    V("targetingMode") != "single" || V("cooldownClass") != "basic_repeat" ||
                    !float.TryParse(V("physicalDamageMultiplier"), NumberStyles.Float, CultureInfo.InvariantCulture, out float multiplier) ||
                    float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier <= 0)
                    throw new InvalidDataException("fixed_unarmed_contract_invalid");
                string[] allowed = { "attackProfileId", "displayNameKey", "profileKind", "basicBindingKind", "effectType",
                    "damageElementId", "physicalDamageMultiplier", "resourceKind", "resourceCost", "actionPointCostUnits",
                    "minimumNetCostUnits", "ctPenalty", "reuseCooldownTicks", "minCastRange", "maxCastRange", "targetingMode",
                    "ruleSetVersion", "abilityTierId", "cooldownClass", "effectBudgetMode", "attackSchemaVersion",
                    "unarmedFamilyId", "unarmedRealmId", "unarmedPhysicalAttackCapacity" };
                if (AttackColumns.Where(field => !allowed.Contains(field, StringComparer.Ordinal)).Any(field => V(field) != ""))
                    throw new InvalidDataException("fixed_unarmed_foreign_fields_present");
                result.Add(mapping);
            }
            return ValidateUnarmedRows(result.ToArray());
        }
        private static UnarmedRow[] ValidateUnarmedRows(UnarmedRow[] rows)
        {
            if (rows.Any(row => row == null) || rows.Select(row => row.ProfileId).Distinct(StringComparer.Ordinal).Count() != rows.Length ||
                rows.Select(row => row.FamilyId + "|" + row.RealmId).Distinct(StringComparer.Ordinal).Count() != rows.Length)
                throw new InvalidDataException("unarmed_mapping_duplicate_or_null");
            if (rows.Length == 0) return rows; // CONFIG production has no body attacks yet.
            if (rows.Length != 5) throw new InvalidDataException("unarmed_mapping_incomplete");
            string[] realms = { "realm_fanren", "realm_lianqi", "realm_zhuji", "realm_jindan" };
            var human = realms.Select(realm => rows.SingleOrDefault(row => row.FamilyId == "body_human" && row.RealmId == realm)).ToArray();
            if (human.Any(row => row == null) || !rows.Any(row => row.FamilyId == "body_shijiahou" && row.RealmId == "realm_lianqi"))
                throw new InvalidDataException("unarmed_mapping_incomplete");
            for (int index = 1; index < human.Length; index++)
                if (human[index].ActionPointCostUnits <= human[index - 1].ActionPointCostUnits ||
                    human[index].PhysicalAttackCapacity <= human[index - 1].PhysicalAttackCapacity)
                    throw new InvalidDataException("unarmed_growth_not_increasing");
            return rows;
        }
        public sealed class UnarmedRow
        {
            public string FamilyId { get; }
            public string RealmId { get; }
            public string ProfileId { get; }
            public string AbilityTierId { get; }
            public int ActionPointCostUnits { get; }
            public int MinimumNetCostUnits { get; }
            public int PhysicalAttackCapacity { get; }
            public UnarmedRow(string familyId, string realmId, string profileId, string abilityTierId,
                int actionPointCostUnits, int minimumNetCostUnits, int physicalAttackCapacity)
            {
                bool human = familyId == "body_human" && new[] { "realm_fanren", "realm_lianqi", "realm_zhuji", "realm_jindan" }.Contains(realmId, StringComparer.Ordinal);
                bool beast = familyId == "body_shijiahou" && realmId == "realm_lianqi";
                if (!(human || beast) || profileId != "basic_" + familyId + "_" + realmId.Substring(6) ||
                    abilityTierId != (realmId == "realm_fanren" ? "tier_lianqi" : "tier_" + realmId.Substring(6)))
                    throw new InvalidDataException("unarmed_identity_invalid");
                if (actionPointCostUnits <= 0 || minimumNetCostUnits <= 0 || minimumNetCostUnits > actionPointCostUnits || physicalAttackCapacity <= 0)
                    throw new InvalidDataException("unarmed_cost_or_capacity_invalid");
                FamilyId = familyId; RealmId = realmId; ProfileId = profileId; AbilityTierId = abilityTierId;
                ActionPointCostUnits = actionPointCostUnits; MinimumNetCostUnits = minimumNetCostUnits; PhysicalAttackCapacity = physicalAttackCapacity;
            }
        }
        public EffectTier GetTier(string id) => Tiers.SingleOrDefault(row => row.AbilityTierId == id)
            ?? throw new InvalidDataException("combat_rule_tier_reference_unknown");
        public int ResolveCooldown(string tierId, string category, int? explicitOverride)
        {
            if (!new[] { "basic_repeat", "burst", "control", "recovery" }.Contains(category, StringComparer.Ordinal))
                throw new InvalidDataException("combat_rule_cooldown_class_unknown");
            GetTier(tierId);
            if (explicitOverride.HasValue) return explicitOverride.Value >= 0 ? explicitOverride.Value :
                throw new InvalidDataException("combat_rule_cooldown_invalid");
            int factor = category switch
            {
                "basic_repeat" => 0, "burst" => Global.BurstCooldownMultiplier,
                "control" => Global.ControlCooldownMultiplier, "recovery" => Global.RecoveryCooldownMultiplier,
                _ => throw new InvalidDataException("combat_rule_cooldown_class_unknown"),
            };
            return checked(GetTier(tierId).ReferenceTurnIntervalTicks * factor);
        }
        public float ResolveDamageMultiplier(string tierId, int baseAp, int spiritCost, bool noMpDivine,
            CombatRuleRatio efficiency, CombatRuleRatio migration, CombatRuleRatio weight, CombatRuleRatio penetration, bool soul)
        {
            var tier = GetTier(tierId);
            var ap = new CombatRuleRatio(baseAp, tier.ActionPointReferenceUnits);
            var mp = new CombatRuleRatio(checked((long)tier.SpiritBudgetReference + spiritCost), tier.SpiritBudgetReference);
            var budget = (noMpDivine ? ap : CombatRuleRatio.Min(ap, mp)) * efficiency * migration * weight;
            var correction = new CombatRuleRatio(100, 1) / (new CombatRuleRatio(100, 1) + penetration);
            return (budget * (soul ? new CombatRuleRatio(221, 400) : new CombatRuleRatio(65, 100)) * correction).ToSingle();
        }
        public static CombatRuleSet Parse(IReadOnlyList<string[]> realmTable, IReadOnlyList<string[]> tierTable,
            IReadOnlyList<string[]> economyTable, string sourceDigest)
        {
            if (sourceDigest == null || !System.Text.RegularExpressions.Regex.IsMatch(sourceDigest, "^[a-f0-9]{64}$"))
                throw new InvalidDataException("combat_rule_source_digest_invalid");
            var realms = Rows(realmTable, RealmColumns).Select(row => new RealmGrowth(row)).ToArray();
            var tiers = Rows(tierTable, TierColumns).Select(row => new EffectTier(row)).ToArray();
            var economy = Rows(economyTable, EconomyColumns).Select(row => new EconomyDefinition(row)).ToArray();
            string[] realmIds = { "realm_fanren", "realm_lianqi", "realm_zhuji", "realm_jindan", "realm_yuanying", "realm_huashen" };
            if (realms.Length != 6 || !realms.Select(row => row.RealmId).OrderBy(id => id, StringComparer.Ordinal)
                .SequenceEqual(realmIds.OrderBy(id => id, StringComparer.Ordinal))) throw new InvalidDataException("combat_rule_realm_set_invalid");
            foreach (var row in realms)
            {
                if (row.RealmOrder != Array.IndexOf(realmIds, row.RealmId) || row.UsageScope != (row.RealmOrder < 4 ? "player" : "reserved"))
                    throw new InvalidDataException("combat_rule_realm_identity_invalid");
                int count = row.RealmOrder switch { 0 => 0, 1 => 9, 2 or 3 => 3, _ => 4 };
                if (row.MovementNodeWeightNumerators.Count != count || (count > 0 && row.MovementNodeWeightNumerators.Sum(value => (long)value) != row.MovementNodeWeightDenominator))
                    throw new InvalidDataException("combat_rule_movement_weights_invalid");
            }
            string[] tierIds = { "tier_lianqi", "tier_zhuji", "tier_jindan" };
            if (tiers.Length != 3 || !tiers.Select(row => row.AbilityTierId).OrderBy(id => id, StringComparer.Ordinal)
                .SequenceEqual(tierIds.OrderBy(id => id, StringComparer.Ordinal))) throw new InvalidDataException("combat_rule_tier_set_invalid");
            foreach (var tier in tiers)
                if (tier.RealmId != "realm_" + tier.AbilityTierId.Substring(5)) throw new InvalidDataException("combat_rule_tier_realm_invalid");
            if (economy.Length != 52 || economy.Count(row => row.RecordKind == "global") != 1 ||
                economy.Select(row => row.DefinitionRef + "|" + row.AbilityTierId).Distinct(StringComparer.Ordinal).Count() != 52)
                throw new InvalidDataException("combat_rule_economy_set_invalid");
            var result = new CombatRuleSet(realms, tiers, economy, sourceDigest);
            foreach (var row in economy.Where(row => row.RecordKind == "test_action"))
            {
                if (!DefinitionIds.Contains(row.DefinitionRef, StringComparer.Ordinal)) throw new InvalidDataException("combat_rule_definition_unknown");
                var tier = result.GetTier(row.AbilityTierId);
                int ap = row.ActionPointCostOverrideUnits ?? CeilRatio(tier.ActionPointReferenceUnits, row.ActionPointFactorNumerator, row.ActionPointFactorDenominator);
                int mp = row.SpiritCostOverride ?? CeilRatio(tier.SpiritBudgetReference, row.SpiritFactorNumerator, row.SpiritFactorDenominator);
                int minimum = row.MinimumNetCostOverrideUnits ?? ap;
                if (ap <= 0 || minimum <= 0 || minimum > ap || mp < 0) throw new InvalidDataException("combat_rule_cost_invalid");
                result.ResolveCooldown(row.AbilityTierId, row.CooldownClass, row.ReuseCooldownOverrideTicks);
            }
            return result;
        }
        private static IEnumerable<string[]> Rows(IReadOnlyList<string[]> table, string[] columns)
        {
            if (table == null || table.Count < 2 || !table[0].SequenceEqual(columns, StringComparer.Ordinal))
                throw new InvalidDataException("combat_rule_header_not_exact");
            foreach (var row in table.Skip(1))
            {
                if (row == null || row.Length != columns.Length) throw new InvalidDataException("combat_rule_row_width_invalid");
                if (row[0] != "1" || row[1] != Version) throw new InvalidDataException("combat_rule_version_unknown");
                yield return row;
            }
        }
        public static int Integer(string value, bool positive = false)
        {
            if (string.IsNullOrEmpty(value) || value.Any(c => c < '0' || c > '9') ||
                !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed < (positive ? 1 : 0))
                throw new InvalidDataException("combat_rule_integer_invalid");
            return parsed;
        }
        public static int CeilRatio(int value, int numerator, int denominator)
        {
            if (value < 0 || numerator < 0 || denominator <= 0) throw new InvalidDataException("combat_rule_ratio_invalid");
            long product = checked((long)value * numerator);
            return checked((int)(product / denominator + (product % denominator == 0 ? 0 : 1)));
        }
        public static string ComputeSourceDigest(IReadOnlyDictionary<string, byte[]> sources)
        {
            using var sha = SHA256.Create();
            var text = new StringBuilder();
            foreach (string path in sources.Keys.OrderBy(value => value, StringComparer.Ordinal))
            {
                text.Append(path).Append('\n').Append(Hex(sha.ComputeHash(sources[path]))).Append('\n');
            }
            return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
        }
        private static string Hex(byte[] bytes) => string.Concat(bytes.Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));

        public sealed class RealmGrowth
        {
            public string RealmId { get; }
            public int RealmOrder { get; }
            public string UsageScope { get; }
            public int ActionPointMaximumUnits { get; }
            public int BaseMovementEfficiencyMilliUnits { get; }
            public int LegacyBaseMovementEfficiencyMilliUnits { get; }
            public IReadOnlyList<int> MovementNodeWeightNumerators { get; }
            public int MovementNodeWeightDenominator { get; }
            public RealmGrowth(string[] row)
            {
                RealmId = row[2]; RealmOrder = Integer(row[3]); UsageScope = row[4];
                ActionPointMaximumUnits = Integer(row[5], true); BaseMovementEfficiencyMilliUnits = Integer(row[6], true);
                LegacyBaseMovementEfficiencyMilliUnits = Integer(row[7], true);
                MovementNodeWeightNumerators = Array.AsReadOnly(row[8] == "" ? Array.Empty<int>() : row[8].Split('|').Select(value => Integer(value, true)).ToArray());
                MovementNodeWeightDenominator = Integer(row[9], true);
            }
        }
        public sealed class EffectTier
        {
            public string AbilityTierId { get; }
            public string RealmId { get; }
            public int ActionPointReferenceUnits { get; }
            public int SpiritBudgetReference { get; }
            public int PhysicalAttackCapacity { get; }
            public int SoulAttackCapacity { get; }
            public int PhysicalDefenseCapacity { get; }
            public int SoulDefenseCapacity { get; }
            public int RecoverySoulAttackCapacity { get; }
            public int RawSpiritRecoveryCapacity { get; }
            public int ReactionCapacity { get; }
            public int ReferenceTurnIntervalTicks { get; }
            public EffectTier(string[] row)
            {
                AbilityTierId = row[2]; RealmId = row[3]; ActionPointReferenceUnits = Integer(row[4], true);
                SpiritBudgetReference = Integer(row[5], true); PhysicalAttackCapacity = Integer(row[6], true);
                SoulAttackCapacity = Integer(row[7], true); PhysicalDefenseCapacity = Integer(row[8], true);
                SoulDefenseCapacity = Integer(row[9], true); RecoverySoulAttackCapacity = Integer(row[10], true);
                RawSpiritRecoveryCapacity = Integer(row[11], true); ReactionCapacity = Integer(row[12], true);
                ReferenceTurnIntervalTicks = Integer(row[13], true);
            }
        }
        public sealed class EconomyDefinition
        {
            public string RecordKind { get; }
            public string DefinitionRef { get; }
            public string AbilityTierId { get; }
            // Named schema fields are appended below; no free-form key/value rules are accepted.
            public int RepeatFirstPermille { get; }
            public int RepeatSecondPermille { get; }
            public int RepeatThirdPermille { get; }
            public int RepeatCappedPermille { get; }
            public int RealmBelowTwoPermille { get; }
            public int RealmBelowOnePermille { get; }
            public int RealmEqualPermille { get; }
            public int RealmAboveOnePermille { get; }
            public int RealmAboveTwoPermille { get; }
            public int RelativeStatusLimitPermille { get; }
            public int ProbabilityPointLimitPermille { get; }
            public int StrongControlLimitPermille { get; }
            public int BurstCooldownMultiplier { get; }
            public int ControlCooldownMultiplier { get; }
            public int RecoveryCooldownMultiplier { get; }
            public int MovementBudgetNumerator { get; }
            public int MovementBudgetDenominator { get; }
            public int WaterBaseChancePermille { get; }
            public int WaterMinimumChancePermille { get; }
            public int WaterMindDifferenceDivisor { get; }
            public int WaterDefensePerStackPermille { get; }
            public int WaterDefenseMaxStacks { get; }
            public int WaterReductionMigrationNumerator { get; }
            public int WaterReductionMigrationDenominator { get; }
            public int ActionPointFactorNumerator { get; }
            public int ActionPointFactorDenominator { get; }
            public int SpiritFactorNumerator { get; }
            public int SpiritFactorDenominator { get; }
            public int? ActionPointCostOverrideUnits { get; }
            public int? MinimumNetCostOverrideUnits { get; }
            public int? SpiritCostOverride { get; }
            public int CtPenalty { get; }
            public string CooldownClass { get; }
            public int? ReuseCooldownOverrideTicks { get; }
            public int DamageMigrationNumerator { get; }
            public int DamageMigrationDenominator { get; }
            public int DamageWeightNumerator { get; }
            public int DamageWeightDenominator { get; }
            public EconomyDefinition(string[] row)
            {
                RecordKind = row[2]; DefinitionRef = row[3]; AbilityTierId = row[4]; CooldownClass = "";
                if (RecordKind == "global")
                {
                    if (DefinitionRef != "action_economy" || AbilityTierId != "" || row.Skip(29).Any(value => value != ""))
                        throw new InvalidDataException("combat_rule_global_shape_invalid");
                    RepeatFirstPermille = Integer(row[5], true);
                    RepeatSecondPermille = Integer(row[6], true);
                    RepeatThirdPermille = Integer(row[7], true);
                    RepeatCappedPermille = Integer(row[8], true);
                    RealmBelowTwoPermille = Integer(row[9], true);
                    RealmBelowOnePermille = Integer(row[10], true);
                    RealmEqualPermille = Integer(row[11], true);
                    RealmAboveOnePermille = Integer(row[12], true);
                    RealmAboveTwoPermille = Integer(row[13], true);
                    RelativeStatusLimitPermille = Integer(row[14], true);
                    ProbabilityPointLimitPermille = Integer(row[15], true);
                    StrongControlLimitPermille = Integer(row[16], true);
                    BurstCooldownMultiplier = Integer(row[17], true);
                    ControlCooldownMultiplier = Integer(row[18], true);
                    RecoveryCooldownMultiplier = Integer(row[19], true);
                    MovementBudgetNumerator = Integer(row[20], true);
                    MovementBudgetDenominator = Integer(row[21], true);
                    WaterBaseChancePermille = Integer(row[22], true);
                    WaterMinimumChancePermille = Integer(row[23], true);
                    WaterMindDifferenceDivisor = Integer(row[24], true);
                    WaterDefensePerStackPermille = Integer(row[25], true);
                    WaterDefenseMaxStacks = Integer(row[26], true);
                    WaterReductionMigrationNumerator = Integer(row[27], true);
                    WaterReductionMigrationDenominator = Integer(row[28], true);
                    var movementBudgetRatio = new CombatRuleRatio(MovementBudgetNumerator, MovementBudgetDenominator);
                    MovementBudgetNumerator = checked((int)movementBudgetRatio.Numerator); MovementBudgetDenominator = checked((int)movementBudgetRatio.Denominator);
                    var waterReductionMigrationRatio = new CombatRuleRatio(WaterReductionMigrationNumerator, WaterReductionMigrationDenominator);
                    WaterReductionMigrationNumerator = checked((int)waterReductionMigrationRatio.Numerator); WaterReductionMigrationDenominator = checked((int)waterReductionMigrationRatio.Denominator);
                    if (RepeatFirstPermille > RepeatSecondPermille || RepeatSecondPermille > RepeatThirdPermille || RepeatThirdPermille > RepeatCappedPermille ||
                        RelativeStatusLimitPermille > 1000 || ProbabilityPointLimitPermille > 1000 || StrongControlLimitPermille > 1000 ||
                        WaterBaseChancePermille > 1000 || WaterMinimumChancePermille > WaterBaseChancePermille || WaterDefensePerStackPermille > 1000)
                        throw new InvalidDataException("combat_rule_global_value_invalid");
                }
                else if (RecordKind == "test_action")
                {
                    if (row.Skip(5).Take(24).Any(value => value != "")) throw new InvalidDataException("combat_rule_test_action_shape_invalid");
                    ActionPointFactorNumerator = Integer(row[29], true);
                    ActionPointFactorDenominator = Integer(row[30], true);
                    SpiritFactorNumerator = Integer(row[31], false);
                    SpiritFactorDenominator = Integer(row[32], true);
                    ActionPointCostOverrideUnits = row[33] == "" ? (int?)null : Integer(row[33]);
                    MinimumNetCostOverrideUnits = row[34] == "" ? (int?)null : Integer(row[34]);
                    SpiritCostOverride = row[35] == "" ? (int?)null : Integer(row[35]);
                    CtPenalty = Integer(row[36], false);
                    CooldownClass = row[37];
                    ReuseCooldownOverrideTicks = row[38] == "" ? (int?)null : Integer(row[38]);
                    DamageMigrationNumerator = Integer(row[39], true);
                    DamageMigrationDenominator = Integer(row[40], true);
                    DamageWeightNumerator = Integer(row[41], true);
                    DamageWeightDenominator = Integer(row[42], true);
                    var actionPointFactorRatio = new CombatRuleRatio(ActionPointFactorNumerator, ActionPointFactorDenominator);
                    ActionPointFactorNumerator = checked((int)actionPointFactorRatio.Numerator); ActionPointFactorDenominator = checked((int)actionPointFactorRatio.Denominator);
                    var spiritFactorRatio = new CombatRuleRatio(SpiritFactorNumerator, SpiritFactorDenominator);
                    SpiritFactorNumerator = checked((int)spiritFactorRatio.Numerator); SpiritFactorDenominator = checked((int)spiritFactorRatio.Denominator);
                    var damageMigrationRatio = new CombatRuleRatio(DamageMigrationNumerator, DamageMigrationDenominator);
                    DamageMigrationNumerator = checked((int)damageMigrationRatio.Numerator); DamageMigrationDenominator = checked((int)damageMigrationRatio.Denominator);
                    var damageWeightRatio = new CombatRuleRatio(DamageWeightNumerator, DamageWeightDenominator);
                    DamageWeightNumerator = checked((int)damageWeightRatio.Numerator); DamageWeightDenominator = checked((int)damageWeightRatio.Denominator);
                    if (CtPenalty > int.MaxValue - 100 || !new[] { "basic_repeat", "burst", "control", "recovery" }.Contains(CooldownClass, StringComparer.Ordinal))
                        throw new InvalidDataException("combat_rule_action_value_invalid");
                }
                else throw new InvalidDataException("combat_rule_record_kind_unknown");
            }
        }
    }

    /// <summary>Reduced checked Int64 arithmetic for import-time budget derivation.</summary>
    public readonly struct CombatRuleRatio
    {
        public long Numerator { get; }
        public long Denominator { get; }
        public CombatRuleRatio(long numerator, long denominator)
        {
            if (numerator < 0 || denominator <= 0) throw new InvalidDataException("combat_rule_ratio_invalid");
            long gcd = Gcd(numerator, denominator); Numerator = numerator / gcd; Denominator = denominator / gcd;
        }
        private static long Gcd(long a, long b) { while (b != 0) { long remainder = a % b; a = b; b = remainder; } return a; }
        public static CombatRuleRatio operator *(CombatRuleRatio a, CombatRuleRatio b)
        {
            long g1 = Gcd(a.Numerator, b.Denominator), g2 = Gcd(b.Numerator, a.Denominator);
            return new CombatRuleRatio(checked((a.Numerator / g1) * (b.Numerator / g2)),
                checked((a.Denominator / g2) * (b.Denominator / g1)));
        }
        public static CombatRuleRatio operator /(CombatRuleRatio a, CombatRuleRatio b) => a * new CombatRuleRatio(b.Denominator, b.Numerator);
        public static CombatRuleRatio operator +(CombatRuleRatio a, CombatRuleRatio b)
        {
            long gcd = Gcd(a.Denominator, b.Denominator);
            return new CombatRuleRatio(checked(a.Numerator * (b.Denominator / gcd) + b.Numerator * (a.Denominator / gcd)),
                checked(a.Denominator * (b.Denominator / gcd)));
        }
        public static CombatRuleRatio Min(CombatRuleRatio a, CombatRuleRatio b) =>
            checked(a.Numerator * b.Denominator) <= checked(b.Numerator * a.Denominator) ? a : b;
        public static CombatRuleRatio FromDecimal(string value)
        {
            string[] parts = value.Split('.');
            if (parts.Length > 2 || parts.Any(part => part.Length == 0 || part.Any(c => c < '0' || c > '9')))
                throw new InvalidDataException("combat_rule_decimal_invalid");
            long denominator = 1;
            if (parts.Length == 2) foreach (char unused in parts[1]) denominator = checked(denominator * 10);
            if (!long.TryParse(string.Concat(parts), NumberStyles.None, CultureInfo.InvariantCulture, out long numerator))
                throw new InvalidDataException("combat_rule_decimal_invalid");
            return new CombatRuleRatio(numerator, denominator);
        }
        public float ToSingle()
        {
            float value = (float)((double)Numerator / Denominator);
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("combat_rule_output_invalid");
            return value;
        }
    }
}
