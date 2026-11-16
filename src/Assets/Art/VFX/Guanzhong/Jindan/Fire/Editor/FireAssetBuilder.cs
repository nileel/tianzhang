using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TianZhang.Art.Jindan.Fire.Samples;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Entity;
using TianZhang.Features.CombatPresentation;
using TianZhang.Infrastructure.UnityContent;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TianZhang.Art.Jindan.Fire.Editor
{
    public static class FireAssetBuilder
    {
        public const string Root = "Assets/Art/VFX/Guanzhong/Jindan/Fire";
        public const string Source = "Assets/Art/Environments/Guanzhong";
        private const string Water = "Assets/Art/VFX/Guanzhong/Jindan/Water";
        public static string Evidence => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../assets/generated-scene-art/guanzhong-jindan-demo/d12-ability-assets/fire/evidence"));

        [MenuItem("TianZhang/Art/D12.3/Rebuild Fire resources and sample")]
        public static void Build()
        {
            foreach (string folder in new[] { "Materials", "Meshes", "Prefabs", "Samples" }) Directory.CreateDirectory(Root + "/" + folder);
            Directory.CreateDirectory(Evidence); AssetDatabase.Refresh();
            var flame = Save(new Material(Shader.Find("TianZhang/Jindan/Low Fire")), "Materials/LowFire.mat");
            var warm = new Material(Shader.Find("TianZhang/Jindan/Warm Trace"));
            warm.SetColor("_BaseColor", new Color(.84f, .43f, .15f, .28f));
            warm = Save(warm, "Materials/WholeBoardWarmth.mat");
            var center = new Material(warm); center.SetColor("_BaseColor", new Color(.95f, .48f, .14f, .68f));
            center = Save(center, "Materials/CenterWarmRune.mat");
            var ember = new Material(Load<Material>("Assets/Art/VFX/Guanzhong/Jindan/Earth/Materials/SoilDust.mat"));
            ember.SetColor("_BaseColor", new Color(1, .43f, .065f, .8f)); ember = Save(ember, "Materials/FiniteEmbers.mat");
            BuildLowFire(flame, ember); BuildEdge(warm); BuildCenter(center); BuildScorched();
            BuildScene(); AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(Root + "/Samples/FireAssetSample.unity");
            var sample = UnityEngine.Object.FindFirstObjectByType<FireAssetSample>();
            if (sample == null || sample.lowFire == null || sample.center == null || sample.steam == null || sample.catalog == null)
                throw new InvalidOperationException("fire_sample_persistence_failed");
            Debug.Log("D12_FIRE_BUILD_OK");
        }
        private static void BuildLowFire(Material flame, Material ember)
        {
            var mesh = new Mesh { name = "SparseLowFlameCards" };
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            var centers = new[] { new Vector3(-.21f, 0, -.07f), new Vector3(.15f, 0, .13f), new Vector3(.04f, 0, -.22f) };
            for (int k = 0; k < centers.Length; k++) for (int side = 0; side < 2; side++)
            {
                float angle = (k * 37 + side * 90) * Mathf.Deg2Rad;
                var width = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * .12f;
                float height = .27f + k * .035f; int start = vertices.Count;
                vertices.AddRange(new[] { centers[k] - width, centers[k] + width, centers[k] + width + Vector3.up * height, centers[k] - width + Vector3.up * height });
                uv.AddRange(new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds(); mesh.RecalculateNormals();
            mesh = Save(mesh, "Meshes/SparseLowFlameCards.asset");
            var go = new GameObject("LowFire");
            var tongues = new GameObject("LowTongues"); tongues.transform.SetParent(go.transform, false);
            tongues.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = tongues.AddComponent<MeshRenderer>(); renderer.sharedMaterial = flame;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            Embers(go.transform, ember, true); SavePrefab(go);
            var tail = new GameObject("FiniteEmbers"); Embers(tail.transform, ember, false); SavePrefab(tail);
        }
        private static void Embers(Transform root, Material material, bool loop)
        {
            var go = new GameObject("SparseEmbers"); go.transform.SetParent(root, false); go.transform.localPosition = Vector3.up * .03f;
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = loop; main.duration = .5f; main.startLifetime = new ParticleSystem.MinMaxCurve(.35f, .65f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.22f, .4f); main.startSize = new ParticleSystem.MinMaxCurve(.018f, .035f);
            main.startColor = Color.white; main.maxParticles = 6; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = ps.emission; emission.rateOverTime = loop ? 3 : 0;
            if (!loop) emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)4) });
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(.42f, .3f, .01f); shape.rotation = new Vector3(-90, 0, 0);
            var colour = ps.colorOverLifetime; colour.enabled = true;
            var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .12f), new GradientAlphaKey(0, 1) }); colour.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            ps.useAutoRandomSeed = false; ps.randomSeed = 1731;
        }
        private static void BuildEdge(Material material)
        {
            var mesh = new Mesh(); var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int i = 0; i < 6; i++)
            {
                float a = (30 + i * 60) * Mathf.Deg2Rad, b = (90 + i * 60) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); var q = new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b));
                int s = vertices.Count;
                vertices.AddRange(new[] { p * .568f, q * .568f, q * .548f, p * .548f });
                triangles.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            mesh = Save(mesh, "Meshes/HexWarmEdge.asset");
            var go = new GameObject("WholeBoardWarmEdge"); go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; SavePrefab(go);
        }
        private static void BuildCenter(Material material)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(Source + "/Props/SupportStele.prefab"));
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "FireCenter";
            foreach (var renderer in go.transform.Find("Intact/Glow").GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray();
            go.transform.Find("Intact/Glow").gameObject.SetActive(false); go.transform.Find("Failed").gameObject.SetActive(false); SavePrefab(go);
        }
        private static void BuildScorched()
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(Source + "/Waterbank/WaterHex.prefab"));
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction); go.name = "ScorchedGround";
            var material = new Material(Load<Material>(Source + "/Waterbank/MudBank.mat"));
            material.SetColor("_BaseColor", new Color(.115f, .105f, .09f)); material = Save(material, "Materials/ScorchedGround.mat");
            go.GetComponentInChildren<Renderer>().sharedMaterial = material; SavePrefab(go);
        }
        private static void BuildScene()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/AdventureScene.unity");
            var terrain = scene.GetRootGameObjects().Single(go => go.name == "GuanzhongTerrainRehearsal");
            var camera = scene.GetRootGameObjects().Single(go => go.GetComponent<Camera>());
            var oldSurface = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CombatSurfaceView>(true)).Single();
            var originals = new SerializedObject(oldSurface).FindProperty("staticWaterOverlays");
            var staticOverlays = Enumerable.Range(0, originals.arraySize).Select(i => (GameObject)originals.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
            foreach (var overlay in staticOverlays) overlay.SetActive(false);
            foreach (var root in scene.GetRootGameObjects()) if (root != terrain && root != camera && !root.GetComponent<Light>()) UnityEngine.Object.DestroyImmediate(root);
            foreach (var component in scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<MonoBehaviour>(true)).ToArray())
                if (!(component is CombatBattlefieldSurface) && !component.GetType().Namespace.StartsWith("UnityEngine.Rendering")) UnityEngine.Object.DestroyImmediate(component);
            terrain.SetActive(true);
            foreach (var t in terrain.GetComponentsInChildren<Transform>(true))
                if (t.name == "fire_domain_support") t.gameObject.SetActive(false);
            var go = new GameObject("D12.3 Fire Resource Sample"); var sample = go.AddComponent<FireAssetSample>();
            sample.surface = terrain.GetComponent<CombatBattlefieldSurface>(); sample.sampleCamera = camera.GetComponent<Camera>(); sample.staticWaterOverlays = staticOverlays;
            sample.provider = go.AddComponent<Static3DCombatUnitPresentationProvider>();
            var provider = new SerializedObject(sample.provider);
            provider.FindProperty("profileSet").objectReferenceValue = Load<Static3DCombatUnitPresentationProfileSet>("Assets/Data/CombatPresentationProfiles/Static3DCombatUnitPresentationProfileSet.asset");
            provider.ApplyModifiedPropertiesWithoutUndo();
            sample.lowFire = LoadPrefab("LowFire"); sample.embers = LoadPrefab("FiniteEmbers"); sample.center = LoadPrefab("FireCenter");
            sample.edge = LoadPrefab("WholeBoardWarmEdge"); sample.scorched = LoadPrefab("ScorchedGround");
            sample.wet = Load<GameObject>(Water + "/Prefabs/WetMud.prefab"); sample.shallow = Load<GameObject>(Water + "/Prefabs/ShallowWater.prefab");
            sample.deep = Load<GameObject>(Water + "/Prefabs/DeepWater.prefab"); sample.steam = Load<GameObject>(Water + "/Prefabs/ExtinguishSteam.prefab");
            sample.catalog = Load<JindanDemoCatalogData>("Assets/Data/JindanDemo/JindanDemoCatalog.asset");
            sample.rules = Load<CombatRuleSetData>("Assets/Data/CombatRuleSet.asset");
            sample.environment = Load<EnvironmentProfileAsset>("Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_jindan_demo.asset");
            sample.attacks = AssetDatabase.FindAssets("t:AttackProfileData", new[] { "Assets/Data/AttackProfiles" }).Select(id => Load<AttackProfileData>(AssetDatabase.GUIDToAssetPath(id))).ToArray();
            EditorSceneManager.SaveScene(scene, Root + "/Samples/FireAssetSample.unity");
        }
        private static GameObject LoadPrefab(string name) => Load<GameObject>(Root + "/Prefabs/" + name + ".prefab");
        private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException(path);
        private static T Save<T>(T value, string relative) where T : UnityEngine.Object
        {
            string path = Root + "/" + relative; value.name = Path.GetFileNameWithoutExtension(relative);
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null) { AssetDatabase.CreateAsset(value, path); return value; }
            EditorUtility.CopySerialized(value, existing); UnityEngine.Object.DestroyImmediate(value); EditorUtility.SetDirty(existing); return existing;
        }
        private static void SavePrefab(GameObject go)
        {
            if (!PrefabUtility.SaveAsPrefabAsset(go, Root + "/Prefabs/" + go.name + ".prefab")) throw new InvalidOperationException(go.name);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
