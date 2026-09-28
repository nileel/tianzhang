using System;
using System.Collections.Generic;
using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    [Serializable]
    public sealed class Static3DCombatUnitPresentationProfile
    {
        public string presentationProfileId;
        public GameObject prefab;
        public string approvedModelAssetPath;
        public string approvedModelSha256;
        public Vector3 approvedBoundsMin;
        public Vector3 approvedBoundsMax;
        public int[] sixDirectionYawDegrees = Array.Empty<int>();
    }

    /// <summary>
    /// Static-3D carrier data. It intentionally owns Prefab references and carrier QA only; stable
    /// combatant identity stays in the provider-neutral Content catalog.
    /// </summary>
    [CreateAssetMenu(fileName = "Static3DCombatUnitPresentationProfileSet", menuName = "天章/战斗表现/静态3D Profile集合")]
    public sealed class Static3DCombatUnitPresentationProfileSet : ScriptableObject
    {
        public const string PlayerProfileId = "combat_player_default_v1";
        public const string ShijiahouProfileId = "combat_enemy_shijiahou_v1";
        public static readonly int[] RequiredSixDirectionYawDegrees = { 90, 150, 210, 270, 330, 30 };

        [SerializeField] private Static3DCombatUnitPresentationProfile[] profiles = Array.Empty<Static3DCombatUnitPresentationProfile>();

        public IReadOnlyList<Static3DCombatUnitPresentationProfile> Profiles => profiles;

        public bool TryGetProfile(string presentationProfileId, out Static3DCombatUnitPresentationProfile profile)
        {
            profile = null;
            if (string.IsNullOrWhiteSpace(presentationProfileId) || !TryValidate(out _))
                return false;

            foreach (Static3DCombatUnitPresentationProfile candidate in profiles)
            {
                if (string.Equals(candidate.presentationProfileId, presentationProfileId, StringComparison.Ordinal))
                {
                    profile = candidate;
                    return true;
                }
            }

            return false;
        }

        public void SetProfiles(Static3DCombatUnitPresentationProfile[] values)
        {
            profiles = values == null ? Array.Empty<Static3DCombatUnitPresentationProfile>() :
                (Static3DCombatUnitPresentationProfile[])values.Clone();
        }

        public bool TryValidate(out string reason) => TryValidateProfiles(profiles, out reason);

        public static bool TryValidateProfiles(Static3DCombatUnitPresentationProfile[] values, out string reason)
        {
            if (values == null || values.Length != 2)
            {
                reason = "static_3d_profile_set_incomplete";
                return false;
            }

            var profileIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Static3DCombatUnitPresentationProfile profile in values)
            {
                if (profile == null || string.IsNullOrWhiteSpace(profile.presentationProfileId) ||
                    !profileIds.Add(profile.presentationProfileId))
                {
                    reason = "static_3d_profile_set_duplicate_or_invalid";
                    return false;
                }

                if (!TryValidateProfile(profile, out reason))
                    return false;
            }

            if (!profileIds.SetEquals(new[]
            {
                PlayerProfileId,
                ShijiahouProfileId,
            }))
            {
                reason = "static_3d_profile_set_unknown_profile";
                return false;
            }

            reason = null;
            return true;
        }

        private static bool TryValidateProfile(Static3DCombatUnitPresentationProfile profile, out string reason)
        {
            if (profile.prefab == null)
            {
                reason = "static_3d_profile_prefab_missing";
                return false;
            }
            if (string.IsNullOrWhiteSpace(profile.approvedModelAssetPath) ||
                string.IsNullOrWhiteSpace(profile.approvedModelSha256) ||
                profile.approvedModelSha256.Length != 64 ||
                !HasLowercaseHex(profile.approvedModelSha256))
            {
                reason = "static_3d_profile_source_identity_invalid";
                return false;
            }
            if (!IsFinite(profile.approvedBoundsMin) || !IsFinite(profile.approvedBoundsMax) ||
                profile.approvedBoundsMin.y < -0.0001f ||
                profile.approvedBoundsMax.x <= profile.approvedBoundsMin.x ||
                profile.approvedBoundsMax.y <= profile.approvedBoundsMin.y ||
                profile.approvedBoundsMax.z <= profile.approvedBoundsMin.z)
            {
                reason = "static_3d_profile_bounds_invalid";
                return false;
            }
            if (!HasRequiredSixDirections(profile.sixDirectionYawDegrees))
            {
                reason = "static_3d_profile_direction_contract_invalid";
                return false;
            }
            if (profile.prefab.transform.localPosition != Vector3.zero ||
                Quaternion.Angle(profile.prefab.transform.localRotation, Quaternion.identity) > 0.01f ||
                Vector3.Distance(profile.prefab.transform.localScale, Vector3.one) > 0.0001f)
            {
                reason = "static_3d_profile_prefab_root_transform_invalid";
                return false;
            }
            if (profile.prefab.GetComponentsInChildren<MeshFilter>(true).Length != 1 ||
                profile.prefab.GetComponentsInChildren<MeshRenderer>(true).Length != 1 ||
                profile.prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0 ||
                profile.prefab.GetComponentsInChildren<Animator>(true).Length != 0 ||
                profile.prefab.GetComponentsInChildren<Animation>(true).Length != 0)
            {
                reason = "static_3d_profile_prefab_components_invalid";
                return false;
            }

            MeshFilter meshFilter = profile.prefab.GetComponentInChildren<MeshFilter>(true);
            MeshRenderer meshRenderer = profile.prefab.GetComponentInChildren<MeshRenderer>(true);
            if (meshFilter.sharedMesh == null || meshFilter.sharedMesh.vertexCount == 0 ||
                meshRenderer.sharedMaterial == null || meshRenderer.sharedMaterial.mainTexture == null)
            {
                reason = "static_3d_profile_prefab_material_or_mesh_missing";
                return false;
            }

            reason = null;
            return true;
        }

        private static bool HasRequiredSixDirections(int[] values)
        {
            if (values == null || values.Length != RequiredSixDirectionYawDegrees.Length)
                return false;
            for (int index = 0; index < RequiredSixDirectionYawDegrees.Length; index++)
            {
                if (values[index] != RequiredSixDirectionYawDegrees[index])
                    return false;
            }

            return true;
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private static bool HasLowercaseHex(string value)
        {
            foreach (char character in value)
            {
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
                    return false;
            }

            return true;
        }
    }
}
