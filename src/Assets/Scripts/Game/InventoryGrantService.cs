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

        public InventoryGrantResult Grant(
            InventoryStateStore inventory,
            ContentCatalogData catalog,
            IReadOnlyList<InventoryGrantRequest> requests)
        {
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));
            if (catalog == null)
                return InventoryGrantResult.Rejected(InventoryGrantFailureReason.CatalogMissing);
            if (requests == null || requests.Count == 0)
                return InventoryGrantResult.Rejected(InventoryGrantFailureReason.EmptyRequest);

            var quantitiesByItemId = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (InventoryGrantRequest request in requests)
            {
                if (request == null || string.IsNullOrWhiteSpace(request.ItemId))
                    return InventoryGrantResult.Rejected(InventoryGrantFailureReason.ItemIdInvalid);
                if (request.Quantity <= 0)
                    return InventoryGrantResult.Rejected(InventoryGrantFailureReason.QuantityInvalid);

                if (!TryAddQuantity(quantitiesByItemId, request.ItemId, request.Quantity))
                    return InventoryGrantResult.Rejected(InventoryGrantFailureReason.QuantityOverflow);
            }

            var candidate = new Dictionary<string, InventoryStateSnapshot>(StringComparer.Ordinal);
            foreach (InventoryStateSnapshot snapshot in inventory.Snapshots)
            {
                if (!IsExistingSnapshotValid(snapshot, catalog))
                    return InventoryGrantResult.Rejected(
                        InventoryGrantFailureReason.ExistingInventoryInvalid);

                candidate.Add(snapshot.ItemId, snapshot);
            }

            foreach (KeyValuePair<string, int> grant in quantitiesByItemId)
            {
                InventoryGrantFailureReason itemFailure = ValidateGrantItem(catalog, grant.Key, out ItemData item);
                if (itemFailure != InventoryGrantFailureReason.None)
                    return InventoryGrantResult.Rejected(itemFailure);

                int currentQuantity = candidate.TryGetValue(grant.Key, out InventoryStateSnapshot current)
                    ? current.Quantity
                    : 0;
                int nextQuantity;
                try
                {
                    nextQuantity = checked(currentQuantity + grant.Value);
                }
                catch (OverflowException)
                {
                    return InventoryGrantResult.Rejected(InventoryGrantFailureReason.QuantityOverflow);
                }

                if (nextQuantity > item.maxStack)
                    return InventoryGrantResult.Rejected(InventoryGrantFailureReason.StackLimitExceeded);

                candidate[grant.Key] = new InventoryStateSnapshot(
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

            inventory.ReplaceAll(candidate.Values);
            return InventoryGrantResult.Succeeded();
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
