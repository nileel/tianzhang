using System;
using System.Collections.Generic;
using TianZhang.Character;
using TianZhang.Content;
using TianZhang.Cultivation;
using TianZhang.Entity;
using TianZhang.Gameplay.Contracts;
using TianZhang.World;

namespace TianZhang.Infrastructure.Persistence
{
    [Serializable]
    public sealed class GameSaveEnvelope
    {
        public int schemaVersion = GameSaveSerializer.SchemaVersion;
        public bool hasPlayer;
        public CharacterRecord player;
        public CultivationRecord cultivation;
        public WorldClockRecord worldClock;
        public QuestRecord[] quests;
        public InventoryRecord[] inventory;
        public NpcRecord[] npcs;
        public BountyRecord[] bounties;
        public CharterRecord charter;
        public NavigationRecord navigation;

        public static GameSaveEnvelope Capture(
            CharacterRuntimeProfile player,
            CultivationState cultivation,
            WorldClockService worldClock,
            QuestStore quests,
            InventoryStore inventory,
            NpcStore npcs,
            BountyStore bounties,
            CharterStore charter,
            NavigationStateSnapshot navigation)
        {
            if (worldClock == null || quests == null || inventory == null || npcs == null ||
                bounties == null || charter == null || navigation == null)
            {
                throw new ArgumentNullException("Runtime state owner is missing.");
            }
            if ((player == null) != (cultivation == null))
                throw new InvalidOperationException("Player and cultivation state must have the same lifetime.");

            var envelope = new GameSaveEnvelope
            {
                hasPlayer = player != null,
                player = player == null ? null : CharacterRecord.Capture(player.Capture()),
                cultivation = cultivation == null ? null : CultivationRecord.Capture(cultivation.Capture()),
                worldClock = WorldClockRecord.Capture(worldClock.Capture()),
                quests = CaptureQuests(quests.Capture()),
                inventory = CaptureInventory(inventory.Capture()),
                npcs = CaptureNpcs(npcs.Capture()),
                bounties = CaptureBounties(bounties.Capture()),
                charter = CharterRecord.Capture(charter.Capture()),
                navigation = NavigationRecord.Capture(navigation),
            };
            return envelope;
        }

        public RestoredGameState Restore(ContentCatalogData catalog)
        {
            if (schemaVersion != GameSaveSerializer.SchemaVersion)
                throw new ArgumentException("Unsupported save schema.", nameof(schemaVersion));
            if (worldClock == null || quests == null || inventory == null || npcs == null ||
                bounties == null || charter == null || navigation == null)
            {
                throw new ArgumentException("Save envelope is incomplete.");
            }
            if (hasPlayer != (player != null && cultivation != null))
                throw new ArgumentException("Player state presence is inconsistent.");

            CharacterRuntimeProfile restoredPlayer = hasPlayer
                ? CharacterRuntimeProfile.FromSnapshot(player.Restore())
                : null;
            CultivationState restoredCultivation = hasPlayer
                ? CultivationState.FromSnapshot(cultivation.Restore())
                : null;
            var restoredClock = new WorldClockService(1);
            restoredClock.Restore(worldClock.Restore());
            var restoredQuests = new QuestStore();
            restoredQuests.Restore(RestoreQuests(quests));
            var restoredInventory = new InventoryStore();
            restoredInventory.Restore(RestoreInventory(inventory, catalog));
            var restoredNpcs = new NpcStore();
            restoredNpcs.Restore(RestoreNpcs(npcs));
            var restoredBounties = new BountyStore();
            BountyStoreSnapshot bountySnapshot = RestoreBounties(bounties, catalog);
            restoredBounties.Restore(bountySnapshot);
            var restoredCharter = new CharterStore();
            CharterStateSnapshot charterSnapshot = charter.Restore();
            CharterUseCase.ValidateRestoredState(catalog, charterSnapshot);
            restoredCharter.Restore(charterSnapshot);
            NavigationStateSnapshot restoredNavigation = navigation.Restore();

            return new RestoredGameState(
                restoredPlayer,
                restoredCultivation,
                restoredClock,
                restoredQuests,
                restoredInventory,
                restoredNpcs,
                restoredBounties,
                restoredCharter,
                restoredNavigation);
        }

