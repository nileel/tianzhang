using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BuildPaperPuppet
{
    const string ModelPath = "Assets/Model/model.fbx";
    const string MaterialMapPath = "Assets/Model/material-map.json";
    const string ControllerPath = "Assets/Generated/JointedDoll.controller";
    const string ScenePath = "Assets/JointedDoll.unity";
    static readonly string[] Names = { "walk", "cast", "attack" };

    public static void Build() => BuildInternal(false);
    public static void BuildShell() => BuildInternal(true);

    public static void Inspect()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ConfigureImport(ModelPath);
        PrepareMappedMaterials();
        WriteImportReport();
    }

    static void WriteImportReport()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var renderers = model.GetComponentsInChildren<Renderer>(true);
        var meshes = model.GetComponentsInChildren<MeshFilter>(true).Select(x => x.sharedMesh)
            .Concat(model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(x => x.sharedMesh)).Where(x => x != null).Distinct().ToArray();
        var materials = renderers.SelectMany(x => x.sharedMaterials).Where(x => x != null).Distinct().ToArray();
        var report = new ImportReport {
            model = ModelPath,
            renderers = renderers.Length,
            skinnedRenderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length,
            meshCount = meshes.Length,
            vertices = meshes.Sum(x => x.vertexCount),
            triangles = meshes.Sum(x => x.triangles.Length / 3),
            clips = Names.Select(name => { var c = LoadClip(name); return new ClipReport { name = c.name, seconds = c.length, fps = c.frameRate, curves = AnimationUtility.GetCurveBindings(c).Length }; }).ToArray(),
            materials = materials.Select(m => new MaterialReport { name = m.name, shader = m.shader.name, path = AssetDatabase.GetAssetPath(m), color = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white, texture = m.mainTexture == null ? "" : AssetDatabase.GetAssetPath(m.mainTexture), width = m.mainTexture == null ? 0 : m.mainTexture.width, height = m.mainTexture == null ? 0 : m.mainTexture.height }).ToArray()
        };
        File.WriteAllText("import-report.json", JsonUtility.ToJson(report, true));
        Debug.Log("PUPPET_IMPORT_REPORT " + JsonUtility.ToJson(report));
    }

    static void PrepareMappedMaterials()
    {
        const string materialDirectory = "Assets/Model/Materials";
        if (!File.Exists(MaterialMapPath)) throw new FileNotFoundException("Expected explicit material map", MaterialMapPath);
        var map = JsonUtility.FromJson<MaterialMap>(File.ReadAllText(MaterialMapPath));
        if (map == null || map.materials == null || map.materials.Length != 4)
            throw new InvalidOperationException("material-map.json must contain exactly four materials.");
        if (map.materials.Any(m => m == null || string.IsNullOrWhiteSpace(m.sourceMaterialName) || string.IsNullOrWhiteSpace(m.baseColorPath)))
            throw new InvalidOperationException("Every material mapping requires sourceMaterialName and baseColorPath.");
        if (map.materials.Select(m => m.sourceMaterialName).Distinct(StringComparer.Ordinal).Count() != map.materials.Length)
            throw new InvalidOperationException("Material mapping source names must be unique.");

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) throw new InvalidOperationException("FBX import did not produce a model.");
        var sourceMaterials = model.GetComponentsInChildren<Renderer>(true)
            .SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
        if (sourceMaterials.Length != map.materials.Length)
            throw new InvalidOperationException("Expected four model materials; got " + sourceMaterials.Length);
        var sources = new Material[map.materials.Length];
        var textureImporters = new TextureImporter[map.materials.Length];
        for (int i = 0; i < map.materials.Length; i++)
        {
            var entry = map.materials[i];
            var matches = sourceMaterials.Where(m => m.name == entry.sourceMaterialName).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("Expected exactly one source material named " + entry.sourceMaterialName + "; got " + matches.Length);
            if (entry.sourceMaterialName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidOperationException("Material name cannot be used as an asset filename: " + entry.sourceMaterialName);
            if (!entry.baseColorPath.StartsWith("Assets/Model/", StringComparison.Ordinal) || !File.Exists(entry.baseColorPath))
                throw new FileNotFoundException("Expected mapped base color within Assets/Model", entry.baseColorPath);
            textureImporters[i] = AssetImporter.GetAtPath(entry.baseColorPath) as TextureImporter;
            if (textureImporters[i] == null) throw new InvalidOperationException("Mapped base color is not an imported texture: " + entry.baseColorPath);
            sources[i] = matches[0];
        }

        var textures = new Texture2D[map.materials.Length];
        for (int i = 0; i < map.materials.Length; i++)
        {
            if (textureImporters[i].maxTextureSize != 4096)
            {
                textureImporters[i].maxTextureSize = 4096;
                textureImporters[i].SaveAndReimport();
            }
            textures[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(map.materials[i].baseColorPath);
            if (textures[i] == null || textures[i].width != 4096 || textures[i].height != 4096)
                throw new InvalidOperationException("Mapped base color must import at 4096 x 4096: " + map.materials[i].baseColorPath);
        }

        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        Directory.CreateDirectory(materialDirectory);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        for (int i = 0; i < map.materials.Length; i++)
        {
            var sourceMaterial = sources[i];
            string materialPath = materialDirectory + "/" + map.materials[i].sourceMaterialName + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(sourceMaterial) { name = sourceMaterial.name };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.mainTexture = textures[i];
            EditorUtility.SetDirty(material);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(sourceMaterial), material);
        }
        AssetDatabase.SaveAssets();
        importer.SaveAndReimport();
    }

    [Serializable] sealed class MaterialMap { public MaterialMapEntry[] materials; }
    [Serializable] sealed class MaterialMapEntry { public string sourceMaterialName, baseColorPath; }
    [Serializable] sealed class ImportReport { public string model; public int renderers, skinnedRenderers, meshCount, vertices, triangles; public ClipReport[] clips; public MaterialReport[] materials; }
    [Serializable] sealed class ClipReport { public string name; public float seconds, fps; public int curves; }
    [Serializable] sealed class MaterialReport { public string name, shader, path, texture; public Color color; public int width, height; }

    static void BuildInternal(bool shellOnly)
    {
        GameObject model = null;
        AnimationClip[] clips = null;
        AnimatorController controller = null;
        if (!shellOnly)
        {
            if (!File.Exists(ModelPath)) throw new FileNotFoundException("Expected the real character at " + ModelPath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureImport(ModelPath);
            PrepareMappedMaterials();
            model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) throw new InvalidOperationException("FBX import did not produce a model.");
            clips = Names.Select(LoadClip).ToArray();
            WriteImportReport();
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (var state in machine.states) machine.RemoveState(state.state);
            for (int i = 0; i < Names.Length; i++)
            {
                var state = machine.AddState(Names[i]);
                state.motion = clips[i];
                state.writeDefaultValues = true;
                if (i == 0) machine.defaultState = state;
            }
            EditorUtility.SetDirty(controller);
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var player = new GameObject("苻渊关节人偶播放器").AddComponent<PaperPuppetPlayer>();
        player.modelPrefab = model;
        player.controller = controller;
        player.clips = clips;
        const string groundPath = "Assets/Generated/StageGround.mat";
        var ground = AssetDatabase.LoadAssetAtPath<Material>(groundPath);
        if (ground == null)
        {
            ground = new Material(Shader.Find("Standard")) { name = "中性哑光地面" };
            ground.color = new Color(.54f, .53f, .49f);
            ground.SetFloat("_Metallic", 0);
            ground.SetFloat("_Glossiness", 0);
            AssetDatabase.CreateAsset(ground, groundPath);
        }
        player.groundMaterial = ground;
        EditorSceneManager.SaveScene(scene, ScenePath);
        PlayerSettings.productName = "苻渊 · 关节人偶实验";
        PlayerSettings.companyName = "TianZhang";
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1120;
        PlayerSettings.defaultScreenHeight = 800;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.SplashScreen.showUnityLogo = false;
        QualitySettings.SetQualityLevel(2);
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Player");
        var result = BuildPipeline.BuildPlayer(new[] { ScenePath }, "Player/FuyuanJointedDoll.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
        if (result.summary.result != BuildResult.Succeeded) throw new InvalidOperationException(result.summary.result.ToString());
        Debug.Log(shellOnly ? "PUPPET_SHELL_BUILD_OK model absent; compile only" : "PUPPET_BUILD_OK model=" + ModelPath + " clips=" + string.Join(",", clips.Select(c => c.name + ":" + c.length)));
    }

    static void ConfigureImport(string path)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        if (importer == null) { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport); importer = (ModelImporter)AssetImporter.GetAtPath(path); }
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.optimizeGameObjects = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.globalScale = 1;
        importer.useFileScale = true;
        importer.SaveAndReimport();
    }

    static AnimationClip LoadClip(string name)
    {
        var choices = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
        var matches = choices.Where(c => c.name == name || c.name.EndsWith("|" + name, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1 || matches[0].length <= 0)
            throw new InvalidOperationException("Expected one embedded clip for " + name + " in " + ModelPath + "; got: " + string.Join(",", choices.Select(c => c.name)));
        return matches[0];
    }
}
