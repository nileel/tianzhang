using System;
using UnityEngine;

namespace TianZhang.Content
{
    /// <summary>Stable visual identity data; current production content contains only <c>none</c>.</summary>
    [CreateAssetMenu(fileName = "AppearanceProfile_", menuName = "天章/外观档案数据")]
    public sealed class AppearanceProfileData : ScriptableObject
    {
        public const string NoneId = "none";

        public string appearanceProfileId;

        public bool IsNone => string.Equals(appearanceProfileId, NoneId, StringComparison.Ordinal);

        public bool TryValidate(out string reason)
        {
            if (string.IsNullOrWhiteSpace(appearanceProfileId))
            {
                reason = "appearance_profile_id_missing";
                return false;
            }

            foreach (char character in appearanceProfileId)
            {
                if (!(character >= 'a' && character <= 'z') &&
                    !(character >= '0' && character <= '9') &&
                    character != '_')
                {
                    reason = "appearance_profile_id_invalid";
                    return false;
                }
            }

            reason = null;
            return true;
        }
    }
}