        private static QuestRecord[] CaptureQuests(QuestStoreSnapshot snapshot)
        {
            var records = new List<QuestRecord>();
            foreach (QuestState state in snapshot.States)
                records.Add(new QuestRecord { questId = state.QuestId, step = state.Step, completed = state.Completed });
            return records.ToArray();
        }

        private static InventoryRecord[] CaptureInventory(InventoryStoreSnapshot snapshot)
        {
            var records = new List<InventoryRecord>();
            foreach (InventoryEntry entry in snapshot.Entries)
                records.Add(new InventoryRecord { itemId = entry.ItemId, quantity = entry.Quantity });
            return records.ToArray();
        }

        private static NpcRecord[] CaptureNpcs(NpcStoreSnapshot snapshot)
        {
            var records = new List<NpcRecord>();
            foreach (NpcState state in snapshot.States)
            {
                records.Add(new NpcRecord
                {
                    npcId = state.NpcId,
                    worldNodeId = state.WorldNodeId,
                    cultivationActionId = state.CultivationActionId,
                    cultivationState = state.CultivationState,
                });
            }
            return records.ToArray();
        }

        private static BountyRecord[] CaptureBounties(BountyStoreSnapshot snapshot)
        {
            var records = new List<BountyRecord>();
            foreach (BountyState state in snapshot.States)
                records.Add(new BountyRecord { bountyId = state.BountyId, status = (int)state.Status, progress = state.Progress });
            return records.ToArray();
        }

        private static QuestStoreSnapshot RestoreQuests(QuestRecord[] records)
        {
            var states = new List<QuestState>();
            foreach (QuestRecord record in records)
            {
                if (record == null) throw new ArgumentException("Quest record is missing.");
                states.Add(new QuestState(record.questId, record.step, record.completed));
            }
            return new QuestStoreSnapshot(states);
        }

        private static InventoryStoreSnapshot RestoreInventory(InventoryRecord[] records, ContentCatalogData catalog)
        {
            var entries = new List<InventoryEntry>();
            foreach (InventoryRecord record in records)
            {
                ItemData item;
                if (record == null || string.IsNullOrWhiteSpace(record.itemId) || record.quantity <= 0 ||
                    catalog == null || !catalog.TryGetItem(record.itemId, out item) || item == null ||
                    record.quantity > item.maxStack)
                {
                    throw new ArgumentException("Inventory record is invalid.");
                }
                entries.Add(new InventoryEntry(record.itemId, record.quantity));
            }
            return new InventoryStoreSnapshot(entries);
        }

        private static NpcStoreSnapshot RestoreNpcs(NpcRecord[] records)
        {
            var states = new List<NpcState>();
            foreach (NpcRecord record in records)
            {
                if (record == null) throw new ArgumentException("NPC record is missing.");
                states.Add(new NpcState(
                    record.npcId,
                    record.worldNodeId,
                    record.cultivationActionId,
                    record.cultivationState));
            }
            return new NpcStoreSnapshot(states);
        }

        private static BountyStoreSnapshot RestoreBounties(BountyRecord[] records, ContentCatalogData catalog)
        {
            var states = new List<BountyState>();
            foreach (BountyRecord record in records)
            {
                BountyStatus status = (BountyStatus)(record == null ? -1 : record.status);
                BountyData bounty;
                if (record == null || !Enum.IsDefined(typeof(BountyStatus), status) || status == BountyStatus.Available ||
                    record.progress < 0 || catalog == null || !catalog.TryGetBounty(record.bountyId, out bounty) || bounty == null ||
                    record.progress > bounty.requiredCount)
                {
                    throw new ArgumentException("Bounty record is invalid.");
                }
                if (status == BountyStatus.Accepted && record.progress >= bounty.requiredCount ||
                    (status == BountyStatus.ObjectiveCompleted || status == BountyStatus.Claimed) &&
                    record.progress != bounty.requiredCount)
                {
                    throw new ArgumentException("Bounty status and progress are inconsistent.");
                }
                states.Add(new BountyState(record.bountyId, status, record.progress));
            }
            return new BountyStoreSnapshot(states);
        }
    }

