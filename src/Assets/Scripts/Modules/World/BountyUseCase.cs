using System;
using System.Collections.Generic;
using TianZhang.Content;

namespace TianZhang.World
{
    public sealed class BountyActionResult
    {
        private BountyActionResult(bool succeeded, string failureReason)
        {
            Succeeded = succeeded;
            FailureReason = failureReason ?? string.Empty;
        }

        public bool Succeeded { get; }
        public string FailureReason { get; }
        public static BountyActionResult Success() { return new BountyActionResult(true, string.Empty); }
        public static BountyActionResult Rejected(string reason) { return new BountyActionResult(false, reason); }
    }

    public static class BountyUseCaseReasons
    {
        public const string SupportedObjectiveType = "defeat_enemy";
        public const string SupportedRepeatPolicy = "one_time";
        public const string CatalogMissing = "bounty_catalog_missing";
        public const string BountyIdInvalid = "bounty_id_invalid";
        public const string BountyMissing = "bounty_missing";
        public const string BountyNotProduction = "bounty_not_production";
        public const string WrongSettlement = "bounty_wrong_settlement";
        public const string SettlementMissing = "bounty_settlement_missing";
        public const string RepeatedAccept = "bounty_accept_repeated";
        public const string ObjectiveUnsupported = "bounty_objective_type_unsupported";
        public const string RepeatUnsupported = "bounty_repeat_policy_unsupported";
        public const string RequiredCountInvalid = "bounty_required_count_invalid";
        public const string TargetInvalid = "bounty_target_invalid";
        public const string TargetEnemyMissing = "bounty_target_enemy_missing";
        public const string AdventureInvalid = "bounty_adventure_invalid";
        public const string RewardInvalid = "bounty_reward_invalid";
        public const string RewardItemMissing = "bounty_reward_item_missing";
        public const string RewardItemNotProduction = "bounty_reward_item_not_production";
        public const string RewardItemStackInvalid = "bounty_reward_item_stack_invalid";
        public const string NotAccepted = "bounty_not_accepted";
        public const string NotCompleted = "bounty_not_completed";
        public const string RepeatedClaim = "bounty_claim_repeated";
        public const string WrongAdventure = "bounty_defeat_wrong_adventure";
        public const string WrongEnemy = "bounty_defeat_wrong_enemy";
        public const string ProgressInvalid = "bounty_progress_invalid";
        public const string ProgressOutOfRange = "bounty_progress_out_of_range";
        public const string InventoryRejected = "bounty_claim_inventory_rejected";
    }

    /// <summary>Only application entry point for bounty state transitions.</summary>
    public sealed class BountyUseCase
    {
        private readonly BountyStore bounties;
        private readonly InventoryStore inventory;

        public BountyUseCase(BountyStore bounties, InventoryStore inventory)
        {
            this.bounties = bounties ?? throw new ArgumentNullException(nameof(bounties));
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        }

        public BountyActionResult Accept(ContentCatalogData catalog, string bountyId, string settlementId)
        {
            if (catalog == null) return BountyActionResult.Rejected(BountyUseCaseReasons.CatalogMissing);
            if (string.IsNullOrWhiteSpace(bountyId)) return BountyActionResult.Rejected(BountyUseCaseReasons.BountyIdInvalid);
            if (string.IsNullOrWhiteSpace(settlementId)) return BountyActionResult.Rejected(BountyUseCaseReasons.WrongSettlement);
            BountyState ignored;
            if (bounties.TryGet(bountyId, out ignored)) return BountyActionResult.Rejected(BountyUseCaseReasons.RepeatedAccept);
            BountyData bounty;
            if (!catalog.TryGetBounty(bountyId, out bounty) || bounty == null)
                return BountyActionResult.Rejected(BountyUseCaseReasons.BountyMissing);
            string reason = ValidateForAccept(catalog, bounty, settlementId);
            if (reason != null) return BountyActionResult.Rejected(reason);
            bounties.Set(new BountyState(bountyId, BountyStatus.Accepted, 0));
            return BountyActionResult.Success();
        }

