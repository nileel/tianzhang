using System;
using System.Linq;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Entity;
using TianZhang.Features.Adventure;
using TianZhang.Features.CombatPresentation;
using TianZhang.Features.Settlement;
using TianZhang.Infrastructure.UnityContent;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace TianZhang.Bootstrap
{
    public sealed class AdventureSceneInstaller : MonoBehaviour
    {
        [Header("Content")]
        [SerializeField] private TextAsset languageTable;
        [SerializeField] private ContentCatalogData contentCatalog;
        [SerializeField] private EnvironmentProfileAsset environmentProfile;
        [SerializeField] private AttackProfileData[] attackProfiles = Array.Empty<AttackProfileData>();
        [SerializeField] private CombatOrdinaryActionDefinition[] ordinaryActions = Array.Empty<CombatOrdinaryActionDefinition>();
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
        [SerializeField] private CombatBountyTrackerView combatBountyTrackerView;
        [SerializeField] private CombatTimelineView combatTimelineView;
        [SerializeField] private CombatActionPreviewView combatActionPreviewView;
        [SerializeField] private CombatTargetView combatTargetView;
        [SerializeField] private CombatTargetInput combatTargetInput;
        [SerializeField] private GameObject explorationPanel;
        [SerializeField] private Static3DCombatUnitPresentationProvider static3DUnitPresentationProvider;
        [SerializeField] private CombatUnitHealthBarView combatUnitHealthBars;
        [SerializeField] private Camera battlefieldCamera;
        [SerializeField] private Canvas hudCanvas;
        [Header("Terrain rehearsal (D3; character loadout remains the current player's)")]
        [SerializeField] private Button terrainRehearsalButton;
        [SerializeField] private GameObject ordinaryBattlefield;
        [SerializeField] private CombatBattlefieldSurface terrainRehearsalSurface;
        [SerializeField] private EnvironmentProfileAsset terrainRehearsalEnvironment;
        [SerializeField] private AdventureNodeData terrainRehearsalStart = new AdventureNodeData();
        [SerializeField] private AdventureNodeData terrainRehearsalEncounter = new AdventureNodeData();

        [Header("Jindan rehearsal")]
        [SerializeField] private Button jindanRehearsalButton;
        [SerializeField] private JindanDemoCatalogData jindanDemoCatalog;
        [SerializeField] private JindanDemoView jindanDemoView;
        [SerializeField] private JindanScaleView jindanScaleView;
        [SerializeField] private JindanSealView jindanSealView;
        [SerializeField] private JindanFireDomainView jindanFireDomainView;
        [SerializeField] private CombatEarthView earthView;
        [SerializeField] private CombatSurfaceView surfaceView;
        private JindanRehearsalPresenter jindan;

        public void OpenJindanPreparation()
        {
            if (controller.CanSelectExplorationNode) jindan.OpenPreparation();
        }

        private void BeginJindanRehearsal(int segmentIndex, string earthEffect)
        {
            if (!controller.CanSelectExplorationNode) return;
            ordinaryBattlefield.SetActive(false);
            terrainRehearsalSurface.gameObject.SetActive(true);
            combatMovementView.UseSurface(terrainRehearsalSurface);
            static3DUnitPresentationProvider.ConfigureSurface(terrainRehearsalSurface);
            combatTargetInput.ConfigureSurface(terrainRehearsalSurface);
            jindanScaleView.ConfigureSurface(terrainRehearsalSurface);
            jindanSealView.ConfigureSurface(terrainRehearsalSurface);
            earthView.ConfigureSurface(terrainRehearsalSurface);
            surfaceView.ConfigureSurface(terrainRehearsalSurface);
            battlefieldCamera.GetComponent<AdventureCameraController>().Configure(battlefieldCamera,
                terrainRehearsalSurface.transform, .340433121f);
            var placement = jindanDemoCatalog.segments[segmentIndex].actors.First(value => value.team == (jindan.ControlFire ? 1 : 0));
            Vector3 focus = terrainRehearsalSurface.Center(new TianZhang.Gameplay.Contracts.CombatUnitPresentationHex(placement.q, placement.r));
            battlefieldCamera.transform.position = focus - battlefieldCamera.transform.forward * 20;
            if (!controller.BeginJindanRehearsal(jindanDemoCatalog, segmentIndex, earthEffect,
                terrainRehearsalEnvironment, UiText.Resolve, out string reason, jindan.ControlFire))
                throw new InvalidOperationException("jindan_rehearsal_entry_failed:" + reason);
        }

        private void HandleJindanAction(string key)
        {
            combatCommandInput.CancelCandidate();
            combatMovementView.Cancel();
            jindan.HandleAction(key);
            combatTargetInput.enabled = !jindan.IsSelecting;
        }

        private void Update()
        {
            if (jindan == null || !jindan.IsSelecting || !encounterCoordinator.AcceptsPlayerCommand) return;
            var mouse = Mouse.current;
            if (combatMovementView.IsSelecting || combatCommandInput.Preview != null ||
                mouse?.rightButton.wasPressedThisFrame == true || Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            {
                jindan.HandleAction("cancel_targeting");
                combatTargetInput.enabled = true;
                return;
            }
            if (mouse != null && mouse.leftButton.wasPressedThisFrame &&
                combatMovementView.TryPickCell(mouse.position.ReadValue(), out var cell))
                jindan.PickCell(cell.Q, cell.R);
        }

        private void LateUpdate()
        {
            if (jindan == null) return;
            jindan.Refresh();
            combatTargetInput.enabled = !jindan.IsSelecting;
            if (jindan.PreparationOpen) jindanDemoView.PresentPreparation(jindan.PreparationDetails);
            else if (encounterCoordinator.CurrentSession?.Jindan != null)
                jindanDemoView.PresentCombat(jindan.CombatDetails, jindan.QuoteDetails,
                    encounterCoordinator.AcceptsPlayerCommand, jindan.CanConfirm);
            else jindanDemoView.HideAll();
            var scale = encounterCoordinator.CurrentSession?.Jindan?.Seats.FirstOrDefault(value => value.ActiveScale != null)?.ActiveScale;
            jindanScaleView.Present(scale?.Path.Select(value => new TianZhang.Gameplay.Contracts.CombatUnitPresentationHex(value.Q, value.R)).ToArray()
                ?? Array.Empty<TianZhang.Gameplay.Contracts.CombatUnitPresentationHex>(), scale?.MetricDistanceUnits ?? 0,
                scale?.Support.Durability ?? 0, scale?.Support.Definition.MaxDurability ?? 0);
            earthView.Present(encounterCoordinator.CurrentSession?.Jindan?.Seats.Where(s=>s.ActiveEarth!=null)
                .SelectMany(s=>s.ActiveEarth.Objects).Select(o=>new TianZhang.Gameplay.Contracts.CombatEarthObjectSnapshot(o.Id,
                    new TianZhang.Gameplay.Contracts.CombatUnitPresentationHex(o.Cell.Q,o.Cell.R),
                    new TianZhang.Gameplay.Contracts.CombatUnitPresentationHex(o.EdgeOther.Value.Q,o.EdgeOther.Value.R),
                    o.Definition.IsTemporaryRamp,o.Durability)).ToArray() ?? Array.Empty<TianZhang.Gameplay.Contracts.CombatEarthObjectSnapshot>());
            surfaceView.Present(jindan.SurfacePresentation);
            var fire = encounterCoordinator.CurrentSession?.Jindan?.Seats.FirstOrDefault(value => value.FireDomain != null)?.FireDomain;
            jindanFireDomainView.Present(fire?.Main != null, fire?.Support.IsDestroyed == true);
            var sealSeat = encounterCoordinator.CurrentSession?.Jindan?.Seats.FirstOrDefault(value => value.EffectId == "SPACE_SEAL");
            var seal = sealSeat?.ActiveSeal;
            jindanSealView.Present(seal == null ? null : new[] {
                new TianZhang.Gameplay.Contracts.CombatUnitPresentationHex(seal.Definition.From.Q, seal.Definition.From.R),
                new TianZhang.Gameplay.Contracts.CombatUnitPresentationHex(seal.Definition.To.Q, seal.Definition.To.R) },
                seal?.OuterDurability ?? 0, seal?.InnerDurability ?? 0, sealSeat?.LastEndReason == "jindan_support_destroyed");
        }

        public void BeginTerrainRehearsal()
        {
            if (!controller.CanSelectExplorationNode) return;
            ordinaryBattlefield.SetActive(false);
            terrainRehearsalSurface.gameObject.SetActive(true);
            combatMovementView.UseSurface(terrainRehearsalSurface);
            static3DUnitPresentationProvider.ConfigureSurface(terrainRehearsalSurface);
            combatTargetInput.ConfigureSurface(terrainRehearsalSurface);
            battlefieldCamera.GetComponent<AdventureCameraController>().Configure(battlefieldCamera,
                terrainRehearsalSurface.transform,.340433121f);
            Vector3 focus = terrainRehearsalSurface.Center(new TianZhang.Gameplay.Contracts.CombatUnitPresentationHex(
                terrainRehearsalStart.q,terrainRehearsalStart.r));
            battlefieldCamera.transform.position = focus-battlefieldCamera.transform.forward*20;
            if (!controller.BeginTerrainRehearsal(terrainRehearsalEnvironment,terrainRehearsalStart,
                    terrainRehearsalEncounter,out string reason))
                throw new InvalidOperationException("terrain_rehearsal_entry_failed:"+reason);
        }

        private void Awake()
        {
            try
            {
                RequireReference(languageTable, "adventure_language_table_missing");
                RequireReference(contentCatalog, "adventure_content_catalog_missing");
                if (!contentCatalog.TryGetCombatRuleSet(out var combatRules, out string ruleReason))
                    throw new InvalidOperationException(ruleReason);
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
                RequireReference(combatBountyTrackerView, "combat_bounty_tracker_missing");
                RequireReference(combatTimelineView, "combat_timeline_view_missing");
                RequireReference(combatActionPreviewView, "combat_action_preview_missing");
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
                runtime.ConfigureGrowth(contentCatalog.CombatRuleSet);
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
                combatBountyTrackerView.Configure(() => controller.ReadBountyTracker(UiText.Resolve));
                combatHudPresenter.ConfigureBountyTracker(combatBountyTrackerView);
                combatUnitHealthBars.Configure(battlefieldCamera, hudCanvas, static3DUnitPresentationProvider);
                combatHudPresenter.ConfigureHealthBars(combatUnitHealthBars);
                combatHudPresenter.ConfigureTimeline(combatTimelineView, explorationPanel);
                combatMovementView.Configure(encounterCoordinator, combatActionBar, combatActionPreviewView);
                combatCommandInput.Configure(encounterCoordinator, combatActionBar, combatMovementView);
                combatCommandInput.ConfigurePreview(encounterCoordinator, combatActionPreviewView, combatTimelineView);
                combatTargetView.Configure(hudCanvas);
                combatHudPresenter.ConfigureTargets(combatTargetView, static3DUnitPresentationProvider, combatMovementView);
                combatTargetInput.Configure(battlefieldCamera, static3DUnitPresentationProvider, combatHudPresenter);
                encounterCoordinator.ConfigureEquipment(runtime.Player, contentCatalog, runtime.OwnsEquipmentInstance, () => runtime.InventoryVersion);
                encounterCoordinator.ConfigureItems(runtime.GetItemQuantity,
                    (string itemId, int quantity, long version, out Action apply, out string reason) =>
                        runtime.TryPrepareCombatConsumption(contentCatalog, itemId, quantity, version, out apply, out reason));
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
                    new CombatEntryAdapter(ordinaryActions, combatRules),
                    runtime,
                    runtime.Bounties,
                    runtime.InventoryGrants,
                    SceneManager.LoadScene);
                if (adventureMap.adventureId == FormalEncounterRules.GuanzhongWildAdventureId)
                    controller.ConfigureWithdrawal(combatCommandInput.ClearContext, combatHudPresenter.Hide);
                if (terrainRehearsalButton != null) terrainRehearsalButton.onClick.AddListener(BeginTerrainRehearsal);
                if (jindanRehearsalButton != null)
                {
                    RequireReference(jindanDemoCatalog, "jindan_demo_catalog_missing");
                    RequireReference(jindanDemoView, "jindan_demo_view_missing");
                    RequireReference(jindanScaleView, "jindan_scale_view_missing");
                    RequireReference(jindanSealView, "jindan_seal_view_missing");
                    RequireReference(jindanFireDomainView, "jindan_fire_domain_view_missing");
                    RequireReference(earthView, "jindan_earth_view_missing");
                    RequireReference(surfaceView, "jindan_surface_view_missing");
                    jindan = new JindanRehearsalPresenter(jindanDemoCatalog, encounterCoordinator, BeginJindanRehearsal, UiText.Resolve);
                    jindanRehearsalButton.onClick.AddListener(OpenJindanPreparation);
                    jindanDemoView.ActionRequested += HandleJindanAction;
                }
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
            return profile == null ? UiText.Resolve(profileId) : UiText.Resolve(profile.displayNameKey);
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
