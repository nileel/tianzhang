using System;
using System.Collections.Generic;
using TianZhang.Character;
using TianZhang.Content;
using TianZhang.Cultivation;
using TianZhang.Entity;
using TianZhang.Features.CharacterCreation;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.Persistence;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TianZhang.Bootstrap
{
    public sealed class StartMenuSceneInstaller : MonoBehaviour, IPlayerEntryHost
    {
        [SerializeField] private ContentCatalogData contentCatalog;
        [SerializeField] private StartMenuController startMenuController;
        [SerializeField] private StartMenuView startMenuView;
        [SerializeField] private CharacterCreationController characterCreationController;
        [SerializeField] private CharacterCreationView characterCreationView;

        private GameBootstrap bootstrap;

        private void Awake()
        {
            try
            {
                RequireReference(contentCatalog, "start_menu_content_catalog_missing");
                RequireReference(startMenuController, "start_menu_controller_missing");
                RequireReference(startMenuView, "start_menu_view_missing");
                RequireReference(characterCreationController, "character_creation_controller_missing");
                RequireReference(characterCreationView, "character_creation_view_missing");
                bootstrap = GameBootstrap.RequireInstance();
                GameRuntime runtime = GameBootstrap.RequireRuntime();
                runtime.Clear();
                characterCreationController.Configure(
                    characterCreationView,
                    startMenuController.CompleteNewPlayer);
                startMenuController.Configure(this, startMenuView, characterCreationController);
            }
            catch (InvalidOperationException exception)
            {
                Disable(startMenuController);
                Disable(characterCreationController);
                Debug.LogError("[StartMenuInstaller] " + exception.Message);
            }
        }

        public IReadOnlyList<PlayerSlotSummary> ListSlots()
        {
            GameSaveSlotListResult result = bootstrap.SlotStore.ListSlots();
            if (!result.Succeeded)
            {
                return new[]
                {
                    new PlayerSlotSummary("slots_unavailable", null, false, result.FailureReason.ToString()),
                };
            }

            var summaries = new List<PlayerSlotSummary>(result.Slots.Count);
            foreach (GameSaveSlotSummary slot in result.Slots)
            {
                summaries.Add(new PlayerSlotSummary(
                    slot.SlotId,
                    slot.CharacterDisplayName,
                    slot.IsReadable,
                    slot.FailureReason == GameSaveSlotFailureReason.None
                        ? null
                        : slot.FailureReason.ToString()));
            }
            return summaries;
        }

        public PlayerEntryResult CreateNewPlayer(
            string slotId,
            CharacterData profile,
            string startNodeId)
        {
            if (profile == null) return PlayerEntryResult.Failed("character_profile_missing");
            GameRuntime runtime = GameBootstrap.RequireRuntime();
            try
            {
                runtime.BeginNewGame(
                    CharacterRuntimeProfile.FromDefinition("player", profile),
                    CultivationState.FromDefinition(profile.foundationPurpleMansionState),
                    startNodeId);
                GameSaveSlotWriteResult write = bootstrap.SlotStore.Write(slotId, runtime.CaptureSave());
                if (!write.Succeeded)
                {
                    runtime.Clear();
                    return PlayerEntryResult.Failed(write.FailureReason.ToString());
                }
                bootstrap.ActivateSlot(slotId);
                SceneManager.LoadScene(runtime.EnterWorld(startNodeId));
                return PlayerEntryResult.Success();
            }
            catch (Exception exception)
            {
                runtime.Clear();
                Debug.LogException(exception);
                return PlayerEntryResult.Failed("new_player_start_failed");
            }
        }

        public PlayerEntryResult LoadPlayer(string slotId)
        {
            GameSaveSlotReadResult read = bootstrap.SlotStore.Read(slotId);
            if (!read.Succeeded) return PlayerEntryResult.Failed(read.FailureReason.ToString());

            GameRuntime runtime = GameBootstrap.RequireRuntime();
            try
            {
                runtime.RestoreSave(read.Envelope, contentCatalog);
                if (!TryValidateNavigation(runtime.Navigation))
                {
                    runtime.Clear();
                    return PlayerEntryResult.Failed("save_navigation_target_unresolved");
                }
                bootstrap.ActivateSlot(slotId);
                SceneManager.LoadScene(ResolveScene(runtime.Navigation));
                return PlayerEntryResult.Success();
            }
            catch (Exception exception)
            {
                runtime.Clear();
                Debug.LogException(exception);
                return PlayerEntryResult.Failed("save_restore_failed");
            }
        }

        private static string ResolveScene(NavigationStateSnapshot navigation)
        {
            if (!string.IsNullOrWhiteSpace(navigation.AdventureId)) return GameplaySceneNames.Adventure;
            if (!string.IsNullOrWhiteSpace(navigation.SettlementId)) return GameplaySceneNames.Settlement;
            return GameplaySceneNames.World;
        }

        private bool TryValidateNavigation(NavigationStateSnapshot navigation)
        {
            if (!string.IsNullOrWhiteSpace(navigation.AdventureId))
                return contentCatalog.TryGetAdventureMap(navigation.AdventureId, out _);
            if (!string.IsNullOrWhiteSpace(navigation.SettlementId))
                return contentCatalog.TryGetSettlement(navigation.SettlementId, out _);
            return IsKnownWorldNode(navigation.WorldNodeId);
        }

        private static bool IsKnownWorldNode(string nodeId)
        {
            return nodeId == "jiangzuo_hub" || nodeId == "guanzhong_hub" ||
                   nodeId == "longxi_hub" || nodeId == "zhongzhou_hub";
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
