using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TianZhang.Art.Jindan.Visibility.Samples;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Entity;
using TianZhang.Features.CombatPresentation;
using TianZhang.Infrastructure.UnityContent;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TianZhang.Art.Jindan.Visibility.Editor
{
    public static class VisibilityAssetBuilder
    {
        public const string Root = "Assets/Art/VFX/Guanzhong/Jindan/Visibility";
        public static string Evidence => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../assets/generated-scene-art/guanzhong-jindan-demo/d12-ability-assets/visibility/evidence/coverage-20261117"));

        [MenuItem("TianZhang/Art/D12.5/Rebuild Visibility resources and sample")]
        public static void Build()
        {
            foreach (string folder in new[] { "Materials", "Meshes", "Prefabs", "Samples" })
                Directory.CreateDirectory(Root + "/" + folder);
            Directory.CreateDirectory(Evidence);
            AssetDatabase.Refresh();
            var mesh = new Mesh { name = "AltarGlyph" };
            mesh.vertices = new[] { new Vector3(-.5f,-.5f,0), new Vector3(.5f,-.5f,0),
                new Vector3(.5f,.5f,0), new Vector3(-.5f,.5f,0) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0,2,1,0,3,2 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            mesh = Save(mesh, "Meshes/AltarGlyph.asset");
            foreach (bool moon in new[] { false, true })
            {
                string prefix = moon ? "Yin" : "Yang";
                var tint = moon ? new Color(.58f,.78f,1f) : new Color(1f,.78f,.36f);
                Material dormant = Material(prefix + "Dormant", tint * .58f, moon, false, .7f);
                Material active = Material(prefix + "Active", tint, moon, false, 1);
                Material halo = Material(prefix + "Halo", tint, moon, true, .75f);
                for (int state = 0; state < 3; state++) Support(prefix, state, mesh, dormant, active, halo);
            }
            BuildScene(); AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(Root + "/Samples/VisibilityAssetSample.unity");
            var sample = UnityEngine.Object.FindFirstObjectByType<VisibilityAssetSample>();
            if (!sample || !sample.yangAltar || !sample.yinAltar || !sample.yangRange || !sample.yinRange || !sample.catalog || !sample.provider)
                throw new InvalidOperationException("visibility_sample_persistence_failed");
            foreach (string side in new[] { "Yang", "Yin" })
            foreach (string state in new[] { "Dormant", "Active", "Destroyed" })
            {
                var prefab = Load<GameObject>(Root + "/Prefabs/" + side + state + ".prefab");
                if (!prefab.transform.Find("Intact/Identity") || !prefab.transform.Find("Intact/Glow/Glyph") || !prefab.transform.Find("Failed/Body"))
                    throw new InvalidOperationException("visibility_prefab_persistence_failed:" + prefab.name);
            }
            if (ShaderUtil.ShaderHasError(Shader.Find("TianZhang/Jindan/Visibility Domain Range")))
                throw new InvalidOperationException("visibility_range_shader_compile_failed");
            Debug.Log("D12_VISIBILITY_BUILD_OK");
        }

        private static Material Material(string name, Color color, bool moon, bool halo, float strength)
        {
            var value = new Material(Shader.Find("TianZhang/Jindan/Public Altar"));
            color.a = 1;
            value.SetColor("_BaseColor", color); value.SetFloat("_Moon", moon ? 1 : 0);
            value.SetFloat("_Halo", halo ? 1 : 0); value.SetFloat("_Strength", strength);
            return Save(value, "Materials/" + name + ".mat");
        }

        private static void Support(string prefix, int state, Mesh mesh, Material dormant, Material active, Material halo)
        {
            // Same already-delivered stele and original meshes. Only this package's copy is changed.
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(
                "Assets/Art/VFX/Guanzhong/Jindan/Space/Prefabs/ScaleSupport.prefab"));
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = prefix + new[] { "Dormant", "Active", "Destroyed" }[state];
            Transform intact = go.transform.Find("Intact");
            UnityEngine.Object.DestroyImmediate(intact.Find("Glow").gameObject);
            var body = intact.Find("Body").GetComponentsInChildren<Renderer>(true);
            Bounds bounds = body[0].bounds;
            foreach (var renderer in body.Skip(1)) bounds.Encapsulate(renderer.bounds);
            float size = bounds.size.x * .69f;
            Vector3 position = new(bounds.center.x, bounds.min.y + bounds.size.y * .53f, bounds.min.z - .007f);
            var glow = new GameObject("Glow"); glow.transform.SetParent(intact, false);
            Quad("Identity", intact, position, size, mesh, dormant);
            Quad("Glyph", glow.transform, position + Vector3.back * .002f, size, mesh, active);
            Quad("LocalGlow", glow.transform, position + Vector3.back * .004f, size * 1.8f, mesh, halo);
            intact.gameObject.SetActive(state != 2);
            go.transform.Find("Failed").gameObject.SetActive(state == 2);
            glow.SetActive(state == 1);
            if (!PrefabUtility.SaveAsPrefabAsset(go, Root + "/Prefabs/" + go.name + ".prefab"))
                throw new InvalidOperationException(go.name);
            UnityEngine.Object.DestroyImmediate(go);
        }

        private static void Quad(string name, Transform parent, Vector3 position, float size, Mesh mesh, Material material)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.transform.position = position; go.transform.localScale = Vector3.one * size;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        }

        private static void BuildScene()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/AdventureScene.unity");
            var terrain = scene.GetRootGameObjects().Single(go => go.name == "GuanzhongTerrainRehearsal");
            var camera = scene.GetRootGameObjects().Single(go => go.GetComponent<Camera>());
            foreach (var root in scene.GetRootGameObjects())
                if (root != terrain && root != camera && !root.GetComponent<Light>()) UnityEngine.Object.DestroyImmediate(root);
            foreach (var component in scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<MonoBehaviour>(true)).ToArray())
                if (!(component is CombatBattlefieldSurface) && !(component.GetType().Namespace ?? "").StartsWith("UnityEngine.Rendering"))
                    UnityEngine.Object.DestroyImmediate(component);
            terrain.SetActive(true);
            foreach (var t in terrain.GetComponentsInChildren<Transform>(true))
                if (t.name is "reveal_support" or "conceal_support") t.gameObject.SetActive(false);
            var goSample = new GameObject("D12.5 Visibility Resource Sample");
            var sample = goSample.AddComponent<VisibilityAssetSample>();
            sample.surface = terrain.GetComponent<CombatBattlefieldSurface>(); sample.sampleCamera = camera.GetComponent<Camera>();
            sample.provider = goSample.AddComponent<Static3DCombatUnitPresentationProvider>();
            var provider = new SerializedObject(sample.provider);
            provider.FindProperty("profileSet").objectReferenceValue = Load<Static3DCombatUnitPresentationProfileSet>(
                "Assets/Data/CombatPresentationProfiles/Static3DCombatUnitPresentationProfileSet.asset");
            provider.FindProperty("attackVfxCatalog").objectReferenceValue = Load<CombatAttackVfxCatalogData>("Assets/Data/CombatAttackVfx/CombatAttackVfxCatalog.asset");
            provider.ApplyModifiedPropertiesWithoutUndo();
            sample.yangAltar = Load<GameObject>(Root + "/Prefabs/YangDormant.prefab");
            sample.yinAltar = Load<GameObject>(Root + "/Prefabs/YinDormant.prefab");
            BuildRanges(sample.surface);
            sample.yangRange = Load<GameObject>(Root + "/Prefabs/YangDomainRange.prefab");
            sample.yinRange = Load<GameObject>(Root + "/Prefabs/YinDomainRange.prefab");
            sample.catalog = Load<JindanDemoCatalogData>("Assets/Data/JindanDemo/JindanDemoCatalog.asset");
            sample.rules = Load<CombatRuleSetData>("Assets/Data/CombatRuleSet.asset");
            sample.environment = Load<EnvironmentProfileAsset>("Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_jindan_demo.asset");
            sample.attacks = AssetDatabase.FindAssets("t:AttackProfileData", new[] { "Assets/Data/AttackProfiles" })
                .Select(id => Load<AttackProfileData>(AssetDatabase.GUIDToAssetPath(id))).ToArray();
            EditorSceneManager.SaveScene(scene, Root + "/Samples/VisibilityAssetSample.unity");
        }

        private static void BuildRanges(CombatBattlefieldSurface surface)
        {
            // VFX carrier only. Bake this fixed map's 144 cells without changing its geometry.
            const float lift = .025f;
            var source = new Mesh();
            var vertices = new Vector3[7]; var indices = new int[18];
            for (int i = 0; i < 6; i++)
            {
                float a = (30+i*60)*Mathf.Deg2Rad;
                vertices[i+1] = new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*.57735027f;
                indices[i*3] = 0; indices[i*3+1] = i+1; indices[i*3+2] = (i+1)%6+1;
            }
            source.vertices = vertices; source.triangles = indices;
            var carrier = new GameObject("Range bake transform");
            var allVertices = new List<Vector3>(); var uv = new List<Vector2>();
            var cells = new List<Vector2>(); var triangles = new List<int>();
            float maxError = 0; int samples = 0;
            foreach (var cell in surface.Cells)
            {
                Vector3 center = CombatBattlefieldSurface.Horizontal(cell.q,cell.r);
                carrier.transform.position = center;
                var fitted = VisibilityRangeSupport.Fit(source,carrier.transform,surface,cell,lift);
                maxError = Mathf.Max(maxError,VisibilityRangeSupport.InteriorError(fitted,carrier.transform,surface,cell,lift,out int count));
                samples += count;
                int offset = allVertices.Count;
                foreach (Vector3 local in fitted.vertices)
                {
                    allVertices.Add(carrier.transform.TransformPoint(local)); uv.Add(new Vector2(local.x,local.z));
                    cells.Add(new Vector2(cell.q,cell.r));
                }
                triangles.AddRange(fitted.triangles.Select(index => offset+index));
                UnityEngine.Object.DestroyImmediate(fitted);
            }
            UnityEngine.Object.DestroyImmediate(carrier); UnityEngine.Object.DestroyImmediate(source);
            if (surface.Cells.Count != 144 || maxError > .001f) throw new InvalidOperationException("visibility_range_fit:"+maxError);
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(allVertices); mesh.SetUVs(0,uv); mesh.SetUVs(1,cells); mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh = Save(mesh,"Meshes/WholeBoardRange.asset");
            VisibilityDomainBuilder.Build(mesh);
            File.WriteAllText(Path.Combine(Evidence,"range-geometry-proof.txt"),
                "144 fixed support cells including non-stoppable cells; UV2 records each cell q/r.\n"
                +"Source=CombatBattlefieldSurface; local FireSampleSupport fitting algorithm; lift="+lift
                +"m; strict interior samples="+samples+"; max height error="+maxError.ToString("F8")+"m\n"
                +"vertices="+mesh.vertexCount+" triangles="+mesh.triangles.Length/3+" bounds="+mesh.bounds+"\n"
                +"Slope: 12 subdivisions; stairs: clipped per tread, no cross-riser triangles. PASS\n");
        }

        private static T Load<T>(string path) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException(path);
        private static T Save<T>(T value, string relative) where T : UnityEngine.Object
        {
            string path = Root + "/" + relative; value.name = Path.GetFileNameWithoutExtension(relative);
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null) { AssetDatabase.CreateAsset(value, path); return value; }
            EditorUtility.CopySerialized(value, existing); UnityEngine.Object.DestroyImmediate(value);
            EditorUtility.SetDirty(existing); return existing;
        }
    }
}
