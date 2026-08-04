using System;
using System.Collections.Generic;
using TianZhang.Entity;
using TianZhang.World;

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
        public FoundationPurpleMansionSaveData FoundationPurpleMansionState { get; }

        public NpcStateSnapshot(string npcId, string worldNodeId, StateStepSnapshot steps)
            : this(npcId, worldNodeId, steps, null)
        {
        }

        public NpcStateSnapshot(
            string npcId,
            string worldNodeId,
            StateStepSnapshot steps,
            FoundationPurpleMansionSaveData foundationPurpleMansionState)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                throw new ArgumentException("NPC ID must not be empty.", nameof(npcId));
            if (string.IsNullOrWhiteSpace(worldNodeId))
                throw new ArgumentException("World node ID must not be empty.", nameof(worldNodeId));

            NpcId = npcId;
            WorldNodeId = worldNodeId;
            Steps = steps ?? throw new ArgumentNullException(nameof(steps));
            FoundationPurpleMansionState = RestoreFoundationPurpleMansionState(
                foundationPurpleMansionState);
        }

        private static FoundationPurpleMansionSaveData RestoreFoundationPurpleMansionState(
            FoundationPurpleMansionSaveData source)
        {
            if (source == null)
                return null;
            if (!FoundationPurpleMansionRuntimeState.TryRestore(
                    source,
                    out FoundationPurpleMansionRuntimeState runtimeState,
                    out string failureReason))
            {
                throw new ArgumentException(failureReason, nameof(source));
            }

            return runtimeState.CaptureSaveData();
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
        public bool hasFoundationPurpleMansionState;
        public FoundationPurpleMansionSaveData foundationPurpleMansionState;
    }

    /// <summary>
    /// 悬赏实例存档投影；只保存 bountyId、状态与进度。
    /// </summary>
    [Serializable]
    public sealed class BountyStateSaveData
    {
        public string bountyId;
        public BountyStatus status;
        public int progress;
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
        public List<BountyStateSaveData> bounties = new List<BountyStateSaveData>();
        public FoundationPurpleMansionSaveData playerFoundationPurpleMansionState;
        public bool hasCharterRuntimeState;
        public int charterDefinitionCatalogVersion;
        public CharterRuntimeStateData charterRuntimeState;
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
        public IReadOnlyList<BountyStateSnapshot> Bounties { get; }
        public FoundationPurpleMansionSaveData PlayerFoundationPurpleMansionSaveData { get; }
        public bool HasCharterRuntimeState { get; }
        public int CharterDefinitionCatalogVersion { get; }
        public CharterRuntimeStateData CharterRuntimeState { get; }

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
            IReadOnlyList<NpcStateSnapshot> npcs,
            IReadOnlyList<BountyStateSnapshot> bounties,
            FoundationPurpleMansionSaveData playerFoundationPurpleMansionSaveData,
            bool hasCharterRuntimeState,
            int charterDefinitionCatalogVersion,
            CharterRuntimeStateData charterRuntimeState)
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
            Bounties = bounties;
            PlayerFoundationPurpleMansionSaveData = playerFoundationPurpleMansionSaveData;
            HasCharterRuntimeState = hasCharterRuntimeState;
            CharterDefinitionCatalogVersion = charterDefinitionCatalogVersion;
            CharterRuntimeState = charterRuntimeState;
        }
    }

    public static class GameSessionSnapshot
    {
        public const int LegacySchemaVersion = 0;
        public const int StateCollectionsSchemaVersion = 1;
        public const int FoundationPurpleMansionSchemaVersion = 2;
        public const int BountySchemaVersion = 3;
        public const int CharterSchemaVersion = 4;
        public const int CurrentSchemaVersion = 4;

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
                lastReturnTarget = CaptureReturnTarget(session.LastReturnTarget),
                playerFoundationPurpleMansionState = CaptureFoundationPurpleMansionState(
                    session.PlayerFoundationPurpleMansionSaveData),
                hasCharterRuntimeState = session.CharterRuntimeState != null,
                charterDefinitionCatalogVersion =
                    session.CharterRuntimeState == null ? 0 : session.CharterDefinitionCatalogVersion,
                charterRuntimeState = session.CharterRuntimeState?.CreateCopy(),
            };
            if (data.hasCharterRuntimeState && data.charterDefinitionCatalogVersion <= 0)
            {
                throw new InvalidOperationException(
                    "Charter runtime state requires a positive definition catalog version; " +
                    "zero or missing versions are never inferred.");
            }

            foreach (QuestStateSnapshot snapshot in session.QuestStates.Snapshots)
                data.quests.Add(CaptureQuest(snapshot));
            data.quests.Sort((left, right) => string.CompareOrdinal(left.questId, right.questId));

            foreach (InventoryStateSnapshot snapshot in session.InventoryStates.Snapshots)
                data.inventory.Add(CaptureInventory(snapshot));
            data.inventory.Sort((left, right) => string.CompareOrdinal(left.itemId, right.itemId));

            foreach (NpcStateSnapshot snapshot in session.NpcStates.Snapshots)
                data.npcs.Add(CaptureNpc(snapshot));
            data.npcs.Sort((left, right) => string.CompareOrdinal(left.npcId, right.npcId));

            foreach (BountyStateSnapshot snapshot in session.BountyStates.Snapshots)
                data.bounties.Add(CaptureBounty(snapshot));
            data.bounties.Sort((left, right) => string.CompareOrdinal(left.bountyId, right.bountyId));

            Restore(data);
            return data;
        }

        internal static GameSessionRestoredState Restore(GameSessionSaveData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.schemaVersion != LegacySchemaVersion &&
                data.schemaVersion != StateCollectionsSchemaVersion &&
                data.schemaVersion != FoundationPurpleMansionSchemaVersion &&
                data.schemaVersion != BountySchemaVersion &&
                data.schemaVersion != CharterSchemaVersion)
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
            var bounties = new List<BountyStateSnapshot>();
            FoundationPurpleMansionSaveData playerFoundationPurpleMansionState = null;
            if (data.schemaVersion == StateCollectionsSchemaVersion ||
                data.schemaVersion == FoundationPurpleMansionSchemaVersion ||
                data.schemaVersion == BountySchemaVersion ||
                data.schemaVersion == CharterSchemaVersion)
            {
                RestoreQuests(data.quests, quests);
                RestoreInventory(data.inventory, inventory);
                RestoreNpcs(
                    data.npcs,
                    npcs,
                    data.schemaVersion == BountySchemaVersion ||
                    data.schemaVersion == CharterSchemaVersion);
            }
            if (data.schemaVersion == BountySchemaVersion ||
                data.schemaVersion == CharterSchemaVersion)
            {
                RestoreBounties(data.bounties, bounties);
            }
            if (data.schemaVersion == FoundationPurpleMansionSchemaVersion ||
                data.schemaVersion == BountySchemaVersion ||
                data.schemaVersion == CharterSchemaVersion)
            {
                playerFoundationPurpleMansionState = RestoreFoundationPurpleMansionState(
                    data.playerFoundationPurpleMansionState);
            }

            // schema 4 成对记录册界状态 presence、定义目录版本与深复制状态；schema 0～3 没有册界
            // 字段，只恢复为明确未接入状态。错误 presence 组合与零/缺失版本一律失败关闭；
            // JsonUtility 会把 JSON 中的 null 反序列化为默认实例，故空 payload 按明确未接入处理。
            bool hasCharterRuntimeState = false;
            int charterDefinitionCatalogVersion = 0;
            CharterRuntimeStateData charterRuntimeState = null;
            if (data.schemaVersion == CharterSchemaVersion)
            {
                if (data.hasCharterRuntimeState)
                {
                    if (IsAbsentCharterRuntimeState(data.charterRuntimeState) ||
                        data.charterDefinitionCatalogVersion <= 0)
                    {
                        throw new ArgumentException(
                            "Charter runtime state presence requires a non-empty payload and a positive definition catalog version.",
                            nameof(data));
                    }

                    hasCharterRuntimeState = true;
                    charterDefinitionCatalogVersion = data.charterDefinitionCatalogVersion;
                    charterRuntimeState = data.charterRuntimeState.CreateCopy();
                }
                else if (!IsAbsentCharterRuntimeState(data.charterRuntimeState) ||
                         data.charterDefinitionCatalogVersion != 0)
                {
                    throw new ArgumentException(
                        "Charter runtime state presence flag does not match its payload.",
                        nameof(data));
                }
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
                npcs,
                bounties,
                playerFoundationPurpleMansionState,
                hasCharterRuntimeState,
                charterDefinitionCatalogVersion,
                charterRuntimeState);
        }

        private static FoundationPurpleMansionSaveData CaptureFoundationPurpleMansionState(
            FoundationPurpleMansionSaveData source)
        {
            return RestoreFoundationPurpleMansionState(source);
        }

        private static FoundationPurpleMansionSaveData RestoreFoundationPurpleMansionState(
            FoundationPurpleMansionSaveData source)
        {
            if (source == null || IsAbsentFoundationPurpleMansionState(source))
                return null;

            if (!FoundationPurpleMansionRuntimeState.TryRestore(
                    source,
                    out FoundationPurpleMansionRuntimeState runtimeState,
                    out string failureReason))
            {
                throw new ArgumentException(failureReason, nameof(source));
            }

            return runtimeState.CaptureSaveData();
        }

        internal static bool IsAbsentFoundationPurpleMansionState(
            FoundationPurpleMansionSaveData source)
        {
            return string.IsNullOrEmpty(source.schemaId) && source.schemaVersion == 0 &&
                string.IsNullOrEmpty(source.characterId) &&
                IsEmpty(source.foundationState) && IsEmpty(source.mansionStates) &&
                IsEmpty(source.effectBindings) && IsEmpty(source.guardianAbilities) &&
                IsEmpty(source.enhancementNodes) && !source.hasCultivationActionState &&
                IsEmpty(source.cultivationActionState) && !source.hasClosedRetreatPlan &&
                IsEmpty(source.closedRetreatPlan) && IsEmpty(source.jindanLock) &&
                !source.hasJindanFormationSnapshot &&
                string.IsNullOrEmpty(source.lastClosedRetreatStopReason);
        }

        /// <summary>
        /// A default-instantiated charter payload (all stable IDs, states and records empty) is the
        /// explicit un-accessed state; JsonUtility materializes a default instance for JSON null.
        /// </summary>
        internal static bool IsAbsentCharterRuntimeState(CharterRuntimeStateData source)
        {
            return source == null ||
                string.IsNullOrEmpty(source.stateId) &&
                string.IsNullOrEmpty(source.charterRelicState) &&
                string.IsNullOrEmpty(source.worldSealState) &&
                IsEmpty(source.registeredRuleEntryIds) &&
                IsEmpty(source.nodeStates) &&
                IsEmpty(source.organizationAuthorizationVersions) &&
                IsEmpty(source.currentCoverageSet) &&
                IsEmpty(source.ruleEntryOccupancies) &&
                IsEmpty(source.nodeOccupancies) &&
                IsEmpty(source.realitySupplyStates) &&
                IsEmpty(source.positiveCommitResults) &&
                IsEmpty(source.negativeCommitResults) &&
                IsEmpty(source.currentRegionRuleEntryIds);
        }

        private static bool IsEmpty(FoundationStateRecord value)
        {
            return value == null || string.IsNullOrEmpty(value.foundationInstanceId);
        }

        private static bool IsEmpty<T>(IReadOnlyCollection<T> value)
        {
            return value == null || value.Count == 0;
        }

        private static bool IsEmpty(CultivationActionStateRecord value)
        {
            return value == null || string.IsNullOrEmpty(value.actionStateId);
        }

        private static bool IsEmpty(ClosedRetreatPlanRecord value)
        {
            return value == null || string.IsNullOrEmpty(value.actionStateId);
        }

        private static bool IsEmpty(JindanLockRecord value)
        {
            return value == null ||
                (value.status == JindanLockStatus.PreJindan &&
                 (value.formationSnapshot == null ||
                  (string.IsNullOrEmpty(value.formationSnapshot.foundationInstanceId) &&
                   (value.formationSnapshot.expansionGrantIds == null ||
                    value.formationSnapshot.expansionGrantIds.Length == 0) &&
                   (value.formationSnapshot.mansionStates == null ||
                    value.formationSnapshot.mansionStates.Length == 0))));
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
                steps = CaptureSteps(snapshot.Steps),
                hasFoundationPurpleMansionState =
                    snapshot.FoundationPurpleMansionState != null,
                foundationPurpleMansionState = CaptureFoundationPurpleMansionState(
                    snapshot.FoundationPurpleMansionState),
            };
        }

        private static BountyStateSaveData CaptureBounty(BountyStateSnapshot snapshot)
        {
            return new BountyStateSaveData
            {
                bountyId = snapshot.BountyId,
                status = snapshot.Status,
                progress = snapshot.Progress,
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
            ICollection<NpcStateSnapshot> destination,
            bool restoreFoundationPurpleMansionState)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (NpcStateSaveData item in source ?? Array.Empty<NpcStateSaveData>())
            {
                if (item == null)
                    throw new ArgumentException("NPC state must not be null.", nameof(source));
                if (restoreFoundationPurpleMansionState &&
                    item.hasFoundationPurpleMansionState !=
                    (item.foundationPurpleMansionState != null &&
                     !IsAbsentFoundationPurpleMansionState(
                         item.foundationPurpleMansionState)))
                {
                    throw new ArgumentException(
                        "NPC cultivation-state presence flag does not match its payload.",
                        nameof(source));
                }
                var snapshot = new NpcStateSnapshot(
                    item.npcId,
                    item.worldNodeId,
                    RestoreSteps(item.steps),
                    restoreFoundationPurpleMansionState &&
                    item.hasFoundationPurpleMansionState
                        ? item.foundationPurpleMansionState
                        : null);
                if (!ids.Add(snapshot.NpcId))
                    throw new ArgumentException("Duplicate NPC ID.", nameof(source));
                destination.Add(snapshot);
            }
        }

        private static void RestoreBounties(
            IReadOnlyList<BountyStateSaveData> source,
            ICollection<BountyStateSnapshot> destination)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (BountyStateSaveData item in source ?? Array.Empty<BountyStateSaveData>())
            {
                if (item == null)
                    throw new ArgumentException("Bounty state must not be null.", nameof(source));
                var snapshot = new BountyStateSnapshot(item.bountyId, item.status, item.progress);
                if (snapshot.Status == BountyStatus.Available)
                {
                    throw new ArgumentException(
                        "An Available bounty has no instance and must not be restored.",
                        nameof(source));
                }
                if (!ids.Add(snapshot.BountyId))
                    throw new ArgumentException("Duplicate bounty ID.", nameof(source));
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
