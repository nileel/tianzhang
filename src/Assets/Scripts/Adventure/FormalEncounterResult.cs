using System;
using System.Collections.Generic;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.World;

namespace TianZhang.Adventure
{
    public interface IFormalEncounterRandomSource
    {
        int NextPercent();
    }

    public sealed class SystemFormalEncounterRandomSource : IFormalEncounterRandomSource
    {
        private readonly Random random;

        public SystemFormalEncounterRandomSource()
            : this(new Random())
        {
        }

        internal SystemFormalEncounterRandomSource(Random random)
        {
            this.random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public int NextPercent()
        {
            return random.Next(0, 100);
        }
    }

    public sealed class FormalDropGrant
    {
        public string ItemId { get; }
        public int Quantity { get; }

        public FormalDropGrant(string itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity;
        }
    }

    public sealed class FormalEncounterResult
    {
        public string EnemyId { get; }
        public string AdventureId { get; }
        public IReadOnlyList<FormalDropGrant> DropGrants { get; }
        public CombatSessionOutcome Outcome { get; }

        private FormalEncounterResult(
            string enemyId,
            string adventureId,
            IReadOnlyList<FormalDropGrant> dropGrants,
            CombatSessionOutcome outcome)
        {
            EnemyId = enemyId;
            AdventureId = adventureId;
            DropGrants = dropGrants;
            Outcome = outcome;
        }

        public static bool TryCreate(
            ContentCatalogData catalog,
            EnemyData defeatedEnemy,
            string adventureId,
            CombatSessionOutcome outcome,
            IFormalEncounterRandomSource randomSource,
            out FormalEncounterResult result,
            out string reason)
        {
            result = null;
            if (!string.Equals(
                    adventureId,
                    FormalEncounterRules.GuanzhongWildAdventureId,
                    StringComparison.Ordinal))
            {
                reason = FormalEncounterRules.AdventureMismatchReason;
                return false;
            }

            if (!FormalEncounterRules.TryResolveGuanzhongEnemy(
                    catalog,
                    out EnemyData catalogEnemy,
                    out _,
                    out reason))
            {
                return false;
            }

            if (!ReferenceEquals(catalogEnemy, defeatedEnemy))
            {
                reason = FormalEncounterRules.EnemyIdentityMismatchReason;
                return false;
            }

            if (outcome != CombatSessionOutcome.Victory &&
                outcome != CombatSessionOutcome.Defeat)
            {
                reason = FormalEncounterRules.OutcomeInvalidReason;
                return false;
            }

            var grants = new List<FormalDropGrant>();
            if (outcome == CombatSessionOutcome.Victory)
            {
                if (randomSource == null)
                {
                    reason = FormalEncounterRules.RandomSourceMissingReason;
                    return false;
                }

                foreach (EnemyDropEntry drop in defeatedEnemy.dropEntries)
                {
                    int roll = randomSource.NextPercent();
                    if (roll < 0 || roll >= 100)
                    {
                        reason = FormalEncounterRules.RandomValueInvalidReason;
                        return false;
                    }

                    if (roll < drop.dropChancePercent)
                        grants.Add(new FormalDropGrant(drop.itemId, drop.quantity));
                }
            }

            result = new FormalEncounterResult(
                defeatedEnemy.enemyId,
                adventureId,
                grants.ToArray(),
                outcome);
            reason = string.Empty;
            return true;
        }
    }

    public static class FormalEncounterRules
    {
        public const string GuanzhongWildAdventureId = "guanzhong_wild";
        public const string ShijiahouEnemyId = "enemy_shijiahou";
        public const string GuanzhongContentScope = "guanzhong";

        public const string CatalogMissingReason = "formal_encounter_catalog_missing";
        public const string EnemyMissingReason = "formal_encounter_enemy_missing";
        public const string EnemyScopeInvalidReason = "formal_encounter_enemy_scope_invalid";
        public const string CombatTemplateMissingReason = "formal_encounter_combat_template_missing";
        public const string DropsMissingReason = "formal_encounter_drops_missing";
        public const string DropInvalidReason = "formal_encounter_drop_invalid";
        public const string DropItemMissingReason = "formal_encounter_drop_item_missing";
        public const string DropItemNotProductionReason = "formal_encounter_drop_item_not_production";
        public const string DropItemStackInvalidReason = "formal_encounter_drop_item_stack_invalid";
        public const string EnemyIdentityMismatchReason = "formal_encounter_enemy_identity_mismatch";
        public const string AdventureMismatchReason = "formal_encounter_adventure_mismatch";
        public const string OutcomeInvalidReason = "formal_encounter_outcome_invalid";
        public const string RandomSourceMissingReason = "formal_encounter_random_source_missing";
        public const string RandomValueInvalidReason = "formal_encounter_random_value_invalid";
        public const string AlreadyConsumedReason = "formal_encounter_already_consumed";
        public const string SessionMissingReason = "formal_encounter_session_missing";

        public static bool TryResolveGuanzhongEnemy(
            ContentCatalogData catalog,
            out EnemyData enemy,
            out ICombatActionPolicy aiController,
            out string reason)
        {
            enemy = null;
            aiController = null;
            if (catalog == null)
            {
                reason = CatalogMissingReason;
                return false;
            }

            if (!catalog.TryGetEnemy(ShijiahouEnemyId, out enemy) || enemy == null)
            {
                enemy = null;
                reason = EnemyMissingReason;
                return false;
            }

            if (!string.Equals(enemy.enemyId, ShijiahouEnemyId, StringComparison.Ordinal) ||
                !string.Equals(
                    enemy.contentScope,
                    GuanzhongContentScope,
                    StringComparison.Ordinal))
            {
                enemy = null;
                reason = EnemyScopeInvalidReason;
                return false;
            }

            if (!EnemyAIProfileResolver.TryResolveCombatActionPolicy(
                    enemy.aiProfileId,
                    out aiController,
                    out reason))
            {
                enemy = null;
                return false;
            }

            if (enemy.combatTemplate == null)
            {
                enemy = null;
                aiController = null;
                reason = CombatTemplateMissingReason;
                return false;
            }

            if (!TryValidateDrops(catalog, enemy.dropEntries, out reason))
            {
                enemy = null;
                aiController = null;
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool TryValidateDrops(
            ContentCatalogData catalog,
            EnemyDropEntry[] drops,
            out string reason)
        {
            if (drops == null || drops.Length == 0)
            {
                reason = DropsMissingReason;
                return false;
            }

            var itemIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (EnemyDropEntry drop in drops)
            {
                if (drop == null ||
                    string.IsNullOrWhiteSpace(drop.itemId) ||
                    !itemIds.Add(drop.itemId) ||
                    drop.dropChancePercent < 0 ||
                    drop.dropChancePercent > 100 ||
                    drop.quantity <= 0)
                {
                    reason = DropInvalidReason;
                    return false;
                }

                if (!catalog.TryGetItem(drop.itemId, out ItemData item) || item == null)
                {
                    reason = DropItemMissingReason + ":" + drop.itemId;
                    return false;
                }

                if (!string.Equals(
                        item.contentScope,
                        InventoryGrantUseCase.ProductionContentScope,
                        StringComparison.Ordinal))
                {
                    reason = DropItemNotProductionReason + ":" + drop.itemId;
                    return false;
                }

                if (item.maxStack <= 0 || drop.quantity > item.maxStack)
                {
                    reason = DropItemStackInvalidReason + ":" + drop.itemId;
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }
    }
}
