using System;
using System.Collections.Generic;
using UnityEngine;

namespace TianZhang.Content
{
    [Serializable]
    public sealed class CombatUnitPresentationProfileCatalogEntry
    {
        public string combatantId;
        public string presentationProfileId;
    }

    /// <summary>
    /// Provider-neutral stable identity map for combat-unit presentation. This catalog deliberately
    /// stores no Unity visual object so a presentation carrier remains free to choose its own asset.
    /// </summary>
    [CreateAssetMenu(fileName = "CombatUnitPresentationProfileCatalog", menuName = "天章/内容/战斗单位表现目录")]
    public sealed class CombatUnitPresentationProfileCatalogData : ScriptableObject
    {
        public const string PlayerCombatantId = "player";
        public const string ShijiahouCombatantId = "enemy_shijiahou";
        public const string PlayerProfileId = "combat_player_default_v1";
        public const string ShijiahouProfileId = "combat_enemy_shijiahou_v1";

        [SerializeField] private CombatUnitPresentationProfileCatalogEntry[] entries = Array.Empty<CombatUnitPresentationProfileCatalogEntry>();

        public IReadOnlyList<CombatUnitPresentationProfileCatalogEntry> Entries => entries;

        public bool TryGetPresentationProfileId(string combatantId, out string presentationProfileId)
        {
            presentationProfileId = null;
            if (string.IsNullOrWhiteSpace(combatantId) || !TryValidate(out _))
                return false;

            foreach (CombatUnitPresentationProfileCatalogEntry entry in entries)
            {
                if (string.Equals(entry.combatantId, combatantId, StringComparison.Ordinal))
                {
                    presentationProfileId = entry.presentationProfileId;
                    return true;
                }
            }

            return false;
        }

        public void SetEntries(CombatUnitPresentationProfileCatalogEntry[] values)
        {
            entries = values == null ? Array.Empty<CombatUnitPresentationProfileCatalogEntry>() :
                (CombatUnitPresentationProfileCatalogEntry[])values.Clone();
        }

        public bool TryValidate(out string reason) => TryValidateEntries(entries, out reason);

        public static bool TryValidateEntries(CombatUnitPresentationProfileCatalogEntry[] values, out string reason)
        {
            if (values == null || values.Length != 2)
            {
                reason = "combat_presentation_profile_catalog_incomplete";
                return false;
            }

            var mappings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (CombatUnitPresentationProfileCatalogEntry entry in values)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.combatantId) ||
                    string.IsNullOrWhiteSpace(entry.presentationProfileId) ||
                    !mappings.TryAdd(entry.combatantId, entry.presentationProfileId))
                {
                    reason = "combat_presentation_profile_catalog_duplicate_or_invalid";
                    return false;
                }
            }

            if (!mappings.TryGetValue(PlayerCombatantId, out string playerProfileId) ||
                !string.Equals(playerProfileId, PlayerProfileId, StringComparison.Ordinal) ||
                !mappings.TryGetValue(ShijiahouCombatantId, out string shijiahouProfileId) ||
                !string.Equals(shijiahouProfileId, ShijiahouProfileId, StringComparison.Ordinal))
            {
                reason = "combat_presentation_profile_catalog_mapping_invalid";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
