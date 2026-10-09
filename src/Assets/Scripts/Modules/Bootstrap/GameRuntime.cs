using System;
using TianZhang.Character;
using TianZhang.Content;
using TianZhang.Cultivation;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.Persistence;
using TianZhang.World;

namespace TianZhang.Bootstrap
{
    /// <summary>Application lifetime owner. Domain algorithms remain in their module use cases.</summary>
    public sealed class GameRuntime : INavigationUseCase
    {
        public const int InitialWorldYear = 387;
        public const string InitialWorldSeasonId = "autumn";
        public const string InitialWorldTimeOfDayId = "dawn";
        public const string DefaultWorldNodeId = "jiangzuo_hub";

        private QuestStore quests;
        private InventoryStore inventory;
        private NpcStore npcs;
        private BountyStore bountyStore;
        private CharterStore charterStore;
        private NpcCultivationUseCase npcCultivation;

        public GameRuntime()
        {
            Initialize(null, null, DefaultWorldNodeId);
        }

        public CharacterRuntimeProfile Player { get; private set; }
        public CultivationState Cultivation { get; private set; }
        public WorldClockService WorldClock { get; private set; }
        public NavigationStateSnapshot Navigation { get; private set; }
        public BountyUseCase Bounties { get; private set; }
        public InventoryGrantUseCase InventoryGrants { get; private set; }
        public CharterUseCase Charters { get; private set; }

        public void BeginNewGame(
            CharacterRuntimeProfile player,
            CultivationState cultivation,
            string startNodeId)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (cultivation == null) throw new ArgumentNullException(nameof(cultivation));
            Initialize(player, cultivation, startNodeId);
        }

        public long InventoryVersion => inventory.Version;
        public bool OwnsEquipmentInstance(string actorId, string itemId, string instanceId) =>
            inventory.OwnsEquipmentInstance(actorId, itemId, instanceId);

        public void BeginEquippedNewGame(CharacterRuntimeProfile player, CultivationState cultivation,
            string startNodeId, ContentCatalogData catalog)
        {
            if (player == null || cultivation == null) throw new ArgumentNullException(nameof(player));
            if (player.Progression.RealmStage != "练气" || catalog == null ||
                !catalog.TryGetEquipment("item_iron_sword", out _) || !catalog.TryGetEquipment("item_iron_blade", out _))
                throw new InvalidOperationException("equipment_definition_invalid");
            var candidateInventory = new InventoryStore();
            var grant = new InventoryGrantUseCase(candidateInventory).Grant(catalog, new[]
            { new InventoryGrantRequest("item_iron_sword", 1), new InventoryGrantRequest("item_iron_blade", 1) });
            if (!grant.Applied) throw new InvalidOperationException("equipment_not_owned");
            string instance = player.Identity.CharacterId + ":initial:item_iron_sword";
            if (!player.TryPrepareEquipmentChange(catalog, "main_hand", "item_iron_sword", instance,
                candidateInventory.OwnsEquipmentInstance(player.Identity.CharacterId, "item_iron_sword", instance),
                out Action commit, out string reason)) throw new InvalidOperationException(reason);
            commit();
            Initialize(player, cultivation, startNodeId);
            inventory = candidateInventory;
            BindUseCases();
        }

        public void Clear()
        {
            Initialize(null, null, DefaultWorldNodeId);
        }

        public WorldClockSnapshot AdvanceWorldDay()
        {
            WorldClock.AdvanceDay();
            return WorldClock.Capture();
        }

