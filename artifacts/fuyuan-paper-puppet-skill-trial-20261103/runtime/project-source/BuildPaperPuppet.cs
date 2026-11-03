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
    const string ControllerPath = "Assets/Generated/PaperPuppet.controller";
    const string ScenePath = "Assets/PaperPuppet.unity";
    static readonly string[] Names = { "walk", "cast", "attack" };

    public static void Build() => BuildInternal(false);
    public static void BuildShell() => BuildInternal(true);

    public static void Inspect()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ConfigureImport(ModelPath);
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

    static void PrepareEmbeddedTexture()
    {
        const string textureDirectory = "Assets/Model/Textures";
        const string texturePath = textureDirectory + "/fuyuan-skill-9cf92743-corrected-4k_basecolor.jpg";
        const string materialDirectory = "Assets/Model/Materials";
        const string materialPath = materialDirectory + "/tripo_mat_aa5cdbc9.mat";
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        Directory.CreateDirectory(textureDirectory);
        Directory.CreateDirectory(materialDirectory);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (!File.Exists(texturePath) && !importer.ExtractTextures(textureDirectory))
            throw new InvalidOperationException("Could not extract the source FBX embedded texture.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var textureImporter = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        if (textureImporter == null) throw new FileNotFoundException("Expected embedded source texture", texturePath);
        if (textureImporter.maxTextureSize != 4096)
        {
            textureImporter.maxTextureSize = 4096;
            textureImporter.SaveAndReimport();
        }
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture.width != 4096 || texture.height != 4096) throw new InvalidOperationException("The source texture did not import at 4096 x 4096.");
        var sourceMaterial = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath).GetComponentsInChildren<Renderer>(true)
            .SelectMany(r => r.sharedMaterials).Distinct().Single(m => m != null && m.name == "tripo_mat_aa5cdbc9");
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(sourceMaterial) { name = sourceMaterial.name };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.mainTexture = texture;
        EditorUtility.SetDirty(material);
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(sourceMaterial), material);
        AssetDatabase.SaveAssets();
        importer.SaveAndReimport();
    }

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
            foreach (var path in Directory.GetFiles("Assets/Model", "*.fbx")) ConfigureImport(path.Replace('\\', '/'));
            PrepareEmbeddedTexture();
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
        var player = new GameObject("苻渊纸偶播放器").AddComponent<PaperPuppetPlayer>();
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
        PlayerSettings.productName = "苻渊 · 纸偶动作小样";
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
        var result = BuildPipeline.BuildPlayer(new[] { ScenePath }, "Player/PaperPuppet.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
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
        string separatePath = "Assets/Model/" + name + ".fbx";
        string path = File.Exists(separatePath) ? separatePath : ModelPath;
        var choices = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
        var matches = path == separatePath ? choices : choices.Where(c => c.name == name || c.name.EndsWith("|" + name, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1 || matches[0].length <= 0)
            throw new InvalidOperationException("Expected one clip for " + name + " in " + path + "; got: " + string.Join(",", choices.Select(c => c.name)));
        return matches[0];
    }
}