        public BountyActionResult RecordDefeat(ContentCatalogData catalog, string adventureId, string enemyId)
        {
            if (catalog == null) return BountyActionResult.Rejected(BountyUseCaseReasons.CatalogMissing);
            if (string.IsNullOrWhiteSpace(adventureId)) return BountyActionResult.Rejected(BountyUseCaseReasons.AdventureInvalid);
            if (string.IsNullOrWhiteSpace(enemyId)) return BountyActionResult.Rejected(BountyUseCaseReasons.TargetInvalid);
            string firstRejection = null;
            bool applied = false;
            foreach (BountyState state in bounties.Capture().States)
            {
                if (state.Status != BountyStatus.Accepted) continue;
                string reason = TryRecordDefeat(state, catalog, adventureId, enemyId);
                if (reason == null) applied = true;
                else if (firstRejection == null) firstRejection = reason;
            }
            return applied
                ? BountyActionResult.Success()
                : BountyActionResult.Rejected(firstRejection ?? BountyUseCaseReasons.NotAccepted);
        }

        public BountyActionResult Claim(ContentCatalogData catalog, string bountyId)
        {
            if (catalog == null) return BountyActionResult.Rejected(BountyUseCaseReasons.CatalogMissing);
            if (string.IsNullOrWhiteSpace(bountyId)) return BountyActionResult.Rejected(BountyUseCaseReasons.BountyIdInvalid);
            BountyState state;
            if (!bounties.TryGet(bountyId, out state)) return BountyActionResult.Rejected(BountyUseCaseReasons.NotCompleted);
            if (state.Status == BountyStatus.Claimed) return BountyActionResult.Rejected(BountyUseCaseReasons.RepeatedClaim);
            if (state.Status != BountyStatus.ObjectiveCompleted) return BountyActionResult.Rejected(BountyUseCaseReasons.NotCompleted);
            BountyData bounty;
            if (!catalog.TryGetBounty(bountyId, out bounty) || bounty == null)
                return BountyActionResult.Rejected(BountyUseCaseReasons.BountyMissing);
            if (state.Progress != bounty.requiredCount) return BountyActionResult.Rejected(BountyUseCaseReasons.ProgressInvalid);
            string rewardReason = ValidateRewards(catalog, bounty);
            if (rewardReason != null) return BountyActionResult.Rejected(rewardReason);

            var requests = new List<InventoryGrantRequest>();
            foreach (BountyRewardEntry reward in bounty.rewardEntries)
                requests.Add(new InventoryGrantRequest(reward.itemId, reward.quantity));
            InventoryStoreSnapshot nextInventory;
            InventoryGrantFailureReason inventoryReason;
            if (!InventoryGrantUseCase.TryBuildGrant(inventory, catalog, requests, out nextInventory, out inventoryReason))
            {
                return BountyActionResult.Rejected(BountyUseCaseReasons.InventoryRejected + ":" + inventoryReason);
            }

            var nextBounties = new List<BountyState>();
            foreach (BountyState existing in bounties.Capture().States)
            {
                nextBounties.Add(existing.BountyId == bountyId
                    ? new BountyState(existing.BountyId, BountyStatus.Claimed, existing.Progress)
                    : existing);
            }
            inventory.Replace(nextInventory);
            bounties.Replace(new BountyStoreSnapshot(nextBounties));
            return BountyActionResult.Success();
        }

        public BountyState GetState(string bountyId)
        {
            if (string.IsNullOrWhiteSpace(bountyId)) throw new ArgumentException("Bounty ID is required.", nameof(bountyId));
            BountyState state;
            return bounties.TryGet(bountyId, out state)
                ? state
                : new BountyState(bountyId, BountyStatus.Available, 0);
        }

