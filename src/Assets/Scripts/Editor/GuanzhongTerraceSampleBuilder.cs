using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TianZhang.Editor
{
    /// <summary>D3.1 material and joint sample only; does not rebuild the formal battlefield.</summary>
    public static class GuanzhongTerraceSampleBuilder
    {
        public const string AssetRoot = GuanzhongTerrainAssetBuilder.AssetRoot + "/Terrace";
        public const string ScenePath = "Assets/Scenes/GuanzhongTerraceSample.unity";
        public const string TilePath = AssetRoot + "/TileTerrace.prefab";
        public const float RootBaseY = .34f;
        public const float LevelRise = .75f;

        [MenuItem("天章/美术/D3.1 构建三档地块样组")]
        public static void Build()
        {
            Directory.CreateDirectory(AssetRoot);
            AssetDatabase.Refresh();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetRoot + "/GrassEarthStone.mat");
            if (material == null)
            {
                material = new Material(SceneBuildSupport.RequireAsset<Material>(
                    GuanzhongTerrainAssetBuilder.AssetRoot + "/Tile.mat"));
                AssetDatabase.CreateAsset(material, AssetRoot + "/GrassEarthStone.mat");
            }
            material.shader = SceneBuildSupport.RequireAsset<Shader>(AssetRoot + "/TerraceLit.shader");
            material.SetColor("_BaseColor", new Color(.94f, .90f, .80f));
            material.SetFloat("_Smoothness", .06f);
            EditorUtility.SetDirty(material);
            var tile = (GameObject)PrefabUtility.InstantiatePrefab(
                SceneBuildSupport.RequireAsset<GameObject>(GuanzhongTerrainAssetBuilder.TilePrefabPath));
            tile.name = "TileTerrace";
            foreach (Renderer renderer in tile.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            PrefabUtility.SaveAsPrefabAsset(tile, TilePath);
            UnityEngine.Object.DestroyImmediate(tile);
            BuildScene(TilePath);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("D31_SAMPLE_BUILT " + ScenePath);
        }

        private static void BuildScene(string prefabPath)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera", typeof(Camera));
            camera.tag = "MainCamera";
            Camera view = camera.GetComponent<Camera>();
            view.orthographic = true;
            view.nearClipPlane = .1f;
            view.farClipPlane = 60;
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(.13f, .16f, .18f);
            SetCamera(view, 38, 0, 3);
            var light = new GameObject("Warm Key", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1, .91f, .78f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(48, -32, 0);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.47f, .55f, .64f);
            RenderSettings.ambientEquatorColor = new Color(.31f, .36f, .40f);
            RenderSettings.ambientGroundColor = new Color(.18f, .17f, .15f);
            var root = new GameObject("D31_ThreeLevelSample");
            for (int q = -2; q <= 2; q++)
            for (int r = -2; r <= 2; r++)
            {
                if (Math.Abs(q + r) > 2) continue;
                int level = r < 0 ? 0 : r == 0 ? 1 : 2;
                // One open two-level cliff and concave boundary, also retained in the final sample.
                if (q == 2 && r == 0) level = 2;
                var cell = (GameObject)PrefabUtility.InstantiatePrefab(SceneBuildSupport.RequireAsset<GameObject>(prefabPath));
                cell.name = $"Tile_{q}_{r}_L{level}";
                cell.transform.SetParent(root.transform, false);
                cell.transform.localPosition = new Vector3(q + .5f * r, RootBaseY + LevelRise * level,
                    Mathf.Sqrt(3) * .5f * r + 1);
                PrefabUtility.RecordPrefabInstancePropertyModifications(cell.transform);
            }
        }

        private static void SetCamera(Camera camera, float pitch, float yaw, float size)
        {
            camera.orthographicSize = size;
            camera.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            camera.transform.position = new Vector3(0, .3f, 1) - camera.transform.forward * 14;
        }
    }
}
