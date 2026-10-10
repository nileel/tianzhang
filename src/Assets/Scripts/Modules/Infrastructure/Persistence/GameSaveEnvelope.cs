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
                cultivation = cultivation == null ? null : CultivationRecord.Capture(cultivation),
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

        public RestoredGameState Restore(ContentCatalogData catalog, CombatRuleSetData growthRules = null)
        {
            if (schemaVersion != GameSaveSerializer.SchemaVersion)
                throw new ArgumentException("Unsupported save schema.", nameof(schemaVersion));
            if (worldClock == null || quests == null || inventory == null || npcs == null ||
                bounties == null || charter == null || navigation == null)
            {
                throw new ArgumentException("Save envelope is incomplete.");
            }
            if (hasPlayer)
            {
                if (player == null || cultivation == null)
                    throw new ArgumentException("Player state presence is inconsistent.");
                CharacterRuntimeProfile.RequirePlayerRealm(player.realmStage);
                if (catalog == null ||
                    !catalog.TryGetAppearanceProfile(player.appearanceProfileId, out _) &&
                    !string.Equals(player.appearanceProfileId, AppearanceProfileData.NoneId, StringComparison.Ordinal))
                    throw new ArgumentException("Player appearance profile is invalid.");
            }
            else if (HasCharacterPayload(player) || HasCultivationPayload(cultivation))
            {
                if (!string.IsNullOrEmpty(player?.mainEquipmentItemId) || !string.IsNullOrEmpty(player?.mainEquipmentInstanceId) ||
                    !string.IsNullOrEmpty(player?.mainEquipmentBasicAttackProfileId))
                    throw new ArgumentException(GameSaveSerializer.EquipmentSaveInvalid);
                throw new ArgumentException("Player state presence is inconsistent.");
            }

            CharacterRuntimeProfile restoredPlayer = hasPlayer
                ? CharacterRuntimeProfile.FromSnapshot(player.Restore(), growthRules ?? catalog?.CombatRuleSet)
                : null;
            CultivationState restoredCultivation = hasPlayer ? cultivation.Restore() : null;
            var restoredClock = new WorldClockService(1);
            restoredClock.Restore(worldClock.Restore());
            var restoredQuests = new QuestStore();
            restoredQuests.Restore(RestoreQuests(quests));
            var restoredInventory = new InventoryStore();
            try { restoredInventory.Restore(RestoreInventory(inventory, catalog)); }
            catch (ArgumentException) when (!string.IsNullOrEmpty(restoredPlayer?.MainEquipmentItemId))
            { throw new ArgumentException(GameSaveSerializer.EquipmentSaveInvalid); }
            if (restoredPlayer != null && !restoredPlayer.ValidateEquipmentState(catalog,
                restoredInventory.OwnsEquipmentInstance(restoredPlayer.Identity.CharacterId,
                    restoredPlayer.MainEquipmentItemId, restoredPlayer.MainEquipmentInstanceId)))
                throw new ArgumentException(GameSaveSerializer.EquipmentSaveInvalid);
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

        private static bool HasCharacterPayload(CharacterRecord record)
        {
            return record != null &&
                (!string.IsNullOrEmpty(record.characterId) ||
                 !string.IsNullOrEmpty(record.displayName) ||
                 record.rootBone != 0 || record.physique != 0 || record.spirit != 0 ||
                 record.mind != 0 || record.reaction != 0 || record.talent != 0 ||
                 record.fortune != 0 || record.maximumHealth != 0 || record.currentHealth != 0 ||
                 record.maximumSpirit != 0 || record.currentSpirit != 0 ||
                 HasItems(record.knownSpells) || HasItems(record.knownSkills) ||
                 HasItems(record.equippedSpells) || HasItems(record.equippedSkills) ||
                 record.spellSlots != 0 || record.skillSlots != 0 ||
                 !string.IsNullOrEmpty(record.mainEquipmentItemId) ||
                 !string.IsNullOrEmpty(record.mainEquipmentInstanceId) ||
                 !string.IsNullOrEmpty(record.mainEquipmentBasicAttackProfileId) ||
                 !string.IsNullOrEmpty(record.unarmedBasicAttackProfileId) ||
                 !string.IsNullOrEmpty(record.appearanceProfileId) ||
                 !string.IsNullOrEmpty(record.visibleRootId) ||
                 !string.IsNullOrEmpty(record.visibleRootGrade) ||
                 !string.IsNullOrEmpty(record.visibleRootElement) ||
                 !string.IsNullOrEmpty(record.gongFaId) ||
                 !string.IsNullOrEmpty(record.realmStage) ||
                 record.realmMultiplier != 0f ||
                 record.hpBonus != 0f || record.mpBonus != 0f ||
                 record.physAtkBonus != 0f || record.magAtkBonus != 0f ||
                 record.physDefBonus != 0f || record.magDefBonus != 0f ||
                 record.blockRate != 0f || record.blockReduction != 0f ||
                 record.soulShieldRate != 0f || record.soulShieldReduction != 0f ||
                 record.dodgeRate != 0f || record.critRate != 0f ||
                 record.critDamage != 0f || record.hitRateBonus != 0f ||
                 record.actionEconomyInputsPresent || record.actionPointMaximumUnits != 0 ||
                 record.movementEfficiencyMilliUnits != 0 || HasItems(record.actionEconomyModifierInputs) ||
                 record.movementGrowthInputsPresent || HasItems(record.movementGrowthSources));
        }

        private static bool HasCultivationPayload(CultivationRecord record)
        {
            return record != null && record.hasFoundationPurpleMansionState;
        }

        private static bool HasItems<T>(T[] values)
        {
            return values != null && values.Length > 0;
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
    public sealed class MovementGrowthRecord
    {
        public string sourceInstanceId;
        public string gongFaId;
        public string chapterId;
        public string realmId;
        public int[] completedNodeIndices;
        public static MovementGrowthRecord Capture(MovementGrowthSource source)
        {
            var nodes = new int[source.CompletedNodeIndices.Count];
            for (int i = 0; i < nodes.Length; i++) nodes[i] = source.CompletedNodeIndices[i];
            return new MovementGrowthRecord { sourceInstanceId = source.SourceInstanceId, gongFaId = source.GongFaId,
                chapterId = source.ChapterId, realmId = source.RealmId, completedNodeIndices = nodes };
        }
        public MovementGrowthSource Restore() => new MovementGrowthSource(sourceInstanceId, gongFaId, chapterId, realmId, completedNodeIndices);
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
        public string mainEquipmentItemId;
        public string mainEquipmentInstanceId;
        public string mainEquipmentBasicAttackProfileId;
        public string unarmedBasicAttackProfileId;
        public string appearanceProfileId;
        public string visibleRootId;
        public string visibleRootGrade;
        public string visibleRootElement;
        public string gongFaId;
        public string realmStage;
        public float realmMultiplier;
        public float hpBonus;
        public float mpBonus;
        public float physAtkBonus;
        public float magAtkBonus;
        public float physDefBonus;
        public float magDefBonus;
        public float blockRate;
        public float blockReduction;
        public float soulShieldRate;
        public float soulShieldReduction;
        public float dodgeRate;
        public float critRate;
        public float critDamage;
        public float hitRateBonus;
        // 长期行动力输入（公共合同§七）：只保存基础长期输入与不可重建的修正来源；
        // 不保存 AP 余额、临时 CT、回合／返还使用账本。
        public bool actionEconomyInputsPresent;
        public int actionPointMaximumUnits;
        public int movementEfficiencyMilliUnits;
        public ActionEconomyModifierRecord[] actionEconomyModifierInputs;
        public bool movementGrowthInputsPresent;
        public MovementGrowthRecord[] movementGrowthSources;

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
                mainEquipmentItemId = snapshot.MainEquipmentItemId,
                mainEquipmentInstanceId = snapshot.MainEquipmentInstanceId,
                mainEquipmentBasicAttackProfileId = snapshot.MainEquipmentBasicAttackProfileId,
                unarmedBasicAttackProfileId = string.Empty,
                appearanceProfileId = snapshot.AppearanceProfileId,
                visibleRootId = snapshot.VisibleRootId,
                visibleRootGrade = snapshot.VisibleRootGrade,
                visibleRootElement = snapshot.VisibleRootElement,
                gongFaId = snapshot.Progression.GongFaId,
                realmStage = snapshot.Progression.RealmStage,
                realmMultiplier = snapshot.Progression.RealmMultiplier,
                hpBonus = snapshot.CombatModifiers.HpBonus,
                mpBonus = snapshot.CombatModifiers.MpBonus,
                physAtkBonus = snapshot.CombatModifiers.PhysAtkBonus,
                magAtkBonus = snapshot.CombatModifiers.MagAtkBonus,
                physDefBonus = snapshot.CombatModifiers.PhysDefBonus,
                magDefBonus = snapshot.CombatModifiers.MagDefBonus,
                blockRate = snapshot.CombatModifiers.BlockRate,
                blockReduction = snapshot.CombatModifiers.BlockReduction,
                soulShieldRate = snapshot.CombatModifiers.SoulShieldRate,
                soulShieldReduction = snapshot.CombatModifiers.SoulShieldReduction,
                dodgeRate = snapshot.CombatModifiers.DodgeRate,
                critRate = snapshot.CombatModifiers.CritRate,
                critDamage = snapshot.CombatModifiers.CritDamage,
                hitRateBonus = snapshot.CombatModifiers.HitRateBonus,
                movementGrowthInputsPresent = snapshot.Progression.MovementGrowthInputsPresent,
                movementGrowthSources = CaptureMovementGrowth(snapshot.Progression.MovementGrowthSources),
                actionEconomyInputsPresent = snapshot.Attributes.ActionEconomyInputsPresent,
                actionPointMaximumUnits = snapshot.Attributes.ActionPointMaximumUnits,
                movementEfficiencyMilliUnits = snapshot.Attributes.MovementEfficiencyMilliUnits,
                actionEconomyModifierInputs = CaptureActionEconomyModifierInputs(snapshot.CombatModifiers.ActionEconomyInputs),
            };
        }

        private static MovementGrowthRecord[] CaptureMovementGrowth(IReadOnlyList<MovementGrowthSource> sources)
        {
            var result = new List<MovementGrowthRecord>();
            foreach (var source in sources) result.Add(MovementGrowthRecord.Capture(source));
            return result.ToArray();
        }
        private MovementGrowthSource[] RestoreMovementGrowth()
        {
            if (!movementGrowthInputsPresent || movementGrowthSources == null)
                throw new ArgumentException("character_growth_inputs_invalid");
            var result = new List<MovementGrowthSource>();
            foreach (var source in movementGrowthSources)
            {
                if (source == null) throw new ArgumentException("character_growth_source_invalid");
                result.Add(source.Restore());
            }
            return result.ToArray();
        }

        private static ActionEconomyModifierRecord[] CaptureActionEconomyModifierInputs(
            IReadOnlyList<ActionEconomyModifierInput> inputs)
        {
            var records = new List<ActionEconomyModifierRecord>();
            foreach (ActionEconomyModifierInput input in inputs)
                records.Add(ActionEconomyModifierRecord.Capture(input));
            return records.ToArray();
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
            if (!actionEconomyInputsPresent || actionPointMaximumUnits <= 0 || movementEfficiencyMilliUnits < 0)
            {
                throw new ArgumentException(
                    GameSaveSerializer.ActionEconomySaveInvalid +
                    ": action economy inputs are missing, the presence marker is false, or a value is out of range.");
            }
            CharacterCombatModifiers combatModifiers = RestoreCombatModifiers();
            return new CharacterStateSnapshot(
                new CharacterIdentitySnapshot(characterId, displayName),
                new CharacterAttributesSnapshot(rootBone, physique, spirit, mind, reaction, talent, fortune,
                    actionPointMaximumUnits, movementEfficiencyMilliUnits, actionEconomyInputsPresent),
                new CharacterResourcesSnapshot(maximumHealth, currentHealth, maximumSpirit, currentSpirit),
                new AbilityLoadoutSnapshot(
                    knownSpells ?? new string[0],
                    knownSkills ?? new string[0],
                    equippedSpells ?? new string[0],
                    equippedSkills ?? new string[0],
                    spellSlots,
                    skillSlots),
                new CharacterProgressionSnapshot(gongFaId, realmStage, realmMultiplier, movementGrowthInputsPresent, RestoreMovementGrowth()),
                combatModifiers,
                mainEquipmentBasicAttackProfileId,
                string.Empty,
                appearanceProfileId,
                visibleRootId, visibleRootGrade, visibleRootElement, mainEquipmentItemId, mainEquipmentInstanceId);
        }

        /// <summary>
        /// Rebuilds the declared long-term correction sources. Every source is validated before the whole
        /// array is published, so a bad entry leaves the caller's live state untouched.
        /// </summary>
        private CharacterCombatModifiers RestoreCombatModifiers()
        {
            var inputs = new List<ActionEconomyModifierInput>();
            foreach (ActionEconomyModifierRecord record in actionEconomyModifierInputs ?? new ActionEconomyModifierRecord[0])
            {
                if (record == null)
                {
                    throw new ArgumentException(
                        GameSaveSerializer.ActionEconomySaveInvalid + ": an action economy modifier input is missing.");
                }
                inputs.Add(record.Restore());
            }
            try
            {
                CharacterCombatModifiers.ValidateActionEconomyInputs(inputs);
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException(
                    GameSaveSerializer.ActionEconomySaveInvalid + ": " + exception.Message, exception);
            }
            return new CharacterCombatModifiers(
                hpBonus,
                mpBonus,
                physAtkBonus,
                magAtkBonus,
                physDefBonus,
                magDefBonus,
                blockRate,
                blockReduction,
                soulShieldRate,
                soulShieldReduction,
                dodgeRate,
                critRate,
                critDamage,
                hitRateBonus,
                inputs);
        }
    }

    /// <summary>
    /// Persistence projection of one frozen <see cref="ActionEconomyModifierInput"/>. Every field is a long-term
    /// declaration of the named source; no runtime balance, turn ledger or usage count is stored here.
    /// </summary>
    [Serializable]
    public sealed class ActionEconomyModifierRecord
    {
        public string sourceInstanceId;
        public string sourceDefinitionId;
        public int actionKind;
        public string profileId;
        public int actionPointMaximumBonusUnits;
        public int movementEfficiencyBonusMilliUnits;
        public int fixedReductionUnits;
        public int retainedCostPermille;
        public int ctPenaltyReduction;
        public int reuseCooldownReductionTicks;
        public int minimumNetCostUnits;
        public int refundOnEvent;
        public int refundUnits;
        public int refundLimitPerOwnTurn;
        public int refundBudgetPerOwnTurnUnits;

        public static ActionEconomyModifierRecord Capture(ActionEconomyModifierInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return new ActionEconomyModifierRecord
            {
                sourceInstanceId = input.SourceInstanceId,
                sourceDefinitionId = input.SourceDefinitionId,
                actionKind = (int)input.ActionKind,
                profileId = input.ProfileId,
                actionPointMaximumBonusUnits = input.ActionPointMaximumBonusUnits,
                movementEfficiencyBonusMilliUnits = input.MovementEfficiencyBonusMilliUnits,
                fixedReductionUnits = input.FixedReductionUnits,
                retainedCostPermille = input.RetainedCostPermille,
                ctPenaltyReduction = input.CtPenaltyReduction,
                reuseCooldownReductionTicks = input.ReuseCooldownReductionTicks,
                minimumNetCostUnits = input.MinimumNetCostUnits,
                refundOnEvent = (int)input.RefundOnEvent,
                refundUnits = input.RefundUnits,
                refundLimitPerOwnTurn = input.RefundLimitPerOwnTurn,
                refundBudgetPerOwnTurnUnits = input.RefundBudgetPerOwnTurnUnits,
            };
        }

        public ActionEconomyModifierInput Restore()
        {
            try
            {
                return new ActionEconomyModifierInput(
                    sourceInstanceId,
                    sourceDefinitionId,
                    (ActionEconomyModifierInput.CommandKind)actionKind,
                    profileId,
                    actionPointMaximumBonusUnits,
                    movementEfficiencyBonusMilliUnits,
                    fixedReductionUnits,
                    retainedCostPermille,
                    ctPenaltyReduction,
                    reuseCooldownReductionTicks,
                    minimumNetCostUnits,
                    (ActionEconomyModifierInput.SuccessEvent)refundOnEvent,
                    refundUnits,
                    refundLimitPerOwnTurn,
                    refundBudgetPerOwnTurnUnits);
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException(
                    GameSaveSerializer.ActionEconomySaveInvalid + ": " + exception.Message, exception);
            }
        }
    }

    [Serializable]
    public sealed class CultivationRecord
    {
        public bool hasFoundationPurpleMansionState;
        public FoundationPurpleMansionSaveData foundationPurpleMansionState;

        public static CultivationRecord Capture(CultivationState state)
        {
            return new CultivationRecord
            {
                hasFoundationPurpleMansionState = state.HasFoundationPurpleMansionState,
                foundationPurpleMansionState = state.CaptureFoundationPurpleMansionSaveData(),
            };
        }

        public CultivationState Restore()
        {
            if (!hasFoundationPurpleMansionState)
                return CultivationState.CreateEmpty();
            if (foundationPurpleMansionState == null)
                throw new ArgumentException("Cultivation record is invalid.");
            return CultivationState.FromFoundationPurpleMansionSaveData(foundationPurpleMansionState);
        }
    }

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
            if (hasRuntimeState)
            {
                if (runtimeState == null || definitionCatalogVersion <= 0)
                    throw new ArgumentException("Charter runtime state presence is inconsistent.");
            }
            else if (HasRuntimePayload(runtimeState) || definitionCatalogVersion != 0)
            {
                throw new ArgumentException("Charter runtime state presence is inconsistent.");
            }
            var states = new List<CharterStateEntry>();
            foreach (CharterEntryRecord entry in entries)
            {
                if (entry == null) throw new ArgumentException("Charter entry is missing.");
                states.Add(new CharterStateEntry(entry.definitionId, entry.operationId, entry.conflictKey));
            }
            return new CharterStateSnapshot(
                states.ToArray(),
                definitionCatalogVersion,
                hasRuntimeState ? runtimeState : null);
        }

        private static bool HasRuntimePayload(CharterRuntimeStateData state)
        {
            return state != null &&
                (!string.IsNullOrEmpty(state.stateId) ||
                 HasItems(state.registeredRuleEntryIds) ||
                 !string.IsNullOrEmpty(state.charterRelicState) ||
                 !string.IsNullOrEmpty(state.worldSealState) ||
                 HasItems(state.nodeStates) ||
                 HasItems(state.organizationAuthorizationVersions) ||
                 HasItems(state.currentCoverageSet) ||
                 HasItems(state.ruleEntryOccupancies) ||
                 HasItems(state.nodeOccupancies) ||
                 HasItems(state.realitySupplyStates) ||
                 HasItems(state.positiveCommitResults) ||
                 HasItems(state.negativeCommitResults) ||
                 HasItems(state.currentRegionRuleEntryIds));
        }

        private static bool HasItems<T>(T[] values)
        {
            return values != null && values.Length > 0;
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
            bool hasDefaultReturnTarget =
                string.IsNullOrEmpty(returnSceneName) &&
                string.IsNullOrEmpty(returnWorldNodeId) &&
                string.IsNullOrEmpty(returnSettlementId);
            bool returnsToWorld =
                returnSceneName == GameplaySceneNames.World &&
                !string.IsNullOrWhiteSpace(returnWorldNodeId) &&
                string.IsNullOrEmpty(returnSettlementId);
            bool returnsToSettlement =
                returnSceneName == GameplaySceneNames.Settlement &&
                !string.IsNullOrWhiteSpace(returnSettlementId) &&
                string.IsNullOrEmpty(returnWorldNodeId);
            if ((!hasDefaultReturnTarget && !returnsToWorld && !returnsToSettlement) ||
                (!string.IsNullOrWhiteSpace(adventureId) && hasDefaultReturnTarget))
            {
                throw new ArgumentException("Return target is invalid.");
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
