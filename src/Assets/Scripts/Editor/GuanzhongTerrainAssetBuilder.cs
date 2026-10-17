using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TianZhang.Editor
{
    /// <summary>Builds the approved Guanzhong terrain and independent obstacle prefabs.</summary>
    public static class GuanzhongTerrainAssetBuilder
    {
        public const string AssetRoot = "Assets/Art/Environments/Guanzhong";
        public const string TilePrefabPath = AssetRoot + "/Tile.prefab";
        public const string StairPrefabPath = AssetRoot + "/Stair.prefab";
        public const string RockPrefabPath = AssetRoot + "/RockCluster.prefab";
        private static string EvidenceRoot => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../assets/source/environments/guanzhong-first-bounty/initial-v1"));

        [MenuItem("天章/美术/构建关中初始地形资源与场景")]
        public static void BuildAssetsAndScene()
        {
            // Measured FBX transforms wrap the original hierarchy; never reshape the source mesh.
            BuildAsset("Tile", "504b1e7b390d5c061d911a3787d2171a241c4c6ce2cb58479aa53dbb899ffbee",
                new Vector3(.24881442194815115f, -.8945829622159506f, -.4738227693251703f),
                new Quaternion(-.17575856859084127f, .6849152688960135f, .6849152688960136f, .1757585685908413f),
                1.9770544758933135f);
            BuildAsset("Stair", "d1e5a51e8d934a827e90f2e77376c8f0697be6999f1c32407aeb41bee2c9eda9",
                new Vector3(-2.4696964348630835e-8f, -1.8870143808589007e-9f, -.4071731671681368f),
                new Quaternion(0, -.7071067811865476f, 0, .7071067811865475f), .4143463054212788f);
            BuildRockClusterAsset();
            AdventureSceneBuilder.Build();
            EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            WriteSceneEvidence();
        }

        [MenuItem("天章/美术/构建关中岩石与正式战场")]
        public static void BuildRockClusterAndScene()
        {
            BuildRockClusterAsset();
            AdventureSceneBuilder.RebuildGuanzhongBattlefield();
        }

        private static void BuildRockClusterAsset()
        {
            const string sourcePath = AssetRoot + "/RockCluster.fbx";
            using (var sha = SHA256.Create())
            {
                string hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(sourcePath)))
                    .Replace("-", "").ToLowerInvariant();
                if (hash != "1b2afc88171c6c8144a61616cf3129824a9a65c5363d15d66a76721940c824ba")
                    throw new InvalidOperationException("Original Tripo rock FBX hash mismatch.");
            }
            var importer = AssetImporter.GetAtPath(sourcePath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Missing original rock FBX.");
            importer.isReadable = true;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();

            string[] textures = { "RockCluster_basecolor.JPEG", "RockCluster_normal.PNG", "RockCluster_metallic_smoothness.png" };
            for (int index = 0; index < textures.Length; index++)
            {
                var textureImporter = (TextureImporter)AssetImporter.GetAtPath(AssetRoot + "/" + textures[index]);
                textureImporter.maxTextureSize = 4096;
                textureImporter.sRGBTexture = index == 0;
                textureImporter.textureType = index == 1 ? TextureImporterType.NormalMap : TextureImporterType.Default;
                textureImporter.SaveAndReimport();
            }
            string materialPath = AssetRoot + "/RockCluster.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", SceneBuildSupport.RequireAsset<Texture2D>(AssetRoot + "/" + textures[0]));
            material.SetTexture("_BumpMap", SceneBuildSupport.RequireAsset<Texture2D>(AssetRoot + "/" + textures[1]));
            material.SetTexture("_MetallicGlossMap", SceneBuildSupport.RequireAsset<Texture2D>(AssetRoot + "/" + textures[2]));
            material.SetFloat("_Smoothness", 1);
            material.SetFloat("_BumpScale", 1);
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(material);

            var root = new GameObject("RockCluster");
            try
            {
                var alignment = new GameObject("MeasuredFbxAlignment").transform;
                alignment.SetParent(root.transform, false);
                // Original mesh stays byte-identical. Fit its measured footprint with prefab transforms.
                alignment.localRotation = Quaternion.Euler(0, 54.8f, 0);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(SceneBuildSupport.RequireAsset<GameObject>(sourcePath));
                instance.transform.SetParent(alignment, false);
                Vector3[] vertices = instance.GetComponentsInChildren<MeshFilter>(true)
                    .SelectMany(filter => filter.sharedMesh.vertices.Select(filter.transform.TransformPoint)).ToArray();
                var bounds = new Bounds(vertices[0], Vector3.zero);
                foreach (Vector3 vertex in vertices) bounds.Encapsulate(vertex);
                float extent = vertices.Max(vertex =>
                {
                    Vector3 p = vertex - bounds.center;
                    return Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(.5f * p.x + .8660254f * p.z),
                        Mathf.Abs(.5f * p.x - .8660254f * p.z));
                });
                float horizontalScale = .4499f / extent;
                // Bury the source's uneven underside; top remains 0.60 m above the measured tile center.
                float verticalScale = (.6004328f + .16f) / bounds.size.y;
                alignment.localScale = new Vector3(horizontalScale, verticalScale, horizontalScale);
                alignment.localPosition = new Vector3(-bounds.center.x * horizontalScale,
                    -.16f - bounds.min.y * verticalScale, -bounds.center.z * horizontalScale);
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
                if (PrefabUtility.SaveAsPrefabAsset(root, RockPrefabPath) == null)
                    throw new InvalidOperationException("Could not save rock prefab.");
                Debug.Log($"GUANZHONG_ROCK_FIT sourceBounds={bounds}; scale={alignment.localScale:F7}; position={alignment.localPosition:F7}");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
        }

        private static void BuildAsset(string kind, string expectedHash, Vector3 position, Quaternion rotation, float scale)
        {
            string sourcePath = AssetRoot + "/" + kind + ".fbx";
            using (var sha = SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(sourcePath)))
                    .Replace("-", "").ToLowerInvariant();
                if (actual != expectedHash) throw new InvalidOperationException("Source FBX hash mismatch: " + sourcePath);
            }
            var importer = AssetImporter.GetAtPath(sourcePath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Missing source FBX: " + sourcePath);
            importer.isReadable = true;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();

            string materialPath = AssetRoot + "/" + kind + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("URP/Lit shader missing");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", SceneBuildSupport.RequireAsset<Texture2D>(AssetRoot + "/" + kind + "_basecolor.JPEG"));
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_Smoothness", .15f);
            EditorUtility.SetDirty(material);

            var root = new GameObject(kind);
            try
            {
                var alignment = new GameObject("MeasuredFbxAlignment");
                alignment.transform.SetParent(root.transform, false);
                alignment.transform.localPosition = position;
                alignment.transform.localRotation = rotation;
                alignment.transform.localScale = Vector3.one * scale;
                var source = SceneBuildSupport.RequireAsset<GameObject>(sourcePath);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
                instance.transform.SetParent(alignment.transform, false);
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
                if (PrefabUtility.SaveAsPrefabAsset(root, AssetRoot + "/" + kind + ".prefab") == null)
                    throw new InvalidOperationException("Could not save terrain prefab: " + kind);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
        }

        public static void WriteSceneEvidence()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != SceneBuildSupport.AdventureScenePath || scene.isDirty)
                throw new InvalidOperationException("Evidence requires the saved, reopened formal AdventureScene");
            GameObject battlefield = scene.GetRootGameObjects().Single(g => g.name == "GuanzhongBattlefield");
            Camera camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>()).Single();
            var proof = new SceneProof
            {
                unityVersion = Application.unityVersion, scene = scene.path, savedAndReopened = !scene.isDirty,
                cameraPosition = camera.transform.position, cameraEuler = camera.transform.eulerAngles,
                orthographicSize = camera.orthographicSize,
                cells = Enumerable.Range(0, battlefield.transform.childCount).Select(index =>
                {
                    Transform cell = battlefield.transform.GetChild(index);
                    return new CellProof
                    {
                        name = cell.name, position = cell.position, scale = cell.localScale,
                        prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(cell.gameObject),
                        colliderCount = cell.GetComponentsInChildren<Collider>(true).Length,
                        renderers = cell.GetComponentsInChildren<MeshRenderer>(true).Select(renderer =>
                        {
                            Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                            return new MeshProof
                            {
                                source = AssetDatabase.GetAssetPath(mesh), vertices = mesh.vertexCount,
                                triangles = mesh.triangles.Length / 3, worldMatrix = renderer.transform.localToWorldMatrix,
                                boundsMin = renderer.bounds.min, boundsMax = renderer.bounds.max,
                                materials = renderer.sharedMaterials.Select(AssetDatabase.GetAssetPath).ToArray(),
                                textures = renderer.sharedMaterials.Select(m => AssetDatabase.GetAssetPath(m.GetTexture("_BaseMap"))).ToArray()
                            };
                        }).ToArray()
                    };
                }).ToArray()
            };
            Directory.CreateDirectory(EvidenceRoot);
            File.WriteAllText(Path.Combine(EvidenceRoot, "unity-scene-proof.json"), JsonUtility.ToJson(proof, true));
            Debug.Log("GUANZHONG_INITIAL_TERRAIN_SAVED " + SceneBuildSupport.AdventureScenePath);
        }

        [Serializable] private sealed class SceneProof
        {
            public string unityVersion, scene;
            public bool savedAndReopened;
            public Vector3 cameraPosition, cameraEuler;
            public float orthographicSize;
            public CellProof[] cells;
        }
        [Serializable] private sealed class CellProof
        {
            public string name, prefab;
            public Vector3 position, scale;
            public int colliderCount;
            public MeshProof[] renderers;
        }
        [Serializable] private sealed class MeshProof
        {
            public string source;
            public int vertices, triangles;
            public Vector3 boundsMin, boundsMax;
            public Matrix4x4 worldMatrix;
            public string[] materials, textures;
        }
    }
}
