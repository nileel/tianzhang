using System;
using System.IO;
using System.Linq;
using TianZhang.Features.CombatPresentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using TianZhang.Art.Jindan.Earth.Samples;

namespace TianZhang.Art.Jindan.Earth.Editor
{
    public static class EarthAssetBuilder
    {
        public const string Root = "Assets/Art/VFX/Guanzhong/Jindan/Earth";
        public const string Source = "Assets/Art/Environments/Guanzhong";
        public static string Evidence => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../assets/generated-scene-art/guanzhong-jindan-demo/d12-ability-assets/earth/evidence"));

        [MenuItem("TianZhang/Art/D12.1/Rebuild Earth resources and sample")]
        public static void Build()
        {
            foreach (string folder in new[] { "Materials", "Animations", "Prefabs" })
                Directory.CreateDirectory(Root + "/" + folder);
            AssetDatabase.Refresh();
            foreach (string texture in new[] { "smoke_03", "trace_01" })
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(Root + "/Textures/" + texture + ".png");
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            var dust = ParticleMaterial("SoilDust", "smoke_03");
            var streak = ParticleMaterial("PressureTrace", "trace_01");
            BuildManifest(false, false, dust);
            BuildManifest(false, true, dust);
            BuildManifest(true, false, dust);
            BuildManifest(true, true, dust);
            BuildPressure(streak);
            var landing = new GameObject("LandingDust");
            Dust(landing.transform, dust, Vector3.up * .035f, new Vector3(.42f, .015f, .42f), 14);
            SavePrefab(landing, "LandingDust");
            BuildScene();
            AssetDatabase.SaveAssets();
            Debug.Log("D12_EARTH_BUILD_OK");
        }

        private static Material ParticleMaterial(string name, string texture)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/" + texture + ".png"));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_Cull", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3000;
            return SaveAsset(material, "Materials/" + name + ".mat");
        }

