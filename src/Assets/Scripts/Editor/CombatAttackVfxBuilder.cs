using System;
using System.Collections.Generic;
using System.IO;
using TianZhang.Content;
using TianZhang.Features.CombatPresentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TianZhang.Editor
{
    /// <summary>
    /// Authors the presentation-only attack VFX catalog and wires it into the formal Adventure
    /// scene. No combat, save or content-catalog data is touched.
    /// </summary>
    public static class CombatAttackVfxBuilder
    {
        public const string CatalogPath = "Assets/Data/CombatAttackVfx/CombatAttackVfxCatalog.asset";
        private const string AttackProfilesPath = "Assets/DataConfig/AttackProfiles.csv";

        [MenuItem("天章/内容/构建攻击特效目录")]
        public static void Build()
        {
            BuildCatalog();
            WireAdventureScene();
        }

        [MenuItem("天章/场景/接入攻击特效目录")]
        public static void WireAdventureScene()
        {
            CombatAttackVfxCatalogData catalog =
                AssetDatabase.LoadAssetAtPath<CombatAttackVfxCatalogData>(CatalogPath);
            if (catalog == null) throw new InvalidOperationException("attack_vfx_catalog_missing");
            WireScene(catalog);
        }

        private static void WireScene(CombatAttackVfxCatalogData catalog)
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            GameObject root = Array.Find(scene.GetRootGameObjects(), value => value.name == "AdventureRoot");
            if (root == null) throw new InvalidOperationException("AdventureScene is missing AdventureRoot.");
            Static3DCombatUnitPresentationProvider provider =
                root.GetComponent<Static3DCombatUnitPresentationProvider>();
            if (provider == null) throw new InvalidOperationException("adventure_static3d_provider_missing");
            // The catalog can be re-imported while the scene opens, which invalidates the instance
            // passed in; resolve the persistent asset after the scene is active.
            CombatAttackVfxCatalogData asset =
                AssetDatabase.LoadAssetAtPath<CombatAttackVfxCatalogData>(CatalogPath);
            if (asset == null) throw new InvalidOperationException("attack_vfx_catalog_missing");
            SceneBuildSupport.SetObject(provider, "attackVfxCatalog", asset);
            var check = new SerializedObject(provider);
            UnityEngine.Object stored = check.FindProperty("attackVfxCatalog").objectReferenceValue;
            Debug.Log("[AttackVfx] catalog=" + AssetDatabase.GetAssetPath(asset) + "; stored=" +
                (stored == null ? "NULL" : AssetDatabase.GetAssetPath(stored)) + "; provider=" + provider.gameObject.name);
            if (stored == null) throw new InvalidOperationException("attack_vfx_catalog_not_stored");
            EditorSceneManager.MarkSceneDirty(scene);
            SceneBuildSupport.Save(SceneBuildSupport.AdventureScenePath);
        }

        public static CombatAttackVfxCatalogData BuildCatalog()
        {
            HashSet<string> known = LoadAttackProfileIds();
            CombatAttackVfxCatalogData catalog =
                AssetDatabase.LoadAssetAtPath<CombatAttackVfxCatalogData>(CatalogPath);
            Debug.Log("[AttackVfxBuild] load=" + (catalog == null ? "NULL" : AssetDatabase.GetAssetPath(catalog)) +
                "; fileExists=" + File.Exists(CatalogPath) + "; folder=" + AssetDatabase.IsValidFolder("Assets/Data/CombatAttackVfx"));
            if (catalog == null)
            {
                // A stale asset can exist after the data type moved assemblies (its script GUID no
                // longer resolves); remove it before creating the current projection.
                if (File.Exists(CatalogPath)) AssetDatabase.DeleteAsset(CatalogPath);
                EnsureDirectory(CatalogPath);
                catalog = ScriptableObject.CreateInstance<CombatAttackVfxCatalogData>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
                Debug.Log("[AttackVfxBuild] created=" + AssetDatabase.Contains(catalog) + "; path=" + AssetDatabase.GetAssetPath(catalog));
                if (!AssetDatabase.Contains(catalog))
                    throw new InvalidOperationException("attack_vfx_catalog_not_persistent");
            }
            catalog.entries = new[]
            {
                new CombatAttackVfxEntry
                {
                    attackProfileId = "basic_iron_sword",
                    kind = CombatAttackVfxKind.Slash,
                    color = new Color(1f, 0.97f, 0.9f, 1f),
                    scale = 1f,
                    durationSeconds = 0.35f,
                },
                new CombatAttackVfxEntry
                {
                    attackProfileId = "spell_feixingzhi",
                    kind = CombatAttackVfxKind.Projectile,
                    color = new Color(1f, 0.84f, 0.35f, 1f),
                    scale = 1f,
                    durationSeconds = 0.5f,
                },
            };
            foreach (CombatAttackVfxEntry entry in catalog.entries)
            {
                if (!known.Contains(entry.attackProfileId))
                    throw new InvalidDataException("attack_vfx_unknown_profile:" + entry.attackProfileId);
            }
            if (!catalog.TryValidate(out string reason)) throw new InvalidDataException(reason);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        private static HashSet<string> LoadAttackProfileIds()
        {
            if (!File.Exists(AttackProfilesPath))
                throw new FileNotFoundException(AttackProfilesPath);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(AttackProfilesPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#") ||
                    line.StartsWith("attackProfileId", StringComparison.Ordinal))
                    continue;
                ids.Add(line.Split(',')[0].Trim());
            }
            if (ids.Count == 0) throw new InvalidDataException("attack_profiles_empty");
            return ids;
        }

        private static void EnsureDirectory(string assetPath)
        {
            string directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory) || AssetDatabase.IsValidFolder(directory)) return;
            string parent = Path.GetDirectoryName(directory)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(parent));
            AssetDatabase.CreateFolder(parent ?? "Assets", Path.GetFileName(directory));
        }
    }
}
