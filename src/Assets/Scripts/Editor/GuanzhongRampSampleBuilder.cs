using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TianZhang.Infrastructure.UnityContent;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TianZhang.Editor
{
    /// <summary>D3.2 finite ramp and platform sample. Does not rebuild Adventure.</summary>
    public static class GuanzhongRampSampleBuilder
    {
        public const string AssetRoot = GuanzhongTerrainAssetBuilder.AssetRoot + "/Ramp";
        public const string ScenePath = "Assets/Scenes/GuanzhongRampSample.unity";
        public const string PrefabPath = AssetRoot + "/EarthRamp24.prefab";
        public const string LandingPath = AssetRoot + "/RampLanding.prefab";
        public const string EnvironmentPath = "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_jindan_demo.asset";

        [MenuItem("天章/美术/D3.2 构建土石长坡样组")]
        public static void Build()
        {
            AssetDatabase.Refresh();
            var environment = SceneBuildSupport.RequireAsset<EnvironmentProfileAsset>(EnvironmentPath);
            var profiles = JsonUtility.FromJson<EnvironmentPhysicalSurfaceProfilesJson>(environment.physicalSurfaceProfiles);
            var profile = profiles.rampProfiles.Single(p => p.id == "left_ramp");
            var source = (ModelImporter)AssetImporter.GetAtPath(AssetRoot + "/EarthRamp24.fbx");
            source.isReadable = true;
            source.importAnimation = false;
            source.importCameras = false;
            source.importLights = false;
            source.addCollider = false;
            source.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            source.materialLocation = ModelImporterMaterialLocation.InPrefab;
            source.SaveAndReimport();
            Material earth = GroundMaterial("Earth", new Color(.43f, .35f, .25f), .15f);
            Material shoulder = GroundMaterial("Shoulder", new Color(.26f, .29f, .28f), .87f);
            Material rock = SceneBuildSupport.RequireAsset<Material>(GuanzhongTerraceSampleBuilder.AssetRoot + "/GrassEarthStone.mat");
            var ramp = new GameObject("EarthRamp24");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(
                SceneBuildSupport.RequireAsset<GameObject>(AssetRoot + "/EarthRamp24.fbx"));
            model.transform.SetParent(ramp.transform, false);
            foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>())
            {
                Debug.Log("D32_SOURCE_MATERIALS " + string.Join(",",renderer.sharedMaterials.Select(m => m.name)));
                renderer.sharedMaterials = renderer.sharedMaterials.Select(m => m.name switch
                {
                    "RampEarth" => earth,
                    "RampShoulder" => shoulder,
                    "RampRock" => rock,
                    _ => throw new InvalidOperationException("Unexpected ramp material: " + m.name)
                }).ToArray();
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                for (int submesh=0;submesh<mesh.subMeshCount;submesh++)
                {
                    int[] triangles=mesh.GetTriangles(submesh);
                    Vector3[] points=mesh.vertices;
                    Vector3 normal=Vector3.Cross(points[triangles[1]]-points[triangles[0]],points[triangles[2]]-points[triangles[0]]).normalized;
                    Debug.Log($"D32_SUBMESH {submesh} {renderer.sharedMaterials[submesh].name} triangles={triangles.Length/3} firstNormal={renderer.transform.TransformDirection(normal):F5}");
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            Vector3[] vertices = ramp.GetComponentsInChildren<MeshFilter>()
                .SelectMany(f => f.sharedMesh.vertices.Select(f.transform.TransformPoint)).ToArray();
            Debug.Log($"D32_IMPORTED_BOUNDS min=({vertices.Min(p => p.x):F8},{vertices.Min(p => p.y):F8},{vertices.Min(p => p.z):F8}) max=({vertices.Max(p => p.x):F8},{vertices.Max(p => p.y):F8},{vertices.Max(p => p.z):F8})");
            if (Mathf.Abs(vertices.Max(p => p.z) - (float)(profile.worldZEnd-profile.worldZStart) - 1/Mathf.Sqrt(3)) > .001f ||
                Mathf.Abs(vertices.Max(p => p.y) - .75f) > .001f || Mathf.Abs(vertices.Min(p => p.x) + .5f) > .001f)
                throw new InvalidOperationException("Ramp FBX axis/metre convention does not match the D1 local frame.");
            PrefabUtility.SaveAsPrefabAsset(ramp, PrefabPath);
            UnityEngine.Object.DestroyImmediate(ramp);
            BuildLanding(rock);
            BuildScene(environment, profile);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("D32_SAMPLE_BUILT " + ScenePath);
        }

        private static void BuildLanding(Material rock)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(AssetRoot + "/RampLanding.fbx");
            importer.isReadable = true;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.SaveAndReimport();
            Material cap = GroundMaterial("LandingTop",new Color(.28f,.36f,.27f),.08f);
            var landing = (GameObject)PrefabUtility.InstantiatePrefab(SceneBuildSupport.RequireAsset<GameObject>(AssetRoot + "/RampLanding.fbx"));
            landing.name = "RampLanding";
            foreach (Renderer renderer in landing.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(m => m.name switch
                {
                    "RampLandingTop" => cap,
                    "RampRock" => rock,
                    _ => throw new InvalidOperationException("Unexpected landing material: " + m.name)
                }).ToArray();
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            PrefabUtility.SaveAsPrefabAsset(landing,LandingPath);
            UnityEngine.Object.DestroyImmediate(landing);
        }

        private static Material GroundMaterial(string name, Color color, float density)
        {
            string path = AssetRoot + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(SceneBuildSupport.RequireAsset<Shader>(AssetRoot + "/RampGround.shader"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_GravelDensity", density);
            material.SetFloat("_Smoothness", .05f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BuildScene(EnvironmentProfileAsset environment, EnvironmentRampSurfaceProfileJson profile)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("D32_RampAndPlatforms");
            var ramp = (GameObject)PrefabUtility.InstantiatePrefab(SceneBuildSupport.RequireAsset<GameObject>(PrefabPath));
            ramp.transform.SetParent(root.transform, false);
            ramp.transform.localPosition = new Vector3(.5f, (float)profile.lowerY, (float)profile.worldZStart);
            PrefabUtility.RecordPrefabInstancePropertyModifications(ramp.transform);
            var covered = new HashSet<string>(profile.coveredCells);
            var platforms = new HashSet<string>(profile.lowerPortalEdges.Concat(profile.upperPortalEdges)
                .SelectMany(e => e.Split('>')).Where(c => !covered.Contains(c)));
            // Actual map neighbours expose the closed side and the adjacent bare-cliff shortcut.
            foreach (var cell in environment.battlefieldCells)
            {
                string key = $"{cell.q}:{cell.r}";
                bool neighbour = cell.q == 4 && cell.r >= 0 && cell.r <= 7;
                if ((!platforms.Contains(key) && !neighbour) || !cell.physicalSurfaceId.StartsWith("flat_")) continue;
                var tile = (GameObject)PrefabUtility.InstantiatePrefab(
                    SceneBuildSupport.RequireAsset<GameObject>(platforms.Contains(key) ? LandingPath : GuanzhongTerraceSampleBuilder.TilePath));
                tile.name = $"Tile_{cell.q}_{cell.r}_L{cell.heightLevel}";
                tile.transform.SetParent(root.transform, false);
                tile.transform.localPosition = new Vector3(cell.q + .5f*cell.r,
                    GuanzhongTerraceSampleBuilder.RootBaseY + .75f*cell.heightLevel, 1 + Mathf.Sqrt(3)*.5f*cell.r);
                PrefabUtility.RecordPrefabInstancePropertyModifications(tile.transform);
            }
            var camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.orthographic = true;
            camera.orthographicSize = 4.7f;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 70;
            camera.backgroundColor = new Color(.13f,.16f,.18f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.transform.rotation = Quaternion.Euler(38, -25, 0);
            camera.transform.position = new Vector3(3.7f,.3f,4) - camera.transform.forward*20;
            var light = new GameObject("Warm Key", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1,.91f,.78f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(48,-32,0);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.47f,.55f,.64f);
            RenderSettings.ambientEquatorColor = new Color(.31f,.36f,.40f);
            RenderSettings.ambientGroundColor = new Color(.18f,.17f,.15f);
        }
    }
}
