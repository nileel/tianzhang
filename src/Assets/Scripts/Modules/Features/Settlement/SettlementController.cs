using System;
using TianZhang.Content;
using TianZhang.Gameplay.Contracts;
using TianZhang.World;
using UnityEngine;

namespace TianZhang.Features.Settlement
{
    public sealed class SettlementController : MonoBehaviour
    {
        public const string GuanzhongSettlementId = SettlementData.GuanzhongId;
        public const string ProductionContentScope = SettlementData.ProductionScope;
        public const string CatalogMissingReason = "settlement_catalog_missing";
        public const string SettlementMissingReason = "settlement_not_found";
        public const string SettlementOutOfScopeReason = "settlement_not_in_first_batch_production_scope";
        public const string AdventureUnavailableReason = "settlement_adventure_not_available";
        public const string CharterSitePanelMissingReason = "settlement_charter_site_panel_missing";
        public const string CharterSiteMissingReason = "settlement_charter_site_missing";
        public const string CharterSiteNotCurrentReason = "settlement_charter_site_not_current_settlement";
        public const string CharterSiteStaticCatalogReason = "settlement_charter_static_catalog_unavailable";
        public const string CharterSiteSessionMissingReason = "settlement_charter_session_missing";
        public const string CharterSiteEntryOpenedReason = "charter_site_entry_opened";

        [SerializeField] private ContentCatalogData contentCatalog;
        [SerializeField] private SettlementView sceneView;
        [SerializeField] private SettlementFeatureDispatcher featureDispatcher;
        [SerializeField] private string charterSiteId;
        private INavigationUseCase navigation;
        private BountyUseCase bounties;
        private CharterUseCase charters;
        private ISettlementEquipmentUseCase equipment;
        private Action<string> loadScene;
        private Func<string> saveAndReturnToMenu;
        private string initialSettlementId;
        private string returnWorldNodeId;

        public SettlementData CurrentSettlement { get; private set; }
        public string CurrentSettlementId => CurrentSettlement == null ? null : CurrentSettlement.settlementId;
        public string LastFailureReason { get; private set; }
        public string LastCharterSiteReason { get; private set; }

        public void Configure(
            ContentCatalogData catalog,
            SettlementView view,
            SettlementFeatureDispatcher dispatcher,
            string nextCharterSiteId,
            INavigationUseCase navigationUseCase,
            BountyUseCase bountyUseCase,
            CharterUseCase charterUseCase,
            string currentSettlementId,
            string currentWorldNodeId,
            Action<string> sceneLoader,
            Func<string> saveAndReturn)
        {
            contentCatalog = catalog;
            sceneView = view;
            featureDispatcher = dispatcher;
            charterSiteId = nextCharterSiteId;
            navigation = navigationUseCase ?? throw new ArgumentNullException(nameof(navigationUseCase));
            bounties = bountyUseCase ?? throw new ArgumentNullException(nameof(bountyUseCase));
            charters = charterUseCase ?? throw new ArgumentNullException(nameof(charterUseCase));
            initialSettlementId = currentSettlementId;
            returnWorldNodeId = currentWorldNodeId;
            loadScene = sceneLoader ?? throw new ArgumentNullException(nameof(sceneLoader));
            saveAndReturnToMenu = saveAndReturn ?? throw new ArgumentNullException(nameof(saveAndReturn));
        }

        private void Start()
        {
            if (sceneView != null)
            {
                sceneView.SetReturnToWorldAction(ReturnToWorld);
                sceneView.SetSaveAndReturnAction(SaveAndReturnToMenu);
                sceneView.BindCharterSiteEntry(RequestOpenCharterSite);
            }

            if (featureDispatcher == null)
            {
                ShowFailure(SettlementFeatureDispatcher.DispatcherMissingReason);
                return;
            }

            featureDispatcher.RegisterInitialFeatureHandlers();
            SelectSettlement(initialSettlementId);
        }

