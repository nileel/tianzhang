using System;
using TianZhang.Content;
using TianZhang.Game;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Settlement
{
    public sealed class SettlementSceneView : MonoBehaviour
    {
        [SerializeField] private Text settlementNameText;
        [SerializeField] private Text settlementDetailText;
        [SerializeField] private Text settlementStatusText;
        [SerializeField] private Button featureButton;
        [SerializeField] private Text featureButtonText;
        [SerializeField] private Button adventureButton;
        [SerializeField] private Text adventureButtonText;
        [SerializeField] private Button returnToWorldButton;
        [SerializeField] private BountyBoardView bountyBoardView;
        [SerializeField] private Button charterSiteEntryButton;
        [SerializeField] private Text charterSiteEntryText;
        [SerializeField] private CharterSiteView charterSiteView;

        public void Configure(
            Text nameText,
            Text detailText,
            Text statusText,
            Button nextFeatureButton,
            Text nextFeatureButtonText,
            Button nextAdventureButton,
            Text nextAdventureButtonText,
            Button nextReturnToWorldButton,
            BountyBoardView nextBountyBoard,
            Button nextCharterSiteEntryButton,
            Text nextCharterSiteEntryText,
            CharterSiteView nextCharterSiteView)
        {
            settlementNameText = nameText;
            settlementDetailText = detailText;
            settlementStatusText = statusText;
            featureButton = nextFeatureButton;
            featureButtonText = nextFeatureButtonText;
            adventureButton = nextAdventureButton;
            adventureButtonText = nextAdventureButtonText;
            returnToWorldButton = nextReturnToWorldButton;
            bountyBoardView = nextBountyBoard;
            charterSiteEntryButton = nextCharterSiteEntryButton;
            charterSiteEntryText = nextCharterSiteEntryText;
            charterSiteView = nextCharterSiteView;
        }

        public bool HasCharterSitePanel => charterSiteView != null;

        public void BindCharterSiteEntry(Action onClick)
        {
            if (charterSiteEntryButton == null)
                return;

            charterSiteEntryButton.onClick.RemoveAllListeners();
            if (onClick != null)
                charterSiteEntryButton.onClick.AddListener(() => onClick());
        }

        public void SetCharterSiteEntryText(string value)
        {
            if (charterSiteEntryText != null)
                charterSiteEntryText.text = value;
        }

        /// <summary>
        /// 打开唯一站点面板：只由 Settlement 控制器在目录取得站点、静态目录和会话引用都合法时调用。
        /// </summary>
        public bool OpenCharterSite(
            CharterSiteData site,
            CharterRuleStaticCatalogData staticCatalog,
            ContentCatalogData catalog,
            GameSession session,
            out string reason)
        {
            if (charterSiteView == null)
            {
                reason = SettlementSceneController.CharterSitePanelMissingReason;
                return false;
            }

            return charterSiteView.Show(site, staticCatalog, catalog, session, out reason);
        }

        public void SetReturnToWorldAction(Action action)
        {
            if (returnToWorldButton == null)
                return;

            returnToWorldButton.onClick.RemoveAllListeners();
            if (action != null)
                returnToWorldButton.onClick.AddListener(() => action());
        }

        public void ShowSettlement(SettlementData settlement, string returnWorldNodeId)
        {
            if (settlementNameText != null)
                settlementNameText.text = settlement.displayNameKey;
            if (settlementDetailText != null)
                settlementDetailText.text = "据点: " + settlement.settlementId + "\n区域: " + settlement.regionId;
            SetStatus("settlement_loaded:" + settlement.settlementId + "; return_world:" + returnWorldNodeId);
        }

        public void ShowFailure(string reason, string returnWorldNodeId)
        {
            if (settlementNameText != null)
                settlementNameText.text = "据点不可用";
            if (settlementDetailText != null)
                settlementDetailText.text = "返回主世界节点: " + returnWorldNodeId;
            SetStatus(reason);
            SetButton(featureButton, featureButtonText, "功能不可用", false, null);
            SetButton(adventureButton, adventureButtonText, "副本不可用", false, null);
        }

        public void BindFeature(SettlementFeatureData feature, Action<SettlementFeatureData> onClick)
        {
            if (feature == null)
            {
                ShowFeatureResult("settlement_feature_missing");
                SetButton(featureButton, featureButtonText, "功能不可用", false, null);
                return;
            }

            bool enabled = string.Equals(feature.availability, "enabled", StringComparison.Ordinal);
            SetButton(featureButton, featureButtonText, feature.displayNameKey, enabled, () => onClick?.Invoke(feature));
            if (!enabled)
                ShowFeatureResult(SettlementFeatureDispatcher.FeatureDisabledReason + ":" + feature.disabledReasonKey);
        }

        public void BindAdventure(string adventureId, Action<string> onClick)
        {
            bool available = !string.IsNullOrWhiteSpace(adventureId);
            SetButton(
                adventureButton,
                adventureButtonText,
                available ? "进入副本: " + adventureId : "副本不可用",
                available,
                () => onClick?.Invoke(adventureId));
        }

        public void ShowFeatureResult(string reason)
        {
            SetStatus(reason);
        }

        public void OpenBountyBoard(ContentCatalogData catalog, string settlementId, GameSession session)
        {
            if (bountyBoardView != null)
                bountyBoardView.Show(catalog, settlementId, session);
        }

        public void ShowAdventureResult(string reason)
        {
            SetStatus(reason);
        }

        private void SetStatus(string value)
        {
            if (settlementStatusText != null)
                settlementStatusText.text = value;
        }

        private static void SetButton(Button button, Text label, string text, bool interactable, Action action)
        {
            if (label != null)
                label.text = text;
            if (button == null)
                return;

            button.onClick.RemoveAllListeners();
            button.interactable = interactable;
            if (action != null)
                button.onClick.AddListener(() => action());
        }
    }
}