        public NpcCultivationResult RecalculateNpcCultivation(
            string npcId,
            NpcCultivationRequest request)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                throw new ArgumentException("NPC ID is required.", nameof(npcId));
            NpcState npc;
            if (!npcs.TryGet(npcId, out npc))
                return NpcCultivationResult.Rejected(NpcCultivationUseCaseReasons.NpcNotFound);
            NpcCultivationResult result = npcCultivation.Recalculate(npc.CultivationState, request);
            if (result.Succeeded)
            {
                npcs.TrySet(
                    npc.NpcId,
                    npc.WorldNodeId,
                    result.SelectedActionStableId,
                    result.State);
            }
            return result;
        }

        public string CaptureSaveJson()
        {
            return GameSaveSerializer.Serialize(CaptureSave());
        }

        public GameSaveEnvelope CaptureSave()
        {
            return GameSaveEnvelope.Capture(
                Player,
                Cultivation,
                WorldClock,
                quests,
                inventory,
                npcs,
                bountyStore,
                charterStore,
                Navigation);
        }

        public void RestoreSaveJson(string json, ContentCatalogData catalog)
        {
            RestoreSave(GameSaveSerializer.Deserialize(json), catalog);
        }

        public void RestoreSave(GameSaveEnvelope envelope, ContentCatalogData catalog)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            RestoredGameState restored = envelope.Restore(catalog);
            Player = restored.Player;
            Cultivation = restored.Cultivation;
            WorldClock = restored.WorldClock;
            quests = restored.Quests;
            inventory = restored.Inventory;
            npcs = restored.Npcs;
            bountyStore = restored.Bounties;
            charterStore = restored.Charter;
            Navigation = restored.Navigation;
            BindUseCases();
        }

        public string EnterWorld(string nodeId)
        {
            Navigation = new NavigationStateSnapshot(
                nodeId,
                null,
                null,
                default(SceneReturnTarget));
            return GameplaySceneNames.World;
        }

        public string EnterSettlement(string settlementId)
        {
            if (string.IsNullOrWhiteSpace(settlementId))
                throw new ArgumentException("Settlement ID is required.", nameof(settlementId));
            Navigation = new NavigationStateSnapshot(
                Navigation.WorldNodeId,
                settlementId,
                null,
                SceneReturnTarget.World(Navigation.WorldNodeId));
            return GameplaySceneNames.Settlement;
        }

        public string EnterAdventure(string adventureId, SceneReturnTarget returnTarget)
        {
            if (string.IsNullOrWhiteSpace(adventureId))
                throw new ArgumentException("Adventure ID is required.", nameof(adventureId));
            bool returnsToWorld =
                returnTarget.SceneName == GameplaySceneNames.World &&
                !string.IsNullOrWhiteSpace(returnTarget.WorldNodeId) &&
                string.IsNullOrEmpty(returnTarget.SettlementId);
            bool returnsToSettlement =
                returnTarget.SceneName == GameplaySceneNames.Settlement &&
                !string.IsNullOrWhiteSpace(returnTarget.SettlementId) &&
                string.IsNullOrEmpty(returnTarget.WorldNodeId);
            if (!returnsToWorld && !returnsToSettlement)
            {
                throw new ArgumentException("Adventure return target is invalid.", nameof(returnTarget));
            }
            Navigation = new NavigationStateSnapshot(
                Navigation.WorldNodeId,
                Navigation.SettlementId,
                adventureId,
                returnTarget);
            return GameplaySceneNames.Adventure;
        }

        public string ReturnToPreviousScene()
        {
            SceneReturnTarget target = Navigation.ReturnTarget;
            if (target.SceneName == GameplaySceneNames.Settlement)
            {
                Navigation = new NavigationStateSnapshot(
                    Navigation.WorldNodeId,
                    target.SettlementId,
                    null,
                    default(SceneReturnTarget));
                return GameplaySceneNames.Settlement;
            }

            Navigation = new NavigationStateSnapshot(
                string.IsNullOrWhiteSpace(target.WorldNodeId) ? Navigation.WorldNodeId : target.WorldNodeId,
                null,
                null,
                default(SceneReturnTarget));
            return GameplaySceneNames.World;
        }

        private void Initialize(
            CharacterRuntimeProfile player,
            CultivationState cultivation,
            string startNodeId)
        {
            Player = player;
            Cultivation = cultivation;
            WorldClock = new WorldClockService(
                InitialWorldYear,
                InitialWorldSeasonId,
                1,
                InitialWorldTimeOfDayId);
            quests = new QuestStore();
            inventory = new InventoryStore();
            npcs = new NpcStore();
            bountyStore = new BountyStore();
            charterStore = new CharterStore();
            Navigation = new NavigationStateSnapshot(startNodeId, null, null, default(SceneReturnTarget));
            BindUseCases();
        }

        private void BindUseCases()
        {
            InventoryGrants = new InventoryGrantUseCase(inventory);
            Bounties = new BountyUseCase(bountyStore, inventory);
            Charters = new CharterUseCase(charterStore);
            npcCultivation = new NpcCultivationUseCase();
        }
    }
}
