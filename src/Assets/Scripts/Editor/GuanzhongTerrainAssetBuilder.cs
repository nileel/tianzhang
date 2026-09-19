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
    /// <summary>Imports only the two approved initial Guanzhong terrain sources.</summary>
    public static class GuanzhongTerrainAssetBuilder
    {
        public const string AssetRoot = "Assets/Art/Environments/Guanzhong";
        public const string TilePrefabPath = AssetRoot + "/Tile.prefab";
        public const string StairPrefabPath = AssetRoot + "/Stair.prefab";
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
            AdventureSceneBuilder.Build();
            EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            WriteSceneEvidence();
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
