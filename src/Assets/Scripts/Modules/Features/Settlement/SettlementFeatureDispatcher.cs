using System;
using System.Collections.Generic;
using TianZhang.Content;
using UnityEngine;

namespace TianZhang.Features.Settlement
{
    public sealed class SettlementFeatureDispatcher : MonoBehaviour
    {
        public const string BountyBoardFeatureId = "bounty_board";
        public const string DispatcherMissingReason = "settlement_feature_dispatcher_missing";
        public const string FeatureMissingReason = "settlement_feature_missing";
        public const string FeatureDisabledReason = "settlement_feature_disabled";
        public const string FeatureUnknownReason = "settlement_feature_unknown";
        public const string FeatureHandlerUnregisteredReason = "settlement_feature_handler_unregistered";
        public const string FeatureHandlerFailedReason = "settlement_feature_handler_failed";
        public const string BountyBoardEntryOpenedReason = "bounty_board_entry_opened";

        private readonly Dictionary<string, Func<SettlementFeatureData, string>> handlers =
            new Dictionary<string, Func<SettlementFeatureData, string>>(StringComparer.Ordinal);

        public string LastDispatchedFeatureId { get; private set; }

        public void RegisterInitialFeatureHandlers()
        {
            handlers[BountyBoardFeatureId] = EnterBountyBoard;
        }

        public bool TryDispatch(SettlementFeatureData feature, out string reason)
        {
            if (feature == null || string.IsNullOrWhiteSpace(feature.featureId))
            {
                reason = FeatureMissingReason;
                return false;
            }

            if (!string.Equals(feature.availability, "enabled", StringComparison.Ordinal))
            {
                reason = FeatureDisabledReason + ":" + feature.disabledReasonKey;
                return false;
            }

            if (!string.Equals(feature.featureId, BountyBoardFeatureId, StringComparison.Ordinal))
            {
                reason = FeatureUnknownReason + ":" + feature.featureId;
                return false;
            }

            if (!handlers.TryGetValue(feature.featureId, out Func<SettlementFeatureData, string> handler) || handler == null)
            {
                reason = FeatureHandlerUnregisteredReason + ":" + feature.featureId;
                return false;
            }

            try
            {
                LastDispatchedFeatureId = feature.featureId;
                reason = handler(feature);
                if (string.IsNullOrWhiteSpace(reason))
                {
                    reason = FeatureHandlerFailedReason + ":" + feature.featureId;
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[SettlementFeatureDispatcher] feature=" + feature.featureId + " error=" + exception.Message);
                reason = FeatureHandlerFailedReason + ":" + feature.featureId;
                return false;
            }
        }

        private static string EnterBountyBoard(SettlementFeatureData feature)
        {
            return BountyBoardEntryOpenedReason;
        }
    }
}
