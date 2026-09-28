using System;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Features.Adventure;
using TianZhang.Features.CombatPresentation;
using TianZhang.Infrastructure.UnityContent;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TianZhang.Bootstrap
{
    public sealed class AdventureSceneInstaller : MonoBehaviour
    {
        [Header("Content")]
        [SerializeField] private TextAsset languageTable;
        [SerializeField] private ContentCatalogData contentCatalog;
        [SerializeField] private EnvironmentProfileAsset environmentProfile;
        [SerializeField] private AttackProfileData[] attackProfiles = Array.Empty<AttackProfileData>();
        [SerializeField] private CombatUnitPresentationProfileCatalogData combatPresentationProfileCatalog;

        [Header("Adventure")]
        [SerializeField] private AdventureController controller;
        [SerializeField] private AdventureInputController input;
        [SerializeField] private AdventureHudPresenter adventureHud;
        [SerializeField] private AdventureUnitSpawner unitSpawner;
        [SerializeField] private EncounterCoordinator encounterCoordinator;

        [Header("Combat presentation")]
        [SerializeField] private CombatHudPresenter combatHudPresenter;
        [SerializeField] private CombatHudView combatHudView;
        [SerializeField] private CombatCommandInput combatCommandInput;
        [SerializeField] private CombatActionBarView combatActionBar;
        [SerializeField] private CombatLogView combatLogView;
        [SerializeField] private Static3DCombatUnitPresentationProvider static3DUnitPresentationProvider;

        private void Awake()
        {
            try
            {
                RequireReference(languageTable, "adventure_language_table_missing");
                RequireReference(contentCatalog, "adventure_content_catalog_missing");
                RequireReference(environmentProfile, "adventure_environment_missing");
                RequireReference(combatPresentationProfileCatalog, "adventure_presentation_catalog_missing");
                if (!combatPresentationProfileCatalog.TryValidate(out _))
                    throw new InvalidOperationException("adventure_presentation_catalog_invalid");
                RequireReference(controller, "adventure_controller_missing");
                RequireReference(input, "adventure_input_missing");
                RequireReference(adventureHud, "adventure_hud_missing");
                RequireReference(unitSpawner, "adventure_unit_spawner_missing");
                RequireReference(encounterCoordinator, "adventure_encounter_coordinator_missing");
                RequireReference(combatHudPresenter, "combat_hud_presenter_missing");
                RequireReference(combatHudView, "combat_hud_view_missing");
                RequireReference(combatCommandInput, "combat_command_input_missing");
                RequireReference(combatActionBar, "combat_action_bar_missing");
                RequireReference(combatLogView, "combat_log_view_missing");
                RequireReference(static3DUnitPresentationProvider, "static_3d_presentation_provider_missing");
                if (!static3DUnitPresentationProvider.TryValidate(out string presentationReason))
                    throw new InvalidOperationException(presentationReason);
                GameRuntime runtime = GameBootstrap.RequireRuntime();
                if (runtime.Player == null) throw new InvalidOperationException("adventure_player_missing");
                if (!contentCatalog.TryGetAdventureMap(
                        runtime.Navigation.AdventureId,
                        out AdventureMapData adventureMap))
                    throw new InvalidOperationException(
                        "adventure_map_unresolved:" + runtime.Navigation.AdventureId);
                combatHudPresenter.Configure(combatHudView, combatCommandInput, combatLogView);
                combatCommandInput.Configure(encounterCoordinator, combatActionBar);
                encounterCoordinator.Configure(
                    combatHudPresenter,
                    static3DUnitPresentationProvider,
                    combatPresentationProfileCatalog,
                    controller.ResolveEncounter);
                controller.Configure(
                    contentCatalog,
                    adventureMap,
                    runtime.Player.Capture(),
                    environmentProfile,
                    attackProfiles,
                    new AdventureMapLoader(),
                    unitSpawner,
                    input,
                    adventureHud,
                    encounterCoordinator,
                    new CombatEntryAdapter(),
                    runtime,
                    runtime.Bounties,
                    runtime.InventoryGrants,
                    SceneManager.LoadScene);
            }
            catch (InvalidOperationException exception)
            {
                Disable(controller);
                Disable(input);
                Disable(encounterCoordinator);
                Disable(combatCommandInput);
                Debug.LogError("[AdventureInstaller] " + exception.Message);
            }
        }

        private static void RequireReference(UnityEngine.Object value, string reason)
        {
            if (value == null) throw new InvalidOperationException(reason);
        }

        private static void Disable(Behaviour behaviour)
        {
            if (behaviour != null) behaviour.enabled = false;
        }
    }
}