    public sealed class RestoredGameState
    {
        public RestoredGameState(
            CharacterRuntimeProfile player,
            CultivationState cultivation,
            WorldClockService worldClock,
            QuestStore quests,
            InventoryStore inventory,
            NpcStore npcs,
            BountyStore bounties,
            CharterStore charter,
            NavigationStateSnapshot navigation)
        {
            Player = player;
            Cultivation = cultivation;
            WorldClock = worldClock;
            Quests = quests;
            Inventory = inventory;
            Npcs = npcs;
            Bounties = bounties;
            Charter = charter;
            Navigation = navigation;
        }

        public CharacterRuntimeProfile Player { get; }
        public CultivationState Cultivation { get; }
        public WorldClockService WorldClock { get; }
        public QuestStore Quests { get; }
        public InventoryStore Inventory { get; }
        public NpcStore Npcs { get; }
        public BountyStore Bounties { get; }
        public CharterStore Charter { get; }
        public NavigationStateSnapshot Navigation { get; }
    }

    [Serializable]
    public sealed class CharacterRecord
    {
        public string characterId;
        public string displayName;
        public int rootBone;
        public int physique;
        public int spirit;
        public int mind;
        public int reaction;
        public int talent;
        public int fortune;
        public int maximumHealth;
        public int currentHealth;
        public int maximumSpirit;
        public int currentSpirit;
        public string[] knownSpells;
        public string[] knownSkills;
        public string[] equippedSpells;
        public string[] equippedSkills;
        public int spellSlots;
        public int skillSlots;
        public string mainEquipmentBasicAttackProfileId;
        public string unarmedBasicAttackProfileId;
        public string gongFaId;
        public string realmStage;
        public float realmMultiplier;

        public static CharacterRecord Capture(CharacterStateSnapshot snapshot)
        {
            return new CharacterRecord
            {
                characterId = snapshot.Identity.CharacterId,
                displayName = snapshot.Identity.DisplayName,
                rootBone = snapshot.Attributes.RootBone,
                physique = snapshot.Attributes.Physique,
                spirit = snapshot.Attributes.Spirit,
                mind = snapshot.Attributes.Mind,
                reaction = snapshot.Attributes.Reaction,
                talent = snapshot.Attributes.Talent,
                fortune = snapshot.Attributes.Fortune,
                maximumHealth = snapshot.Resources.MaximumHealth,
                currentHealth = snapshot.Resources.CurrentHealth,
                maximumSpirit = snapshot.Resources.MaximumSpirit,
                currentSpirit = snapshot.Resources.CurrentSpirit,
                knownSpells = snapshot.AbilityLoadout.KnownSpells,
                knownSkills = snapshot.AbilityLoadout.KnownSkills,
                equippedSpells = snapshot.AbilityLoadout.EquippedSpells,
                equippedSkills = snapshot.AbilityLoadout.EquippedSkills,
                spellSlots = snapshot.AbilityLoadout.SpellSlots,
                skillSlots = snapshot.AbilityLoadout.SkillSlots,
                mainEquipmentBasicAttackProfileId = snapshot.MainEquipmentBasicAttackProfileId,
                unarmedBasicAttackProfileId = snapshot.UnarmedBasicAttackProfileId,
                gongFaId = snapshot.Progression.GongFaId,
                realmStage = snapshot.Progression.RealmStage,
                realmMultiplier = snapshot.Progression.RealmMultiplier,
            };
        }

        public CharacterStateSnapshot Restore()
        {
            if (string.IsNullOrWhiteSpace(characterId) || string.IsNullOrWhiteSpace(displayName) ||
                maximumHealth < 0 || currentHealth < 0 || currentHealth > maximumHealth ||
                maximumSpirit < 0 || currentSpirit < 0 || currentSpirit > maximumSpirit ||
                spellSlots < 0 || skillSlots < 0 || realmMultiplier <= 0f)
            {
                throw new ArgumentException("Character record is invalid.");
            }
            return new CharacterStateSnapshot(
                new CharacterIdentitySnapshot(characterId, displayName),
                new CharacterAttributesSnapshot(rootBone, physique, spirit, mind, reaction, talent, fortune),
                new CharacterResourcesSnapshot(maximumHealth, currentHealth, maximumSpirit, currentSpirit),
                new AbilityLoadoutSnapshot(
                    knownSpells ?? new string[0],
                    knownSkills ?? new string[0],
                    equippedSpells ?? new string[0],
                    equippedSkills ?? new string[0],
                    spellSlots,
                    skillSlots),
                new CharacterProgressionSnapshot(gongFaId, realmStage, realmMultiplier),
                mainEquipmentBasicAttackProfileId,
                unarmedBasicAttackProfileId);
        }
    }

