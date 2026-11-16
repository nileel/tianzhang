using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TianZhang.Art.Jindan.Space.Samples;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Entity;
using TianZhang.Features.CombatPresentation;
using TianZhang.Infrastructure.UnityContent;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TianZhang.Art.Jindan.Space.Editor
{
    public static class SpaceAssetBuilder
    {
        public const string Root="Assets/Art/VFX/Guanzhong/Jindan/Space";
        private const string Source="Assets/Art/Environments/Guanzhong/Props";
        public static string Evidence=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../assets/generated-scene-art/guanzhong-jindan-demo/d12-ability-assets/space/evidence/redesign-20261117"));
        [MenuItem("TianZhang/Art/D12.4/Rebuild Space resources and sample")]
        public static void Build()
        {
            foreach(string folder in new[]{"Materials","Meshes","Prefabs","Samples"})Directory.CreateDirectory(Root+"/"+folder);
            Directory.CreateDirectory(Evidence);AssetDatabase.Refresh();
            Mesh mesh=BandMesh(),wave=EnergyMesh(false),membrane=EnergyMesh(true);
            Band("ScaleCompressed",0,new Color(.30f,.88f,.88f,.9f),mesh,wave);
            Band("ScaleExpanded",1,new Color(.96f,.77f,.34f,.9f),mesh,wave);
            Band("SealOuter",2,new Color(.73f,.46f,1f,.9f),mesh,membrane);
            Band("SealInner",3,new Color(1f,.73f,.37f,.9f),mesh,membrane);
            Support("ScaleSupport",new Color(.35f,.78f,.69f),true,false);
            Support("ScaleSupportDamaged",new Color(.35f,.78f,.69f),false,false);
            Support("SealSupport",new Color(.69f,.45f,.85f),true,false);
            Support("SealSupportOuterBroken",new Color(.69f,.45f,.85f),false,false);
            Support("SealFailed",new Color(.69f,.45f,.85f),false,true);
            BuildScene();AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(Root+"/Samples/SpaceAssetSample.unity");
            var sample=UnityEngine.Object.FindFirstObjectByType<SpaceAssetSample>();
            if(!sample||!sample.compressed||!sample.sealOuter||!sample.catalog||!sample.scaleSupport)
                throw new InvalidOperationException("space_sample_persistence_failed");
            foreach(var prefab in new[]{sample.compressed,sample.expanded,sample.sealOuter,sample.sealInner})
                if(prefab.GetComponentsInChildren<Renderer>().Length!=2)throw new InvalidOperationException("space_energy_film_persistence_failed: "+prefab.name);
            Debug.Log("D12_SPACE_BUILD_OK");
        }
        private static Mesh BandMesh()
        {
            // Flat overlay only, no solid geometry, collision, or ground displacement.
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            const int segments=24;
            for(int i=0;i<=segments;i++)
            {float x=i/(float)segments;vertices.Add(new Vector3(x,0,-.22f));vertices.Add(new Vector3(x,0,.22f));uv.Add(new Vector2(x,0));uv.Add(new Vector2(x,1));}
            for(int i=0;i<segments;i++){int k=i*2;triangles.AddRange(new[]{k,k+1,k+2,k+2,k+1,k+3});}
            var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            return Save(mesh,"Meshes/SpaceBand.asset");
        }
        private static Mesh EnergyMesh(bool seal)
        {
            // Transparent VFX carriers only: no new solid model or collision.
            // A slanted film keeps a boundary visible from the fixed gameplay camera.
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            const int columns=1,rows=1;
            for(int y=0;y<=rows;y++)for(int x=0;x<=columns;x++)
            {
                float u=x/(float)columns,v=y/(float)rows;
                vertices.Add(seal?new Vector3((u-.5f)*.62f,v*1.1f+.035f,(u-.5f)*.74f):new Vector3(u,v*.9f+.035f,-.06f));
                uv.Add(new Vector2(u,v));
            }
            for(int y=0;y<rows;y++)for(int x=0;x<columns;x++)
            {int k=y*(columns+1)+x;triangles.AddRange(new[]{k,k+columns+1,k+1,k+1,k+columns+1,k+columns+2});}
            var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            return Save(mesh,"Meshes/"+(seal?"SealEnergyFilm":"ScaleSpaceWave")+".asset");
        }
        private static void Band(string name,int pattern,Color color,Mesh mesh,Mesh energyMesh)
        {
            var material=new Material(Shader.Find("TianZhang/Jindan/Space Band"));
            material.SetColor("_BaseColor",color);material.SetFloat("_Pattern",pattern);material.SetFloat("_Shimmer",1);material.SetFloat("_Opacity",1);
            material.SetFloat("_Layer",0);material.SetFloat("_EventTime",-100);
            material=Save(material,"Materials/"+name+".mat");
            var go=new GameObject(name);go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            var filmMaterial=new Material(material);filmMaterial.SetFloat("_Layer",1);
            filmMaterial=Save(filmMaterial,"Materials/"+name+"Energy.mat");
            var film=new GameObject(pattern<2?"DirectionalSpaceWave":"EnergyMembrane");film.transform.SetParent(go.transform,false);
            if(pattern>=2)film.transform.localPosition=new Vector3(pattern==2?.42f:.66f,0,pattern==2?-.07f:.07f);
            film.AddComponent<MeshFilter>().sharedMesh=energyMesh;
            var filmRenderer=film.AddComponent<MeshRenderer>();filmRenderer.sharedMaterial=filmMaterial;
            filmRenderer.shadowCastingMode=ShadowCastingMode.Off;filmRenderer.receiveShadows=false;
            SavePrefab(go);
        }
        private static void Support(string name,Color tint,bool glow,bool failed)
        {
            var go=(GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(Source+"/SupportStele.prefab"));
            PrefabUtility.UnpackPrefabInstance(go,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);go.name=name;
            var material=new Material(Load<Material>(Source+"/SealGlow.mat"));
            material.SetColor("_BaseColor",tint);if(material.HasProperty("_EmissionColor"))material.SetColor("_EmissionColor",tint*.25f);
            material=Save(material,"Materials/"+name+"Glow.mat");
            foreach(var renderer in go.transform.Find("Intact/Glow").GetComponentsInChildren<Renderer>(true))renderer.sharedMaterial=material;
            go.transform.Find("Intact").gameObject.SetActive(!failed);go.transform.Find("Failed").gameObject.SetActive(failed);
            go.transform.Find("Intact/Glow").gameObject.SetActive(glow&&!failed);SavePrefab(go);
        }
        private static void BuildScene()
        {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/AdventureScene.unity");
            var terrain=scene.GetRootGameObjects().Single(go=>go.name=="GuanzhongTerrainRehearsal");
            var camera=scene.GetRootGameObjects().Single(go=>go.GetComponent<Camera>());
            foreach(var root in scene.GetRootGameObjects())if(root!=terrain&&root!=camera&&!root.GetComponent<Light>())UnityEngine.Object.DestroyImmediate(root);
            foreach(var component in scene.GetRootGameObjects().SelectMany(go=>go.GetComponentsInChildren<MonoBehaviour>(true)).ToArray())
                if(!(component is CombatBattlefieldSurface)&&!(component.GetType().Namespace??"").StartsWith("UnityEngine.Rendering"))UnityEngine.Object.DestroyImmediate(component);
            terrain.SetActive(true);
            foreach(var t in terrain.GetComponentsInChildren<Transform>(true))if(t.name=="space_lock_support")t.gameObject.SetActive(false);
            var go=new GameObject("D12.4 Space Resource Sample");var sample=go.AddComponent<SpaceAssetSample>();
            sample.surface=terrain.GetComponent<CombatBattlefieldSurface>();sample.sampleCamera=camera.GetComponent<Camera>();
            sample.provider=go.AddComponent<Static3DCombatUnitPresentationProvider>();
            var provider=new SerializedObject(sample.provider);
            provider.FindProperty("profileSet").objectReferenceValue=Load<Static3DCombatUnitPresentationProfileSet>("Assets/Data/CombatPresentationProfiles/Static3DCombatUnitPresentationProfileSet.asset");
            provider.FindProperty("attackVfxCatalog").objectReferenceValue=Load<CombatAttackVfxCatalogData>("Assets/Data/CombatAttackVfx/CombatAttackVfxCatalog.asset");
            provider.ApplyModifiedPropertiesWithoutUndo();
            sample.compressed=Prefab("ScaleCompressed");sample.expanded=Prefab("ScaleExpanded");sample.scaleSupport=Prefab("ScaleSupport");
            sample.sealSupport=Prefab("SealSupport");sample.sealOuter=Prefab("SealOuter");sample.sealInner=Prefab("SealInner");
            sample.catalog=Load<JindanDemoCatalogData>("Assets/Data/JindanDemo/JindanDemoCatalog.asset");
            sample.rules=Load<CombatRuleSetData>("Assets/Data/CombatRuleSet.asset");
            sample.environment=Load<EnvironmentProfileAsset>("Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_jindan_demo.asset");
            sample.attacks=AssetDatabase.FindAssets("t:AttackProfileData",new[]{"Assets/Data/AttackProfiles"}).Select(id=>Load<AttackProfileData>(AssetDatabase.GUIDToAssetPath(id))).ToArray();
            EditorSceneManager.SaveScene(scene,Root+"/Samples/SpaceAssetSample.unity");
        }
        private static GameObject Prefab(string name)=>Load<GameObject>(Root+"/Prefabs/"+name+".prefab");
        private static T Load<T>(string path)where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(path)??throw new InvalidOperationException(path);
        private static T Save<T>(T value,string relative)where T:UnityEngine.Object
        {
            string path=Root+"/"+relative;value.name=Path.GetFileNameWithoutExtension(relative);var existing=AssetDatabase.LoadAssetAtPath<T>(path);
            if(existing==null){AssetDatabase.CreateAsset(value,path);return value;}
            EditorUtility.CopySerialized(value,existing);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(existing);return existing;
        }
        private static void SavePrefab(GameObject go)
        {if(!PrefabUtility.SaveAsPrefabAsset(go,Root+"/Prefabs/"+go.name+".prefab"))throw new InvalidOperationException(go.name);UnityEngine.Object.DestroyImmediate(go);}
    }
}
