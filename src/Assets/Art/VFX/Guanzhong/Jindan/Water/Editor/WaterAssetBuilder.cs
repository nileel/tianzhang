using System;
using System.IO;
using System.Linq;
using TianZhang.Art.Jindan.Water.Samples;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Entity;
using TianZhang.Features.CombatPresentation;
using TianZhang.Infrastructure.UnityContent;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TianZhang.Art.Jindan.Water.Editor
{
    public static class WaterAssetBuilder
    {
        public const string Root = "Assets/Art/VFX/Guanzhong/Jindan/Water";
        public const string Source = "Assets/Art/Environments/Guanzhong";
        private const string Earth = "Assets/Art/VFX/Guanzhong/Jindan/Earth";
        public static string Evidence => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../assets/generated-scene-art/guanzhong-jindan-demo/d12-ability-assets/water/evidence"));

        [MenuItem("TianZhang/Art/D12.2/Rebuild Water resources and sample")]
        public static void Build()
        {
            foreach (string folder in new[] { "Materials", "Animations", "Prefabs", "Samples", "Meshes" }) Directory.CreateDirectory(Root + "/" + folder);
            Directory.CreateDirectory(Evidence);
            AssetDatabase.Refresh();
            foreach (string name in new[] { "foam_detail_tiling", "puddle_norm" })
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(Root + "/Textures/" + name + ".png");
                importer.textureType = name == "puddle_norm" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = false; importer.wrapMode = TextureWrapMode.Repeat; importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            var wet = Surface("WetMud", 0, new Color(.19f, .215f, .16f), "WetShore");
            var shallow = Surface("ShallowWater", 1, new Color(.10f, .48f, .53f), "Water");
            var deep = Surface("DeepWater", 2, new Color(.035f, .24f, .39f), "Water");
            foreach (var material in new[] { wet, shallow, deep }) SaveSheet(material, material.name);
            var dry = InstantiateSheet(); dry.name = "WetToDry";
            var dryMaterial = new Material(wet); dryMaterial.SetColor("_MudColor", Load<Material>(Source + "/Waterbank/MudBank.mat").GetColor("_BaseColor"));
            dryMaterial = Save(dryMaterial, "Materials/RecedingMud.mat");
            dry.GetComponentInChildren<Renderer>().sharedMaterial = dryMaterial;
            Animate(dry, "WetToDry", "_Opacity", 1, 0, .4f);
            SavePrefab(dry);
            Front("TideRise", Vector2.up); Front("TideEbb", Vector2.down);
            BuildSteam(); BuildPlant(); BuildScene();
            AssetDatabase.SaveAssets();
            // Reopen the saved sample so references, not transient builder instances, are the deliverable.
            EditorSceneManager.OpenScene(Root + "/Samples/WaterAssetSample.unity");
            var sample = UnityEngine.Object.FindFirstObjectByType<WaterAssetSample>();
            if (sample == null || sample.wet == null || sample.steam == null || sample.catalog == null) throw new InvalidOperationException("water_sample_persistence_failed");
            Debug.Log("D12_WATER_BUILD_OK");
        }

        private static Material Surface(string name, float depth, Color tint, string source)
        {
            var material = new Material(Load<Material>(Source + "/Waterbank/" + source + ".mat")) { shader = Shader.Find("TianZhang/Jindan/Water Surface") };
            material.SetTexture("_FoamMap", Load<Texture2D>(Root + "/Textures/foam_detail_tiling.png"));
            material.SetTexture("_NormalMap", Load<Texture2D>(Root + "/Textures/puddle_norm.png"));
            material.SetColor("_BaseColor", tint);
            material.SetColor("_MudColor", Load<Material>(Source + "/Waterbank/WetShore.mat").GetColor("_BaseColor"));
            material.SetFloat("_Depth", depth); material.SetFloat("_Opacity", 1); material.SetVector("_Flow", new Vector4(0, 1, .12f, 0));
            return Save(material, "Materials/" + name + ".mat");
        }

        private static GameObject InstantiateSheet()
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(Source + "/Waterbank/WaterHex.prefab"));
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            return go;
        }
        private static void SaveSheet(Material material, string name)
        {
            var go = InstantiateSheet(); go.name = name;
            go.GetComponentInChildren<Renderer>().sharedMaterial = material; SavePrefab(go);
        }
        private static void Front(string name, Vector2 flow)
        {
            var material = new Material(Shader.Find("TianZhang/Jindan/Tide Front"));
            material.SetTexture("_FoamMap", Load<Texture2D>(Root + "/Textures/foam_detail_tiling.png"));
            material.SetVector("_Flow", new Vector4(flow.x, flow.y, 0, 0));
            material = Save(material, "Materials/" + name + ".mat");
            var go = InstantiateSheet(); go.name = name;
            go.GetComponentInChildren<Renderer>().sharedMaterial = material;
            var filter = go.GetComponentInChildren<MeshFilter>();
            filter.sharedMesh = Save(SubdivideFront(filter.sharedMesh), "Meshes/TideCurlSheet.asset");
            var spray = new GameObject("CrestSpray"); spray.transform.SetParent(go.transform, false);
            spray.transform.localPosition = Vector3.up * .17f;
            spray.transform.localRotation = Quaternion.Euler(-65, 0, 0);
            var ps = spray.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = false; main.duration = .65f; main.startDelay = .12f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.24f, .40f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.55f, .9f);
            main.startSize = new ParticleSystem.MinMaxCurve(.026f, .060f); main.gravityModifier = .65f;
            main.maxParticles = 14; main.startColor = Color.white;
            var emission = ps.emission; emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)12) });
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 24; shape.radius = .18f;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
            var pr = spray.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = Save(new Material(Shader.Find("TianZhang/Jindan/Water Spray")), "Materials/WaterSpray.mat");
            pr.shadowCastingMode = ShadowCastingMode.Off; pr.receiveShadows = false;
            ps.useAutoRandomSeed = false; ps.randomSeed = 1731;
            Animate(go, name, "_Progress", 0, 1, .65f); SavePrefab(go);
        }
        private static Mesh SubdivideFront(Mesh source)
        {
            // Subdivide the authorized water footprint; this is a transient VFX sheet, not a new terrain model.
            const int n = 28;
            var vertices = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();
            var p = source.vertices; var indices = source.triangles;
            void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                foreach (var v in new[] { a, b, c }) { triangles.Add(vertices.Count); vertices.Add(v); }
            }
            for (int t = 0; t < indices.Length; t += 3)
            {
                Vector3 a = p[indices[t]], b = p[indices[t + 1]], c = p[indices[t + 2]];
                Vector3 Point(int i, int j) => a + (b - a) * (i / (float)n) + (c - a) * (j / (float)n);
                for (int i = 0; i < n; i++) for (int j = 0; j < n - i; j++)
                {
                    Triangle(Point(i, j), Point(i + 1, j), Point(i, j + 1));
                    if (i + j < n - 1) Triangle(Point(i + 1, j), Point(i + 1, j + 1), Point(i, j + 1));
                }
            }
            var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            // Include the shader's 0.30m crest in the culling bounds.
            var bounds = mesh.bounds; bounds.Expand(new Vector3(.3f, .6f, .3f)); mesh.bounds = bounds;
            return mesh;
        }
        private static void Animate(GameObject go, string name, string property, float from, float to, float duration)
        {
            var clip = new AnimationClip { legacy = true, frameRate = 60, wrapMode = WrapMode.ClampForever };
            foreach (var renderer in go.GetComponentsInChildren<MeshRenderer>(true))
                clip.SetCurve(AnimationUtility.CalculateTransformPath(renderer.transform, go.transform), typeof(MeshRenderer),
                    "material." + property, AnimationCurve.Linear(0, from, duration, to));
            clip = Save(clip, "Animations/" + name + ".anim");
            var animation = go.AddComponent<Animation>(); animation.AddClip(clip, name); animation.clip = clip;
            animation.playAutomatically = true; animation.cullingType = AnimationCullingType.AlwaysAnimate;
        }
        private static void BuildSteam()
        {
            var material = new Material(Load<Material>(Earth + "/Materials/SoilDust.mat"));
            material = Save(material, "Materials/ExtinguishSteam.mat");
            var go = new GameObject("ExtinguishSteam");
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = false; main.duration = .4f; main.startLifetime = new ParticleSystem.MinMaxCurve(.3f, .55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.22f, .4f); main.startSize = new ParticleSystem.MinMaxCurve(.14f, .24f);
            main.startColor = new Color(.73f, .85f, .85f, .48f); main.maxParticles = 10;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var emission = ps.emission; emission.rateOverTime = 0; emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)7) });
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(.54f, .54f, .02f); shape.rotation = new Vector3(-90, 0, 0);
            var colour = ps.colorOverLifetime; colour.enabled = true;
            var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .12f), new GradientAlphaKey(0, 1) }); colour.color = gradient;
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .6f, 1, 1.6f));
            var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            ps.useAutoRandomSeed = false; ps.randomSeed = 1729;
            SavePrefab(go);
        }
        private static void BuildPlant()
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(Source + "/Props/LowPlant.prefab"));
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "LowPlantErode"; UnityEngine.Object.DestroyImmediate(go.transform.Find("Charred").gameObject);
            int index = 0;
            foreach (var renderer in go.GetComponentsInChildren<MeshRenderer>())
            {
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                renderer.sharedMaterials = renderer.sharedMaterials.Select(source =>
                {
                    var material = new Material(source) { shader = Shader.Find("TianZhang/Jindan/Earth Manifest") };
                    material.SetFloat("_Progress", 1); material.SetFloat("_MinY", mesh.bounds.min.y);
                    material.SetFloat("_Height", mesh.bounds.size.y); material.SetFloat("_EarthGrain", 0);
                    return Save(material, "Materials/LowPlantErode" + index++ + ".mat");
                }).ToArray();
            }
            Animate(go, go.name, "_Progress", 1, 0, .28f); SavePrefab(go);
        }
        private static void BuildScene()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/AdventureScene.unity");
            var terrain = scene.GetRootGameObjects().Single(go => go.name == "GuanzhongTerrainRehearsal");
            var camera = scene.GetRootGameObjects().Single(go => go.GetComponent<Camera>());
            var oldSurface = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CombatSurfaceView>(true)).Single();
            var originals = new SerializedObject(oldSurface).FindProperty("staticWaterOverlays");
            var staticOverlays = Enumerable.Range(0, originals.arraySize)
                .Select(i => (GameObject)originals.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
            foreach (var overlay in staticOverlays) overlay.SetActive(false);
            foreach (var root in scene.GetRootGameObjects())
                if (root != terrain && root != camera && !root.GetComponent<Light>()) UnityEngine.Object.DestroyImmediate(root);
            foreach (var component in scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<MonoBehaviour>(true)).ToArray())
                if (!(component is CombatBattlefieldSurface) && !component.GetType().Namespace.StartsWith("UnityEngine.Rendering")) UnityEngine.Object.DestroyImmediate(component);
            terrain.SetActive(true);
            // Only the sample plant is replaced; the source scene and prefab stay intact.
            foreach (var t in terrain.GetComponentsInChildren<Transform>(true)) if (t.name == "LowPlant_4_6") t.gameObject.SetActive(false);
            var go = new GameObject("D12.2 Water Resource Sample"); var sample = go.AddComponent<WaterAssetSample>();
            sample.surface = terrain.GetComponent<CombatBattlefieldSurface>(); sample.sampleCamera = camera.GetComponent<Camera>();
            sample.staticWaterOverlays = staticOverlays;
            sample.provider = go.AddComponent<Static3DCombatUnitPresentationProvider>();
            var provider = new SerializedObject(sample.provider);
            provider.FindProperty("profileSet").objectReferenceValue = Load<Static3DCombatUnitPresentationProfileSet>("Assets/Data/CombatPresentationProfiles/Static3DCombatUnitPresentationProfileSet.asset");
            provider.ApplyModifiedPropertiesWithoutUndo();
            sample.wet = LoadPrefab("WetMud"); sample.shallow = LoadPrefab("ShallowWater"); sample.deep = LoadPrefab("DeepWater");
            sample.dry = LoadPrefab("WetToDry"); sample.rise = LoadPrefab("TideRise"); sample.ebb = LoadPrefab("TideEbb");
            sample.steam = LoadPrefab("ExtinguishSteam"); sample.plantErode = LoadPrefab("LowPlantErode");
            sample.plantSource = Load<GameObject>(Source + "/Props/LowPlant.prefab");
            sample.burningMaterial = new Material(Load<Material>(Source + "/Waterbank/MudBank.mat"));
            sample.burningMaterial.SetColor("_BaseColor", new Color(.92f, .27f, .055f));
            sample.burningMaterial = Save(sample.burningMaterial, "Samples/ExistingBurningReference.mat");
            sample.catalog = Load<JindanDemoCatalogData>("Assets/Data/JindanDemo/JindanDemoCatalog.asset");
            sample.rules = Load<CombatRuleSetData>("Assets/Data/CombatRuleSet.asset");
            sample.environment = Load<EnvironmentProfileAsset>("Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_jindan_demo.asset");
            sample.attacks = AssetDatabase.FindAssets("t:AttackProfileData", new[] { "Assets/Data/AttackProfiles" })
                .Select(id => Load<AttackProfileData>(AssetDatabase.GUIDToAssetPath(id))).ToArray();
            EditorSceneManager.SaveScene(scene, Root + "/Samples/WaterAssetSample.unity");
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
