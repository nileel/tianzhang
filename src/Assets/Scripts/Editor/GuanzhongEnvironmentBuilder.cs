using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TianZhang.Editor
{
    /// <summary>
    /// V2 environment integration: imports the V1 layered art, builds the far canyon, both near
    /// foregrounds and the cliff-bottom mist as decoration under a root that is a sibling of the
    /// battlefield (so camera framing and pan bounds stay driven by the battlefield only), and
    /// establishes the base light/ambient/shadow setup V3 tunes in the real game view.
    /// </summary>
    public static class GuanzhongEnvironmentBuilder
    {
        public const string EnvironmentRootName = "GuanzhongEnvironment";
        public const string AssetFolder = "Assets/Art/Environments/Guanzhong/EnvironmentLayers";
        public const string MaterialFolder = AssetFolder + "/Materials";
        public const string QuadMeshPath = AssetFolder + "/EnvironmentQuad.asset";
        private const string RepoArtFolder = "assets/generated-scene-art/guanzhong-first-bounty/environment-layers-20261021";

        private const float CameraPitchDegrees = 38f;
        private const float FarDistance = 35f;
        private const float BackgroundWidth = 40f;
        private const float BackgroundHeight = 22.51196f;
        private const float NearBottomProjection = -6.6f;
        private const float LeftWidth = 18f, LeftHeight = 12f, LeftCenterX = -4f, LeftZ = 0.5f;
        private const float RightWidth = 17f, RightHeight = 11.33333f, RightCenterX = 18f, RightZ = 0.5f;
        private const float FogHeight = -1.30f;
        private const float FogWidth = 40f, FogDepth = 33f;
        private const float FogAlpha = 0.30f;
        private const float BoardTolerance = 0.15f;

        private static readonly string[] LayerFiles =
        {
            "01-distant-canyon.png",
            "02-foreground-left.png",
            "03-foreground-right.png",
            "04-valley-mist.png",
        };

        [MenuItem("天章/场景/重建关中环境层")]
        public static void Rebuild()
        {
            Scene scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            Build();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, SceneBuildSupport.AdventureScenePath))
                throw new InvalidOperationException("Could not save the Guanzhong environment.");
            AssetDatabase.SaveAssets();
        }

        /// <summary>Builds into the currently open Adventure scene; the full scene rebuild calls this too.</summary>
        public static void Build()
        {
            ImportLayers();
            Camera camera = Camera.main ?? throw new InvalidOperationException("adventure_environment_camera_missing");
            GameObject battlefield = GameObject.Find("GuanzhongBattlefield") ??
                throw new InvalidOperationException("adventure_environment_battlefield_missing");
            Bounds board = BattlefieldBounds(battlefield);
            WarnIfBoardMoved(board);

            GameObject existing = GameObject.Find(EnvironmentRootName);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
            var root = new GameObject(EnvironmentRootName);

            float groundY = battlefield.transform.GetChild(0).position.y;
            float sin = Mathf.Sin(CameraPitchDegrees * Mathf.Deg2Rad);
            float cos = Mathf.Cos(CameraPitchDegrees * Mathf.Deg2Rad);
            Vector3 focus = new Vector3(board.center.x, groundY, board.center.z);
            Vector3 forward = camera.transform.forward;
            Quaternion facing = camera.transform.rotation;

            CreateLayer(root.transform, "Environment_DistantCanyon", "01-distant-canyon.png",
                BackgroundWidth, BackgroundHeight, camera.transform.position + forward * FarDistance, facing, Color.white);

            float leftY = (NearBottomProjection + LeftHeight * 0.5f - sin * LeftZ) / cos;
            CreateLayer(root.transform, "Environment_ForegroundLeft", "02-foreground-left.png",
                LeftWidth, LeftHeight, new Vector3(LeftCenterX, leftY, LeftZ), facing, Color.white);

            float rightY = (NearBottomProjection + RightHeight * 0.5f - sin * RightZ) / cos;
            CreateLayer(root.transform, "Environment_ForegroundRight", "03-foreground-right.png",
                RightWidth, RightHeight, new Vector3(RightCenterX, rightY, RightZ), facing, Color.white);

            float fogZ = focus.z + (groundY - FogHeight) * (cos / sin);
            CreateLayer(root.transform, "Environment_ValleyMist", "04-valley-mist.png",
                FogWidth, FogDepth, new Vector3(focus.x, FogHeight, fogZ), Quaternion.Euler(90f, 0f, 0f),
                new Color(1f, 1f, 1f, FogAlpha));

            ConfigureLighting(camera);
            Selection.activeGameObject = root;
        }

        private static Bounds BattlefieldBounds(GameObject battlefield)
        {
            var bounds = new Bounds();
            bool any = false;
            foreach (Renderer renderer in battlefield.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!any) throw new InvalidOperationException("adventure_environment_battlefield_empty");
            return bounds;
        }

        private static void WarnIfBoardMoved(Bounds board)
        {
            // V1 derived the layer sizes from these bounds; keep the check visible instead of
            // silently misplacing the decoration when the battlefield changes.
            var expected = new Vector2(14.30252f + 0.84457f, 9.606903f - 0.221975f);
            var actual = new Vector2(board.size.x, board.size.z);
            if (Mathf.Abs(actual.x - expected.x) > BoardTolerance || Mathf.Abs(actual.y - expected.y) > BoardTolerance)
                Debug.LogWarning("[Environment] Battlefield bounds changed; recompute the V1 layer sizes. actual=" +
                    actual + " expected=" + expected);
        }

        private static void CreateLayer(Transform parent, string name, string file, float width, float height,
            Vector3 position, Quaternion rotation, Color tint)
        {
            Material material = EnsureMaterial(name, AssetFolder + "/" + file, tint);
            var layer = new GameObject(name);
            layer.transform.SetParent(parent, false);
            layer.transform.SetPositionAndRotation(position, rotation);
            layer.transform.localScale = new Vector3(width, height, 1f);
            layer.AddComponent<MeshFilter>().sharedMesh = EnsureQuadMesh();
            var renderer = layer.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static Material EnsureMaterial(string layerName, string texturePath, Color tint)
        {
            EnsureFolder(MaterialFolder);
            string path = MaterialFolder + "/" + layerName + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Sprites/Default"));
                AssetDatabase.CreateAsset(material, path);
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null) throw new InvalidOperationException("adventure_environment_texture_missing:" + texturePath);
            material.shader = Shader.Find("Sprites/Default");
            material.mainTexture = texture;
            material.SetColor("_Color", tint);
            material.renderQueue = 3000;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh EnsureQuadMesh()
        {
            if (!AssetDatabase.IsValidFolder(AssetFolder)) EnsureFolder(AssetFolder);
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(QuadMeshPath);
            if (mesh != null) return mesh;
            mesh = new Mesh { name = "EnvironmentQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
            };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, QuadMeshPath);
            return mesh;
        }

        private static void ImportLayers()
        {
            EnsureFolder(AssetFolder);
            string sourceFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", RepoArtFolder));
            foreach (string file in LayerFiles)
            {
                string source = Path.Combine(sourceFolder, file);
                if (!File.Exists(source)) throw new FileNotFoundException("environment_layer_missing", source);
                string target = AssetFolder + "/" + file;
                if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length)
                {
                    File.Copy(source, target, true);
                    AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate);
                }
                ConfigureImporter(target, file != "01-distant-canyon.png");
            }
        }

        private static void ConfigureImporter(string assetPath, bool hasAlpha)
        {
            if (!(AssetImporter.GetAtPath(assetPath) is TextureImporter importer))
                throw new InvalidOperationException("environment_texture_importer_missing:" + assetPath);
            bool changed = importer.textureType != TextureImporterType.Default || !importer.sRGBTexture ||
                importer.npotScale != TextureImporterNPOTScale.None || importer.maxTextureSize != 2048 ||
                importer.wrapMode != TextureWrapMode.Clamp || importer.filterMode != FilterMode.Bilinear ||
                !importer.mipmapEnabled || importer.alphaIsTransparency != hasAlpha ||
                importer.textureCompression != TextureImporterCompression.Uncompressed;
            if (!changed) return;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.alphaIsTransparency = hasAlpha;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static void EnsureFolder(string assetFolder)
        {
            string[] parts = assetFolder.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }

        private static void ConfigureLighting(Camera camera)
        {
            Light[] lights = UnityEngine.Object.FindObjectsOfType<Light>();
            var directional = Array.Find(lights, light => light.type == LightType.Directional);
            if (directional == null) throw new InvalidOperationException("adventure_environment_directional_light_missing");
            directional.color = new Color(1f, 0.93f, 0.82f);
            directional.intensity = 1.1f;
            directional.shadows = LightShadows.Hard;
            directional.shadowStrength = 0.85f;
            directional.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            // Warm key from the upper left; cool sky/ground ambient keeps shadows readable.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.30f, 0.38f, 0.48f);
            RenderSettings.ambientEquatorColor = new Color(0.20f, 0.25f, 0.31f);
            RenderSettings.ambientGroundColor = new Color(0.09f, 0.10f, 0.12f);
            RenderSettings.ambientIntensity = 0.9f;
            RenderSettings.fog = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.07f, 0.10f);
            EditorUtility.SetDirty(directional);
        }
    }
}
