using System;
using UnityEngine;

namespace TianZhang.Content
{
    /// <summary>
    /// The single approved production charter site contract. It owns only the static identity of
    /// one charter site — passage, seal management, rule entry occupancy and the two high-tier
    /// demo samples — and never saves player-completed actions. Cross-contract fields (nodes,
    /// coverage, positive/negative commits, reality supplies, world variables and environment
    /// profiles) resolve through the existing charter definition and reference catalog instead of
    /// being repeated in the site row.
    /// </summary>
    [CreateAssetMenu(fileName = "CharterSite_", menuName = "天章/内容/册界站点契约")]
    public sealed class CharterSiteData : ScriptableObject
    {
        // 站点身份
        public string siteId;
        public string displayNameKey;
        public string settlementId;

        // 通行：《开阖九章》的目标识别、操作者与目标门禁；协议、结构与供能必须显式可操作。
        public string passageCapabilityId;
        public string passageOperatorId;
        public string passageTargetId;
        public string passageProtocolState;
        public string passageStructureState;
        public string passagePowerState;
        public string interactionTimeProfileId;
        public string recognitionTiming;
        public string operationTiming;
        public string cancellationPolicy;

        // 管理：太玄界印与设施职责；操作者不等于管理者。
        public string facilityId;
        public string sealRelicId;
        public string sealManagerId;
        public string sealBeneficiaryId;
        public string sealAuthorizationVersionId;

        // 册界：只声明条目与占用 ID；锚点、覆盖、提交和供给由既有定义与目录解析。
        public string ruleEntryId;
        public string ruleEntryOccupancyId;
        public string nodeOccupancyId;

        // 金丹样例：版本化 CrossTierChallengeGrant 与左右候选；charterCandidateId 唯一锁定册界侧。
        public string jindanConflictEventId;
        public string jindanChallengeEventId;
        public CharterSiteCrossTierChallengeGrantData jindanGrant;
        public CharterSiteRuleConflictCandidateData leftCandidate;
        public CharterSiteRuleConflictCandidateData rightCandidate;
        public string charterCandidateId;

        // 元婴样例：只携带受锚身份，结果固定为受锚且不提交状态；不得夹带金丹候选或 grant。
        public string yuanyingConflictEventId;
        public string yuanyingTargetVariableId;
        public string yuanyingTargetId;
        public string yuanyingScopeId;
        public string yuanyingRealityAnchorId;
    }

    /// <summary>
    /// Versioned cross-tier challenge grant owned by the site static contract. It is a serializable
    /// data type mirroring the shared <c>CrossTierChallengeGrant</c> boundary; the importer builds
    /// the shared runtime object from this data only, without a second authorization source.
    /// </summary>
    [Serializable]
    public sealed class CharterSiteCrossTierChallengeGrantData
    {
        public string grantId;
        public int definitionVersion;
        public string targetVariableId;
        public string challengerId;
        public string qualificationSource;
        public string allowedOperationId;
        public string targetId;
        public string scopeId;
        public string beneficiaryId;
        public string realityAnchorId;
        public string resourceLedgerRef;
        public string capacityLedgerRef;
        public int challengeRuleTier;
        public int effectiveAtTick;
        public int expiresAtTick;
        public bool isRevoked;
        public string revocationReason;
        public string displaySource;
    }

    /// <summary>
    /// One conflict candidate owned by the site static contract. It mirrors the shared
    /// <c>RuleConflictCandidate</c> boundary; every field is explicit, never a runtime default.
    /// </summary>
    [Serializable]
    public sealed class CharterSiteRuleConflictCandidateData
    {
        public string candidateId;
        public string targetVariableId;
        public string targetId;
        public bool hasVariableAuthority;
        public bool hasLegalTarget;
        public int positionRank;
        public int realityAnchorRank;
        public int alreadyPaidCost;
        public bool hasActiveContinuousCarrier;
        public int conflictReserve;
        public int pulseCost;
        public int settlementCooldown;
    }
}
