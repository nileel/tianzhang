using System;
using System.Collections.Generic;
using TianZhang.Content;

namespace TianZhang.World
{
    public sealed class InventoryStore
    {
        private Dictionary<string, int> quantities =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public int GetQuantity(string itemId)
        {
            int value;
            return !string.IsNullOrWhiteSpace(itemId) && quantities.TryGetValue(itemId, out value)
                ? value
                : 0;
        }

        internal void Replace(InventoryStoreSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var replacement = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (InventoryEntry entry in snapshot.Entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.ItemId) || entry.Quantity <= 0 ||
                    replacement.ContainsKey(entry.ItemId))
                {
                    throw new InvalidOperationException("Invalid inventory snapshot.");
                }
                replacement.Add(entry.ItemId, entry.Quantity);
            }
            quantities = replacement;
        }

        public InventoryStoreSnapshot Capture()
        {
            var entries = new List<InventoryEntry>();
            foreach (KeyValuePair<string, int> pair in quantities)
                entries.Add(new InventoryEntry(pair.Key, pair.Value));
            entries.Sort((left, right) => string.CompareOrdinal(left.ItemId, right.ItemId));
            return new InventoryStoreSnapshot(entries);
        }

        public void Restore(InventoryStoreSnapshot snapshot)
        {
            Replace(snapshot);
        }
    }

    public sealed class InventoryEntry
    {
        public InventoryEntry(string itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity;
        }

        public string ItemId { get; }
        public int Quantity { get; }
    }

    public sealed class InventoryStoreSnapshot
    {
        public InventoryStoreSnapshot(IEnumerable<InventoryEntry> entries)
        {
            Entries = entries == null ? new InventoryEntry[0] : new List<InventoryEntry>(entries).ToArray();
        }

        public InventoryStoreSnapshot(IDictionary<string, int> quantities)
        {
            var entries = new List<InventoryEntry>();
            if (quantities != null)
            {
                foreach (KeyValuePair<string, int> pair in quantities)
                    entries.Add(new InventoryEntry(pair.Key, pair.Value));
            }
            entries.Sort((left, right) => string.CompareOrdinal(left.ItemId, right.ItemId));
            Entries = entries.ToArray();
        }

        public InventoryEntry[] Entries { get; }
    }

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
        StackLimitExceeded,
    }

    public sealed class InventoryGrantRequest
    {
        public InventoryGrantRequest(string itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity;
        }

        public string ItemId { get; }
        public int Quantity { get; }
    }

    public sealed class InventoryGrantResult
    {
        private InventoryGrantResult(bool applied, InventoryGrantFailureReason failureReason)
        {
            Applied = applied;
            FailureReason = failureReason;
        }

        public bool Applied { get; }
        public InventoryGrantFailureReason FailureReason { get; }

        public static InventoryGrantResult Succeeded()
        {
            return new InventoryGrantResult(true, InventoryGrantFailureReason.None);
        }

        public static InventoryGrantResult Rejected(InventoryGrantFailureReason reason)
        {
            return new InventoryGrantResult(false, reason);
        }
    }

    /// <summary>Validates an entire grant before atomically replacing inventory state.</summary>
    public sealed class InventoryGrantUseCase
    {
        public const string ProductionContentScope = "content_scope_production";
        private readonly InventoryStore inventory;

        public InventoryGrantUseCase(InventoryStore inventory)
        {
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        }

        public InventoryGrantResult Grant(
            ContentCatalogData catalog,
            IReadOnlyList<InventoryGrantRequest> requests)
        {
            InventoryStoreSnapshot candidate;
            InventoryGrantFailureReason reason;
            if (!TryBuildGrant(inventory, catalog, requests, out candidate, out reason))
                return InventoryGrantResult.Rejected(reason);
            inventory.Replace(candidate);
            return InventoryGrantResult.Succeeded();
        }

        internal static bool TryBuildGrant(
            InventoryStore inventory,
            ContentCatalogData catalog,
            IReadOnlyList<InventoryGrantRequest> requests,
            out InventoryStoreSnapshot candidate,
            out InventoryGrantFailureReason failureReason)
        {
            candidate = null;
            if (inventory == null)
            {
                failureReason = InventoryGrantFailureReason.ExistingInventoryInvalid;
                return false;
            }
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

            var next = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (InventoryEntry entry in inventory.Capture().Entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.ItemId) || entry.Quantity <= 0 ||
                    next.ContainsKey(entry.ItemId))
                {
                    failureReason = InventoryGrantFailureReason.ExistingInventoryInvalid;
                    return false;
                }
                next.Add(entry.ItemId, entry.Quantity);
            }

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
                ItemData item;
                if (!catalog.TryGetItem(request.ItemId, out item) || item == null)
                {
                    failureReason = InventoryGrantFailureReason.ItemNotFound;
                    return false;
                }
                if (!string.Equals(item.contentScope, ProductionContentScope, StringComparison.Ordinal))
                {
                    failureReason = InventoryGrantFailureReason.ItemNotProduction;
                    return false;
                }
                if (item.maxStack <= 0)
                {
                    failureReason = InventoryGrantFailureReason.MaxStackInvalid;
                    return false;
                }

                int current;
                next.TryGetValue(request.ItemId, out current);
                int updated;
                try { updated = checked(current + request.Quantity); }
                catch (OverflowException)
                {
                    failureReason = InventoryGrantFailureReason.QuantityOverflow;
                    return false;
                }
                if (updated > item.maxStack)
                {
                    failureReason = InventoryGrantFailureReason.StackLimitExceeded;
                    return false;
                }
                next[request.ItemId] = updated;
            }

            candidate = new InventoryStoreSnapshot(next);
            failureReason = InventoryGrantFailureReason.None;
            return true;
        }
    }
}
