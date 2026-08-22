using System;
using System.IO;
using System.Linq;
using TianZhang.Bootstrap;
using TianZhang.Features.Adventure;
using TianZhang.Features.CharacterCreation;
using TianZhang.Features.CombatPresentation;
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
        private static readonly int[,] VisualBaselineCells =
        {
            { -2, 0, 0 }, { -1, 0, 1 }, { 0, 0, 2 }, { 1, 0, 1 }, { 2, 0, 0 },
            { -1, 1, 0 }, { 0, 1, 1 }, { 1, -1, 0 }, { 0, -1, 0 },
        };

        private static readonly int[,] FacingProbeExpectations =
        {
            { 1, 0, 1, 90 }, { 1, -1, 0, 150 }, { 0, -1, 0, 210 },
            { -1, 0, 1, 270 }, { -1, 1, 0, 330 }, { 0, 1, 1, 30 },
        };

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
            ValidateStaticChessPrefab();

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

        private static void ValidateStaticChessPrefab()
        {
            ModelImporter importer = AssetImporter.GetAtPath(VisualBaselineBuilder.StaticChessModelPath) as ModelImporter;
            Require(importer != null && !importer.importAnimation &&
                    importer.animationType == ModelImporterAnimationType.None,
                "The static chess FBX importer must keep animation disabled.");
            GameObject root = PrefabUtility.LoadPrefabContents(VisualBaselineBuilder.StaticChessPrefabPath);
            try
            {
                Require(root.name == "FuYuan_StaticChess" && root.transform.localPosition == Vector3.zero &&
                        root.transform.localScale == Vector3.one,
                    "The static chess root must retain its unit ground anchor and scale.");
                Require(root.GetComponentsInChildren<Animator>(true).Length == 0 &&
                        root.GetComponentsInChildren<Animation>(true).Length == 0 &&
                        root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0,
                    "The static chess prefab must not import a runtime animation or rig.");
                Require(root.GetComponents<StaticChessPresentationController>().Length == 1,
                    "The static chess root must own exactly one presentation controller.");
                Transform basePlaceholder = root.transform.Find("StaticChessBase");
                Require(basePlaceholder != null && Mathf.Abs(basePlaceholder.localPosition.y + 0.04f) < 0.001f,
                    "The independent static chess base must share the root ground anchor.");
                MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
                Require(renderers.Length >= 2, "The static chess prefab must contain figure and base renderers.");
                foreach (MeshRenderer renderer in renderers)
                    Require(renderer.shadowCastingMode == ShadowCastingMode.On && renderer.receiveShadows,
                        "Every static chess mesh must cast and receive shadows.");
                Require(AssetDatabase.GetAssetPath(basePlaceholder.GetComponent<MeshRenderer>().sharedMaterial) ==
                        VisualBaselineBuilder.UnitMaterialPath,
                    "The independent base must remain separate from the character material.");
                foreach (MeshRenderer renderer in renderers.Where(item => item.transform != basePlaceholder))
                    Require(AssetDatabase.GetAssetPath(renderer.sharedMaterial) ==
                            VisualBaselineBuilder.StaticChessMaterialPath,
                        "The imported static chess figure must use the frozen URP material.");
                Require(!AssetDatabase.LoadAllAssetsAtPath(VisualBaselineBuilder.StaticChessModelPath)
                            .OfType<AnimationClip>().Any(),
                    "The static chess FBX must not expose imported animation clips.");
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
            for (int index = 0; index < VisualBaselineCells.GetLength(0); index++)
                ValidateVisualBaselineCell(board.transform, VisualBaselineCells[index, 0], VisualBaselineCells[index, 1],
                    VisualBaselineCells[index, 2]);
            for (int index = 0; index < FacingProbeExpectations.GetLength(0); index++)
                ValidateFacingProbe(board.transform, index, FacingProbeExpectations[index, 0],
                    FacingProbeExpectations[index, 1], FacingProbeExpectations[index, 2],
                    FacingProbeExpectations[index, 3]);
            foreach (string name in new[]
                     { "SurfaceOverlay", "ReachableOverlay", "SelectedOverlay", "AttackOverlay", "VisualBaselineOccluder" })
                Require(FindNamed(scene, name) != null, "AdventureScene is missing visual layer: " + name);
        }

        private static void ValidateVisualBaselineCell(Transform board, int q, int r, int heightLevel)
        {
            Transform cell = board.Find("VisualHex_" + q + "_" + r + "_Height_" + heightLevel);
            Require(cell != null,
                "AdventureScene is missing visual baseline cell: (" + q + "," + r + ") height " + heightLevel + ".");
            Require(Vector3.Distance(cell.localPosition, HexToVisualPosition(q, r, 0f)) < 0.001f,
                "Visual baseline cell is not at its frozen hex center.");
            Require(Mathf.Abs(cell.localScale.y - HeightForLevel(heightLevel)) < 0.001f,
                "Visual baseline cell does not reflect its height level.");
        }

        private static void ValidateFacingProbe(Transform board, int direction, int q, int r, int heightLevel, int yaw)
        {
            Transform probe = board.Find("FacingProbe_" + direction);
            Require(probe != null,
                "AdventureScene is missing facing probe for direction " + direction + ".");
            Vector3 expectedPosition = HexToVisualPosition(q, r, HeightForLevel(heightLevel));
            Require(Vector3.Distance(probe.localPosition, expectedPosition) < 0.001f,
                "Facing probe is not centered on its rule neighbor.");
            Require(Quaternion.Angle(probe.localRotation, Quaternion.Euler(0f, yaw, 0f)) < 0.01f,
                "Facing probe does not use the frozen facing yaw.");
            Vector3 expectedForward = new Vector3(q + r * 0.5f, 0f, r * 0.8660254f).normalized;
            Require(Vector3.Angle(probe.localRotation * Vector3.forward, expectedForward) < 0.01f,
                "Facing probe local +Z does not point at its rule neighbor.");
            Require(probe.GetComponent<StaticChessPresentationController>() != null,
                "Facing probe must instantiate the static chess presentation root.");
            MeshRenderer[] renderers = probe.GetComponentsInChildren<MeshRenderer>(true);
            Require(renderers.Length >= 2, "Facing probe must instantiate the figure and independent base.");
            foreach (MeshRenderer renderer in renderers)
                Require(renderer.shadowCastingMode == ShadowCastingMode.On && renderer.receiveShadows,
                    "Facing probe meshes must cast and receive 3D shadows.");
        }

        private static float HeightForLevel(int heightLevel) => 0.34f + heightLevel * 0.28f;

        private static Vector3 HexToVisualPosition(int q, int r, float y) =>
            new Vector3(q + r * 0.5f, y, r * 0.8660254f + 1f);

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
