using System;
using System.Collections.Generic;
using UnityEngine;

namespace TianZhang.Content
{
    [CreateAssetMenu(fileName = "ContentCatalog", menuName = "天章/内容/只读目录")]
    public sealed class ContentCatalogData : ScriptableObject
    {
        [SerializeField] private SettlementData[] settlements = Array.Empty<SettlementData>();
        [SerializeField] private EnemyData[] enemies = Array.Empty<EnemyData>();
        [SerializeField] private ItemData[] items = Array.Empty<ItemData>();
        [SerializeField] private BountyData[] bounties = Array.Empty<BountyData>();
        [SerializeField] private CharterRuleStaticCatalogData charterRuleStaticCatalog;

        public CharterRuleStaticCatalogData CharterRuleStaticCatalog => charterRuleStaticCatalog;

        /// <summary>
        /// Fail-closed access to the single approved charter static directory. It resolves only when
        /// the asset reference exists and its version, declared catalog and definitions all validate;
        /// Editor import, fixtures and defaulted data never substitute for it.
        /// </summary>
        public bool TryGetCharterRuleStaticCatalog(
            out CharterRuleStaticCatalogData staticCatalog,
            out string reason)
        {
            if (charterRuleStaticCatalog == null)
            {
                staticCatalog = null;
                reason = "charter_static_catalog_unavailable";
                return false;
            }
            if (!charterRuleStaticCatalog.TryValidateDefinitions(out reason))
            {
                staticCatalog = null;
                return false;
            }

            staticCatalog = charterRuleStaticCatalog;
            reason = null;
            return true;
        }

        public void SetCharterRuleStaticCatalog(CharterRuleStaticCatalogData staticCatalog)
        {
            charterRuleStaticCatalog = staticCatalog;
        }

        public bool TryGetSettlement(string settlementId, out SettlementData settlement)
        {
            return TryFind(settlements, settlementId, value => value.settlementId, out settlement);
        }

        public bool TryGetEnemy(string enemyId, out EnemyData enemy)
        {
            return TryFind(enemies, enemyId, value => value.enemyId, out enemy);
        }

        public bool TryGetItem(string itemId, out ItemData item)
        {
            return TryFind(items, itemId, value => value.itemId, out item);
        }

        public bool TryGetBounty(string bountyId, out BountyData bounty)
        {
            return TryFind(bounties, bountyId, value => value.bountyId, out bounty);
        }

        public IReadOnlyList<BountyData> GetBountiesByIssuer(string issuerSettlementId)
        {
            var matches = new List<BountyData>();
            foreach (var bounty in bounties)
            {
                if (bounty != null && string.Equals(bounty.issuerSettlementId, issuerSettlementId, StringComparison.Ordinal))
                    matches.Add(bounty);
            }

            return matches;
        }

        public void ReplaceEntries(
            SettlementData[] nextSettlements,
            EnemyData[] nextEnemies,
            ItemData[] nextItems,
            BountyData[] nextBounties)
        {
            settlements = nextSettlements == null ? Array.Empty<SettlementData>() : (SettlementData[])nextSettlements.Clone();
            enemies = nextEnemies == null ? Array.Empty<EnemyData>() : (EnemyData[])nextEnemies.Clone();
            items = nextItems == null ? Array.Empty<ItemData>() : (ItemData[])nextItems.Clone();
            bounties = nextBounties == null ? Array.Empty<BountyData>() : (BountyData[])nextBounties.Clone();
        }

        private static bool TryFind<T>(T[] values, string id, Func<T, string> getId, out T result)
            where T : UnityEngine.Object
        {
            foreach (var value in values)
            {
                if (value != null && string.Equals(getId(value), id, StringComparison.Ordinal))
                {
                    result = value;
                    return true;
                }
            }

            result = null;
            return false;
        }
    }
}
