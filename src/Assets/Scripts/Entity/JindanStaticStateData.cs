using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TianZhang.Entity
{
    public enum JindanStaticPositionType
    {
        Source,
        Transformation,
        Domain,
    }

    [Serializable]
    public sealed class JindanMansionInput
    {
        public PurpleMansionKind mansionKind;
        public PurpleMansionBuildState state;
        public string mansionInstanceId;
        public string mansionBodyEffectBindingId;
        public string guardianAbilityInstanceId;
        public string sourceSpellId;
        public string upgradePlanId;
        public string sourceSpellDisposition;
    }

    [Serializable]
    public sealed class JindanCoreBindingData
    {
        public string jindanCoreBindingId;
        public string jindanInstanceId;
        public string boundDanshuCoreId;
        public string formationTransactionId;
        public int formationVersion;
    }

    [Serializable]
    public sealed class JindanDanxiangData
    {
        public string danxiangInstanceId;
        public string jindanInstanceId;
        public string danxiangNameKey;
        public string danxingDefinitionId;
        public string danxiangPresentationProfileId;
    }

    [Serializable]
    public sealed class JindanStablePositionBindingData
    {
        public string positionId;
        public int expectedPositionVersion;
        public string roadId;
        public JindanStaticPositionType positionType;
        public string proofProfileId;
        public string equippedBaseEffectId;
        public string compatibilityProfileId;
        public string primaryCarrierAbilityInstanceId;
        public string[] auxiliaryCarrierAbilityInstanceIds;
    }

    [Serializable]
    public sealed class JindanAbilityLedgerBindingData
    {
        public string abilityInstanceId;
        public string resourceDebitLedgerRef;
        public string cooldownLedgerRef;
        public string chargeLedgerRef;
        public string costLedgerRef;
        public string conflictReserveLedgerRef;
        public string conflictCostProfileId;
    }

    [CreateAssetMenu(fileName = "JindanStaticState_", menuName = "天章/金丹静态状态数据")]
    public sealed class JindanStaticStateData : ScriptableObject
    {
        public string schemaId;
        public int schemaVersion;
        public string characterId;
        public string foundationPurpleMansionStateRef;
        public JindanMansionInput[] mansionInputs;
        public JindanCoreBindingData jindanCoreBinding;
        public JindanDanxiangData danxiang;
        public JindanStablePositionBindingData[] stablePositionBindings;
        public JindanAbilityLedgerBindingData[] abilityLedgerBindings;
    }

    /// <summary>
    /// Import-time authority only. Production callers must provide the real static authorities;
    /// direct EditMode fixtures provide a deliberately small in-memory catalog instead.
    /// </summary>
    public sealed class JindanStaticReferenceCatalog
    {
        public FoundationPurpleMansionStateData[] foundationPurpleMansionStates;
        public JindanRoadReference[] roads;
        public JindanPositionReference[] positions;
        public JindanCompatibilityReference[] compatibilityProfiles;
        public string[] danxingDefinitionIds;
        public string[] danxiangPresentationProfileIds;
        public string[] ledgerReferences;
        public string[] conflictCostProfileIds;

        public bool ContainsDanxingDefinition(string id) =>
            !string.IsNullOrWhiteSpace(id) && danxingDefinitionIds != null &&
            danxingDefinitionIds.Contains(id, StringComparer.Ordinal);

        public bool ContainsDanxiangPresentationProfile(string id) =>
            danxiangPresentationProfileIds != null &&
            danxiangPresentationProfileIds.Contains(id, StringComparer.Ordinal);

        public bool ContainsLedgerReference(string id) =>
            string.IsNullOrWhiteSpace(id) || (ledgerReferences != null && ledgerReferences.Contains(id, StringComparer.Ordinal));

        public bool ContainsConflictCostProfile(string id) =>
            string.IsNullOrWhiteSpace(id) || (conflictCostProfileIds != null && conflictCostProfileIds.Contains(id, StringComparer.Ordinal));
    }

    public sealed class JindanRoadReference
    {
        public string roadId;
        public string[] baseEffectCandidateIds;
    }

    public sealed class JindanPositionReference
    {
        public string positionId;
        public int version;
        public string roadId;
        public JindanStaticPositionType positionType;
        public string proofProfileId;
    }

    public sealed class JindanCompatibilityReference
    {
        public string compatibilityProfileId;
        public string roadId;
        public string positionId;
        public string equippedBaseEffectId;
        public string primaryCarrierAbilityInstanceId;
        public string[] auxiliaryCarrierAbilityInstanceIds;
    }
}
