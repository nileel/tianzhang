using System;
using System.Collections.Generic;
using TianZhang.Content;

namespace TianZhang.Game
{
    /// <summary>
    /// 悬赏动作结果；失败时返回稳定原因且不修改任何状态。
    /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Flash；变更范围：新增文件
    /// </summary>
    public sealed class BountyActionResult
    {
        public bool Succeeded { get; }
        public string FailureReason { get; }

        private BountyActionResult(bool succeeded, string failureReason)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
        }

        public static BountyActionResult Success()
        {
            return new BountyActionResult(true, string.Empty);
        }

        public static BountyActionResult Rejected(string failureReason)
        {
            if (string.IsNullOrWhiteSpace(failureReason))
                throw new ArgumentException("Failure reason must not be empty.", nameof(failureReason));
            return new BountyActionResult(false, failureReason);
        }
    }

    /// <summary>
    /// 悬赏规则稳定原因与支持范围；只使用稳定 ID，不依赖显示名或路径。
    /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Flash；变更范围：新增文件
    /// </summary>
    public static class BountyRuntimeRules
    {
        public const string SupportedObjectiveType = "defeat_enemy";
        public const string SupportedRepeatPolicy = "one_time";

        public const string CatalogMissingReason = "bounty_catalog_missing";
        public const string BountyIdInvalidReason = "bounty_id_invalid";
        public const string BountyMissingReason = "bounty_missing";
        public const string BountyNotProductionReason = "bounty_not_production";
        public const string WrongSettlementReason = "bounty_wrong_settlement";
        public const string RepeatedAcceptReason = "bounty_accept_repeated";
        public const string ObjectiveTypeUnsupportedReason = "bounty_objective_type_unsupported";
        public const string RepeatPolicyUnsupportedReason = "bounty_repeat_policy_unsupported";
        public const string RequiredCountInvalidReason = "bounty_required_count_invalid";
        public const string TargetInvalidReason = "bounty_target_invalid";
        public const string AdventureInvalidReason = "bounty_adventure_invalid";
        public const string RewardInvalidReason = "bounty_reward_invalid";
        public const string RewardItemMissingReason = "bounty_reward_item_missing";
        public const string RewardItemNotProductionReason = "bounty_reward_item_not_production";
        public const string RewardItemStackInvalidReason = "bounty_reward_item_stack_invalid";
        public const string NotAcceptedReason = "bounty_not_accepted";
        public const string NotCompletedReason = "bounty_not_completed";
        public const string RepeatedClaimReason = "bounty_claim_repeated";
        public const string DefeatWrongAdventureReason = "bounty_defeat_wrong_adventure";
        public const string DefeatWrongEnemyReason = "bounty_defeat_wrong_enemy";
        public const string ProgressInvalidReason = "bounty_progress_invalid";
        public const string ProgressOutOfRangeReason = "bounty_progress_out_of_range";
        public const string ClaimInventoryRejectedReason = "bounty_claim_inventory_rejected";
    }

    /// <summary>
    /// 悬赏纯规则边界：只通过稳定 ID、只读目录与会话状态交互，不接触界面。
    /// ⚠️ 已修改/未审核；修改方：DeepSeek V4 Flash；变更范围：新增文件
    /// </summary>
    public sealed class BountyRuntime
    {
        private readonly InventoryGrantService inventoryGrantService = new InventoryGrantService();

        /// <summary>
        /// 接取：只接受可解析的生产范围悬赏、玩家位于发布城市且状态为 Available。
        /// </summary>
        public BountyActionResult Accept(
            BountyStateStore store,
            ContentCatalogData catalog,
            string bountyId,
            string settlementId)
        {
            if (store == null)
                throw new ArgumentNullException(nameof(store));
            if (catalog == null)
                return BountyActionResult.Rejected(BountyRuntimeRules.CatalogMissingReason);
            if (string.IsNullOrWhiteSpace(bountyId))
                return BountyActionResult.Rejected(BountyRuntimeRules.BountyIdInvalidReason);
            if (string.IsNullOrWhiteSpace(settlementId))
                return BountyActionResult.Rejected(BountyRuntimeRules.WrongSettlementReason);

            if (store.TryGet(bountyId, out _))
                return BountyActionResult.Rejected(BountyRuntimeRules.RepeatedAcceptReason);
            if (!catalog.TryGetBounty(bountyId, out BountyData bounty) || bounty == null)
                return BountyActionResult.Rejected(BountyRuntimeRules.BountyMissingReason);

            string validationReason = ValidateBountyForAccept(catalog, bounty, settlementId);
            if (validationReason != null)
                return BountyActionResult.Rejected(validationReason);

            store.Set(new BountyStateSnapshot(bountyId, BountyStatus.Accepted, 0));
            return BountyActionResult.Success();
        }

        /// <summary>
        /// 击败登记：只接受结构化 adventureId + enemyId；每个匹配的 Accepted 实例至多推进一次。
        /// </summary>
        public BountyActionResult RecordDefeat(
            BountyStateStore store,
            ContentCatalogData catalog,
            string adventureId,
            string enemyId)
        {
            if (store == null)
                throw new ArgumentNullException(nameof(store));
            if (catalog == null)
                return BountyActionResult.Rejected(BountyRuntimeRules.CatalogMissingReason);
            if (string.IsNullOrWhiteSpace(adventureId))
                return BountyActionResult.Rejected(BountyRuntimeRules.AdventureInvalidReason);
            if (string.IsNullOrWhiteSpace(enemyId))
                return BountyActionResult.Rejected(BountyRuntimeRules.TargetInvalidReason);

            string firstRejection = null;
            bool applied = false;
            foreach (BountyStateSnapshot snapshot in new List<BountyStateSnapshot>(store.Snapshots))
            {
                if (snapshot.Status != BountyStatus.Accepted)
                    continue;

                string reason = TryRecordDefeatOnInstance(store, catalog, snapshot, adventureId, enemyId);
                if (reason == null)
                {
                    applied = true;
                }
                else if (firstRejection == null)
                {
                    firstRejection = reason;
                }
            }

            if (applied)
                return BountyActionResult.Success();
            return BountyActionResult.Rejected(
                firstRejection ?? BountyRuntimeRules.NotAcceptedReason);
        }

        /// <summary>
        /// 领奖：先完整校验奖励、数量与堆叠并构造新库存快照，全部合法后才与 Claimed 状态一次性替换。
        /// </summary>
        public BountyActionResult Claim(
            BountyStateStore store,
            ContentCatalogData catalog,
            InventoryStateStore inventory,
            string bountyId)
        {
            if (store == null)
                throw new ArgumentNullException(nameof(store));
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));
            if (catalog == null)
                return BountyActionResult.Rejected(BountyRuntimeRules.CatalogMissingReason);
            if (string.IsNullOrWhiteSpace(bountyId))
                return BountyActionResult.Rejected(BountyRuntimeRules.BountyIdInvalidReason);

            if (!store.TryGet(bountyId, out BountyStateSnapshot snapshot))
                return BountyActionResult.Rejected(BountyRuntimeRules.NotCompletedReason);
            if (snapshot.Status == BountyStatus.Claimed)
                return BountyActionResult.Rejected(BountyRuntimeRules.RepeatedClaimReason);
            if (snapshot.Status != BountyStatus.ObjectiveCompleted)
                return BountyActionResult.Rejected(BountyRuntimeRules.NotCompletedReason);
            if (!catalog.TryGetBounty(bountyId, out BountyData bounty) || bounty == null)
                return BountyActionResult.Rejected(BountyRuntimeRules.BountyMissingReason);
            if (snapshot.Progress != bounty.requiredCount)
                return BountyActionResult.Rejected(BountyRuntimeRules.ProgressInvalidReason);

            string rewardReason = ValidateRewards(catalog, bounty);
            if (rewardReason != null)
                return BountyActionResult.Rejected(rewardReason);

            var requests = new List<InventoryGrantRequest>(bounty.rewardEntries.Length);
            foreach (BountyRewardEntry entry in bounty.rewardEntries)
                requests.Add(new InventoryGrantRequest(entry.itemId, entry.quantity));

            if (!inventoryGrantService.TryBuildGrant(
                    inventory,
                    catalog,
                    requests,
                    out IReadOnlyList<InventoryStateSnapshot> nextInventory,
                    out InventoryGrantFailureReason inventoryFailure))
            {
                return BountyActionResult.Rejected(
                    BountyRuntimeRules.ClaimInventoryRejectedReason + ":" + inventoryFailure);
            }

            var nextBounties = new List<BountyStateSnapshot>();
            foreach (BountyStateSnapshot existing in store.Snapshots)
            {
                nextBounties.Add(existing.BountyId == bountyId
                    ? new BountyStateSnapshot(bountyId, BountyStatus.Claimed, existing.Progress)
                    : existing);
            }

            inventoryGrantService.ApplyGrant(inventory, nextInventory);
            store.ReplaceAll(nextBounties);
            return BountyActionResult.Success();
        }

        /// <summary>
        /// 查询：无实例时返回 Available（进度 0）。
        /// </summary>
        public BountyStateSnapshot GetState(BountyStateStore store, string bountyId)
        {
            if (store == null)
                throw new ArgumentNullException(nameof(store));
            if (string.IsNullOrWhiteSpace(bountyId))
                throw new ArgumentException("Bounty ID must not be empty.", nameof(bountyId));

            return store.TryGet(bountyId, out BountyStateSnapshot snapshot)
                ? snapshot
                : new BountyStateSnapshot(bountyId, BountyStatus.Available, 0);
        }

        private static string ValidateBountyForAccept(
            ContentCatalogData catalog,
            BountyData bounty,
            string settlementId)
        {
            if (!string.Equals(
                    bounty.contentScope,
                    InventoryGrantService.ProductionContentScope,
                    StringComparison.Ordinal))
            {
                return BountyRuntimeRules.BountyNotProductionReason;
            }
            if (!string.Equals(bounty.issuerSettlementId, settlementId, StringComparison.Ordinal))
                return BountyRuntimeRules.WrongSettlementReason;
            if (!string.Equals(
                    bounty.objectiveType,
                    BountyRuntimeRules.SupportedObjectiveType,
                    StringComparison.Ordinal))
            {
                return BountyRuntimeRules.ObjectiveTypeUnsupportedReason;
            }
            if (bounty.requiredCount <= 0)
                return BountyRuntimeRules.RequiredCountInvalidReason;
            if (string.IsNullOrWhiteSpace(bounty.targetEnemyId))
                return BountyRuntimeRules.TargetInvalidReason;
            if (string.IsNullOrWhiteSpace(bounty.allowedAdventureId))
                return BountyRuntimeRules.AdventureInvalidReason;
            if (!string.Equals(
                    bounty.repeatPolicy,
                    BountyRuntimeRules.SupportedRepeatPolicy,
                    StringComparison.Ordinal))
            {
                return BountyRuntimeRules.RepeatPolicyUnsupportedReason;
            }

            return ValidateRewards(catalog, bounty);
        }

        private static string ValidateRewards(ContentCatalogData catalog, BountyData bounty)
        {
            if (bounty.rewardEntries == null || bounty.rewardEntries.Length == 0)
                return BountyRuntimeRules.RewardInvalidReason;

            foreach (BountyRewardEntry entry in bounty.rewardEntries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.itemId) || entry.quantity <= 0)
                    return BountyRuntimeRules.RewardInvalidReason;
                if (!catalog.TryGetItem(entry.itemId, out ItemData item) || item == null)
                    return BountyRuntimeRules.RewardItemMissingReason + ":" + entry.itemId;
                if (!string.Equals(
                        item.contentScope,
                        InventoryGrantService.ProductionContentScope,
                        StringComparison.Ordinal))
                {
                    return BountyRuntimeRules.RewardItemNotProductionReason + ":" + entry.itemId;
                }
                if (item.maxStack <= 0)
                    return BountyRuntimeRules.RewardItemStackInvalidReason + ":" + entry.itemId;
                if (entry.quantity > item.maxStack)
                    return BountyRuntimeRules.RewardInvalidReason + ":" + entry.itemId;
            }

            return null;
        }

        private static string TryRecordDefeatOnInstance(
            BountyStateStore store,
            ContentCatalogData catalog,
            BountyStateSnapshot snapshot,
            string adventureId,
            string enemyId)
        {
            if (!catalog.TryGetBounty(snapshot.BountyId, out BountyData bounty) || bounty == null)
                return BountyRuntimeRules.BountyMissingReason;
            if (!string.Equals(
                    bounty.contentScope,
                    InventoryGrantService.ProductionContentScope,
                    StringComparison.Ordinal))
            {
                return BountyRuntimeRules.BountyNotProductionReason;
            }
            if (!string.Equals(
                    bounty.objectiveType,
                    BountyRuntimeRules.SupportedObjectiveType,
                    StringComparison.Ordinal))
            {
                return BountyRuntimeRules.ObjectiveTypeUnsupportedReason;
            }
            if (!string.Equals(bounty.allowedAdventureId, adventureId, StringComparison.Ordinal))
                return BountyRuntimeRules.DefeatWrongAdventureReason;
            if (!string.Equals(bounty.targetEnemyId, enemyId, StringComparison.Ordinal))
                return BountyRuntimeRules.DefeatWrongEnemyReason;

            int nextProgress;
            try
            {
                nextProgress = checked(snapshot.Progress + 1);
            }
            catch (OverflowException)
            {
                return BountyRuntimeRules.ProgressOutOfRangeReason;
            }
            if (nextProgress > bounty.requiredCount)
                return BountyRuntimeRules.ProgressOutOfRangeReason;

            BountyStatus nextStatus = nextProgress == bounty.requiredCount
                ? BountyStatus.ObjectiveCompleted
                : BountyStatus.Accepted;
            store.Set(new BountyStateSnapshot(snapshot.BountyId, nextStatus, nextProgress));
            return null;
        }
    }
}
