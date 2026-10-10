using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace TianZhang.Content
{
    [CreateAssetMenu(fileName = "CombatRuleSet", menuName = "天章/内容/战斗规则")]
    public sealed class CombatRuleSetData : ScriptableObject
    {
        public int schemaVersion;
        public string ruleSetVersion;
        public string sourceDigest;
        public RealmRow[] realms = Array.Empty<RealmRow>();
        public TierRow[] tiers = Array.Empty<TierRow>();
        public EconomyRow[] economy = Array.Empty<EconomyRow>();
        public UnarmedRow[] unarmedRows = Array.Empty<UnarmedRow>();
        public bool TryValidateIdentity(out string reason)
        {
            bool valid = schemaVersion == 1 && ruleSetVersion == "AP-GROWTH@0.1" &&
                sourceDigest != null && System.Text.RegularExpressions.Regex.IsMatch(sourceDigest, "^[a-f0-9]{64}$") &&
                realms != null && realms.Length == 6 && tiers != null && tiers.Length == 3 && economy != null && economy.Length == 52 &&
                realms.All(row => row != null && row.movementNodeWeightNumerators != null) && tiers.All(row => row != null) && economy.All(row => row != null) &&
                unarmedRows != null && unarmedRows.All(row => row != null && row.TryValidate()) &&
                (unarmedRows.Length == 0 || unarmedRows.Length == 5 &&
                 unarmedRows.Select(row => row.profileId).Distinct(StringComparer.Ordinal).Count() == 5 &&
                 unarmedRows.Select(row => row.familyId + "|" + row.realmId).Distinct(StringComparer.Ordinal).Count() == 5 &&
                 HumanGrowthIncreases());
            reason = valid ? null : "combat_rule_set_invalid"; return valid;
        }
        private bool HumanGrowthIncreases()
        {
            string[] ids = { "realm_fanren", "realm_lianqi", "realm_zhuji", "realm_jindan" };
            var rows = ids.Select(id => unarmedRows.SingleOrDefault(row => row.familyId == "body_human" && row.realmId == id)).ToArray();
            return rows.All(row => row != null) && rows.Skip(1).Select((row, index) =>
                row.actionPointCostUnits > rows[index].actionPointCostUnits && row.physicalAttackCapacity > rows[index].physicalAttackCapacity).All(value => value);
        }
        [Serializable] public sealed class UnarmedRow
        {
            public string familyId;
            public string realmId;
            public string profileId;
            public string abilityTierId;
            public int actionPointCostUnits;
            public int minimumNetCostUnits;
            public int physicalAttackCapacity;
            public bool TryValidate()
            {
                bool human = familyId == "body_human" && realmId is "realm_fanren" or "realm_lianqi" or "realm_zhuji" or "realm_jindan";
                bool beast = familyId == "body_shijiahou" && realmId == "realm_lianqi";
                return (human || beast) && profileId == "basic_" + familyId + "_" + realmId.Substring(6) &&
                    abilityTierId == (realmId == "realm_fanren" ? "tier_lianqi" : "tier_" + realmId.Substring(6)) &&
                    actionPointCostUnits > 0 && minimumNetCostUnits > 0 && minimumNetCostUnits <= actionPointCostUnits && physicalAttackCapacity > 0;
            }
        }
        public string[][] ExportRealms() => realms.Select(row => row.Columns(schemaVersion, ruleSetVersion)).ToArray();
        public string[][] ExportTiers() => tiers.Select(row => row.Columns(schemaVersion, ruleSetVersion)).ToArray();
        public string[][] ExportEconomy() => economy.Select(row => row.Columns(schemaVersion, ruleSetVersion)).ToArray();
        private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
        [Serializable] public struct OptionalInteger
        {
            public bool present;
            public int value;
            public string Text => present ? N(value) : "";
        }
        [Serializable] public sealed class RealmRow
        {
            public string realmId;
            public int realmOrder;
            public string usageScope;
            public int actionPointMaximumUnits;
            public int baseMovementEfficiencyMilliUnits;
            public int legacyBaseMovementEfficiencyMilliUnits;
            public int[] movementNodeWeightNumerators;
            public int movementNodeWeightDenominator;
            public string[] Columns(int schema, string version) => new[]
            {
                N(schema), version,
                realmId,
                N(realmOrder),
                usageScope,
                N(actionPointMaximumUnits),
                N(baseMovementEfficiencyMilliUnits),
                N(legacyBaseMovementEfficiencyMilliUnits),
                string.Join("|", movementNodeWeightNumerators.Select(N)),
                N(movementNodeWeightDenominator),
            };
        }
        [Serializable] public sealed class TierRow
        {
            public string abilityTierId;
            public string realmId;
            public int actionPointReferenceUnits;
            public int spiritBudgetReference;
            public int physicalAttackCapacity;
            public int soulAttackCapacity;
            public int physicalDefenseCapacity;
            public int soulDefenseCapacity;
            public int recoverySoulAttackCapacity;
            public int rawSpiritRecoveryCapacity;
            public int reactionCapacity;
            public int referenceTurnIntervalTicks;
            public string[] Columns(int schema, string version) => new[]
            {
                N(schema), version,
                abilityTierId,
                realmId,
                N(actionPointReferenceUnits),
                N(spiritBudgetReference),
                N(physicalAttackCapacity),
                N(soulAttackCapacity),
                N(physicalDefenseCapacity),
                N(soulDefenseCapacity),
                N(recoverySoulAttackCapacity),
                N(rawSpiritRecoveryCapacity),
                N(reactionCapacity),
                N(referenceTurnIntervalTicks),
            };
        }
        [Serializable] public sealed class EconomyRow
        {
            public string recordKind;
            public string definitionRef;
            public string abilityTierId;
            public OptionalInteger repeatFirstPermille;
            public OptionalInteger repeatSecondPermille;
            public OptionalInteger repeatThirdPermille;
            public OptionalInteger repeatCappedPermille;
            public OptionalInteger realmBelowTwoPermille;
            public OptionalInteger realmBelowOnePermille;
            public OptionalInteger realmEqualPermille;
            public OptionalInteger realmAboveOnePermille;
            public OptionalInteger realmAboveTwoPermille;
            public OptionalInteger relativeStatusLimitPermille;
            public OptionalInteger probabilityPointLimitPermille;
            public OptionalInteger strongControlLimitPermille;
            public OptionalInteger burstCooldownMultiplier;
            public OptionalInteger controlCooldownMultiplier;
            public OptionalInteger recoveryCooldownMultiplier;
            public OptionalInteger movementBudgetNumerator;
            public OptionalInteger movementBudgetDenominator;
            public OptionalInteger waterBaseChancePermille;
            public OptionalInteger waterMinimumChancePermille;
            public OptionalInteger waterMindDifferenceDivisor;
            public OptionalInteger waterDefensePerStackPermille;
            public OptionalInteger waterDefenseMaxStacks;
            public OptionalInteger waterReductionMigrationNumerator;
            public OptionalInteger waterReductionMigrationDenominator;
            public OptionalInteger actionPointFactorNumerator;
            public OptionalInteger actionPointFactorDenominator;
            public OptionalInteger spiritFactorNumerator;
            public OptionalInteger spiritFactorDenominator;
            public OptionalInteger actionPointCostOverrideUnits;
            public OptionalInteger minimumNetCostOverrideUnits;
            public OptionalInteger spiritCostOverride;
            public OptionalInteger ctPenalty;
            public string cooldownClass;
            public OptionalInteger reuseCooldownOverrideTicks;
            public OptionalInteger damageMigrationNumerator;
            public OptionalInteger damageMigrationDenominator;
            public OptionalInteger damageWeightNumerator;
            public OptionalInteger damageWeightDenominator;
            public string[] Columns(int schema, string version) => new[]
            {
                N(schema), version,
                recordKind,
                definitionRef,
                abilityTierId,
                repeatFirstPermille.Text,
                repeatSecondPermille.Text,
                repeatThirdPermille.Text,
                repeatCappedPermille.Text,
                realmBelowTwoPermille.Text,
                realmBelowOnePermille.Text,
                realmEqualPermille.Text,
                realmAboveOnePermille.Text,
                realmAboveTwoPermille.Text,
                relativeStatusLimitPermille.Text,
                probabilityPointLimitPermille.Text,
                strongControlLimitPermille.Text,
                burstCooldownMultiplier.Text,
                controlCooldownMultiplier.Text,
                recoveryCooldownMultiplier.Text,
                movementBudgetNumerator.Text,
                movementBudgetDenominator.Text,
                waterBaseChancePermille.Text,
                waterMinimumChancePermille.Text,
                waterMindDifferenceDivisor.Text,
                waterDefensePerStackPermille.Text,
                waterDefenseMaxStacks.Text,
                waterReductionMigrationNumerator.Text,
                waterReductionMigrationDenominator.Text,
                actionPointFactorNumerator.Text,
                actionPointFactorDenominator.Text,
                spiritFactorNumerator.Text,
                spiritFactorDenominator.Text,
                actionPointCostOverrideUnits.Text,
                minimumNetCostOverrideUnits.Text,
                spiritCostOverride.Text,
                ctPenalty.Text,
                cooldownClass,
                reuseCooldownOverrideTicks.Text,
                damageMigrationNumerator.Text,
                damageMigrationDenominator.Text,
                damageWeightNumerator.Text,
                damageWeightDenominator.Text,
            };
        }
    }
}
