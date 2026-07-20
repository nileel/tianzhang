using System;
using System.Collections.Generic;

namespace TianZhang.Game
{
    public sealed class StateStepSnapshot
    {
        public bool Shown { get; }
        public bool Clicked { get; }
        public bool Opened { get; }
        public bool Selected { get; }
        public bool Applied { get; }
        public bool Completed { get; }
        public bool Persisted { get; }

        public StateStepSnapshot(
            bool shown,
            bool clicked,
            bool opened,
            bool selected,
            bool applied,
            bool completed,
            bool persisted)
        {
            Shown = shown;
            Clicked = clicked;
            Opened = opened;
            Selected = selected;
            Applied = applied;
            Completed = completed;
            Persisted = persisted;
        }
    }

    public sealed class QuestStateSnapshot
    {
        public string QuestId { get; }
        public StateStepSnapshot Steps { get; }

        public QuestStateSnapshot(string questId, StateStepSnapshot steps)
        {
            QuestId = RequireId(questId, nameof(questId));
            Steps = steps ?? throw new ArgumentNullException(nameof(steps));
        }

        private static string RequireId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Quest ID must not be empty.", parameterName);
            return value;
        }
    }

    public sealed class InventoryStateSnapshot
    {
        public string ItemId { get; }
        public int Quantity { get; }
        public StateStepSnapshot Steps { get; }

        public InventoryStateSnapshot(string itemId, int quantity, StateStepSnapshot steps)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                throw new ArgumentException("Item ID must not be empty.", nameof(itemId));
            if (quantity < 0)
                throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Quantity must not be negative.");

            ItemId = itemId;
            Quantity = quantity;
            Steps = steps ?? throw new ArgumentNullException(nameof(steps));
        }
    }

    public sealed class NpcStateSnapshot
    {
        public string NpcId { get; }
        public string WorldNodeId { get; }
        public StateStepSnapshot Steps { get; }

        public NpcStateSnapshot(string npcId, string worldNodeId, StateStepSnapshot steps)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                throw new ArgumentException("NPC ID must not be empty.", nameof(npcId));
            if (string.IsNullOrWhiteSpace(worldNodeId))
                throw new ArgumentException("World node ID must not be empty.", nameof(worldNodeId));

            NpcId = npcId;
            WorldNodeId = worldNodeId;
            Steps = steps ?? throw new ArgumentNullException(nameof(steps));
        }
    }

    public sealed class QuestStateStore
    {
        private readonly Dictionary<string, QuestStateSnapshot> snapshots =
            new Dictionary<string, QuestStateSnapshot>(StringComparer.Ordinal);

        public int Count => snapshots.Count;

        public void Set(QuestStateSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            snapshots[snapshot.QuestId] = snapshot;
        }

        public bool TryGet(string questId, out QuestStateSnapshot snapshot)
        {
            return snapshots.TryGetValue(questId, out snapshot);
        }

        public void Clear()
        {
            snapshots.Clear();
        }
    }

    public sealed class InventoryStateStore
    {
        private readonly Dictionary<string, InventoryStateSnapshot> snapshots =
            new Dictionary<string, InventoryStateSnapshot>(StringComparer.Ordinal);

        public int Count => snapshots.Count;

        public void Set(InventoryStateSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            snapshots[snapshot.ItemId] = snapshot;
        }

        public bool TryGet(string itemId, out InventoryStateSnapshot snapshot)
        {
            return snapshots.TryGetValue(itemId, out snapshot);
        }

        public void Clear()
        {
            snapshots.Clear();
        }
    }

    public sealed class NpcStateStore
    {
        private readonly Dictionary<string, NpcStateSnapshot> snapshots =
            new Dictionary<string, NpcStateSnapshot>(StringComparer.Ordinal);

        public int Count => snapshots.Count;

        public void Set(NpcStateSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            snapshots[snapshot.NpcId] = snapshot;
        }

        public bool TryGet(string npcId, out NpcStateSnapshot snapshot)
        {
            return snapshots.TryGetValue(npcId, out snapshot);
        }

        public void Clear()
        {
            snapshots.Clear();
        }
    }
}