    [Serializable]
    public sealed class CultivationRecord
    {
        public int foundationPhase;
        public float foundationProgress;
        public int totalMansionCapacity;
        public MansionRecord[] mansions;
        public GuardianRecord[] guardians;
        public string actionStateId;
        public int actionStatus;
        public string[] committedCycleIds;
        public string retreatId;
        public bool retreatActive;
        public string retreatStopReason;
        public bool jindanFormed;
        public string jindanFormedBy;

        public static CultivationRecord Capture(CultivationStateSnapshot snapshot)
        {
            var mansionRecords = new List<MansionRecord>();
            foreach (MansionStateSnapshot state in snapshot.Mansions)
                mansionRecords.Add(new MansionRecord { mansionId = state.MansionId, buildState = state.BuildState, capacity = state.Capacity });
            var guardianRecords = new List<GuardianRecord>();
            foreach (GuardianAbilityStateSnapshot state in snapshot.Guardians)
                guardianRecords.Add(new GuardianRecord { mansionId = state.MansionId, abilityInstanceId = state.AbilityInstanceId, form = state.Form });
            return new CultivationRecord
            {
                foundationPhase = snapshot.Foundation.Phase,
                foundationProgress = snapshot.Foundation.ContinuousProgress,
                totalMansionCapacity = snapshot.Foundation.TotalMansionCapacity,
                mansions = mansionRecords.ToArray(),
                guardians = guardianRecords.ToArray(),
                actionStateId = snapshot.Action.ActionStateId,
                actionStatus = snapshot.Action.Status,
                committedCycleIds = snapshot.Action.CommittedCycleIds,
                retreatId = snapshot.Retreat.RetreatId,
                retreatActive = snapshot.Retreat.Active,
                retreatStopReason = snapshot.Retreat.LastStopReason,
                jindanFormed = snapshot.JindanLock.IsFormed,
                jindanFormedBy = snapshot.JindanLock.FormedBy,
            };
        }

        public CultivationStateSnapshot Restore()
        {
            if (foundationPhase < 0 || foundationProgress < 0f || totalMansionCapacity < 0 ||
                mansions == null || guardians == null || committedCycleIds == null)
            {
                throw new ArgumentException("Cultivation record is invalid.");
            }
            var mansionStates = new List<MansionStateSnapshot>();
            foreach (MansionRecord state in mansions)
            {
                if (state == null || string.IsNullOrWhiteSpace(state.mansionId) || state.capacity < 0)
                    throw new ArgumentException("Mansion record is invalid.");
                mansionStates.Add(new MansionStateSnapshot(state.mansionId, state.buildState, state.capacity));
            }
            var guardianStates = new List<GuardianAbilityStateSnapshot>();
            foreach (GuardianRecord state in guardians)
            {
                if (state == null || string.IsNullOrWhiteSpace(state.mansionId))
                    throw new ArgumentException("Guardian record is invalid.");
                guardianStates.Add(new GuardianAbilityStateSnapshot(state.mansionId, state.abilityInstanceId, state.form));
            }
            return new CultivationStateSnapshot(
                new FoundationStateSnapshot(foundationPhase, foundationProgress, totalMansionCapacity),
                mansionStates,
                guardianStates,
                new CultivationActionStateSnapshot(actionStateId, actionStatus, committedCycleIds),
                new ClosedRetreatStateSnapshot(retreatId, retreatActive, retreatStopReason),
                new JindanLockStateSnapshot(jindanFormed, jindanFormedBy));
        }
    }

