using System;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Features.Adventure;
using TianZhang.Features.CombatPresentation;
using TianZhang.Features.Settlement;
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
        [SerializeField] private CombatMovementView combatMovementView;
        [SerializeField] private CombatActionBarView combatActionBar;
        [SerializeField] private CombatLogView combatLogView;
        [SerializeField] private CombatTimelineView combatTimelineView;
        [SerializeField] private CombatTargetView combatTargetView;
        [SerializeField] private CombatTargetInput combatTargetInput;
        [SerializeField] private GameObject explorationPanel;
        [SerializeField] private Static3DCombatUnitPresentationProvider static3DUnitPresentationProvider;
        [SerializeField] private CombatUnitHealthBarView combatUnitHealthBars;
        [SerializeField] private Camera battlefieldCamera;
        [SerializeField] private Canvas hudCanvas;

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
                RequireReference(combatMovementView, "combat_movement_view_missing");
                RequireReference(combatActionBar, "combat_action_bar_missing");
                RequireReference(combatLogView, "combat_log_view_missing");
                RequireReference(combatTimelineView, "combat_timeline_view_missing");
                RequireReference(combatTargetView, "combat_target_view_missing");
                RequireReference(combatTargetInput, "combat_target_input_missing");
                RequireReference(explorationPanel, "combat_exploration_panel_missing");
                RequireReference(static3DUnitPresentationProvider, "static_3d_presentation_provider_missing");
                RequireReference(combatUnitHealthBars, "combat_unit_health_bars_missing");
                RequireReference(battlefieldCamera, "battlefield_camera_missing");
                RequireReference(hudCanvas, "combat_hud_canvas_missing");
                if (!static3DUnitPresentationProvider.TryValidate(out string presentationReason))
                    throw new InvalidOperationException(presentationReason);
                GameRuntime runtime = GameBootstrap.RequireRuntime();
                if (runtime.Player == null) throw new InvalidOperationException("adventure_player_missing");
                if (!contentCatalog.TryGetAdventureMap(
                        runtime.Navigation.AdventureId,
                        out AdventureMapData adventureMap))
                    throw new InvalidOperationException(
                        "adventure_map_unresolved:" + runtime.Navigation.AdventureId);
                UiText.Load(languageTable);
                adventureHud.ConfigureText(UiText.Resolve, reason => UiText.ReasonDisplay(reason, "操作未完成"));
                encounterCoordinator.ConfigureText(UiText.Resolve, reason => UiText.ReasonDisplay(reason, "操作未完成"), ResolveAttackProfileName);
                combatActionBar.ConfigureText(ResolveAttackProfileName, reason => UiText.ReasonDisplay(reason, "操作未完成"));
                combatHudPresenter.Configure(combatHudView, combatCommandInput, combatLogView);
                combatUnitHealthBars.Configure(battlefieldCamera, hudCanvas, static3DUnitPresentationProvider);
                combatHudPresenter.ConfigureHealthBars(combatUnitHealthBars);
                combatHudPresenter.ConfigureTimeline(combatTimelineView, explorationPanel);
                combatMovementView.Configure(encounterCoordinator, combatActionBar);
                combatCommandInput.Configure(encounterCoordinator, combatActionBar, combatMovementView);
                combatTargetView.Configure(battlefieldCamera, hudCanvas, static3DUnitPresentationProvider);
                combatHudPresenter.ConfigureTargets(combatTargetView, static3DUnitPresentationProvider, combatMovementView);
                combatTargetInput.Configure(battlefieldCamera, static3DUnitPresentationProvider, combatHudPresenter);
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
                Disable(combatMovementView);
                Disable(combatTargetInput);
                Debug.LogError("[AdventureInstaller] " + exception.Message);
            }
        }

        private string ResolveAttackProfileName(string profileId)
        {
            AttackProfileData profile = Array.Find(attackProfiles,
                item => item != null && string.Equals(item.attackProfileId, profileId, StringComparison.Ordinal));
            return profile == null ? profileId : UiText.Resolve(profile.displayNameKey);
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
