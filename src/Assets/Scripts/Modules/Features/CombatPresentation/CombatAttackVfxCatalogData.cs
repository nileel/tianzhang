using System;
using System.Collections.Generic;
using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    public enum CombatAttackVfxKind
    {
        Slash = 0,
        Projectile = 1,
    }

    /// <summary>Presentation-only mapping from an authoritative attack profile to one light VFX.</summary>
    [Serializable]
    public sealed class CombatAttackVfxEntry
    {
        public string attackProfileId;
        public CombatAttackVfxKind kind;
        public Color color = new Color(0.75f, 0.95f, 1f, 1f);
        [Min(0.1f)] public float scale = 1f;
        [Min(0.05f)] public float durationSeconds = 0.4f;
    }

    [CreateAssetMenu(fileName = "CombatAttackVfxCatalog", menuName = "天章/内容/攻击特效目录")]
    public sealed class CombatAttackVfxCatalogData : ScriptableObject
    {
        public CombatAttackVfxEntry[] entries = Array.Empty<CombatAttackVfxEntry>();

        public bool TryGet(string attackProfileId, out CombatAttackVfxEntry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(attackProfileId) || entries == null) return false;
            foreach (CombatAttackVfxEntry candidate in entries)
            {
                if (candidate == null ||
                    !string.Equals(candidate.attackProfileId, attackProfileId, StringComparison.Ordinal))
                    continue;
                entry = candidate;
                return true;
            }
            return false;
        }

        public bool TryValidate(out string reason)
        {
            if (entries == null || entries.Length == 0)
            {
                reason = "attack_vfx_catalog_empty";
                return false;
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (CombatAttackVfxEntry entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.attackProfileId) || !ids.Add(entry.attackProfileId))
                {
                    reason = "attack_vfx_catalog_entry_invalid";
                    return false;
                }
                if (!Enum.IsDefined(typeof(CombatAttackVfxKind), entry.kind) ||
                    float.IsNaN(entry.scale) || float.IsInfinity(entry.scale) || entry.scale <= 0f ||
                    float.IsNaN(entry.durationSeconds) || float.IsInfinity(entry.durationSeconds) ||
                    entry.durationSeconds <= 0f)
                {
                    reason = "attack_vfx_catalog_entry_invalid:" + entry.attackProfileId;
                    return false;
                }
            }
            reason = string.Empty;
            return true;
        }
    }
}
