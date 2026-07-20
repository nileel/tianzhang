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
        private Dictionary<string, QuestStateSnapshot> snapshots =
            new Dictionary<string, QuestStateSnapshot>(StringComparer.Ordinal);

        public int Count => snapshots.Count;
        internal IEnumerable<QuestStateSnapshot> Snapshots => snapshots.Values;

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

        internal void ReplaceAll(IEnumerable<QuestStateSnapshot> source)
        {
            var replacement = new Dictionary<string, QuestStateSnapshot>(StringComparer.Ordinal);
            foreach (QuestStateSnapshot snapshot in source)
            {
                if (snapshot == null || !replacement.TryAdd(snapshot.QuestId, snapshot))
                    throw new ArgumentException("Duplicate or null quest state.", nameof(source));
            }
            snapshots = replacement;
        }
    }

    public sealed class InventoryStateStore
    {
        private Dictionary<string, InventoryStateSnapshot> snapshots =
            new Dictionary<string, InventoryStateSnapshot>(StringComparer.Ordinal);

        public int Count => snapshots.Count;
        internal IEnumerable<InventoryStateSnapshot> Snapshots => snapshots.Values;

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

        internal void ReplaceAll(IEnumerable<InventoryStateSnapshot> source)
        {
            var replacement = new Dictionary<string, InventoryStateSnapshot>(StringComparer.Ordinal);
            foreach (InventoryStateSnapshot snapshot in source)
            {
                if (snapshot == null || !replacement.TryAdd(snapshot.ItemId, snapshot))
                    throw new ArgumentException("Duplicate or null inventory state.", nameof(source));
            }
            snapshots = replacement;
        }
    }

    public sealed class NpcStateStore
    {
        private Dictionary<string, NpcStateSnapshot> snapshots =
            new Dictionary<string, NpcStateSnapshot>(StringComparer.Ordinal);

        public int Count => snapshots.Count;
        internal IEnumerable<NpcStateSnapshot> Snapshots => snapshots.Values;

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

        internal void ReplaceAll(IEnumerable<NpcStateSnapshot> source)
        {
            var replacement = new Dictionary<string, NpcStateSnapshot>(StringComparer.Ordinal);
            foreach (NpcStateSnapshot snapshot in source)
            {
                if (snapshot == null || !replacement.TryAdd(snapshot.NpcId, snapshot))
                    throw new ArgumentException("Duplicate or null NPC state.", nameof(source));
            }
            snapshots = replacement;
        }
    }

    [Serializable]
    public sealed class StateStepSaveData
    {
        public bool shown;
        public bool clicked;
        public bool opened;
        public bool selected;
        public bool applied;
        public bool completed;
        public bool persisted;
    }

    [Serializable]
    public sealed class QuestStateSaveData
    {
        public string questId;
        public StateStepSaveData steps;
    }

    [Serializable]
    public sealed class InventoryStateSaveData
    {
        public string itemId;
        public int quantity;
        public StateStepSaveData steps;
    }

    [Serializable]
    public sealed class NpcStateSaveData
    {
        public string npcId;
        public string worldNodeId;
        public StateStepSaveData steps;
    }

    [Serializable]
    public sealed class SceneReturnTargetSaveData
    {
        public string sceneName;
        public string worldNodeId;
        public string settlementId;
        public string adventureId;
    }

    [Serializable]
    public sealed class GameSessionSaveData
    {
        public int schemaVersion = GameSessionSnapshot.CurrentSchemaVersion;
        public string currentWorldNodeId;
        public int worldYear;
        public string worldSeasonId;
        public int worldDay;
        public string worldTimeOfDayId;
        public string currentSettlementId;
        public string currentAdventureId;
        public SceneReturnTargetSaveData lastReturnTarget = new SceneReturnTargetSaveData();
        public List<QuestStateSaveData> quests = new List<QuestStateSaveData>();
        public List<InventoryStateSaveData> inventory = new List<InventoryStateSaveData>();
        public List<NpcStateSaveData> npcs = new List<NpcStateSaveData>();
    }

    internal sealed class GameSessionRestoredState
    {
        public string CurrentWorldNodeId { get; }
        public int WorldYear { get; }
        public string WorldSeasonId { get; }
        public int WorldDay { get; }
        public string WorldTimeOfDayId { get; }
        public string CurrentSettlementId { get; }
        public string CurrentAdventureId { get; }
        public SceneReturnTarget LastReturnTarget { get; }
        public IReadOnlyList<QuestStateSnapshot> Quests { get; }
        public IReadOnlyList<InventoryStateSnapshot> Inventory { get; }
        public IReadOnlyList<NpcStateSnapshot> Npcs { get; }

        public GameSessionRestoredState(
            string currentWorldNodeId,
            int worldYear,
            string worldSeasonId,
            int worldDay,
            string worldTimeOfDayId,
            string currentSettlementId,
            string currentAdventureId,
            SceneReturnTarget lastReturnTarget,
            IReadOnlyList<QuestStateSnapshot> quests,
            IReadOnlyList<InventoryStateSnapshot> inventory,
            IReadOnlyList<NpcStateSnapshot> npcs)
        {
            CurrentWorldNodeId = currentWorldNodeId;
            WorldYear = worldYear;
            WorldSeasonId = worldSeasonId;
            WorldDay = worldDay;
            WorldTimeOfDayId = worldTimeOfDayId;
            CurrentSettlementId = currentSettlementId;
            CurrentAdventureId = currentAdventureId;
            LastReturnTarget = lastReturnTarget;
            Quests = quests;
            Inventory = inventory;
            Npcs = npcs;
        }
    }

    public static class GameSessionSnapshot
    {
        public const int LegacySchemaVersion = 0;
        public const int CurrentSchemaVersion = 1;

        public static GameSessionSaveData Capture(GameSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            var data = new GameSessionSaveData
            {
                schemaVersion = CurrentSchemaVersion,
                currentWorldNodeId = session.CurrentWorldNodeId,
                worldYear = session.WorldYear,
                worldSeasonId = session.WorldSeasonId,
                worldDay = session.WorldDay,
                worldTimeOfDayId = session.WorldTimeOfDayId,
                currentSettlementId = session.CurrentSettlementId,
                currentAdventureId = session.CurrentAdventureId,
                lastReturnTarget = CaptureReturnTarget(session.LastReturnTarget)
            };

            foreach (QuestStateSnapshot snapshot in session.QuestStates.Snapshots)
                data.quests.Add(CaptureQuest(snapshot));
            data.quests.Sort((left, right) => string.CompareOrdinal(left.questId, right.questId));

            foreach (InventoryStateSnapshot snapshot in session.InventoryStates.Snapshots)
                data.inventory.Add(CaptureInventory(snapshot));
            data.inventory.Sort((left, right) => string.CompareOrdinal(left.itemId, right.itemId));

            foreach (NpcStateSnapshot snapshot in session.NpcStates.Snapshots)
                data.npcs.Add(CaptureNpc(snapshot));
            data.npcs.Sort((left, right) => string.CompareOrdinal(left.npcId, right.npcId));

            Restore(data);
            return data;
        }

        internal static GameSessionRestoredState Restore(GameSessionSaveData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.schemaVersion != LegacySchemaVersion &&
                data.schemaVersion != CurrentSchemaVersion)
            {
                throw new NotSupportedException(
                    "Unsupported game session save schema: " + data.schemaVersion);
            }

            string worldNodeId = RequireId(data.currentWorldNodeId, "currentWorldNodeId");
            if (data.worldYear <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    "worldYear",
                    data.worldYear,
                    "World year must be positive.");
            }
            if (data.worldDay <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    "worldDay",
                    data.worldDay,
                    "World day must be positive.");
            }

            string seasonId = RequireId(data.worldSeasonId, "worldSeasonId");
            string timeOfDayId = RequireId(data.worldTimeOfDayId, "worldTimeOfDayId");
            string settlementId = OptionalId(data.currentSettlementId, "currentSettlementId");
            string adventureId = OptionalId(data.currentAdventureId, "currentAdventureId");
            SceneReturnTarget returnTarget = RestoreReturnTarget(data.lastReturnTarget);

            var quests = new List<QuestStateSnapshot>();
            var inventory = new List<InventoryStateSnapshot>();
            var npcs = new List<NpcStateSnapshot>();
            if (data.schemaVersion == CurrentSchemaVersion)
            {
                RestoreQuests(data.quests, quests);
                RestoreInventory(data.inventory, inventory);
                RestoreNpcs(data.npcs, npcs);
            }

            return new GameSessionRestoredState(
                worldNodeId,
                data.worldYear,
                seasonId,
                data.worldDay,
                timeOfDayId,
                settlementId,
                adventureId,
                returnTarget,
                quests,
                inventory,
                npcs);
        }

        private static QuestStateSaveData CaptureQuest(QuestStateSnapshot snapshot)
        {
            return new QuestStateSaveData
            {
                questId = snapshot.QuestId,
                steps = CaptureSteps(snapshot.Steps)
            };
        }

        private static InventoryStateSaveData CaptureInventory(InventoryStateSnapshot snapshot)
        {
            return new InventoryStateSaveData
            {
                itemId = snapshot.ItemId,
                quantity = snapshot.Quantity,
                steps = CaptureSteps(snapshot.Steps)
            };
        }

        private static NpcStateSaveData CaptureNpc(NpcStateSnapshot snapshot)
        {
            return new NpcStateSaveData
            {
                npcId = snapshot.NpcId,
                worldNodeId = snapshot.WorldNodeId,
                steps = CaptureSteps(snapshot.Steps)
            };
        }

        private static StateStepSaveData CaptureSteps(StateStepSnapshot steps)
        {
            return new StateStepSaveData
            {
                shown = steps.Shown,
                clicked = steps.Clicked,
                opened = steps.Opened,
                selected = steps.Selected,
                applied = steps.Applied,
                completed = steps.Completed,
                persisted = steps.Persisted
            };
        }

        private static SceneReturnTargetSaveData CaptureReturnTarget(SceneReturnTarget target)
        {
            return new SceneReturnTargetSaveData
            {
                sceneName = target.SceneName,
                worldNodeId = target.WorldNodeId,
                settlementId = target.SettlementId,
                adventureId = target.AdventureId
            };
        }

        private static void RestoreQuests(
            IReadOnlyList<QuestStateSaveData> source,
            ICollection<QuestStateSnapshot> destination)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (QuestStateSaveData item in source ?? Array.Empty<QuestStateSaveData>())
            {
                if (item == null)
                    throw new ArgumentException("Quest state must not be null.", nameof(source));
                var snapshot = new QuestStateSnapshot(item.questId, RestoreSteps(item.steps));
                if (!ids.Add(snapshot.QuestId))
                    throw new ArgumentException("Duplicate quest ID.", nameof(source));
                destination.Add(snapshot);
            }
        }

        private static void RestoreInventory(
            IReadOnlyList<InventoryStateSaveData> source,
            ICollection<InventoryStateSnapshot> destination)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (InventoryStateSaveData item in source ?? Array.Empty<InventoryStateSaveData>())
            {
                if (item == null)
                    throw new ArgumentException("Inventory state must not be null.", nameof(source));
                var snapshot = new InventoryStateSnapshot(
                    item.itemId,
                    item.quantity,
                    RestoreSteps(item.steps));
                if (!ids.Add(snapshot.ItemId))
                    throw new ArgumentException("Duplicate item ID.", nameof(source));
                destination.Add(snapshot);
            }
        }

        private static void RestoreNpcs(
            IReadOnlyList<NpcStateSaveData> source,
            ICollection<NpcStateSnapshot> destination)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (NpcStateSaveData item in source ?? Array.Empty<NpcStateSaveData>())
            {
                if (item == null)
                    throw new ArgumentException("NPC state must not be null.", nameof(source));
                var snapshot = new NpcStateSnapshot(
                    item.npcId,
                    item.worldNodeId,
                    RestoreSteps(item.steps));
                if (!ids.Add(snapshot.NpcId))
                    throw new ArgumentException("Duplicate NPC ID.", nameof(source));
                destination.Add(snapshot);
            }
        }

        private static StateStepSnapshot RestoreSteps(StateStepSaveData data)
        {
            if (data == null)
                throw new ArgumentException("State steps must not be null.", nameof(data));
            return new StateStepSnapshot(
                data.shown,
                data.clicked,
                data.opened,
                data.selected,
                data.applied,
                data.completed,
                data.persisted);
        }

        private static SceneReturnTarget RestoreReturnTarget(SceneReturnTargetSaveData data)
        {
            if (data == null ||
                string.IsNullOrEmpty(data.sceneName) &&
                string.IsNullOrEmpty(data.worldNodeId) &&
                string.IsNullOrEmpty(data.settlementId) &&
                string.IsNullOrEmpty(data.adventureId))
            {
                return default;
            }

            if (data.sceneName == "WorldScene" &&
                string.IsNullOrEmpty(data.settlementId) &&
                string.IsNullOrEmpty(data.adventureId))
            {
                return SceneReturnTarget.World(RequireId(data.worldNodeId, "lastReturnTarget.worldNodeId"));
            }

            if (data.sceneName == "SettlementScene" &&
                string.IsNullOrEmpty(data.worldNodeId) &&
                string.IsNullOrEmpty(data.adventureId))
            {
                return SceneReturnTarget.Settlement(
                    RequireId(data.settlementId, "lastReturnTarget.settlementId"));
            }

            throw new ArgumentException("Invalid scene return target.", nameof(data));
        }

        private static string RequireId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Stable ID must not be empty.", parameterName);
            return value;
        }

        private static string OptionalId(string value, string parameterName)
        {
            if (string.IsNullOrEmpty(value))
                return null;
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Stable ID must not be whitespace.", parameterName);
            return value;
        }
    }
}