        private static void BuildManifest(bool ramp, bool dissolve, Material dust)
        {
            string name = (ramp ? "Ramp" : "Wall") + (dissolve ? "Dissolve" : "Form");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                Source + (ramp ? "/EarthManifest/EarthManifestRamp.prefab" : "/Props/HighWall.prefab")));
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = name;
            // Retain Intact and the exact mesh transform. A magical wall never exposes persistent rubble.
            if (!ramp) foreach (Transform child in root.transform.Cast<Transform>().ToArray())
                if (child.name != "Intact") UnityEngine.Object.DestroyImmediate(child.gameObject);
            var renderer = root.GetComponentInChildren<MeshRenderer>();
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            var materials = renderer.sharedMaterials.Select((source, index) =>
            {
                string materialPath = "Materials/" + (ramp ? "RampEarth" : "WallStone" + index) + ".mat";
                if (dissolve) return AssetDatabase.LoadAssetAtPath<Material>(Root + "/" + materialPath);
                var material = new Material(source) { shader = Shader.Find("TianZhang/Jindan/Earth Manifest") };
                material.SetFloat("_Progress", 1);
                material.SetFloat("_MinY", mesh.bounds.min.y);
                material.SetFloat("_Height", mesh.bounds.size.y);
                material.SetFloat("_EarthGrain", ramp ? 1 : 0);
                material.SetFloat("_GravelDensity", .15f);
                return SaveAsset(material, materialPath);
            }).ToArray();
            renderer.sharedMaterials = materials;
            var clip = new AnimationClip { legacy = true, frameRate = 60, wrapMode = WrapMode.ClampForever };
            string path = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);
            clip.SetCurve(path, typeof(MeshRenderer), "material._Progress",
                AnimationCurve.Linear(0, dissolve ? 1 : 0, .35f, dissolve ? 0 : 1));
            clip = SaveAsset(clip, "Animations/" + name + ".anim");
            var animation = root.AddComponent<Animation>();
            animation.AddClip(clip, name); animation.clip = clip; animation.playAutomatically = true;
            animation.cullingType = AnimationCullingType.AlwaysAnimate;
            Transform dustParent = ramp ? root.transform : root.transform.Find("Intact");
            if (ramp)
            {
                for (int i = 0; i < 4; i++)
                {
                    float z = .12f + i * .12f;
                    float y = .75f * (1 - Mathf.Cos(Mathf.PI * Mathf.Clamp01((z - .1f) / .4f))) * .5f;
                    Dust(dustParent, dust, new Vector3(0, y + .025f, z), new Vector3(.48f, .015f, .06f), 3,
                        dissolve ? .04f : .04f + i * .055f);
                }
            }
            else Dust(dustParent, dust, new Vector3(0, .09f, 0), new Vector3(.56f, .015f, .24f), 12);
            SavePrefab(root, name);
        }

        private static void Dust(Transform parent, Material material, Vector3 position, Vector3 box, short count, float delay = 0)
        {
            var go = new GameObject("ShortSoilDust"); go.transform.SetParent(parent, false); go.transform.localPosition = position;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = false; main.duration = .4f; main.startDelay = delay;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.22f, .38f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.09f, .22f);
            main.startSize = new ParticleSystem.MinMaxCurve(.12f, .22f);
            main.startColor = new Color(.66f, .55f, .37f, .55f); main.maxParticles = 20;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var emission = ps.emission; emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, count) });
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(box.x, box.z, box.y);
            shape.rotation = new Vector3(-90, 0, 0);
            var color = ps.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .15f), new GradientAlphaKey(0, 1) }); color.color = gradient;
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .65f, 1, 1.7f));
            var render = ps.GetComponent<ParticleSystemRenderer>(); render.sharedMaterial = material;
            render.shadowCastingMode = ShadowCastingMode.Off; render.receiveShadows = false;
            ps.useAutoRandomSeed = false; ps.randomSeed = 1729;
        }

        private static void BuildPressure(Material material)
        {
            var root = new GameObject("GravityPressure");
            // Four body-external, world-down emitters; centre and feet remain uncovered.
            for (int i = 0; i < 4; i++)
            {
                float angle = (45 + i * 90) * Mathf.Deg2Rad;
                var go = new GameObject("DownTrace" + i); go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(Mathf.Cos(angle) * .36f, 1.08f, Mathf.Sin(angle) * .36f);
                go.transform.localRotation = Quaternion.Euler(90, 0, 0);
                var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main; main.loop = false; main.duration = .36f; main.startLifetime = .14f;
                main.startSpeed = 2.8f; main.startSize3D = true; main.startSizeX = .22f; main.startSizeY = .48f; main.startSizeZ = 1;
                main.startColor = new Color(.95f, .8f, .5f, .85f);
                main.maxParticles = 4; main.simulationSpace = ParticleSystemSimulationSpace.World;
                var emission = ps.emission; emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)1), new ParticleSystem.Burst(.09f, (short)1),
                    new ParticleSystem.Burst(.18f, (short)1), new ParticleSystem.Burst(.27f, (short)1) });
                var shape = ps.shape; shape.enabled = false;
                var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
                renderer.renderMode = ParticleSystemRenderMode.VerticalBillboard;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                var color = ps.colorOverLifetime; color.enabled = true;
                var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .15f), new GradientAlphaKey(0, 1) }); color.color = gradient;
                ps.useAutoRandomSeed = false; ps.randomSeed = (uint)(1729 + i);
            }
            SavePrefab(root, "GravityPressure");
        }

        private static void BuildScene()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/AdventureScene.unity");
            var terrain = scene.GetRootGameObjects().Single(g => g.name == "GuanzhongTerrainRehearsal");
            var camera = scene.GetRootGameObjects().Single(g => g.GetComponent<Camera>());
            foreach (var root in scene.GetRootGameObjects())
                if (root != terrain && root != camera && !root.GetComponent<Light>()) UnityEngine.Object.DestroyImmediate(root);
            foreach (var component in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MonoBehaviour>(true)).ToArray())
                if (!(component is CombatBattlefieldSurface) && !component.GetType().Namespace.StartsWith("UnityEngine.Rendering"))
                    UnityEngine.Object.DestroyImmediate(component);
            terrain.SetActive(true);
            var rootSample = new GameObject("D12.1 Resource Sample");
            var sample = rootSample.AddComponent<EarthAssetSample>();
            sample.surface = terrain.GetComponent<CombatBattlefieldSurface>(); sample.sampleCamera = camera.GetComponent<Camera>();
            sample.provider = rootSample.AddComponent<Static3DCombatUnitPresentationProvider>();
            SetReference(sample.provider, "profileSet", AssetDatabase.LoadAssetAtPath<Static3DCombatUnitPresentationProfileSet>(
                "Assets/Data/CombatPresentationProfiles/Static3DCombatUnitPresentationProfileSet.asset"));
            SetReference(sample.provider, "flightLineMaterial", AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Art/VisualBaseline/Materials/VisualBaseline_Selected.mat"));
            sample.wallForm = LoadPrefab("WallForm"); sample.wallDissolve = LoadPrefab("WallDissolve");
            sample.rampForm = LoadPrefab("RampForm"); sample.rampDissolve = LoadPrefab("RampDissolve");
            sample.pressure = LoadPrefab("GravityPressure"); sample.landingDust = LoadPrefab("LandingDust");
            EditorSceneManager.SaveScene(scene, Root + "/Samples/EarthAssetSample.unity");
        }

        private static void SetReference(UnityEngine.Object target, string property, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target); serialized.FindProperty(property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        private static GameObject LoadPrefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/" + name + ".prefab");
        private static T SaveAsset<T>(T value, string relative) where T : UnityEngine.Object
        {
            string path = Root + "/" + relative;
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null) { AssetDatabase.CreateAsset(value, path); return value; }
            EditorUtility.CopySerialized(value, existing); UnityEngine.Object.DestroyImmediate(value); EditorUtility.SetDirty(existing); return existing;
        }
        private static void SavePrefab(GameObject value, string name)
        {
            if (!PrefabUtility.SaveAsPrefabAsset(value, Root + "/Prefabs/" + name + ".prefab")) throw new InvalidOperationException(name);
            UnityEngine.Object.DestroyImmediate(value);
        }

        public static void Inspect()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/AdventureScene.unity");
            var lines = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .Where(t => t.parent == null || t.GetComponent<CombatBattlefieldSurface>() || t.GetComponent<Camera>() ||
                    t.GetComponent<Static3DCombatUnitPresentationProvider>() || t.name.Contains("Terrain") || t.name.Contains("Battlefield"))
                .Select(t => PathOf(t) + " active=" + t.gameObject.activeSelf + " pos=" + t.position.ToString("F4") +
                    " rot=" + t.eulerAngles + " components=" + string.Join(",", t.GetComponents<Component>().Select(c => c.GetType().Name)))
                .ToList();
            foreach (string path in new[] { Source + "/Props/HighWall.prefab", Source + "/EarthManifest/EarthManifestRamp.prefab" })
            {
                var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
                    lines.Add(path + " / " + PathOf(f.transform) + " mesh=" + f.sharedMesh.name + " bounds=" +
                        f.sharedMesh.bounds + " world=" + f.GetComponent<Renderer>().bounds + " matrix=" + f.transform.localToWorldMatrix);
                UnityEngine.Object.DestroyImmediate(go);
            }
            Directory.CreateDirectory(Evidence);
            File.WriteAllLines(Path.Combine(Evidence, "source-inspection.txt"), lines);
        }

        private static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
    }
}
