using System;
using System.IO;
using System.Linq;
using TianZhang.Bootstrap;
using TianZhang.Features.Adventure;
using TianZhang.Features.CharacterCreation;
using TianZhang.Features.Settlement;
using TianZhang.Features.WorldMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TianZhang.Editor
{
    public static class SceneArchitectureValidator
    {
        [MenuItem("天章/场景/验证正式场景")]
        public static void Validate()
        {
            string[] expected =
            {
                SceneBuildSupport.StartMenuScenePath,
                SceneBuildSupport.WorldScenePath,
                SceneBuildSupport.SettlementScenePath,
                SceneBuildSupport.AdventureScenePath,
            };
            string[] enabled = EditorBuildSettings.scenes.Where(item => item.enabled).Select(item => item.path).ToArray();
            Require(expected.SequenceEqual(enabled), "Build Settings must contain exactly the four formal scenes in order.");
            ValidateRenderingAssets();
            ValidateScene<StartMenuSceneInstaller, StartMenuController>(expected[0], true);
            ValidateScene<WorldSceneInstaller, WorldMapController>(expected[1], false);
            ValidateScene<SettlementSceneInstaller, SettlementController>(expected[2], false);
            ValidateScene<AdventureSceneInstaller, AdventureController>(expected[3], false);
        }

        public static void ValidateForBatchMode() => Validate();

        private static void ValidateRenderingAssets()
        {
            RenderPipelineAsset pipeline =
                AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(VisualBaselineBuilder.PipelineAssetPath);
            Require(pipeline != null &&
                    pipeline.GetType().FullName ==
                    "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset",
                "The TianZhang URP asset is missing.");
            Require(GraphicsSettings.defaultRenderPipeline == pipeline,
                "Graphics Settings must keep the TianZhang URP asset.");
            for (int index = 0; index < QualitySettings.names.Length; index++)
                Require(QualitySettings.GetRenderPipelineAssetAt(index) == pipeline,
                    "Quality level is not bound to the TianZhang URP asset: " + QualitySettings.names[index]);

            string[] pipelines = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets" });
            Require(pipelines.Length == 1, "Assets must contain exactly one UniversalRenderPipelineAsset.");
            string[] rendererGuids = AssetDatabase.FindAssets("t:ScriptableRendererData", new[] { "Assets" });
            Require(rendererGuids.Length == 1, "Assets must contain exactly one ScriptableRendererData asset.");
            string rendererPath = AssetDatabase.GUIDToAssetPath(rendererGuids[0]);
            UnityEngine.Object renderer = AssetDatabase.LoadMainAssetAtPath(rendererPath);
            Require(renderer != null &&
                    renderer.GetType().FullName ==
                    "UnityEngine.Rendering.Universal.UniversalRendererData" &&
                    rendererPath == VisualBaselineBuilder.UniversalRendererAssetPath,
                "The only renderer must be the frozen UniversalRendererData asset.");
            Require(!AssetDatabase.AssetPathExists(VisualBaselineBuilder.LegacyRendererAssetPath),
                "The legacy Renderer2DData asset still exists.");

            var serialized = new SerializedObject(pipeline);
            SerializedProperty list = serialized.FindProperty("m_RendererDataList");
            Require(list != null && list.arraySize == 1 &&
                    list.GetArrayElementAtIndex(0).objectReferenceValue == renderer,
                "The URP asset must reference only the frozen UniversalRendererData.");
            SerializedProperty defaultIndex = serialized.FindProperty("m_DefaultRendererIndex");
            Require(defaultIndex != null && defaultIndex.intValue == 0,
                "The URP default renderer index must remain zero.");
            SerializedProperty msaa = serialized.FindProperty("m_MSAA");
            Require(msaa != null && msaa.intValue == 2,
                "The URP asset must keep the approved 2x MSAA baseline.");
            Require(GraphicsSettings.lightsUseColorTemperature,
                "URP must persist its required color-temperature lighting setting.");
            Require(QualitySettings.antiAliasing == 2,
                "The active Quality level must keep its existing 2x anti-aliasing setting.");

            Mesh column = AssetDatabase.LoadAssetAtPath<Mesh>(VisualBaselineBuilder.HexColumnMeshPath);
            Mesh overlay = AssetDatabase.LoadAssetAtPath<Mesh>(VisualBaselineBuilder.HexOverlayMeshPath);
            Require(column != null && column.subMeshCount == 2,
                "The reusable hex column must separate top and side submeshes.");
            Require(overlay != null && overlay.subMeshCount == 1,
                "The overlay mesh must remain independent from the hex column.");
            ValidateTransparentMaterial(VisualBaselineBuilder.SurfaceMaterialPath);
            ValidateTransparentMaterial(VisualBaselineBuilder.ReachableMaterialPath);
            ValidateTransparentMaterial(VisualBaselineBuilder.SelectedMaterialPath);
            ValidateTransparentMaterial(VisualBaselineBuilder.AttackMaterialPath);
            ValidateUnitMarkerPrefab();

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            Require(!string.IsNullOrWhiteSpace(projectRoot) &&
                    File.Exists(Path.Combine(projectRoot, "ProjectSettings", "ShaderGraphSettings.asset")),
                "Unity did not persist the URP Shader Graph project defaults.");
            Require(File.Exists(Path.Combine(projectRoot, "ProjectSettings", "URPProjectSettings.asset")),
                "Unity did not persist the URP project defaults.");
        }

        private static void ValidateTransparentMaterial(string path)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Require(material != null && material.shader != null &&
                    material.shader.name == "Universal Render Pipeline/Unlit" &&
                    material.HasProperty("_Surface") && material.GetFloat("_Surface") == 1f &&
                    material.renderQueue >= (int)RenderQueue.Transparent,
                "Invalid transparent visual baseline material: " + path);
        }

        private static void ValidateUnitMarkerPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(VisualBaselineBuilder.UnitMarkerPrefabPath);
            try
            {
                Require(root.GetComponentsInChildren<SpriteRenderer>(true).Length == 0,
                    "UnitMarker must not retain a SpriteRenderer.");
                MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
                Require(renderers.Length >= 2, "UnitMarker must contain a body and a facing mesh.");
                foreach (MeshRenderer renderer in renderers)
                    Require(renderer.shadowCastingMode == ShadowCastingMode.On && renderer.receiveShadows,
                        "Every UnitMarker mesh must cast and receive 3D shadows.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ValidateScene<TInstaller, TController>(string path, bool expectsBootstrap)
            where TInstaller : Component
            where TController : Component
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Require(scene.IsValid() && scene.isLoaded, "Invalid formal scene: " + path);
            Require(FindComponents<TInstaller>(scene).Length == 1, path + " must contain one installer.");
            Require(FindComponents<TController>(scene).Length == 1, path + " must contain one controller.");
            int bootstrapCount = FindComponents<GameBootstrap>(scene).Length;
            Require(bootstrapCount == (expectsBootstrap ? 1 : 0),
                path + " has an invalid serialized GameBootstrap count.");

            Camera[] cameras = FindComponents<Camera>(scene);
            Require(cameras.Length == 1 && cameras[0].name == "Main Camera" && cameras[0].orthographic,
                path + " must contain one orthographic Main Camera.");
            Require(Vector3.Distance(cameras[0].transform.position, new Vector3(0f, 8f, -10f)) < 0.001f &&
                    Quaternion.Angle(cameras[0].transform.rotation, Quaternion.Euler(38f, 0f, 0f)) < 0.01f,
                path + " does not use the frozen oblique camera composition.");
            Light[] lights = FindComponents<Light>(scene);
            Require(lights.Length == 1 && lights[0].type == LightType.Directional &&
                    lights[0].shadows != LightShadows.None,
                path + " must contain one realtime shadow-casting Directional Light.");
            Canvas[] canvases = FindComponents<Canvas>(scene);
            Require(canvases.Length == 1 && canvases[0].name == "UICanvas" &&
                    canvases[0].renderMode == RenderMode.ScreenSpaceOverlay,
                path + " must contain one ScreenSpaceOverlay UICanvas.");
            Require(FindNamed(scene, "VisualBackdrop") != null,
                path + " is missing the shared 3D backdrop.");

            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
                Require(component != null, path + " contains a Missing Script.");

            if (path == SceneBuildSupport.AdventureScenePath) ValidateAdventureVisualMatrix(scene);
        }

        private static void ValidateAdventureVisualMatrix(Scene scene)
        {
            GameObject board = FindNamed(scene, "VisualBaselineBoard");
            Require(board != null, "AdventureScene is missing the visual baseline board.");
            Require(board.GetComponentsInChildren<MeshRenderer>(true)
                        .Count(item => item.name.StartsWith("VisualHex_", StringComparison.Ordinal)) == 9,
                "AdventureScene must contain the bounded nine-cell technical matrix.");
            foreach (string name in new[]
                     { "SurfaceOverlay", "ReachableOverlay", "SelectedOverlay", "AttackOverlay", "VisualBaselineOccluder" })
                Require(FindNamed(scene, name) != null, "AdventureScene is missing visual layer: " + name);
        }

        private static T[] FindComponents<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

        private static GameObject FindNamed(Scene scene, string name)
        {
            foreach (Transform transform in FindComponents<Transform>(scene))
                if (transform.name == name) return transform.gameObject;
            return null;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
