using System;
using TianZhang.Content;
using TianZhang.Features.Settlement;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.Persistence;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TianZhang.Bootstrap
{
    public sealed class SettlementSceneInstaller : MonoBehaviour
    {
        [SerializeField] private ContentCatalogData contentCatalog;
        [SerializeField] private SettlementController controller;
        [SerializeField] private SettlementView view;
        [SerializeField] private SettlementFeatureDispatcher dispatcher;
        [SerializeField] private string charterSiteId = "charter_site_old_water_station";

        private void Awake()
        {
            try
            {
                if (contentCatalog == null) throw new InvalidOperationException("settlement_content_catalog_missing");
                if (controller == null) throw new InvalidOperationException("settlement_controller_missing");
                if (view == null) throw new InvalidOperationException("settlement_view_missing");
                if (dispatcher == null) throw new InvalidOperationException("settlement_dispatcher_missing");
                GameRuntime runtime = GameBootstrap.RequireRuntime();
                controller.Configure(
                    contentCatalog,
                    view,
                    dispatcher,
                    charterSiteId,
                    runtime,
                    runtime.Bounties,
                    runtime.Charters,
                    runtime.Navigation.SettlementId,
                    runtime.Navigation.WorldNodeId,
                    SceneManager.LoadScene,
                    SaveAndReturnToMenu);
            }
            catch (InvalidOperationException exception)
            {
                if (controller != null) controller.enabled = false;
                Debug.LogError("[SettlementInstaller] " + exception.Message);
            }
        }

        private string SaveAndReturnToMenu()
        {
            GameSaveSlotWriteResult result = GameBootstrap.RequireInstance().SaveActiveSlot();
            if (!result.Succeeded) return "save_failed:" + result.FailureReason;
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            return null;
        }
    }
}