        private string TryRecordDefeat(BountyState state, ContentCatalogData catalog, string adventureId, string enemyId)
        {
            BountyData bounty;
            if (!catalog.TryGetBounty(state.BountyId, out bounty) || bounty == null) return BountyUseCaseReasons.BountyMissing;
            if (!string.Equals(bounty.contentScope, InventoryGrantUseCase.ProductionContentScope, StringComparison.Ordinal)) return BountyUseCaseReasons.BountyNotProduction;
            if (!string.Equals(bounty.objectiveType, BountyUseCaseReasons.SupportedObjectiveType, StringComparison.Ordinal)) return BountyUseCaseReasons.ObjectiveUnsupported;
            if (!string.Equals(bounty.allowedAdventureId, adventureId, StringComparison.Ordinal)) return BountyUseCaseReasons.WrongAdventure;
            if (!string.Equals(bounty.targetEnemyId, enemyId, StringComparison.Ordinal)) return BountyUseCaseReasons.WrongEnemy;
            int next;
            try { next = checked(state.Progress + 1); }
            catch (OverflowException) { return BountyUseCaseReasons.ProgressOutOfRange; }
            if (next > bounty.requiredCount) return BountyUseCaseReasons.ProgressOutOfRange;
            bounties.Set(new BountyState(
                state.BountyId,
                next == bounty.requiredCount ? BountyStatus.ObjectiveCompleted : BountyStatus.Accepted,
                next));
            return null;
        }

        private static string ValidateForAccept(ContentCatalogData catalog, BountyData bounty, string settlementId)
        {
            if (!string.Equals(bounty.contentScope, InventoryGrantUseCase.ProductionContentScope, StringComparison.Ordinal)) return BountyUseCaseReasons.BountyNotProduction;
            if (!string.Equals(bounty.issuerSettlementId, settlementId, StringComparison.Ordinal)) return BountyUseCaseReasons.WrongSettlement;
            SettlementData ignoredSettlement;
            if (!catalog.TryGetSettlement(bounty.issuerSettlementId, out ignoredSettlement)) return BountyUseCaseReasons.SettlementMissing;
            if (!string.Equals(bounty.objectiveType, BountyUseCaseReasons.SupportedObjectiveType, StringComparison.Ordinal)) return BountyUseCaseReasons.ObjectiveUnsupported;
            if (bounty.requiredCount <= 0) return BountyUseCaseReasons.RequiredCountInvalid;
            if (string.IsNullOrWhiteSpace(bounty.targetEnemyId)) return BountyUseCaseReasons.TargetInvalid;
            EnemyData ignoredEnemy;
            if (!catalog.TryGetEnemy(bounty.targetEnemyId, out ignoredEnemy)) return BountyUseCaseReasons.TargetEnemyMissing;
            if (string.IsNullOrWhiteSpace(bounty.allowedAdventureId)) return BountyUseCaseReasons.AdventureInvalid;
            if (!string.Equals(bounty.repeatPolicy, BountyUseCaseReasons.SupportedRepeatPolicy, StringComparison.Ordinal)) return BountyUseCaseReasons.RepeatUnsupported;
            return ValidateRewards(catalog, bounty);
        }

        private static string ValidateRewards(ContentCatalogData catalog, BountyData bounty)
        {
            if (bounty.rewardEntries == null || bounty.rewardEntries.Length == 0) return BountyUseCaseReasons.RewardInvalid;
            foreach (BountyRewardEntry reward in bounty.rewardEntries)
            {
                if (reward == null || string.IsNullOrWhiteSpace(reward.itemId) || reward.quantity <= 0) return BountyUseCaseReasons.RewardInvalid;
                ItemData item;
                if (!catalog.TryGetItem(reward.itemId, out item) || item == null) return BountyUseCaseReasons.RewardItemMissing + ":" + reward.itemId;
                if (!string.Equals(item.contentScope, InventoryGrantUseCase.ProductionContentScope, StringComparison.Ordinal)) return BountyUseCaseReasons.RewardItemNotProduction + ":" + reward.itemId;
                if (item.maxStack <= 0) return BountyUseCaseReasons.RewardItemStackInvalid + ":" + reward.itemId;
                if (reward.quantity > item.maxStack) return BountyUseCaseReasons.RewardInvalid + ":" + reward.itemId;
            }
            return null;
        }
    }
}
