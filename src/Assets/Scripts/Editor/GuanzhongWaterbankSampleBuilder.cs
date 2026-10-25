using System;
using System.Linq;
using TianZhang.Content;
using TianZhang.Infrastructure.UnityContent;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TianZhang.Editor
{
    /// <summary>D3.4 static surface assets; the formal Adventure map remains D3.6.</summary>
    public static class GuanzhongWaterbankSampleBuilder
    {
        public const string AssetRoot = GuanzhongTerrainAssetBuilder.AssetRoot + "/Waterbank";
        public const string ScenePath = "Assets/Scenes/GuanzhongWaterbankSample.unity";
        private const float SupportY = .340433121f;
        private static readonly int[,] Directions = { {1,0},{1,-1},{0,-1},{-1,0},{-1,1},{0,1} };

        [MenuItem("天章/美术/D3.4 构建水面与泥岸样组")]
        public static void Build()
        {
            AssetDatabase.Refresh();
            Material mud = Ground("MudBank",new Color(.30f,.245f,.16f),.13f,.28f);
            Material wet = Ground("WetShore",new Color(.19f,.215f,.16f),.08f,.38f);
            Material bed = Ground("WaterBed",new Color(.23f,.29f,.23f),.18f,.22f);
            Material water = MaterialAt("Water",SceneBuildSupport.RequireAsset<Shader>(AssetRoot+"/StillWater.shader"));
            water.SetColor("_BaseColor",new Color(.17f,.37f,.36f));
            water.SetFloat("_Smoothness",.68f);
            EditorUtility.SetDirty(water);
            BuildSheet("WaterHex",water);
            BuildSheet("ShoreEdge",wet);
            BuildTile("MudBankTile",mud,false);
            BuildTile("ShallowWaterTile",bed,true);
            var environment = SceneBuildSupport.RequireAsset<EnvironmentProfileAsset>(GuanzhongRampSampleBuilder.EnvironmentPath);
            EditorSceneManager.OpenScene(GuanzhongStairSampleBuilder.ScenePath,OpenSceneMode.Single);
            var root = GameObject.Find("D33_StairsAndGroundRoute");
            root.name = "D34_WaterbankAndRoutes";
            var surfaces = new GameObject("WaterAndMud");
            surfaces.transform.SetParent(root.transform,false);
            foreach (EnvironmentBattlefieldCell cell in environment.battlefieldCells.Where(c => IsWet(c)))
            {
                if (!cell.physicalSurfaceId.StartsWith("flat_"))
                    throw new InvalidOperationException("D3.4 sheets require the frozen flat water/shore cells.");
                string tileName = $"Tile_{cell.q}_{cell.r}_L{cell.heightLevel}";
                Transform old = root.transform.Find(tileName);
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
                bool isWater = cell.surfaceKind=="shallow_water";
                GameObject tile = Place(surfaces.transform,AssetRoot+(isWater ? "/ShallowWaterTile.prefab" : "/MudBankTile.prefab"),
                    $"{(isWater ? "Water" : "Mud")}_{cell.q}_{cell.r}",Position(cell));
                // A shore is a thin colour band inside the water hex, never a raised obstacle.
                if (isWater)
                for (int d=0;d<6;d++)
                {
                    int q=cell.q+Directions[d,0],r=cell.r+Directions[d,1];
                    bool adjoiningWater=environment.battlefieldCells.Any(c => c.q==q && c.r==r &&
                        c.surfaceKind=="shallow_water" && c.heightLevel==cell.heightLevel);
                    if (adjoiningWater) continue;
                    GameObject edge=Place(tile.transform,AssetRoot+"/ShoreEdge.prefab","Shore_"+d,new Vector3(0,.005f,0));
                    edge.transform.localRotation=Quaternion.Euler(0,d*60,0);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(edge.transform);
                }
            }
            // Expose all dry landings, the short-stair connection and the alternative ramp's approach.
            string[] dry = { "4:-1","5:-1","6:-1","6:0","7:0","8:0","9:0","4:6" };
            foreach (var cell in environment.battlefieldCells.Where(c => dry.Contains($"{c.q}:{c.r}") && !IsWet(c)))
            {
                string name=$"Tile_{cell.q}_{cell.r}_L{cell.heightLevel}";
                if (root.transform.Find(name)==null)
                    Place(root.transform,GuanzhongRampSampleBuilder.LandingPath,name,Position(cell)-Vector3.up*.000433121f);
            }
            EnvironmentBattlefieldCell rockCell=environment.battlefieldCells.Single(c => c.q==4 && c.r==3);
            PlaceRock(root.transform,Position(rockCell));
            Camera camera=Camera.main;
            camera.orthographicSize=4.5f;
            camera.transform.rotation=Quaternion.Euler(47,-20,0);
            camera.transform.position=new Vector3(6,.6f,3.4f)-camera.transform.forward*25;
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),ScenePath);
            EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            Debug.Log("D34_SAMPLE_BUILT "+ScenePath);
        }

        private static bool IsWet(EnvironmentBattlefieldCell cell) => cell.surfaceKind=="shallow_water" || cell.surfaceKind=="mud_bank";
        private static Vector3 Position(EnvironmentBattlefieldCell cell) => new Vector3(cell.q+.5f*cell.r,SupportY+.75f*cell.heightLevel,1+Mathf.Sqrt(3)*.5f*cell.r);

        private static Material Ground(string name,Color color,float gravel,float smoothness)
        {
            Material material=MaterialAt(name,SceneBuildSupport.RequireAsset<Shader>(GuanzhongRampSampleBuilder.AssetRoot+"/RampGround.shader"));
            material.SetColor("_BaseColor",color);
            material.SetFloat("_GravelDensity",gravel);
            material.SetFloat("_Smoothness",smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material MaterialAt(string name,Shader shader)
        {
            string path=AssetRoot+"/"+name+".mat";
            Material material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material==null) { material=new Material(shader); AssetDatabase.CreateAsset(material,path); }
            material.shader=shader;
            return material;
        }

        private static void BuildSheet(string name,Material material)
        {
            string path=AssetRoot+"/"+name+".fbx";
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            importer.isReadable=true;
            importer.importAnimation=false;
            importer.importCameras=false;
            importer.importLights=false;
            importer.addCollider=false;
            importer.SaveAndReimport();
            var obj=(GameObject)PrefabUtility.InstantiatePrefab(SceneBuildSupport.RequireAsset<GameObject>(path));
            obj.name=name;
            foreach (Renderer renderer in obj.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterial=material;
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            PrefabUtility.SaveAsPrefabAsset(obj,AssetRoot+"/"+name+".prefab");
            UnityEngine.Object.DestroyImmediate(obj);
        }

        private static void BuildTile(string name,Material top,bool water)
        {
            var obj=new GameObject(name);
            GameObject baseTile=Place(obj.transform,GuanzhongRampSampleBuilder.LandingPath,"UniqueSupport",new Vector3(0,-.000433121f,0));
            Renderer cap=baseTile.GetComponentsInChildren<Renderer>().Single(r => r.name=="LandingExactCap");
            Material[] materials=cap.sharedMaterials;
            materials[0]=top;
            cap.sharedMaterials=materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(cap);
            if (water) Place(obj.transform,AssetRoot+"/WaterHex.prefab","WaterSurface",new Vector3(0,.004f,0));
            PrefabUtility.SaveAsPrefabAsset(obj,AssetRoot+"/"+name+".prefab");
            UnityEngine.Object.DestroyImmediate(obj);
        }

        private static GameObject Place(Transform parent,string path,string name,Vector3 position)
        {
            var obj=(GameObject)PrefabUtility.InstantiatePrefab(SceneBuildSupport.RequireAsset<GameObject>(path));
            obj.name=name;
            obj.transform.SetParent(parent,false);
            obj.transform.localPosition=position;
            PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
            PrefabUtility.RecordPrefabInstancePropertyModifications(obj.transform);
            return obj;
        }

        private static void PlaceRock(Transform parent,Vector3 support)
        {
            GameObject rock=Place(parent,GuanzhongTerrainAssetBuilder.AssetRoot+"/RockCluster.prefab","RockDecoration_4_3",Vector3.zero);
            Bounds bounds=rock.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a,b) => { a.Encapsulate(b); return a; });
            rock.transform.localScale*=.64f/Mathf.Max(bounds.size.x,bounds.size.z);
            bounds=rock.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a,b) => { a.Encapsulate(b); return a; });
            rock.transform.position=support-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
            PrefabUtility.RecordPrefabInstancePropertyModifications(rock.transform);
        }
    }
}
