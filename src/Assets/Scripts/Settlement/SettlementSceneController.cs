using System;
using TianZhang.Content;
using TianZhang.Game;
using UnityEngine;

namespace TianZhang.Settlement
{
    public sealed class SettlementSceneController : MonoBehaviour
    {
        public const string GuanzhongSettlementId = "guanzhong_city";
        public const string ProductionContentScope = "content_scope_production";
        public const string CatalogMissingReason = "settlement_catalog_missing";
        public const string SettlementMissingReason = "settlement_not_found";
        public const string SettlementOutOfScopeReason = "settlement_not_in_first_batch_production_scope";
        public const string AdventureUnavailableReason = "settlement_adventure_not_available";

        [SerializeField] private ContentCatalogData contentCatalog;
        [SerializeField] private SettlementSceneView sceneView;
        [SerializeField] private SettlementFeatureDispatcher featureDispatcher;

        public SettlementData CurrentSettlement { get; private set; }
        public string CurrentSettlementId => CurrentSettlement == null ? null : CurrentSettlement.settlementId;
        public string LastFailureReason { get; private set; }

        public void Configure(
            ContentCatalogData catalog,
            SettlementSceneView view,
            SettlementFeatureDispatcher dispatcher)
        {
            contentCatalog = catalog;
            sceneView = view;
            featureDispatcher = dispatcher;
        }

        private void Start()
        {
            if (sceneView != null)
                sceneView.SetReturnToWorldAction(ReturnToWorld);

            if (featureDispatcher == null)
            {
                ShowFailure(SettlementFeatureDispatcher.DispatcherMissingReason);
                return;
            }

            featureDispatcher.RegisterInitialFeatureHandlers();
            SelectSettlement(GameSession.Instance == null ? null : GameSession.Instance.CurrentSettlementId);
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
            if (GameSession.Instance != null)
                GameSession.Instance.SetSettlementId(settlement.settlementId);

            RefreshSettlementUi();
            return true;
        }

        public void ReturnToWorld()
        {
            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.EnterWorld(ResolveReturnWorldNodeId());
        }

        public bool EnterAdventure(string adventureId)
        {
            if (CurrentSettlement == null ||
                string.IsNullOrWhiteSpace(adventureId) ||
                !ContainsAdventureEntrance(CurrentSettlement, adventureId) ||
                SceneFlowManager.Instance == null)
            {
                sceneView?.ShowAdventureResult(AdventureUnavailableReason);
                return false;
            }

            SceneFlowManager.Instance.EnterAdventure(adventureId, BuildAdventureReturnTarget());
            return true;
        }

        public SceneReturnTarget BuildAdventureReturnTarget()
        {
            return SceneReturnTarget.Settlement(CurrentSettlementId);
        }

        public string ResolveReturnWorldNodeId()
        {
            return GameSession.Instance == null ? "jiangzuo_hub" : GameSession.Instance.CurrentWorldNodeId;
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

            if (!string.Equals(settlement.settlementId, GuanzhongSettlementId, StringComparison.Ordinal) ||
                !string.Equals(settlement.contentScope, ProductionContentScope, StringComparison.Ordinal))
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

            sceneView.BindAdventure(adventureEntranceIds[0], adventureId => EnterAdventure(adventureId));
        }

        private void DispatchFeature(SettlementFeatureData feature)
        {
            if (featureDispatcher == null)
            {
                sceneView?.ShowFeatureResult(SettlementFeatureDispatcher.DispatcherMissingReason);
                return;
            }

            featureDispatcher.TryDispatch(feature, out string reason);
            sceneView?.ShowFeatureResult(reason);
        }

        private void ShowFailure(string reason)
        {
            CurrentSettlement = null;
            LastFailureReason = reason;
            sceneView?.ShowFailure(reason, ResolveReturnWorldNodeId());
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