        public bool TryGetSettlement(string settlementId, out SettlementData settlement)
        {
            return TryGetFormalSettlement(settlementId, out settlement, out _);
        }

        public bool SelectSettlement(string settlementId)
        {
            if (!TryGetFormalSettlement(settlementId, out SettlementData settlement, out string reason))
            {
                ShowFailure(reason);
                return false;
            }

            CurrentSettlement = settlement;
            LastFailureReason = null;
            navigation.EnterSettlement(settlement.settlementId);

            RefreshSettlementUi();
            return true;
        }

        /// <summary>Out-of-combat weapon growth; the settlement scene only offers it when bound.</summary>
        public void ConfigureEquipment(ISettlementEquipmentUseCase useCase) => equipment = useCase;

        public string RequestEquipMainHand(string itemId)
        {
            if (equipment == null) return SettlementEquipmentReasons.Unavailable;
            string reason = equipment.EquipMainHand(contentCatalog, itemId);
            sceneView?.ShowEquipmentResult(reason);
            BindEquipment();
            return reason;
        }

        /// <summary>
        /// 打开旧水驿入口：只从 <see cref="ContentCatalogData"/> 按站点 ID 精确取得唯一站点，并校验
        /// 站点属于当前正式据点、唯一静态目录可校验且会话引用存在；任一不合法返回稳定原因且不打开面板。
        /// </summary>
        public void RequestOpenCharterSite()
        {
            if (CurrentSettlement == null)
            {
                ShowCharterSiteEntryFailure(SettlementMissingReason);
                return;
            }
            if (sceneView == null || !sceneView.HasCharterSitePanel)
            {
                ShowCharterSiteEntryFailure(CharterSitePanelMissingReason);
                return;
            }
            if (string.IsNullOrWhiteSpace(charterSiteId))
            {
                ShowCharterSiteEntryFailure(CharterSiteMissingReason);
                return;
            }
            if (contentCatalog == null ||
                !contentCatalog.TryGetCharterSite(charterSiteId, out CharterSiteData site))
            {
                ShowCharterSiteEntryFailure(CharterSiteMissingReason + ":" + charterSiteId);
                return;
            }
            if (!string.Equals(site.settlementId, CurrentSettlementId, StringComparison.Ordinal))
            {
                ShowCharterSiteEntryFailure(CharterSiteNotCurrentReason + ":" + site.settlementId);
                return;
            }
            if (!contentCatalog.TryGetCharterRuleStaticCatalog(
                    out CharterRuleStaticCatalogData staticCatalog,
                    out string catalogReason))
            {
                ShowCharterSiteEntryFailure(CharterSiteStaticCatalogReason + ":" + catalogReason);
                return;
            }
            if (!sceneView.OpenCharterSite(
                    site,
                    staticCatalog,
                    contentCatalog,
                    charters,
                    CurrentSettlementId,
                    out string openReason))
            {
                ShowCharterSiteEntryFailure(openReason);
                return;
            }

            LastCharterSiteReason = CharterSiteEntryOpenedReason + ":" + site.siteId;
            sceneView.SetCharterSiteEntryText(LastCharterSiteReason);
        }

        public void ReturnToWorld()
        {
            loadScene(navigation.EnterWorld(ResolveReturnWorldNodeId()));
        }

        public void SaveAndReturnToMenu()
        {
            string reason = saveAndReturnToMenu();
            if (!string.IsNullOrWhiteSpace(reason)) sceneView?.ShowSaveResult(reason);
        }

        public bool EnterAdventure(string adventureId, AdventureEntrance entrance = AdventureEntrance.Unspecified)
        {
            if (CurrentSettlement == null ||
                string.IsNullOrWhiteSpace(adventureId) ||
                !ContainsAdventureEntrance(CurrentSettlement, adventureId))
            {
                sceneView?.ShowAdventureResult(AdventureUnavailableReason);
                return false;
            }

            loadScene(navigation.EnterAdventure(adventureId, BuildAdventureReturnTarget(), entrance));
            return true;
        }

