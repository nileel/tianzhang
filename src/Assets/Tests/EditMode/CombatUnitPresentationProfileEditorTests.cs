using System;
using NUnit.Framework;
using TianZhang.Content;
using TianZhang.Editor;
using TianZhang.Features.CombatPresentation;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Tests.EditMode
{
    public sealed class CombatUnitPresentationProfileEditorTests
    {
        [Test]
        public void BuilderCreatesAndReloadsTheApprovedDualProfileSetIdempotently()
        {
            FormalStatic3DCombatPieceBuilder.Build();
            string firstCatalogGuid = AssetDatabase.AssetPathToGUID(FormalStatic3DCombatPieceBuilder.CatalogAssetPath);
            string firstProfileSetGuid = AssetDatabase.AssetPathToGUID(FormalStatic3DCombatPieceBuilder.ProfileSetAssetPath);
            string firstPlayerPrefabGuid = AssetDatabase.AssetPathToGUID(FormalStatic3DCombatPieceBuilder.PlayerPrefabPath);
            string firstShijiahouPrefabGuid = AssetDatabase.AssetPathToGUID(FormalStatic3DCombatPieceBuilder.ShijiahouPrefabPath);

            FormalStatic3DCombatPieceBuilder.Build();
            AssetDatabase.SaveAssets();
            FormalStatic3DCombatPieceBuilder.VerifySavedOutputs();

            Assert.AreEqual(firstCatalogGuid, AssetDatabase.AssetPathToGUID(FormalStatic3DCombatPieceBuilder.CatalogAssetPath));
            Assert.AreEqual(firstProfileSetGuid, AssetDatabase.AssetPathToGUID(FormalStatic3DCombatPieceBuilder.ProfileSetAssetPath));
            Assert.AreEqual(firstPlayerPrefabGuid, AssetDatabase.AssetPathToGUID(FormalStatic3DCombatPieceBuilder.PlayerPrefabPath));
            Assert.AreEqual(firstShijiahouPrefabGuid, AssetDatabase.AssetPathToGUID(FormalStatic3DCombatPieceBuilder.ShijiahouPrefabPath));

            CombatUnitPresentationProfileCatalogData catalog = AssetDatabase.LoadAssetAtPath<CombatUnitPresentationProfileCatalogData>(
                FormalStatic3DCombatPieceBuilder.CatalogAssetPath);
            Static3DCombatUnitPresentationProfileSet profileSet = AssetDatabase.LoadAssetAtPath<Static3DCombatUnitPresentationProfileSet>(
                FormalStatic3DCombatPieceBuilder.ProfileSetAssetPath);
            Assert.IsTrue(catalog.TryGetPresentationProfileId(CombatUnitPresentationProfileCatalogData.PlayerCombatantId, out string playerProfileId));
            Assert.IsTrue(catalog.TryGetPresentationProfileId(CombatUnitPresentationProfileCatalogData.ShijiahouCombatantId, out string shijiahouProfileId));
            Assert.AreEqual(CombatUnitPresentationProfileCatalogData.PlayerProfileId, playerProfileId);
            Assert.AreEqual(CombatUnitPresentationProfileCatalogData.ShijiahouProfileId, shijiahouProfileId);
            Assert.IsTrue(profileSet.TryGetProfile(playerProfileId, out Static3DCombatUnitPresentationProfile playerProfile));
            Assert.IsTrue(profileSet.TryGetProfile(shijiahouProfileId, out Static3DCombatUnitPresentationProfile shijiahouProfile));
            Assert.AreEqual(FormalStatic3DCombatPieceBuilder.PlayerPrefabPath, AssetDatabase.GetAssetPath(playerProfile.prefab));
            Assert.AreEqual(FormalStatic3DCombatPieceBuilder.ShijiahouPrefabPath, AssetDatabase.GetAssetPath(shijiahouProfile.prefab));
        }

        [Test]
        public void CatalogFailsClosedForPartialDuplicateAndUnknownMappings()
        {
            Assert.IsFalse(CombatUnitPresentationProfileCatalogData.TryValidateEntries(new[]
            {
                new CombatUnitPresentationProfileCatalogEntry
                {
                    combatantId = CombatUnitPresentationProfileCatalogData.PlayerCombatantId,
                    presentationProfileId = CombatUnitPresentationProfileCatalogData.PlayerProfileId,
                },
            }, out string partialReason));
            Assert.AreEqual("combat_presentation_profile_catalog_incomplete", partialReason);

            Assert.IsFalse(CombatUnitPresentationProfileCatalogData.TryValidateEntries(new[]
            {
                new CombatUnitPresentationProfileCatalogEntry
                {
                    combatantId = CombatUnitPresentationProfileCatalogData.PlayerCombatantId,
                    presentationProfileId = CombatUnitPresentationProfileCatalogData.PlayerProfileId,
                },
                new CombatUnitPresentationProfileCatalogEntry
                {
                    combatantId = CombatUnitPresentationProfileCatalogData.PlayerCombatantId,
                    presentationProfileId = CombatUnitPresentationProfileCatalogData.ShijiahouProfileId,
                },
            }, out string duplicateReason));
            Assert.AreEqual("combat_presentation_profile_catalog_duplicate_or_invalid", duplicateReason);

            Assert.IsFalse(CombatUnitPresentationProfileCatalogData.TryValidateEntries(new[]
            {
                new CombatUnitPresentationProfileCatalogEntry
                {
                    combatantId = CombatUnitPresentationProfileCatalogData.PlayerCombatantId,
                    presentationProfileId = CombatUnitPresentationProfileCatalogData.PlayerProfileId,
                },
                new CombatUnitPresentationProfileCatalogEntry
                {
                    combatantId = CombatUnitPresentationProfileCatalogData.ShijiahouCombatantId,
                    presentationProfileId = "combat_enemy_unknown_v1",
                },
            }, out string unknownReason));
            Assert.AreEqual("combat_presentation_profile_catalog_mapping_invalid", unknownReason);
        }

        [Test]
        public void StaticProfileSetFailsClosedForPartialMissingPrefabMaterialAndDirectionContract()
        {
            Assert.IsFalse(Static3DCombatUnitPresentationProfileSet.TryValidateProfiles(new[]
            {
                CreateValidProfile(Static3DCombatUnitPresentationProfileSet.PlayerProfileId, null),
            }, out string partialReason));
            Assert.AreEqual("static_3d_profile_set_incomplete", partialReason);

            GameObject missingMaterial = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject missingComponent = new GameObject("MissingStatic3DMesh");
            GameObject validPrefab = CreateRuntimeValidPrefab("ValidStatic3DMesh");
            GameObject secondValidPrefab = CreateRuntimeValidPrefab("SecondValidStatic3DMesh");
            missingMaterial.GetComponent<MeshRenderer>().sharedMaterial = null;
            try
            {
                Assert.IsFalse(Static3DCombatUnitPresentationProfileSet.TryValidateProfiles(new[]
                {
                    CreateValidProfile(Static3DCombatUnitPresentationProfileSet.PlayerProfileId, missingMaterial),
                    CreateValidProfile(Static3DCombatUnitPresentationProfileSet.ShijiahouProfileId, validPrefab),
                }, out string materialReason));
                Assert.AreEqual("static_3d_profile_prefab_material_or_mesh_missing", materialReason);

                Assert.IsFalse(Static3DCombatUnitPresentationProfileSet.TryValidateProfiles(new[]
                {
                    CreateValidProfile(Static3DCombatUnitPresentationProfileSet.PlayerProfileId, missingComponent),
                    CreateValidProfile(Static3DCombatUnitPresentationProfileSet.ShijiahouProfileId, validPrefab),
                }, out string componentReason));
                Assert.AreEqual("static_3d_profile_prefab_components_invalid", componentReason);

                Static3DCombatUnitPresentationProfile invalidDirection = CreateValidProfile(
                    Static3DCombatUnitPresentationProfileSet.PlayerProfileId, validPrefab);
                invalidDirection.sixDirectionYawDegrees = new[] { 90, 150, 210, 270, 330, 90 };
                Assert.IsFalse(Static3DCombatUnitPresentationProfileSet.TryValidateProfiles(new[]
                {
                    invalidDirection,
                    CreateValidProfile(Static3DCombatUnitPresentationProfileSet.ShijiahouProfileId, secondValidPrefab),
                }, out string directionReason));
                Assert.AreEqual("static_3d_profile_direction_contract_invalid", directionReason);
            }
            finally
            {
                DestroyRuntimePrefab(missingMaterial);
                DestroyRuntimePrefab(missingComponent);
                DestroyRuntimePrefab(validPrefab);
                DestroyRuntimePrefab(secondValidPrefab);
            }
        }

        [Test]
        public void FormalProfileSetHasNoFuYuanDependency()
        {
            FormalStatic3DCombatPieceBuilder.Build();
            Assert.IsFalse(Array.Exists(
                AssetDatabase.GetDependencies(FormalStatic3DCombatPieceBuilder.ProfileSetAssetPath, true),
                path => path.IndexOf("FuYuan_StaticChess", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static Static3DCombatUnitPresentationProfile CreateValidProfile(string profileId, GameObject prefab)
        {
            return new Static3DCombatUnitPresentationProfile
            {
                presentationProfileId = profileId,
                prefab = prefab,
                approvedModelAssetPath = "Assets/Test/Static3D.fbx",
                approvedModelSha256 = new string('a', 64),
                approvedBoundsMin = new Vector3(-1f, 0f, -1f),
                approvedBoundsMax = new Vector3(1f, 1f, 1f),
                sixDirectionYawDegrees = (int[])Static3DCombatUnitPresentationProfileSet.RequiredSixDirectionYawDegrees.Clone(),
            };
        }

        private static GameObject CreateRuntimeValidPrefab(string name)
        {
            GameObject prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prefab.name = name;
            Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Texture2D texture = new Texture2D(1, 1);
            material.mainTexture = texture;
            prefab.GetComponent<MeshRenderer>().sharedMaterial = material;
            return prefab;
        }

        private static void DestroyRuntimePrefab(GameObject prefab)
        {
            if (prefab == null) return;
            Material material = prefab.GetComponent<MeshRenderer>() == null ? null : prefab.GetComponent<MeshRenderer>().sharedMaterial;
            Texture texture = material == null ? null : material.mainTexture;
            UnityEngine.Object.DestroyImmediate(prefab);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