    [Serializable] public sealed class MansionRecord { public string mansionId; public int buildState; public int capacity; }
    [Serializable] public sealed class GuardianRecord { public string mansionId; public string abilityInstanceId; public string form; }

    [Serializable]
    public sealed class WorldClockRecord
    {
        public int year;
        public string seasonId;
        public int day;
        public string timeOfDayId;
        public static WorldClockRecord Capture(WorldClockSnapshot state) { return new WorldClockRecord { year = state.Year, seasonId = state.SeasonId, day = state.Day, timeOfDayId = state.TimeOfDayId }; }
        public WorldClockSnapshot Restore() { return new WorldClockSnapshot(year, seasonId, day, timeOfDayId); }
    }

    [Serializable] public sealed class QuestRecord { public string questId; public int step; public bool completed; }
    [Serializable] public sealed class InventoryRecord { public string itemId; public int quantity; }
    [Serializable] public sealed class NpcRecord { public string npcId; public string worldNodeId; public string cultivationActionId; public FoundationPurpleMansionSaveData cultivationState; }
    [Serializable] public sealed class BountyRecord { public string bountyId; public int status; public int progress; }

    [Serializable]
    public sealed class CharterRecord
    {
        public CharterEntryRecord[] entries;
        public bool hasRuntimeState;
        public int definitionCatalogVersion;
        public CharterRuntimeStateData runtimeState;

        public static CharterRecord Capture(CharterStateSnapshot snapshot)
        {
            var records = new List<CharterEntryRecord>();
            foreach (CharterStateEntry entry in snapshot.Entries)
                records.Add(new CharterEntryRecord { definitionId = entry.DefinitionId, operationId = entry.OperationId, conflictKey = entry.ConflictKey });
            return new CharterRecord
            {
                entries = records.ToArray(),
                hasRuntimeState = snapshot.RuntimeState != null,
                definitionCatalogVersion = snapshot.DefinitionCatalogVersion,
                runtimeState = snapshot.RuntimeState == null ? null : snapshot.RuntimeState.CreateCopy(),
            };
        }

        public CharterStateSnapshot Restore()
        {
            if (entries == null) throw new ArgumentException("Charter entries are missing.");
            var states = new List<CharterStateEntry>();
            foreach (CharterEntryRecord entry in entries)
            {
                if (entry == null) throw new ArgumentException("Charter entry is missing.");
                states.Add(new CharterStateEntry(entry.definitionId, entry.operationId, entry.conflictKey));
            }
            if (hasRuntimeState && runtimeState == null)
                throw new ArgumentException("Charter runtime state is missing.");
            return new CharterStateSnapshot(
                states.ToArray(),
                definitionCatalogVersion,
                hasRuntimeState ? runtimeState : null);
        }
    }

    [Serializable] public sealed class CharterEntryRecord { public string definitionId; public string operationId; public string conflictKey; }

    [Serializable]
    public sealed class NavigationRecord
    {
        public string worldNodeId;
        public string settlementId;
        public string adventureId;
        public string returnSceneName;
        public string returnWorldNodeId;
        public string returnSettlementId;

        public static NavigationRecord Capture(NavigationStateSnapshot state)
        {
            return new NavigationRecord
            {
                worldNodeId = state.WorldNodeId,
                settlementId = state.SettlementId,
                adventureId = state.AdventureId,
                returnSceneName = state.ReturnTarget.SceneName,
                returnWorldNodeId = state.ReturnTarget.WorldNodeId,
                returnSettlementId = state.ReturnTarget.SettlementId,
            };
        }

        public NavigationStateSnapshot Restore()
        {
            if (string.IsNullOrWhiteSpace(worldNodeId)) throw new ArgumentException("World node ID is missing.");
            if (!string.IsNullOrEmpty(returnSceneName) &&
                returnSceneName != GameplaySceneNames.World && returnSceneName != GameplaySceneNames.Settlement)
            {
                throw new ArgumentException("Return scene is invalid.");
            }
            return new NavigationStateSnapshot(
                worldNodeId,
                settlementId,
                adventureId,
                new SceneReturnTarget
                {
                    SceneName = returnSceneName,
                    WorldNodeId = returnWorldNodeId,
                    SettlementId = returnSettlementId,
                });
        }
    }
}