        public SceneReturnTarget BuildAdventureReturnTarget()
        {
            return SceneReturnTarget.Settlement(CurrentSettlementId);
        }

        public string ResolveReturnWorldNodeId()
        {
            return returnWorldNodeId;
        }

        private bool TryGetFormalSettlement(string settlementId, out SettlementData settlement, out string reason)
        {
            settlement = null;
            if (contentCatalog == null)
            {
                reason = CatalogMissingReason;
                return false;
            }

            if (string.IsNullOrWhiteSpace(settlementId) ||
                !contentCatalog.TryGetSettlement(settlementId, out settlement))
            {
                reason = SettlementMissingReason;
                return false;
            }

            if (!settlement.IsPlayable)
            {
                settlement = null;
                reason = SettlementOutOfScopeReason;
                return false;
            }

            reason = null;
            return true;
        }

        private void RefreshSettlementUi()
        {
            if (sceneView == null || CurrentSettlement == null)
                return;

            sceneView.ShowSettlement(CurrentSettlement, ResolveReturnWorldNodeId());
            BindFeature(CurrentSettlement.features);
            BindAdventure(CurrentSettlement.adventureEntranceIds);
            BindEquipment();
        }

        private void BindEquipment()
        {
            if (sceneView == null || contentCatalog == null || equipment == null) return;
            sceneView.BindEquipment(
                equipment.OwnedMainHandItemIds(contentCatalog),
                equipment.CurrentMainHandItemId,
                contentCatalog,
                itemId => RequestEquipMainHand(itemId));
        }

        private void BindFeature(SettlementFeatureData[] features)
        {
            if (features == null || features.Length != 1)
            {
                sceneView.ShowFeatureResult("settlement_feature_cardinality_invalid");
                return;
            }

            sceneView.BindFeature(features[0], DispatchFeature);
        }

        private void BindAdventure(string[] adventureEntranceIds)
        {
            if (adventureEntranceIds == null || adventureEntranceIds.Length != 1)
            {
                sceneView.ShowAdventureResult(AdventureUnavailableReason);
                return;
            }

            sceneView.BindAdventure(adventureEntranceIds[0], (adventureId, entrance) => EnterAdventure(adventureId, entrance));
        }

        private void DispatchFeature(SettlementFeatureData feature)
        {
            if (featureDispatcher == null)
            {
                sceneView?.ShowFeatureResult(SettlementFeatureDispatcher.DispatcherMissingReason);
                return;
            }

            if (!featureDispatcher.TryDispatch(feature, out string reason))
            {
                sceneView?.ShowFeatureResult(reason);
                return;
            }

            sceneView?.ShowFeatureResult(reason);
            if (string.Equals(
                    reason,
                    SettlementFeatureDispatcher.BountyBoardEntryOpenedReason,
                    StringComparison.Ordinal))
            {
                sceneView?.OpenBountyBoard(
                    contentCatalog,
                    CurrentSettlementId,
                    bounties,
                    OnBountyRewardClaimed);
            }
        }

        private void OnBountyRewardClaimed()
        {
            BindEquipment();
            sceneView.ShowFeatureResult("bounty_status_claimed");
        }

        private void ShowFailure(string reason)
        {
            CurrentSettlement = null;
            LastFailureReason = reason;
            sceneView?.ShowFailure(reason, ResolveReturnWorldNodeId());
        }

        private void ShowCharterSiteEntryFailure(string reason)
        {
            LastCharterSiteReason = reason;
            sceneView?.SetCharterSiteEntryText("charter_site_entry_unavailable:" + reason);
        }

        private static bool ContainsAdventureEntrance(SettlementData settlement, string adventureId)
        {
            if (settlement.adventureEntranceIds == null)
                return false;

            foreach (string entranceId in settlement.adventureEntranceIds)
            {
                if (string.Equals(entranceId, adventureId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
