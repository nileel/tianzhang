using System;
using System.Collections.Generic;
using TianZhang.Content;

namespace TianZhang.Game
{
    public enum InventoryGrantFailureReason
    {
        None,
        EmptyRequest,
        CatalogMissing,
        ItemIdInvalid,
        QuantityInvalid,
        QuantityOverflow,
        ItemNotFound,
        ItemNotProduction,
        MaxStackInvalid,
        ExistingInventoryInvalid,
        StackLimitExceeded
    }

    public sealed class InventoryGrantRequest
    {
        public string ItemId { get; }
        public int Quantity { get; }

        public InventoryGrantRequest(string itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity;
        }
    }

    public sealed class InventoryGrantResult
    {
        public bool Applied { get; }
        public InventoryGrantFailureReason FailureReason { get; }

        private InventoryGrantResult(bool applied, InventoryGrantFailureReason failureReason)
        {
            Applied = applied;
            FailureReason = failureReason;
        }

        public static InventoryGrantResult Succeeded()
        {
            return new InventoryGrantResult(true, InventoryGrantFailureReason.None);
        }

        public static InventoryGrantResult Rejected(InventoryGrantFailureReason failureReason)
        {
            return new InventoryGrantResult(false, failureReason);
        }
    }

    public sealed class InventoryGrantService
    {
        public const string ProductionContentScope = "content_scope_production";

        /// <summary>
        /// 普通授予：先构造并校验完整新快照，全部合法后一次性替换背包。
        /// </summary>
        public InventoryGrantResult Grant(
            InventoryStateStore inventory,
            ContentCatalogData catalog,
            IReadOnlyList<InventoryGrantRequest> requests)
        {
            if (!TryBuildGrant(
                    inventory,
                    catalog,
                    requests,
                    out IReadOnlyList<InventoryStateSnapshot> candidate,
                    out InventoryGrantFailureReason failureReason))
            {
                return InventoryGrantResult.Rejected(failureReason);
            }

            ApplyGrant(inventory, candidate);
            return InventoryGrantResult.Succeeded();
        }

        /// <summary>
        /// 只构造并校验全部授予后的新库存快照，不修改背包；失败时返回稳定原因且候选为空。
        /// </summary>
        public bool TryBuildGrant(
            InventoryStateStore inventory,
            ContentCatalogData catalog,
            IReadOnlyList<InventoryGrantRequest> requests,
            out IReadOnlyList<InventoryStateSnapshot> candidate,
            out InventoryGrantFailureReason failureReason)
        {
            candidate = null;
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));
            if (catalog == null)
            {
                failureReason = InventoryGrantFailureReason.CatalogMissing;
                return false;
            }
            if (requests == null || requests.Count == 0)
            {
                failureReason = InventoryGrantFailureReason.EmptyRequest;
                return false;
            }

            var quantitiesByItemId = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (InventoryGrantRequest request in requests)
            {
                if (request == null || string.IsNullOrWhiteSpace(request.ItemId))
                {
                    failureReason = InventoryGrantFailureReason.ItemIdInvalid;
                    return false;
                }
                if (request.Quantity <= 0)
                {
                    failureReason = InventoryGrantFailureReason.QuantityInvalid;
                    return false;
                }

                if (!TryAddQuantity(quantitiesByItemId, request.ItemId, request.Quantity))
                {
                    failureReason = InventoryGrantFailureReason.QuantityOverflow;
                    return false;
                }
            }

            var candidateById = new Dictionary<string, InventoryStateSnapshot>(StringComparer.Ordinal);
            foreach (InventoryStateSnapshot snapshot in inventory.Snapshots)
            {
                if (!IsExistingSnapshotValid(snapshot, catalog))
                {
                    failureReason = InventoryGrantFailureReason.ExistingInventoryInvalid;
                    return false;
                }

                candidateById.Add(snapshot.ItemId, snapshot);
            }

            foreach (KeyValuePair<string, int> grant in quantitiesByItemId)
            {
                InventoryGrantFailureReason itemFailure = ValidateGrantItem(catalog, grant.Key, out ItemData item);
                if (itemFailure != InventoryGrantFailureReason.None)
                {
                    failureReason = itemFailure;
                    return false;
                }

                int currentQuantity = candidateById.TryGetValue(grant.Key, out InventoryStateSnapshot current)
                    ? current.Quantity
                    : 0;
                int nextQuantity;
                try
                {
                    nextQuantity = checked(currentQuantity + grant.Value);
                }
                catch (OverflowException)
                {
                    failureReason = InventoryGrantFailureReason.QuantityOverflow;
                    return false;
                }

                if (nextQuantity > item.maxStack)
                {
                    failureReason = InventoryGrantFailureReason.StackLimitExceeded;
                    return false;
                }

                candidateById[grant.Key] = new InventoryStateSnapshot(
                    grant.Key,
                    nextQuantity,
                    new StateStepSnapshot(
                        shown: false,
                        clicked: false,
                        opened: false,
                        selected: false,
                        applied: true,
                        completed: true,
                        persisted: false));
            }

            candidate = new List<InventoryStateSnapshot>(candidateById.Values);
            failureReason = InventoryGrantFailureReason.None;
            return true;
        }

        /// <summary>
        /// 用已通过 <see cref="TryBuildGrant"/> 校验的完整快照一次性替换背包；替换本身不再失败。
        /// </summary>
        public void ApplyGrant(
            InventoryStateStore inventory,
            IReadOnlyList<InventoryStateSnapshot> candidate)
        {
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));
            if (candidate == null)
                throw new ArgumentNullException(nameof(candidate));
            inventory.ReplaceAll(candidate);
        }

        private static bool TryAddQuantity(
            IDictionary<string, int> quantitiesByItemId,
            string itemId,
            int quantity)
        {
            if (!quantitiesByItemId.TryGetValue(itemId, out int existing))
            {
                quantitiesByItemId.Add(itemId, quantity);
                return true;
            }

            try
            {
                quantitiesByItemId[itemId] = checked(existing + quantity);
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static bool IsExistingSnapshotValid(
            InventoryStateSnapshot snapshot,
            ContentCatalogData catalog)
        {
            if (snapshot == null ||
                ValidateGrantItem(catalog, snapshot.ItemId, out ItemData item) !=
                InventoryGrantFailureReason.None)
            {
                return false;
            }

            return snapshot.Quantity <= item.maxStack;
        }

        private static InventoryGrantFailureReason ValidateGrantItem(
            ContentCatalogData catalog,
            string itemId,
            out ItemData item)
        {
            item = null;
            if (!catalog.TryGetItem(itemId, out ItemData found) || found == null)
                return InventoryGrantFailureReason.ItemNotFound;
            if (!string.Equals(
                    found.contentScope,
                    ProductionContentScope,
                    StringComparison.Ordinal))
            {
                return InventoryGrantFailureReason.ItemNotProduction;
            }
            if (found.maxStack <= 0)
                return InventoryGrantFailureReason.MaxStackInvalid;

            item = found;
            return InventoryGrantFailureReason.None;
        }
    }
}
