using System.Collections.Generic;
using TianZhang.Entity;

namespace TianZhang.Combat
{
    /// <summary>Runtime-only gate that combines a character snapshot with immutable content requirements.</summary>
    public static class AbilityRequirementPolicy
    {
        private static readonly Dictionary<string, float> RealmThresholds = new()
        {
            ["realm_fanren"] = 1f, ["realm_lianqi"] = 1.5f, ["realm_zhuji"] = 3f,
            ["realm_jindan"] = 6f, ["realm_yuanying"] = 12f, ["realm_huashen"] = 24f,
        };

        public static bool IsSatisfied(Character character, string realmRequirement, string elementRequirement)
        {
            if (character == null || string.IsNullOrWhiteSpace(realmRequirement) ||
                !RealmThresholds.TryGetValue(realmRequirement.Trim(), out var minimum) || character.RealmMultiplier < minimum)
                return false;
            if (string.Equals(elementRequirement?.Trim(), "element_none", System.StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.IsNullOrWhiteSpace(elementRequirement) || string.IsNullOrWhiteSpace(character.VisibleRootElement))
                return false;

            const string prefix = "element_";
            var normalized = elementRequirement.Trim().ToLowerInvariant();
            if (!normalized.StartsWith(prefix, System.StringComparison.Ordinal))
                return false;

            var characterElement = DamageCalculator.ResolveElement(character.VisibleRootElement);
            foreach (var alternative in normalized.Substring(prefix.Length).Replace("_root", "").Split(new[] { "_or_" }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                if (DamageCalculator.ResolveElement(prefix + alternative) == characterElement)
                    return true;
            }
            return false;
        }
    }
}
