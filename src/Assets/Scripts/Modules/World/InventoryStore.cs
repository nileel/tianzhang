using System.Collections.Generic;

namespace TianZhang.World
{
    public sealed class InventoryStore
    {
        private readonly Dictionary<string, int> quantities = new Dictionary<string, int>();
        public int GetQuantity(string itemId) { int value; return !string.IsNullOrWhiteSpace(itemId) && quantities.TryGetValue(itemId, out value) ? value : 0; }
        public bool TryGrant(string itemId, int amount)
        {
            if (string.IsNullOrWhiteSpace(itemId) || amount <= 0) return false;
            quantities[itemId] = GetQuantity(itemId) + amount; return true;
        }
        public bool TryConsume(string itemId, int amount)
        {
            if (string.IsNullOrWhiteSpace(itemId) || amount <= 0 || GetQuantity(itemId) < amount) return false;
            int remaining = GetQuantity(itemId) - amount; if (remaining == 0) quantities.Remove(itemId); else quantities[itemId] = remaining; return true;
        }
        public InventoryStoreSnapshot Capture() { return new InventoryStoreSnapshot(quantities); }
        public void Restore(InventoryStoreSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot)); quantities.Clear();
            foreach (InventoryEntry entry in snapshot.Entries) if (!TryGrant(entry.ItemId, entry.Quantity)) throw new System.InvalidOperationException("Invalid inventory snapshot.");
        }
    }
    public sealed class InventoryEntry { public InventoryEntry(string itemId, int quantity) { ItemId = itemId; Quantity = quantity; } public string ItemId { get; } public int Quantity { get; } }
    public sealed class InventoryStoreSnapshot
    {
        public InventoryStoreSnapshot(IDictionary<string, int> quantities) { var entries = new List<InventoryEntry>(); if (quantities != null) foreach (KeyValuePair<string, int> pair in quantities) entries.Add(new InventoryEntry(pair.Key, pair.Value)); Entries = entries.ToArray(); }
        public InventoryEntry[] Entries { get; }
    }
}
