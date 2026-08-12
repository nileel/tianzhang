using System;
using System.Collections.Generic;
using TianZhang.Character;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.World;
using UnityEngine;

namespace TianZhang.Features.Adventure
{
    public enum AdventureSceneState
    {
        Loading,
        Exploration,
        Combat,
        Returning,
    }

    public sealed class AdventureController : MonoBehaviour
    {
        private ContentCatalogData catalog;
        private AdventureMapData map;
        private CharacterStateSnapshot player;
        private EnvironmentProfileAsset environmentProfile;
        private GameObject unitMarkerPrefab;
        private AttackProfileData[] attackProfiles;
        private AdventureMapLoader mapLoader;
        private AdventureUnitSpawner unitSpawner;
        private AdventureInputController input;
        private AdventureHudPresenter hud;
        private EncounterCoordinator encounters;
        private CombatEntryAdapter combatEntry;
        private INavigationUseCase navigation;
        private BountyUseCase bounties;
        private InventoryGrantUseCase inventoryGrants;
        private Action<string> loadScene;
        private IFormalEncounterRandomSource randomSource = new SystemFormalEncounterRandomSource();
        private AdventureSession session;
        private bool formalEncounterConsumed;

        public AdventureSceneState CurrentState { get; private set; } = AdventureSceneState.Loading;
        public CombatSessionOutcome LastEncounterOutcome { get; private set; } = CombatSessionOutcome.Ongoing;
        public FormalEncounterResult LastFormalEncounterResult { get; private set; }
        public string LastFailureReason { get; private set; }
        public AdventureSession Session => session;

        public void Configure(
            ContentCatalogData contentCatalog,
            AdventureMapData adventureMap,
            CharacterStateSnapshot playerSnapshot,
            EnvironmentProfileAsset environment,
            GameObject markerPrefab,
            AttackProfileData[] profiles,
            AdventureMapLoader loader,
            AdventureUnitSpawner spawner,
            AdventureInputController inputController,
            AdventureHudPresenter presenter,
            EncounterCoordinator encounterCoordinator,
            CombatEntryAdapter entryAdapter,
            INavigationUseCase navigationUseCase,
            BountyUseCase bountyUseCase,
            InventoryGrantUseCase inventoryGrantUseCase,
            Action<string> sceneLoader)
        {
            catalog = contentCatalog ?? throw new ArgumentNullException(nameof(contentCatalog));
            map = adventureMap ?? throw new ArgumentNullException(nameof(adventureMap));
            player = playerSnapshot ?? throw new ArgumentNullException(nameof(playerSnapshot));
            environmentProfile = environment ?? throw new ArgumentNullException(nameof(environment));
            unitMarkerPrefab = markerPrefab ?? throw new ArgumentNullException(nameof(markerPrefab));
            attackProfiles = profiles ?? Array.Empty<AttackProfileData>();
            mapLoader = loader ?? throw new ArgumentNullException(nameof(loader));
            unitSpawner = spawner ?? throw new ArgumentNullException(nameof(spawner));
            input = inputController ?? throw new ArgumentNullException(nameof(inputController));
            hud = presenter ?? throw new ArgumentNullException(nameof(presenter));
            encounters = encounterCoordinator ?? throw new ArgumentNullException(nameof(encounterCoordinator));
            combatEntry = entryAdapter ?? throw new ArgumentNullException(nameof(entryAdapter));
            navigation = navigationUseCase ?? throw new ArgumentNullException(nameof(navigationUseCase));
            bounties = bountyUseCase ?? throw new ArgumentNullException(nameof(bountyUseCase));
            inventoryGrants = inventoryGrantUseCase ?? throw new ArgumentNullException(nameof(inventoryGrantUseCase));
            loadScene = sceneLoader ?? throw new ArgumentNullException(nameof(sceneLoader));
        }

        private void Start()
        {
            var dispatcher = new AdventureNodeDispatcher(new IAdventureNodeHandler[]
            {
                new AdventureNodeDispatcher.StartNodeHandler(),
                new EncounterNodeHandler(BeginEncounter),
                new ReturnNodeHandler(ReturnToSource),
            });
            if (!mapLoader.TryLoad(map, catalog, dispatcher, out session, out string reason))
            {
                Fail(reason);
                return;
            }
            input.Configure(session, dispatcher, hud);
            hud.Present(session, input.SelectNode, null);
            CurrentState = AdventureSceneState.Exploration;
        }

        public void SetEncounterRandomSource(IFormalEncounterRandomSource source)
        {
            randomSource = source ?? throw new ArgumentNullException(nameof(source));
        }

        public string BuildSourceDescription()
        {
            SceneReturnTarget target = navigation.Navigation.ReturnTarget;
            if (target.SceneName == GameplaySceneNames.Settlement)
                return "来源据点: " + (target.SettlementId ?? "未记录");
            if (target.SceneName == GameplaySceneNames.World)
                return "来源主世界: " + (target.WorldNodeId ?? "未记录");
            return "来源: 未记录";
        }

        private bool BeginEncounter(AdventureNodeData encounterNode, out string reason)
        {
            if (CurrentState != AdventureSceneState.Exploration)
            {
                reason = "adventure_not_exploring";
                return false;
            }
            AdventureNodeData startNode = session.CurrentNode;
            if (!encounters.TryBegin(
                    player,
                    catalog,
                    startNode,
                    encounterNode,
                    unitMarkerPrefab,
                    attackProfiles,
                    environmentProfile,
                    unitSpawner,
                    combatEntry,
                    out reason))
            {
                Fail(reason);
                return false;
            }
            CurrentState = AdventureSceneState.Combat;
            return true;
        }

        private bool ReturnToSource(AdventureNodeData node, out string reason)
        {
            CurrentState = AdventureSceneState.Returning;
            reason = "adventure_returning";
            loadScene(navigation.ReturnToPreviousScene());
            return true;
        }

        public void ResolveEncounter(CombatSessionOutcome outcome, EnemyData enemy)
        {
            if (formalEncounterConsumed)
            {
                Fail(FormalEncounterRules.AlreadyConsumedReason);
                return;
            }

            formalEncounterConsumed = true;
            LastEncounterOutcome = outcome;
            if (!FormalEncounterResult.TryCreate(
                    catalog,
                    enemy,
                    map.adventureId,
                    outcome,
                    randomSource,
                    out FormalEncounterResult result,
                    out string reason))
            {
                Fail(reason);
                return;
            }
            LastFormalEncounterResult = result;
            if (outcome == CombatSessionOutcome.Victory)
            {
                bounties.RecordDefeat(catalog, result.AdventureId, result.EnemyId);
                var grants = new List<InventoryGrantRequest>(result.DropGrants.Count);
                foreach (FormalDropGrant grant in result.DropGrants)
                    grants.Add(new InventoryGrantRequest(grant.ItemId, grant.Quantity));
                if (grants.Count > 0)
                {
                    InventoryGrantResult grantResult = inventoryGrants.Grant(catalog, grants);
                    if (!grantResult.Applied)
                    {
                        Fail("formal_encounter_inventory_grant_failed:" + grantResult.FailureReason);
                        return;
                    }
                }
            }
            CurrentState = AdventureSceneState.Returning;
            loadScene(navigation.ReturnToPreviousScene());
        }

        private void Fail(string reason)
        {
            LastFailureReason = string.IsNullOrWhiteSpace(reason) ? "adventure_failed" : reason;
            CurrentState = AdventureSceneState.Loading;
            Debug.LogError("[Adventure] " + LastFailureReason);
            if (session != null) hud?.Present(session, input.SelectNode, LastFailureReason);
        }
    }
}
