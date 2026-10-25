using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TianZhang.Editor
{
    /// <summary>D3.5 static prefab states and a comparison sample. No gameplay registration.</summary>
    public static class GuanzhongPropSampleBuilder
    {
        public const string AssetRoot=GuanzhongTerrainAssetBuilder.AssetRoot+"/Props";
        public const string ScenePath="Assets/Scenes/GuanzhongPropSample.unity";
        private static readonly string[] Models={ "LowWallIntact","LowWallDamaged","HighWallIntact","HighWallDamaged",
            "SharedRubble","SteleIntact","SteleGlow","SteleFailed","LowPlantLiving","LowPlantCharred" };

        [MenuItem("天章/美术/D3.5 构建墙碑低植状态样组")]
        public static void Build()
        {
            AssetDatabase.Refresh();
            var materials=new Dictionary<string,Material> {
                {"WeatheredStone",Stone("WeatheredStone",new Color(.66f,.69f,.63f))},
                {"StoneEdge",Stone("StoneEdge",new Color(.81f,.79f,.67f))},
                {"DarkStone",Stone("DarkStone",new Color(.32f,.37f,.34f))},
                {"SealMetal",Solid("SealMetal",new Color(.35f,.29f,.14f))},
                {"SealGlow",Solid("SealGlow",new Color(.44f,.72f,.67f))},
                {"Leaf",Solid("Leaf",new Color(.23f,.31f,.14f))},
                {"LeafTip",Solid("LeafTip",new Color(.40f,.44f,.22f))},
                {"DryStem",Solid("DryStem",new Color(.30f,.26f,.15f))},
                {"Charcoal",Solid("Charcoal",new Color(.095f,.085f,.07f))},
                {"Ash",Solid("Ash",new Color(.32f,.30f,.26f))}
            };
            materials["SealGlow"].EnableKeyword("_EMISSION");
            materials["SealGlow"].SetColor("_EmissionColor",new Color(.20f,.50f,.42f)*1.2f);
            EditorUtility.SetDirty(materials["SealGlow"]);
            foreach (string name in Models) BuildModel(name,materials);
            BuildStates("LowWall",new[]{"Intact","Damaged","Remnant"},new[]{"LowWallIntact","LowWallDamaged","SharedRubble"});
            BuildStates("HighWall",new[]{"Intact","Damaged","Remnant"},new[]{"HighWallIntact","HighWallDamaged","SharedRubble"});
            BuildStates("SupportStele",new[]{"Intact","Failed"},new[]{"SteleIntact","SteleFailed"});
            BuildStates("LowPlant",new[]{"Living","Charred"},new[]{"LowPlantLiving","LowPlantCharred"});
            BuildScene();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),ScenePath);
            EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            Debug.Log("D35_SAMPLE_BUILT "+ScenePath);
        }

        private static Material Stone(string name,Color tint)
        {
            Material material=MaterialAt(name,SceneBuildSupport.RequireAsset<Shader>(GuanzhongTerraceSampleBuilder.AssetRoot+"/TerraceLit.shader"));
            material.SetTexture("_BaseMap",SceneBuildSupport.RequireAsset<Texture2D>(GuanzhongTerrainAssetBuilder.AssetRoot+"/Stair_basecolor.JPEG"));
            material.SetColor("_BaseColor",tint);
            material.SetFloat("_Smoothness",.09f);
            EditorUtility.SetDirty(material);
            return material;
        }
        private static Material Solid(string name,Color color)
        {
            Material material=MaterialAt(name,Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor",color);
            material.SetFloat("_Smoothness",.12f);
            EditorUtility.SetDirty(material);
            return material;
        }
        private static Material MaterialAt(string name,Shader shader)
        {
            if (shader==null) throw new InvalidOperationException("Required URP shader is unavailable.");
            string path=AssetRoot+"/"+name+".mat";
            Material material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material==null) { material=new Material(shader); AssetDatabase.CreateAsset(material,path); }
            material.shader=shader;
            return material;
        }
        private static void BuildModel(string name,Dictionary<string,Material> materials)
        {
            string path=AssetRoot+"/"+name+".fbx";
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            importer.isReadable=true;
            importer.importAnimation=false;
            importer.importCameras=false;
            importer.importLights=false;
            importer.addCollider=false;
            importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation=ModelImporterMaterialLocation.InPrefab;
            importer.SaveAndReimport();
            var obj=(GameObject)PrefabUtility.InstantiatePrefab(SceneBuildSupport.RequireAsset<GameObject>(path));
            obj.name=name;
            foreach (Renderer renderer in obj.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterials=renderer.sharedMaterials.Select(m => materials[m.name]).ToArray();
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            PrefabUtility.SaveAsPrefabAsset(obj,AssetRoot+"/"+name+".prefab");
            UnityEngine.Object.DestroyImmediate(obj);
        }
        private static void BuildStates(string name,string[] states,string[] models)
        {
            var root=new GameObject(name);
            for (int i=0;i<states.Length;i++)
            {
                var state=new GameObject(states[i]);
                state.transform.SetParent(root.transform,false);
                Place(state.transform,AssetRoot+"/"+models[i]+".prefab","Body",Vector3.zero);
                if (name=="SupportStele" && i==0)
                    Place(state.transform,AssetRoot+"/SteleGlow.prefab","Glow",Vector3.zero);
                state.SetActive(i==0);
            }
            PrefabUtility.SaveAsPrefabAsset(root,AssetRoot+"/"+name+".prefab");
            UnityEngine.Object.DestroyImmediate(root);
        }
        private static void BuildScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("D35_StaticPropStates");
            var ground=new GameObject("ReusedGround");
            ground.transform.SetParent(root.transform,false);
            for (int r=0;r<8;r++)
            for (int column=-1;column<=5;column++)
            {
                int q=column-r/2;
                // Original Tile grass remains on the outer border; clear flat caps carry the sample paths.
                string path=column==-1 || column==5 ? GuanzhongTerraceSampleBuilder.TilePath : GuanzhongRampSampleBuilder.LandingPath;
                Place(ground.transform,path,$"Tile_{q}_{r}",new Vector3(q+.5f*r,.34f,1+Mathf.Sqrt(3)*.5f*r));
            }
            var props=new GameObject("IndependentProps");
            props.transform.SetParent(root.transform,false);
            AddSpecimen(props.transform,"LowWall","Intact",0,0);
            AddSpecimen(props.transform,"LowWall","Damaged",1,0);
            AddSpecimen(props.transform,"LowWall","Remnant",2,0);
            AddSpecimen(props.transform,"HighWall","Intact",0,1);
            AddSpecimen(props.transform,"HighWall","Damaged",1,1);
            AddSpecimen(props.transform,"HighWall","Remnant",2,1);
            AddSpecimen(props.transform,"SupportStele","Intact",0,2);
            GameObject unlit=AddSpecimen(props.transform,"SupportStele","Intact",1,2);
            unlit.name="SupportStele_Unlit";
            unlit.transform.Find("Intact/Glow").gameObject.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(unlit.transform.Find("Intact/Glow").gameObject);
            AddSpecimen(props.transform,"SupportStele","Failed",2,2);
            AddSpecimen(props.transform,"LowPlant","Living",0,3);
            AddSpecimen(props.transform,"LowPlant","Charred",1,3);
            var camera=new GameObject("Main Camera",typeof(Camera)).GetComponent<Camera>();
            camera.tag="MainCamera";
            camera.orthographic=true;
            camera.orthographicSize=4.4f;
            camera.nearClipPlane=.1f;
            camera.farClipPlane=70;
            camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.13f,.16f,.18f);
            camera.transform.rotation=Quaternion.Euler(53,-12,0);
            camera.transform.position=new Vector3(2.1f,.6f,4)-camera.transform.forward*25;
            var light=new GameObject("Warm Key",typeof(Light)).GetComponent<Light>();
            light.type=LightType.Directional;
            light.color=new Color(1,.91f,.78f);
            light.intensity=1.15f;
            light.shadows=LightShadows.Soft;
            light.transform.rotation=Quaternion.Euler(48,-32,0);
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.47f,.55f,.64f);
            RenderSettings.ambientEquatorColor=new Color(.31f,.36f,.40f);
            RenderSettings.ambientGroundColor=new Color(.18f,.17f,.15f);
        }
        private static GameObject AddSpecimen(Transform parent,string prefab,string state,int column,int row)
        {
            // Put the prop on the far shared edge. Row-centre travel strips remain clear.
            Vector3 center=new Vector3(column*2,.340433121f,1+Mathf.Sqrt(3)*row);
            GameObject obj=Place(parent,AssetRoot+"/"+prefab+".prefab",prefab+"_"+state,center+new Vector3(.25f,0,.4330127f));
            if (prefab.EndsWith("Wall")) obj.transform.localRotation=Quaternion.Euler(0,30,0);
            if (prefab=="LowWall") obj.transform.localScale=new Vector3(.93f,1,1);
            foreach (Transform child in obj.transform)
            {
                child.gameObject.SetActive(child.name==state);
                PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(obj.transform);
            return obj;
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
    }
}
