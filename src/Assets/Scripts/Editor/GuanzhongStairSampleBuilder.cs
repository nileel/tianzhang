using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Infrastructure.UnityContent;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TianZhang.Editor
{
    /// <summary>D3.3 route/joint sample; production Adventure assembly remains D3.6.</summary>
    public static class GuanzhongStairSampleBuilder
    {
        public const string AssetRoot = GuanzhongTerrainAssetBuilder.AssetRoot + "/Stairs";
        public const string ScenePath = "Assets/Scenes/GuanzhongStairSample.unity";
        public const string PrefabPath = AssetRoot + "/StoneStair3.prefab";

        [MenuItem("天章/美术/D3.3 构建两处石阶与通路样组")]
        public static void Build()
        {
            AssetDatabase.Refresh();
            var environment = SceneBuildSupport.RequireAsset<EnvironmentProfileAsset>(GuanzhongRampSampleBuilder.EnvironmentPath);
            var profiles = JsonUtility.FromJson<Profiles>(environment.physicalSurfaceProfiles);
            var importer = (ModelImporter)AssetImporter.GetAtPath(AssetRoot + "/StoneStair3.fbx");
            importer.isReadable = true;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.SaveAndReimport();
            Material tread = StoneMaterial("Tread", new Color(.94f,.90f,.80f));
            Material riser = StoneMaterial("Riser", new Color(.70f,.72f,.68f));
            Material wall = StoneMaterial("Wall", new Color(.78f,.79f,.72f));
            var stair = (GameObject)PrefabUtility.InstantiatePrefab(SceneBuildSupport.RequireAsset<GameObject>(AssetRoot + "/StoneStair3.fbx"));
            stair.name = "StoneStair3";
            foreach (Renderer renderer in stair.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(m => m.name switch
                {
                    "StairTread" => tread,
                    "StairRiser" => riser,
                    "StairWall" => wall,
                    _ => throw new InvalidOperationException("Unexpected stair material: " + m.name)
                }).ToArray();
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            Vector3[] points = stair.GetComponentsInChildren<MeshFilter>()
                .SelectMany(f => f.sharedMesh.vertices.Select(f.transform.TransformPoint)).ToArray();
            if (Mathf.Abs(points.Min(v => v.x)+.5f)>.0001f || Mathf.Abs(points.Max(v => v.x)-1.5f)>.0001f ||
                Mathf.Abs(points.Max(v => v.y)-.75f)>.0001f || Mathf.Abs(points.Max(v => v.z)-4/Mathf.Sqrt(3))>.0001f)
                throw new InvalidOperationException("Stair FBX does not match the D1 metre/axis frame.");
            PrefabUtility.SaveAsPrefabAsset(stair,PrefabPath);
            UnityEngine.Object.DestroyImmediate(stair);
            BuildScene(environment,profiles);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),ScenePath);
            EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            Debug.Log("D33_SAMPLE_BUILT " + ScenePath);
        }

        private static Material StoneMaterial(string name, Color tint)
        {
            string path = AssetRoot + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(SceneBuildSupport.RequireAsset<Material>(GuanzhongTerrainAssetBuilder.AssetRoot + "/Stair.mat"));
                AssetDatabase.CreateAsset(material,path);
            }
            material.shader = SceneBuildSupport.RequireAsset<Shader>(GuanzhongTerraceSampleBuilder.AssetRoot + "/TerraceLit.shader");
            material.SetColor("_BaseColor",tint);
            material.SetFloat("_Smoothness",.06f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BuildScene(EnvironmentProfileAsset environment, Profiles profiles)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root = new GameObject("D33_StairsAndGroundRoute");
            EnvironmentRampSurfaceProfileJson ramp = profiles.rampProfiles.Single(p => p.id=="left_ramp");
            Place(root,GuanzhongRampSampleBuilder.PrefabPath,"left_ramp",
                new Vector3(.5f,(float)ramp.lowerY,(float)ramp.worldZStart));
            var flatCells = new HashSet<string>(new[] {
                "4:0","3:0","2:7","3:7","4:7","5:7","6:7","7:7","8:7","8:11","8:12",
                "4:1","5:1","5:0","6:0","5:5","5:6"
            });
            foreach (string edge in ramp.lowerPortalEdges.Concat(ramp.upperPortalEdges))
                foreach (string cell in edge.Split('>')) flatCells.Add(cell);
            int[,] offsets = { {1,0},{1,-1},{0,-1},{-1,0},{-1,1},{0,1} };
            foreach (Stair profile in profiles.stairConnections)
            {
                if (profile.clearanceLimit!=1 || profile.coveredCells.Length!=3 || profile.risers.Length!=8)
                    throw new InvalidOperationException("D3.3 requires the frozen three-platform clearance-1 stairs.");
                Place(root,PrefabPath,profile.id,new Vector3(profile.platformCenters[0].worldX,profile.lowerY,profile.worldZStart));
                foreach (string key in profile.coveredCells)
                {
                    int[] qr = key.Split(':').Select(int.Parse).ToArray();
                    for (int direction=0;direction<6;direction++)
                        flatCells.Add($"{qr[0]+offsets[direction,0]}:{qr[1]+offsets[direction,1]}");
                }
            }
            for (int r=0;r<=7;r++) flatCells.Add($"4:{r}");
            // Reuse the D3.2 exact-cap Tile derivative at all exposed ports. No Tile overlaps a connection domain.
            foreach (var cell in environment.battlefieldCells.Where(c => flatCells.Contains($"{c.q}:{c.r}") && c.physicalSurfaceId.StartsWith("flat_")))
                Place(root,GuanzhongRampSampleBuilder.LandingPath,$"Tile_{cell.q}_{cell.r}_L{cell.heightLevel}",
                    new Vector3(cell.q+.5f*cell.r,.34f+.75f*cell.heightLevel,1+Mathf.Sqrt(3)*.5f*cell.r));
            var camera = new GameObject("Main Camera",typeof(Camera)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.orthographic = true;
            camera.orthographicSize = 6.4f;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 70;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.13f,.16f,.18f);
            camera.transform.rotation = Quaternion.Euler(48,-22,0);
            camera.transform.position = new Vector3(7,.5f,6)-camera.transform.forward*25;
            var light = new GameObject("Warm Key",typeof(Light)).GetComponent<Light>();
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

        private static void Place(GameObject root, string path, string name, Vector3 position)
        {
            var obj = (GameObject)PrefabUtility.InstantiatePrefab(SceneBuildSupport.RequireAsset<GameObject>(path));
            obj.name = name;
            obj.transform.SetParent(root.transform,false);
            obj.transform.localPosition = position;
            PrefabUtility.RecordPrefabInstancePropertyModifications(obj.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
        }

        [Serializable] private sealed class Profiles
        {
            public EnvironmentRampSurfaceProfileJson[] rampProfiles;
            public Stair[] stairConnections;
        }
        [Serializable] private sealed class Stair
        {
            public string id;
            public string[] coveredCells;
            public int clearanceLimit;
            public float lowerY,worldZStart;
            public Platform[] platformCenters;
            public Riser[] risers;
        }
        [Serializable] private sealed class Platform { public float worldX; }
        [Serializable] private sealed class Riser { public float worldZ,supportYAfter; }
    }
}
