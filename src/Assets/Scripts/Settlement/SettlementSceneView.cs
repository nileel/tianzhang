using System;
using TianZhang.Content;
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

        public void Configure(
            Text nameText,
            Text detailText,
            Text statusText,
            Button nextFeatureButton,
            Text nextFeatureButtonText,
            Button nextAdventureButton,
            Text nextAdventureButtonText,
            Button nextReturnToWorldButton)
        {
            settlementNameText = nameText;
            settlementDetailText = detailText;
            settlementStatusText = statusText;
            featureButton = nextFeatureButton;
            featureButtonText = nextFeatureButtonText;
            adventureButton = nextAdventureButton;
            adventureButtonText = nextAdventureButtonText;
            returnToWorldButton = nextReturnToWorldButton;
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
